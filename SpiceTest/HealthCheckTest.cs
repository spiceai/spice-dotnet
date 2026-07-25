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

namespace SpiceTest;

/// <summary>
/// Covers the health and readiness probes against a stub HTTP endpoint, so the behaviour is
/// verified without a live Spice runtime.
/// </summary>
public class HealthCheckTest
{
    [Test]
    public async Task Test_IsSpiceHealthyAsync_ReturnsTrue_WhenRuntimeReportsOk()
    {
        using var runtime = new StubRuntime((_) => (HttpStatusCode.OK, "ok\n"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        Assert.That(await client.IsSpiceHealthyAsync(), Is.True);
        Assert.That(runtime.RequestedPaths, Does.Contain("/health"));
    }

    [Test]
    public async Task Test_IsSpiceHealthyAsync_ReturnsFalse_WhenRuntimeReportsFailure()
    {
        using var runtime = new StubRuntime((_) => (HttpStatusCode.ServiceUnavailable, "unavailable"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        Assert.That(await client.IsSpiceHealthyAsync(), Is.False);
    }

    [Test]
    public async Task Test_IsSpiceReadyAsync_ReturnsTrue_WhenRuntimeReportsReady()
    {
        using var runtime = new StubRuntime((_) => (HttpStatusCode.OK, "ready"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        Assert.That(await client.IsSpiceReadyAsync(), Is.True);
        Assert.That(runtime.RequestedPaths, Does.Contain("/v1/ready"));
    }

    /// <summary>
    /// The runtime answers "not ready" while components are still loading. A substring match on
    /// "ready" would read that as success, so the probe compares the whole body.
    /// </summary>
    [Test]
    public async Task Test_IsSpiceReadyAsync_ReturnsFalse_WhenRuntimeIsStillLoading()
    {
        using var runtime = new StubRuntime((_) => (HttpStatusCode.ServiceUnavailable, "not ready"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        Assert.That(await client.IsSpiceReadyAsync(), Is.False);
    }

    [Test]
    public async Task Test_IsSpiceReadyAsync_ReturnsFalse_WhenBodyIsUnexpected_DespiteSuccessStatus()
    {
        using var runtime = new StubRuntime((_) => (HttpStatusCode.OK, "not ready"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();

        Assert.That(await client.IsSpiceReadyAsync(), Is.False);
    }

    [Test]
    public async Task Test_Probes_ReturnFalse_WhenRuntimeIsUnreachable()
    {
        var unreachable = $"http://127.0.0.1:{StubRuntime.ReserveFreePort()}";
        using var client = new SpiceClientBuilder().WithHttpAddress(unreachable).Build();

        Assert.That(await client.IsSpiceHealthyAsync(), Is.False);
        Assert.That(await client.IsSpiceReadyAsync(), Is.False);
    }

    [Test]
    public void Test_Probes_Throw_WhenCallerCancels()
    {
        using var runtime = new StubRuntime((_) => (HttpStatusCode.OK, "ok"));
        using var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(async () => await client.IsSpiceHealthyAsync(cts.Token));
        Assert.ThrowsAsync<TaskCanceledException>(async () => await client.IsSpiceReadyAsync(cts.Token));
    }

    [Test]
    public void Test_Probes_Throw_WhenClientIsDisposed()
    {
        using var runtime = new StubRuntime((_) => (HttpStatusCode.OK, "ok"));
        var client = new SpiceClientBuilder().WithHttpAddress(runtime.Address).Build();
        client.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.IsSpiceHealthyAsync());
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.IsSpiceReadyAsync());
    }

    /// <summary>
    /// Minimal loopback HTTP endpoint that answers every request with a caller-supplied status and
    /// body, and records the paths it was asked for.
    /// </summary>
    private sealed class StubRuntime : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly ConcurrentQueue<string> _paths = new();
        private bool _disposed;

        public string Address { get; }

        public IEnumerable<string> RequestedPaths => _paths;

        public StubRuntime(Func<string, (HttpStatusCode Status, string Body)> respond)
        {
            // HttpListener needs the literal "localhost" to bind without elevation on Windows.
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

                    var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                    _paths.Enqueue(path);

                    var (status, body) = respond(path);
                    var payload = Encoding.UTF8.GetBytes(body);
                    context.Response.StatusCode = (int)status;
                    context.Response.ContentType = "text/plain";
                    context.Response.ContentLength64 = payload.Length;
                    await context.Response.OutputStream.WriteAsync(payload.AsMemory()).ConfigureAwait(false);
                    context.Response.Close();
                }
            });
        }

        /// <summary>
        /// Returns a loopback port that is free at the moment of the call.
        /// </summary>
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
