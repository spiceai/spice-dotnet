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
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spice;
using Spice.Auth;
using Spice.Common;
using Spice.Query;

namespace SpiceTest;

/// <summary>
/// Covers Flight address schemes (grpc://, grpc+tls://, grpc+tcp://, http://, https://) and API key
/// formats (plain OSS keys and Spice Cloud 'appId|key' keys).
/// </summary>
[TestFixture]
[NonParallelizable]
public class ConnectionSettingsTest
{
    // ==================== Flight address schemes ====================

    [TestCase("grpc://my-server:50051", false, "http://my-server:50051", false)]
    [TestCase("grpc+tcp://my-server:50051", false, "http://my-server:50051", false)]
    [TestCase("grpc+tls://my-server:50051", false, "https://my-server:50051", true)]
    [TestCase("GRPC+TLS://my-server:50051", false, "https://my-server:50051", true)]
    [TestCase("http://my-server:50051", false, "http://my-server:50051", false)]
    [TestCase("https://my-server:50051", false, "https://my-server:50051", true)]
    [TestCase("my-server:50051", false, "http://my-server:50051", false)]
    [TestCase("my-server:50051", true, "https://my-server:50051", true)]
    public void Normalize_MapsSchemeToHttpOrHttps(string address, bool useTls, string expected, bool expectedRequiresTls)
    {
        var normalized = FlightAddress.Normalize(address, useTls, out var requiresTls);

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Is.EqualTo(expected));
            Assert.That(requiresTls, Is.EqualTo(expectedRequiresTls));
        });
    }

    [TestCase("ftp://my-server:50051")]
    [TestCase("grpc+unix://my-server")]
    [TestCase("grpc://")]
    [TestCase("grpc://:50051")]
    [TestCase("grpc:///query")]
    [TestCase("https://")]
    public void WithFlightAddress_UnsupportedScheme_ThrowsArgumentException(string address)
    {
        Assert.Throws<ArgumentException>(() => new SpiceClientBuilder().WithFlightAddress(address));
    }

    [TestCase("grpc://my-server:50051")]
    [TestCase("grpc+tls://my-server:50051")]
    [TestCase("http://my-server:50051")]
    public void Build_AcceptsDocumentedSchemes(string address)
    {
        // The README's Custom Connection Settings example, which threw
        // "No address resolver configured for the scheme 'grpc'" before the address was normalized.
        // Build() does not connect without an API key, so the host need not exist.
        Assert.DoesNotThrow(() =>
        {
            using var client = new SpiceClientBuilder()
                .WithFlightAddress(address)
                .WithHttpAddress("http://my-server:8090")
                .Build();
        });
    }

    [Test]
    public void Build_ReadmeTlsExample_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            using var client = new SpiceClientBuilder()
                .WithFlightAddress("grpc+tls://my-server:50051")
                .WithHttpAddress("https://my-server:8090")
                .WithTls(true)
                .Build();
        });
    }

    [TestCase("grpc")]
    [TestCase("grpc+tcp")]
    [TestCase("http")]
    public async Task GrpcAndHttpSchemes_ReachTheFlightServer(string scheme)
    {
        await using var server = await AuthTestServer.StartAsync();

        using var client = new SpiceClientBuilder().WithFlightAddress($"{scheme}://{server.HostPort}").Build();
        var query = await client.QueryAsync("SELECT 1");

        Assert.That(query.Id, Is.EqualTo("q-auth"));
    }

    // ==================== API key formats ====================

    [Test]
    public void WithApiKey_PlainKey_IsAccepted()
    {
        using var client = new SpiceClientBuilder()
            .WithFlightAddress("grpc://127.0.0.1:1")
            .WithApiKey("plain-key")
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(client.ApiKey, Is.EqualTo("plain-key"));
            Assert.That(client.AppId, Is.Null);
            Assert.That(client.UseBearerAuth, Is.True);
        });
    }

    [Test]
    public void WithApiKey_CloudKey_SetsAppId()
    {
        var builder = new SpiceClientBuilder().WithApiKey("app|secret");

        // Build() would handshake against Cloud, so inspect the builder's state without it.
        var client = GetClient(builder);
        Assert.Multiple(() =>
        {
            Assert.That(client.AppId, Is.EqualTo("app"));
            Assert.That(client.ApiKey, Is.EqualTo("app|secret"));
        });
    }

    [TestCase("plain-key", "http://localhost:50051", true)]
    [TestCase("app|secret", "http://localhost:50051", true)]
    [TestCase("app|secret", "grpc+tls://flight.spiceai.io:443", false)]
    [TestCase("app|secret", "https://flight.spiceai.io:443", false)]
    [TestCase("plain-key", "https://flight.spiceai.io:443", true)]
    public void UseBearerAuth_DependsOnKeyAndAddress(string apiKey, string address, bool expected)
    {
        var client = GetClient(new SpiceClientBuilder().WithFlightAddress(address).WithApiKey(apiKey));

        Assert.That(client.UseBearerAuth, Is.EqualTo(expected));
    }

    [Test]
    public void WithSpiceCloud_PlainKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SpiceClientBuilder().WithSpiceCloud("plain-key"));
    }

    [TestCase("plain-key", null)]
    [TestCase("app|secret", "app")]
    [TestCase("a|b|c", null)]
    [TestCase("|secret", null)]
    [TestCase("app|", null)]
    public void GetCloudAppId_OnlyForWellFormedCloudKeys(string apiKey, string? expected)
    {
        Assert.That(AuthHeaderBuilder.GetCloudAppId(apiKey), Is.EqualTo(expected));
    }

    [TestCase("https://flight.spiceai.io:443", true)]
    [TestCase("grpc+tls://flight.spiceai.io", true)]
    [TestCase("https://spiceai.io", true)]
    [TestCase("http://localhost:50051", false)]
    [TestCase("grpc://my-server:50051", false)]
    [TestCase("https://notspiceai.io", false)]
    [TestCase(" https://flight.spiceai.io:443 ", true)]
    [TestCase("https://flight.spiceai.io:443/", true)]
    public void IsCloudAddress_MatchesSpiceCloudHosts(string address, bool expected)
    {
        Assert.That(AuthHeaderBuilder.IsCloudAddress(address), Is.EqualTo(expected));
    }

    [TestCase("grpc+tls://private-flight.example:443", true)]
    [TestCase("https://private-flight.example:443", true)]
    [TestCase("private-flight.example:443", true)]
    [TestCase("https://private-flight.example", true)]
    [TestCase("grpc+tls://private-flight.example", true)]
    [TestCase("https://PRIVATE-FLIGHT.example:443/", true)]
    [TestCase("https://private-flight.example:8443", false)]
    [TestCase("grpc://private-flight.example:443", false)]
    [TestCase("https://other-flight.example:443", false)]
    public void IsCloudAddress_MatchesConfiguredCloudEndpointAcrossSchemeAliases(string address, bool expected)
    {
        // A configured Cloud endpoint (SPICE_FLIGHT_URL) outside spiceai.io, addressed through a scheme alias.
        // An address with no scheme picks https:// from the TLS setting.
        var isCloud = AuthHeaderBuilder.IsCloudAddress(address, useTls: true, "https://private-flight.example:443");

        Assert.That(isCloud, Is.EqualTo(expected));
    }

    [TestCase("https://private-flight.example:443", true)]
    [TestCase("grpc+tls://private-flight.example:443", true)]
    [TestCase("https://private-flight.example:8443", false)]
    public void IsCloudAddress_ConfiguredCloudEndpointWithoutPort_MatchesDefaultPort(string address, bool expected)
    {
        var isCloud = AuthHeaderBuilder.IsCloudAddress(address, useTls: false, "https://private-flight.example");

        Assert.That(isCloud, Is.EqualTo(expected));
    }

    [TestCase("plain-key")]
    [TestCase("app|plain-key")]
    public async Task ApiKey_SentAsBearerToLocalRuntime_WithoutHandshake(string apiKey)
    {
        await using var server = await AuthTestServer.StartAsync();

        using var client = new SpiceClientBuilder()
            .WithFlightAddress($"grpc://{server.HostPort}")
            .WithApiKey(apiKey)
            .Build();
        await client.QueryAsync("SELECT 1");

        Assert.Multiple(() =>
        {
            Assert.That(AuthTestFlightServer.Authorizations, Is.EqualTo(new[] { $"Bearer {apiKey}" }));
            Assert.That(AuthTestFlightServer.HandshakeCount, Is.Zero);
        });
    }

    [TestCase("plain-key")]
    [TestCase("app|plain-key")]
    public async Task ApiKey_SentAsXApiKeyOnHttpCalls(string apiKey)
    {
        await using var server = await AuthTestServer.StartAsync();

        using var client = new SpiceClientBuilder()
            .WithFlightAddress($"grpc://{server.HostPort}")
            .WithHttpAddress(server.HttpAddress)
            .WithApiKey(apiKey)
            .Build();
        var healthy = await client.IsSpiceHealthyAsync();

        Assert.Multiple(() =>
        {
            Assert.That(healthy, Is.True);
            Assert.That(AuthTestServer.HttpApiKeys, Is.EqualTo(new[] { apiKey }));
        });
    }

    [Test]
    public async Task ApiKey_NotForwardedOnHttpRedirect()
    {
        await using var server = await AuthTestServer.StartAsync();

        using var client = new SpiceClientBuilder()
            .WithFlightAddress($"grpc://{server.HostPort}")
            .WithHttpAddress(server.HttpAddress)
            .WithApiKey("plain-key")
            .Build();
        var ready = await client.IsSpiceReadyAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ready, Is.False, "the redirect is not followed");
            Assert.That(AuthTestServer.RedirectTargetHits, Is.Zero);
        });
    }

    private static SpiceClient GetClient(SpiceClientBuilder builder)
    {
        var field = typeof(SpiceClientBuilder).GetField("_spiceClient",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (SpiceClient)field!.GetValue(builder)!;
    }

    /// <summary>
    /// In-process server with a Flight endpoint (HTTP/2) that records the authorization header
    /// and handshakes it sees, plus an HTTP/1.1 /health endpoint that records X-API-Key.
    /// </summary>
    private sealed class AuthTestServer : IAsyncDisposable
    {
        public static ConcurrentQueue<string> HttpApiKeys { get; private set; } = new();

        private static int _redirectTargetHits;

        public static int RedirectTargetHits => Volatile.Read(ref _redirectTargetHits);

        private readonly WebApplication _app;
        private readonly int _flightPort;
        private readonly int _httpPort;

        private AuthTestServer(WebApplication app, int flightPort, int httpPort)
        {
            _app = app;
            _flightPort = flightPort;
            _httpPort = httpPort;
        }

        public static async Task<AuthTestServer> StartAsync()
        {
            AuthTestFlightServer.Reset();
            HttpApiKeys = new ConcurrentQueue<string>();
            Interlocked.Exchange(ref _redirectTargetHits, 0);

            var flightPort = FreePort();
            var httpPort = FreePort();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o =>
            {
                o.Listen(System.Net.IPAddress.Loopback, flightPort, lo => lo.Protocols = HttpProtocols.Http2);
                o.Listen(System.Net.IPAddress.Loopback, httpPort, lo => lo.Protocols = HttpProtocols.Http1);
            });
            builder.Services.AddGrpc().AddFlightServer<AuthTestFlightServer>();

            var app = builder.Build();
            app.MapFlightEndpoint();
            app.MapGet("/health", (HttpContext ctx) =>
            {
                HttpApiKeys.Enqueue(ctx.Request.Headers["X-API-Key"].ToString());
                return "ok";
            });
            // /v1/ready redirects elsewhere, so tests can check the key is not forwarded.
            app.MapGet("/v1/ready", () => Results.Redirect("/redirect-target"));
            app.MapGet("/redirect-target", () =>
            {
                Interlocked.Increment(ref _redirectTargetHits);
                return "ready";
            });
            await app.StartAsync().ConfigureAwait(false);

            return new AuthTestServer(app, flightPort, httpPort);
        }

        public string HostPort => $"127.0.0.1:{_flightPort}";

        public string HttpAddress => $"http://127.0.0.1:{_httpPort}";

        private static int FreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class AuthTestFlightServer : FlightServer
    {
        // Static: the gRPC host activates a new instance per call.
        public static ConcurrentQueue<string> Authorizations { get; private set; } = new();

        private static int _handshakeCount;

        public static int HandshakeCount => Volatile.Read(ref _handshakeCount);

        public static void Reset()
        {
            Authorizations = new ConcurrentQueue<string>();
            Interlocked.Exchange(ref _handshakeCount, 0);
        }

        public override Task Handshake(IAsyncStreamReader<FlightHandshakeRequest> requestStream,
            IAsyncStreamWriter<FlightHandshakeResponse> responseStream, ServerCallContext context)
        {
            Interlocked.Increment(ref _handshakeCount);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "handshake not expected"));
        }

        public override Task DoAction(FlightAction request, IAsyncStreamWriter<FlightResult> responseStream, ServerCallContext context)
        {
            var authorization = context.RequestHeaders.GetValue("authorization");
            if (authorization != null)
            {
                Authorizations.Enqueue(authorization);
            }

            if (request.Type != AsyncQueryActions.Submit)
            {
                throw new RpcException(new Status(StatusCode.Unimplemented, request.Type));
            }

            var body = Encoding.UTF8.GetBytes("""{"query_id":"q-auth","status":"PENDING"}""");
            return responseStream.WriteAsync(new FlightResult(body));
        }
    }
}
