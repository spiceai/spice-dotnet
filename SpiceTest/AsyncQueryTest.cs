/*
Copyright 2024 The Spice.ai OSS Authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Apache.Arrow;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spice;
using Spice.Query;

namespace SpiceTest;

/// <summary>
/// Covers async query submission over Flight DoAction against an in-process Flight server,
/// so the behaviour is verified without a live, cluster-mode Spice runtime.
/// </summary>
public class AsyncQueryTest
{
    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        // Required once per process for the Grpc.Net.Client HttpClient to speak plaintext HTTP/2
        // to the loopback test server (no TLS in tests).
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    // ==================== Submit + status ====================

    [Test]
    public async Task Test_QueryAsync_ReturnsHandle_WithInitialStatus()
    {
        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, _ => """{"query_id":"q-1","status":"PENDING"}""");
        server.On(AsyncQueryActions.GetStatus, _ => """{"query_id":"q-1","status":"RUNNING"}""");

        using var client = server.BuildClient();

        var query = await client.QueryAsync("SELECT 1");

        Assert.That(query.Id, Is.EqualTo("q-1"));
        Assert.That(query.Status, Is.EqualTo(QueryStatus.Pending));

        var status = await query.GetStatusAsync();

        Assert.That(status, Is.EqualTo(QueryStatus.Running));
        Assert.That(query.Status, Is.EqualTo(QueryStatus.Running));
    }

    [Test]
    public async Task Test_WaitAsync_PollsUntilTerminal()
    {
        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, _ => """{"query_id":"q-2","status":"PENDING"}""");

        var polls = 0;
        server.On(AsyncQueryActions.GetStatus, _ =>
        {
            polls++;
            return polls < 3
                ? """{"query_id":"q-2","status":"RUNNING"}"""
                : """{"query_id":"q-2","status":"SUCCEEDED","result":{"total_row_count":0,"total_chunk_count":0}}""";
        });

        using var client = server.BuildClient();
        var query = await client.QueryAsync("SELECT 1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var status = await query.WaitAsync(cts.Token);

        Assert.That(status, Is.EqualTo(QueryStatus.Succeeded));
        Assert.That(polls, Is.GreaterThanOrEqualTo(3));
    }

    // ==================== Results ====================

    [Test]
    public async Task Test_GetResultsAsync_EmptyResult_ReturnsNoRecords()
    {
        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, _ => """{"query_id":"q-3","status":"PENDING"}""");
        server.On(AsyncQueryActions.GetStatus, _ =>
            """{"query_id":"q-3","status":"SUCCEEDED","result":{"total_row_count":0,"total_chunk_count":0}}""");
        server.On(AsyncQueryActions.GetResult, _ => throw new RpcException(new Status(StatusCode.NotFound, "no chunks for an empty result")));

        using var client = server.BuildClient();
        var query = await client.QueryAsync("SELECT 1 WHERE false");

        using var reader = await query.GetResultsAsync();
        var batch = await reader.ReadNextRecordBatchAsync();

        Assert.That(batch, Is.Null);
    }

    [Test]
    public async Task Test_GetResultsAsync_OneChunk_DecodesData()
    {
        var chunk = BuildInt64Chunk("n", new long[] { 1, 2, 3 });

        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, _ => """{"query_id":"q-4","status":"PENDING"}""");
        server.On(AsyncQueryActions.GetStatus, _ =>
            """{"query_id":"q-4","status":"SUCCEEDED","result":{"total_row_count":3,"total_chunk_count":1}}""");
        server.OnBytes(AsyncQueryActions.GetResult, _ => chunk);

        using var client = server.BuildClient();
        var query = await client.QueryAsync("SELECT n FROM t");

        using var reader = await query.GetResultsAsync();
        var batch = await reader.ReadNextRecordBatchAsync();

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.Length, Is.EqualTo(3));
        var column = (Int64Array)batch.Column(0);
        Assert.That(new[] { column.GetValue(0), column.GetValue(1), column.GetValue(2) }, Is.EqualTo(new long?[] { 1, 2, 3 }));

        Assert.That(await reader.ReadNextRecordBatchAsync(), Is.Null);
    }

    [Test]
    public async Task Test_GetResultsAsync_FailedQuery_SurfacesRuntimeError()
    {
        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, _ => """{"query_id":"q-5","status":"PENDING"}""");
        server.On(AsyncQueryActions.GetStatus, _ =>
            """{"query_id":"q-5","status":"FAILED","error":{"error_code":"QueryExecutionError","message":"boom"}}""");

        using var client = server.BuildClient();
        var query = await client.QueryAsync("SELECT 1/0");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await query.GetResultsAsync());
        Assert.That(ex!.Message, Does.Contain("boom"));
    }

    // ==================== Cancel ====================

    [Test]
    public async Task Test_CancelAsync_UpdatesStatus_AndCallsActionOnce()
    {
        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, _ => """{"query_id":"q-6","status":"RUNNING"}""");
        server.On(AsyncQueryActions.Cancel, _ => """{"query_id":"q-6","cancelled":true,"status":"CANCELLED"}""");

        using var client = server.BuildClient();
        var query = await client.QueryAsync("SELECT 1");

        await query.CancelAsync();

        Assert.That(query.Status, Is.EqualTo(QueryStatus.Cancelled));
        Assert.That(server.CallCount(AsyncQueryActions.Cancel), Is.EqualTo(1));
    }

    // ==================== Parameters ====================

    [Test]
    public async Task Test_QueryWithParamsAsync_PlacesSqlAndParametersInRequestBody()
    {
        string? submittedBody = null;

        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, body =>
        {
            submittedBody = body;
            return """{"query_id":"q-7","status":"PENDING"}""";
        });

        using var client = server.BuildClient();
        await client.QueryWithParamsAsync("SELECT * FROM t WHERE id = $1", 42);

        Assert.That(submittedBody, Is.Not.Null);
        using var document = JsonDocument.Parse(submittedBody!);
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("sql").GetString(), Is.EqualTo("SELECT * FROM t WHERE id = $1"));
            Assert.That(document.RootElement.GetProperty("parameters")[0].GetInt32(), Is.EqualTo(42));
        });
    }

    [Test]
    public async Task Test_QueryAsync_WithoutParams_OmitsParametersField()
    {
        string? submittedBody = null;

        await using var server = await AsyncQueryTestServer.StartAsync();
        server.On(AsyncQueryActions.Submit, body =>
        {
            submittedBody = body;
            return """{"query_id":"q-8","status":"PENDING"}""";
        });

        using var client = server.BuildClient();
        await client.QueryAsync("SELECT 1");

        using var document = JsonDocument.Parse(submittedBody!);
        Assert.That(document.RootElement.TryGetProperty("parameters", out _), Is.False);
    }

    // ==================== Helpers ====================

    private static byte[] BuildInt64Chunk(string columnName, long[] values)
    {
        var schema = new Schema.Builder().Field(f => f.Name(columnName).DataType(Int64Type.Default).Nullable(false)).Build();

        var builder = new Int64Array.Builder();
        builder.AppendRange(values);
        var column = builder.Build();

        using var batch = new RecordBatch(schema, new IArrowArray[] { column }, values.Length);
        using var stream = new MemoryStream();
        using (var writer = new ArrowStreamWriter(stream, schema))
        {
            writer.WriteRecordBatch(batch);
            writer.WriteEnd();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Minimal in-process Flight server hosting only DoAction, driven by per-action-type
    /// handlers registered by each test. Lets tests stay declarative instead of each writing
    /// its own <see cref="FlightServer"/> subclass.
    /// </summary>
    private sealed class AsyncQueryTestServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private AsyncQueryTestServer(WebApplication app)
        {
            _app = app;
        }

        public static async Task<AsyncQueryTestServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o =>
                o.Listen(System.Net.IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc().AddFlightServer<TestFlightServer>();

            // AddFlightServer<T>() activates T itself for each DoAction call (its own DI
            // container path, not visibly affected by registering T with a different
            // lifetime), so a per-instance Handlers dictionary set up before the request is
            // sent would not be visible to the instance that actually serves it. Route
            // through process-wide static state instead, reset per server instance -- tests
            // in this fixture run sequentially and each owns its own server/port.
            TestFlightServer.Reset();

            var app = builder.Build();
            app.MapFlightEndpoint();
            await app.StartAsync().ConfigureAwait(false);

            return new AsyncQueryTestServer(app);
        }

        public string Address => _app.Urls.First();

        // These forward to TestFlightServer's static state (see the comment in StartAsync)
        // rather than an instance field, so they don't access `this` -- kept as instance
        // members anyway since conceptually they configure this particular server instance.
#pragma warning disable CA1822
        /// <summary>Registers a handler that returns a UTF-8 JSON/text response body.</summary>
        public void On(string actionType, Func<string, string> handler) =>
            TestFlightServer.Handlers[actionType] = body => Encoding.UTF8.GetBytes(handler(Encoding.UTF8.GetString(body)));

        /// <summary>Registers a handler that returns a raw response body (for Arrow IPC chunks).</summary>
        public void OnBytes(string actionType, Func<byte[], byte[]> handler) =>
            TestFlightServer.Handlers[actionType] = handler;

        public int CallCount(string actionType) => TestFlightServer.CallCounts.GetOrAdd(actionType, 0);
#pragma warning restore CA1822

        public SpiceClient BuildClient() => new SpiceClientBuilder().WithFlightAddress(Address).Build();

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class TestFlightServer : FlightServer
    {
        // Static: the ASP.NET Core gRPC host activates a new TestFlightServer per DoAction
        // call, so per-instance state set up by a test would never reach the instance that
        // serves the request. See the comment in AsyncQueryTestServer.StartAsync.
        public static ConcurrentDictionary<string, Func<byte[], byte[]>> Handlers { get; private set; } = new();
        public static ConcurrentDictionary<string, int> CallCounts { get; private set; } = new();

        public static void Reset()
        {
            Handlers = new ConcurrentDictionary<string, Func<byte[], byte[]>>();
            CallCounts = new ConcurrentDictionary<string, int>();
        }

        public override Task DoAction(FlightAction request, IAsyncStreamWriter<FlightResult> responseStream, ServerCallContext context)
        {
            CallCounts.AddOrUpdate(request.Type, 1, (_, count) => count + 1);

            if (!Handlers.TryGetValue(request.Type, out var handler))
            {
                throw new RpcException(new Status(StatusCode.Unimplemented, $"no handler registered for action {request.Type}"));
            }

            var responseBody = handler(request.Body.ToByteArray());
            return responseStream.WriteAsync(new FlightResult(responseBody));
        }
    }
}
