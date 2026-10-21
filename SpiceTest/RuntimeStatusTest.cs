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
using System.Text.Json;
using Spice;
using Spice.Status;

namespace SpiceTest;

/// <summary>
/// Covers <see cref="SpiceClient.GetRuntimeStatusAsync"/> against a stub HTTP endpoint, so the
/// behaviour is verified without a live Spice runtime.
/// </summary>
public class RuntimeStatusTest
{
    // The body a local runtime returns from GET /v1/status with metrics disabled.
    private const string LocalRuntimeStatus = """
        [{"name":"http","endpoint":"127.0.0.1:8090","status":"Ready"},{"name":"flight","endpoint":"127.0.0.1:50051","status":"Ready"},{"name":"metrics","endpoint":"N/A","status":"Disabled"},{"name":"opentelemetry","endpoint":"127.0.0.1:50051","status":"Ready"}]
        """;

    private static readonly string[] LocalRuntimeComponents = { "http", "flight", "metrics", "opentelemetry" };

    [Test]
    public async Task Test_GetRuntimeStatus_ReturnsEachComponent_InRuntimeOrder()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.OK, LocalRuntimeStatus));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var status = await client.GetRuntimeStatusAsync();

        Assert.That(runtime.RequestedPaths, Does.Contain("/v1/status"));
        Assert.That(runtime.RequestedMethods, Does.Contain("GET"));
        Assert.That(status.Select(c => c.Name), Is.EqualTo(LocalRuntimeComponents));
        Assert.Multiple(() =>
        {
            Assert.That(status[0].Endpoint, Is.EqualTo("127.0.0.1:8090"));
            Assert.That(status[0].Status, Is.EqualTo(ComponentStatus.Ready));
            Assert.That(status[0].StatusText, Is.EqualTo("Ready"));
            Assert.That(status[1].Endpoint, Is.EqualTo("127.0.0.1:50051"));
            Assert.That(status[2].Endpoint, Is.EqualTo("N/A"));
            Assert.That(status[2].Status, Is.EqualTo(ComponentStatus.Disabled));
        });
    }

    // Every value the runtime's ComponentStatus serializes to.
    [TestCase("Initializing", ComponentStatus.Initializing)]
    [TestCase("Ready", ComponentStatus.Ready)]
    [TestCase("Disabled", ComponentStatus.Disabled)]
    [TestCase("Error", ComponentStatus.Error)]
    [TestCase("Refreshing", ComponentStatus.Refreshing)]
    [TestCase("ShuttingDown", ComponentStatus.ShuttingDown)]
    [TestCase("NotLoaded", ComponentStatus.NotLoaded)]
    [TestCase("ready", ComponentStatus.Ready)]
    public async Task Test_GetRuntimeStatus_MapsStatus(string wire, ComponentStatus expected)
    {
        using var runtime = new StubRuntime((_, _) =>
            (HttpStatusCode.OK, $$"""[{"name":"flight","endpoint":"127.0.0.1:50051","status":"{{wire}}"}]"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var status = await client.GetRuntimeStatusAsync();

        Assert.That(status.Single().Status, Is.EqualTo(expected));
        Assert.That(status.Single().StatusText, Is.EqualTo(wire));
    }

    // A state added by a newer runtime must not break this client, and the numeric and flag-list
    // forms Enum.TryParse would accept are not states the runtime sends.
    [TestCase("Draining")]
    [TestCase("2")]
    [TestCase("Ready, Error")]
    [TestCase("")]
    public async Task Test_GetRuntimeStatus_MapsUnrecognizedStatusToUnknown_KeepingTheText(string wire)
    {
        using var runtime = new StubRuntime((_, _) =>
            (HttpStatusCode.OK, $$"""[{"name":"flight","endpoint":"127.0.0.1:50051","status":"{{wire}}"}]"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var status = await client.GetRuntimeStatusAsync();

        Assert.That(status.Single().Status, Is.EqualTo(ComponentStatus.Unknown));
        Assert.That(status.Single().StatusText, Is.EqualTo(wire));
    }

    [Test]
    public async Task Test_GetRuntimeStatus_IgnoresFieldsItDoesNotKnow()
    {
        using var runtime = new StubRuntime((_, _) =>
            (HttpStatusCode.OK, """[{"name":"http","endpoint":"127.0.0.1:8090","status":"Ready","since_ms":1750000000000}]"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var status = await client.GetRuntimeStatusAsync();

        Assert.That(status.Single().Name, Is.EqualTo("http"));
    }

    // An empty list would read as a runtime with no components, so a body that is not a list of
    // components is rejected rather than coerced.
    [TestCase("""{"error":"unexpected"}""", "an object")]
    [TestCase("null", "null")]
    [TestCase("""[42]""", "entry 0 is a number, not an object")]
    [TestCase("""[{"name":"http","endpoint":"127.0.0.1:8090","status":"Ready"},{"name":"flight","status":"Ready"}]""", "entry 1 has no \"endpoint\"")]
    [TestCase("""[{"name":"http","endpoint":"127.0.0.1:8090","status":1}]""", "entry 0 has a \"status\" that is a number, not a string")]
    public void Test_GetRuntimeStatus_RejectsMalformedResponse(string body, string expectedProblem)
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.OK, body));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<JsonException>(async () => await client.GetRuntimeStatusAsync());
        Assert.That(ex!.Message, Does.Contain(expectedProblem));
    }

    [Test]
    public void Test_GetRuntimeStatus_Throws_WhenForbidden()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.Forbidden, "forbidden"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetRuntimeStatusAsync());
        Assert.That(ex!.Message, Does.Contain("read access"));
    }

    [Test]
    public void Test_GetRuntimeStatus_Throws_WithRuntimeMessage_OnServerError()
    {
        using var runtime = new StubRuntime((_, _) => (HttpStatusCode.InternalServerError, """{"error":"Error converting to CSV"}"""));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetRuntimeStatusAsync());
        Assert.That(ex!.Message, Does.Contain("500"));
        Assert.That(ex.Message, Does.Contain("Error converting to CSV"));
    }

    [Test]
    public void Test_GetRuntimeStatus_Throws_WhenRuntimeIsUnreachable()
    {
        var address = $"http://localhost:{StubRuntime.ReserveFreePort()}";
        using var client = new SpiceClientBuilder().WithHttpAddress(address).Build();

        Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetRuntimeStatusAsync());
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
