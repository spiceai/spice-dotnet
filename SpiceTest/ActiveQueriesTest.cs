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
using System.Net;
using System.Net.Sockets;
using System.Text;
using Spice;
using Spice.Query;

namespace SpiceTest;

/// <summary>
/// Covers <see cref="SpiceClient.ListActiveQueriesAsync"/> and
/// <see cref="SpiceClient.CancelActiveQueryAsync"/> against a stub HTTP endpoint, so the behaviour
/// is verified without a live Spice runtime.
/// </summary>
public class ActiveQueriesTest
{
    private const string ValidQueryId = "0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f70";

    // ==================== ListActiveQueriesAsync ====================

    [Test]
    public async Task Test_ListActiveQueries_ReturnsQueries_WhenRuntimeReportsThem()
    {
        var body = """
        {"queries":[
            {"query_id":"0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f70","protocol":"flight","sql_preview":"SELECT * FROM taxi_trips","started_at_ms":1750000000000},
            {"query_id":"0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f71","protocol":"http","sql_preview":"SELECT 1","started_at_ms":1750000000500}
        ],"total_count":2}
        """;
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.OK, body));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var queries = await client.ListActiveQueriesAsync();

        Assert.That(runtime.RequestedPaths, Does.Contain("/v1/sql/active"));
        Assert.That(runtime.RequestedMethods, Does.Contain("GET"));
        Assert.Multiple(() =>
        {
            Assert.That(queries, Has.Count.EqualTo(2));
            Assert.That(queries[0].QueryId, Is.EqualTo("0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f70"));
            Assert.That(queries[0].Protocol, Is.EqualTo("flight"));
            Assert.That(queries[0].SqlPreview, Is.EqualTo("SELECT * FROM taxi_trips"));
            Assert.That(queries[0].StartedAtMs, Is.EqualTo(1750000000000));
            Assert.That(queries[1].Protocol, Is.EqualTo("http"));
        });
    }

    [Test]
    public async Task Test_ListActiveQueries_ReturnsEmptyList_WhenNoneRunning()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.OK, """{"queries":[],"total_count":0}"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var queries = await client.ListActiveQueriesAsync();

        Assert.That(queries, Is.Not.Null.And.Empty);
    }

    [Test]
    public void Test_ListActiveQueries_Throws_WhenForbidden()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.Forbidden, "write access required"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.ListActiveQueriesAsync());
        Assert.That(ex!.Message, Does.Contain("write access"));
    }

    [Test]
    public void Test_ListActiveQueries_Throws_NamingStatus_OnServerError()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.InternalServerError, "boom"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.ListActiveQueriesAsync());
        Assert.That(ex!.Message, Does.Contain("500"));
    }

    // ==================== CancelActiveQueryAsync ====================

    [Test]
    public async Task Test_CancelActiveQuery_Succeeds()
    {
        using var runtime = new StubRuntime((_, _) =>
            (HttpStatusCode.OK, $$"""{"query_id":"{{ValidQueryId}}","status":"cancelled"}"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        await client.CancelActiveQueryAsync(ValidQueryId);

        Assert.That(runtime.RequestedPaths, Does.Contain($"/v1/sql/{ValidQueryId}/cancel"));
        Assert.That(runtime.RequestedMethods, Does.Contain("POST"));
    }

    [Test]
    public void Test_CancelActiveQuery_Throws_WhenQueryIdIsBlank_WithoutSendingARequest()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.OK, "{}"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        Assert.ThrowsAsync<ArgumentException>(async () => await client.CancelActiveQueryAsync(""));
        Assert.ThrowsAsync<ArgumentException>(async () => await client.CancelActiveQueryAsync("   "));
        Assert.That(runtime.RequestedPaths, Is.Empty);
    }

    // An ID that is not a UUID must never become a request path: "." and ".." are unreserved, so
    // escaping leaves them intact, and a proxy or server that resolves dot segments would route
    // this POST off the cancel route entirely.
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("../queries/escape")]
    [TestCase("not-a-uuid")]
    [TestCase("0198f0a1-9c3d-7c4e-8a11")]
    [TestCase("0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f7g")]
    [TestCase("0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f70/cancel")]
    [TestCase("{0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f70}")]
    [TestCase(" 0198f0a1-9c3d-7c4e-8a11-2b3c4d5e6f70")]
    public void Test_CancelActiveQuery_RejectsNonUuidQueryId_WithoutSendingARequest(string queryId)
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.OK, "{}"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<ArgumentException>(async () => await client.CancelActiveQueryAsync(queryId));

        Assert.That(ex!.Message, Does.Contain("not a valid UUID"));
        Assert.That(runtime.RequestedPaths, Is.Empty, "an invalid query ID reached the runtime");
    }

    [Test]
    public void Test_CancelActiveQuery_AcceptsUppercaseUuid()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.OK, "{}"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        Assert.DoesNotThrowAsync(async () => await client.CancelActiveQueryAsync(ValidQueryId.ToUpperInvariant()));
    }

    [Test]
    public void Test_CancelActiveQuery_Throws_WhenNotFound()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.NotFound, """{"error":"not found"}"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.CancelActiveQueryAsync(ValidQueryId));
        Assert.That(ex!.Message, Does.Contain("already finished"));
    }

    [Test]
    public void Test_CancelActiveQuery_Throws_WhenRuntimeReturnsBadRequest()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.BadRequest, """{"error":"Invalid query_id"}"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<ArgumentException>(async () => await client.CancelActiveQueryAsync(ValidQueryId));
        Assert.That(ex!.Message, Does.Contain("not a valid UUID"));
    }

    [Test]
    public void Test_CancelActiveQuery_Throws_WhenForbidden()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.Forbidden, "write access required"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.CancelActiveQueryAsync(ValidQueryId));
        Assert.That(ex!.Message, Does.Contain("write access"));
    }

    [Test]
    public void Test_CancelActiveQuery_Throws_NamingStatus_OnServerError()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.InternalServerError, "boom"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.CancelActiveQueryAsync(ValidQueryId));
        Assert.That(ex!.Message, Does.Contain("500"));
    }

    // ==================== ActiveQuery.StartedAt ====================

    [Test]
    public void Test_ActiveQuery_StartedAt_ConvertsFromUnixMillis()
    {
        var query = new ActiveQuery { StartedAtMs = 1750000000000 };

        Assert.That(query.StartedAt, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1750000000000)));
    }

    /// <summary>
    /// Minimal loopback HTTP endpoint that answers every request with a caller-supplied status and
    /// body, and records the paths/methods it was asked for.
    /// </summary>
    private sealed class StubRuntime : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly ConcurrentQueue<string> _paths = new();
        private readonly ConcurrentQueue<string> _methods = new();
        private bool _disposed;

        public string Address { get; }

        public IEnumerable<string> RequestedPaths => _paths;

        public IEnumerable<string> RequestedMethods => _methods;

        public StubRuntime(Func<string, string, (HttpStatusCode Status, string Body)> respond)
        {
            var port = ReserveFreePort();
            Address = $"http://localhost:{port}";

            _listener = new HttpListener();
            _listener.Prefixes.Add($"{Address}/");
            _listener.Start();

            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (HttpListenerException)
                    {
                        return; // Listener stopped.
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    try
                    {
                        var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                        var method = context.Request.HttpMethod;
                        _paths.Enqueue(path);
                        _methods.Enqueue(method);

                        var (status, body) = respond(path, method);
                        var payload = Encoding.UTF8.GetBytes(body);
                        context.Response.StatusCode = (int)status;
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = payload.Length;
                        await context.Response.OutputStream.WriteAsync(payload.AsMemory()).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // The client may have disconnected, or the listener may be disposing
                        // concurrently with this response - either way there's no caller left
                        // to report a test failure to.
                    }
                    finally
                    {
                        try
                        {
                            context.Response.Close();
                        }
                        catch (Exception)
                        {
                            // Same rationale as above.
                        }
                    }
                }
            });
        }

        public static int ReserveFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _listener.Stop();
            _listener.Close();
        }
    }
}
