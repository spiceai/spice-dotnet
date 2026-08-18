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

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;
using Spice;
using Spice.Search;

namespace SpiceTest;

/// <summary>
/// Exercises WithTlsClientCertificate/WithTlsRootCertificate end-to-end against a raw
/// TLS server that requires a client certificate, proving the SDK actually drives a
/// mutual-TLS handshake rather than just recording file paths.
/// </summary>
[TestFixture]
public class MtlsTest
{
    /// <summary>
    /// A minimal self-signed CA plus a server and a client leaf certificate, generated
    /// fresh per test so no fixture files need to live in the repo. Internal (rather than
    /// private) so PooledConnectionLifetimeTest can reuse it for its own TLS configuration.
    /// </summary>
    internal sealed class TestPki : IDisposable
    {
        public X509Certificate2 CaCert { get; }
        public X509Certificate2 ServerCert { get; }
        public string ClientCertFile { get; }
        public string ClientKeyFile { get; }
        public string CaCertFile { get; }

        private readonly string _tempDir;
        // A single fixed validity window shared by the CA and every leaf certificate:
        // computing "now + 1 hour" separately for each cert let the leaf's notAfter land
        // a few milliseconds past the CA's, which CertificateRequest.Create rejects.
        private readonly DateTimeOffset _notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        private readonly DateTimeOffset _notAfter = DateTimeOffset.UtcNow.AddHours(1);

        public TestPki()
        {
            _tempDir = Directory.CreateTempSubdirectory("spice-mtls-test-").FullName;

            using var caKey = RSA.Create(2048);
            var caRequest = new CertificateRequest(
                "CN=spice-dotnet-test-ca", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature, true));
            CaCert = caRequest.CreateSelfSigned(_notBefore, _notAfter);

            ServerCert = IssueLeaf("CN=127.0.0.1", isServer: true);
            var (clientCertPem, clientKeyPem) = IssueLeafPem("CN=spice-dotnet-test-client");

            CaCertFile = Path.Combine(_tempDir, "ca.pem");
            File.WriteAllText(CaCertFile, CaCert.ExportCertificatePem());

            ClientCertFile = Path.Combine(_tempDir, "client-cert.pem");
            File.WriteAllText(ClientCertFile, clientCertPem);

            ClientKeyFile = Path.Combine(_tempDir, "client-key.pem");
            File.WriteAllText(ClientKeyFile, clientKeyPem);
        }

        private X509Certificate2 IssueLeaf(string subject, bool isServer)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid(isServer ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
            if (isServer)
            {
                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(sanBuilder.Build());
            }

            var serial = new byte[8];
            RandomNumberGenerator.Fill(serial);
            using var publicOnly = request.Create(CaCert, _notBefore, _notAfter, serial);
            using var withEphemeralKey = publicOnly.CopyWithPrivateKey(key);

            // A cert whose private key is still the ephemeral CNG key CopyWithPrivateKey
            // attached fails SslStream server-side use on Windows (Schannel needs the key
            // in an importable PKCS#12 form; OpenSSL-backed macOS/Linux don't care) — round
            // -trip through PKCS#12 so the certificate works as a server certificate on all
            // three platforms this SDK targets.
#pragma warning disable SYSLIB0057
            return new X509Certificate2(
                withEphemeralKey.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
        }

        private (string CertPem, string KeyPem) IssueLeafPem(string subject)
        {
            using var cert = IssueLeaf(subject, isServer: false);
            using var key = cert.GetRSAPrivateKey()!;
            return (cert.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
        }

        public void Dispose()
        {
            CaCert.Dispose();
            ServerCert.Dispose();
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; leaving a temp dir behind isn't worth failing the test.
            }
        }
    }

    /// <summary>
    /// A raw TLS server that accepts exactly one connection, optionally requiring a
    /// client certificate, and answers with a canned /v1/search response.
    /// </summary>
    private sealed class MtlsTestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serverTask;

        public int Port { get; }

        public MtlsTestServer(X509Certificate2 serverCert, X509Certificate2 caCert, bool requireClientCert)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = AcceptOnceAsync(serverCert, caCert, requireClientCert);
        }

        private async Task AcceptOnceAsync(X509Certificate2 serverCert, X509Certificate2 caCert, bool requireClientCert)
        {
            using var tcpClient = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            await using var networkStream = tcpClient.GetStream();
            await using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, certificate, _, _) =>
                {
                    // .NET still invokes this callback with a null certificate when the
                    // client presents none, even though ClientCertificateRequired is false
                    // below — only reject a missing certificate when one was required.
                    if (certificate == null) return !requireClientCert;
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(caCert);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build(new X509Certificate2(certificate));
                });

            var options = new SslServerAuthenticationOptions
            {
                ServerCertificate = serverCert,
                ClientCertificateRequired = requireClientCert,
                EnabledSslProtocols = SslProtocols.Tls12,
            };

            // A client that omits its certificate (or presents one the callback rejects)
            // fails the handshake here; the exception simply ends this one-shot server task,
            // which is exactly what the "handshake fails" test expects.
            await sslStream.AuthenticateAsServerAsync(options).ConfigureAwait(false);

            // Read the request headers, then drain exactly its declared Content-Length body
            // bytes. Closing the socket while the OS still has unread incoming data queued
            // makes it send an abortive RST instead of a graceful FIN, which the client can
            // observe as "response ended prematurely" even though the response was fully
            // written — so the body must be fully drained before this method returns.
            var buffer = new byte[8192];
            var received = 0;
            int headerEnd;
            while (true)
            {
                var read = await sslStream.ReadAsync(buffer.AsMemory(received)).ConfigureAwait(false);
                if (read == 0) return;
                received += read;
                headerEnd = IndexOfHeaderTerminator(buffer, received);
                if (headerEnd >= 0) break;
            }

            var headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            var contentLength = ParseContentLength(headerText);
            var bodyAlreadyRead = received - (headerEnd + 4);
            var remainingBody = contentLength - bodyAlreadyRead;
            while (remainingBody > 0)
            {
                var read = await sslStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remainingBody))).ConfigureAwait(false);
                if (read == 0) break;
                remainingBody -= read;
            }

            const string body = "{\"results\":[],\"duration_ms\":1}";
            var response =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                "Connection: close\r\n\r\n" +
                body;
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await sslStream.WriteAsync(responseBytes).ConfigureAwait(false);
            await sslStream.FlushAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch
            {
                // The handshake-failure tests expect this task to fault; nothing further to do.
            }
        }

        private static int IndexOfHeaderTerminator(byte[] buffer, int length)
        {
            for (var i = 0; i + 3 < length; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    return i;
                }
            }
            return -1;
        }

        private static int ParseContentLength(string headerText)
        {
            foreach (var line in headerText.Split("\r\n"))
            {
                var separator = line.IndexOf(':');
                if (separator > 0 && string.Equals(line[..separator].Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    return int.Parse(line[(separator + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            return 0;
        }
    }

    [Test]
    public async Task Test_MutualTls_HandshakeSucceeds_WithClientCertificate()
    {
        using var pki = new TestPki();
        await using var server = new MtlsTestServer(pki.ServerCert, pki.CaCert, requireClientCert: true);

        using var client = new SpiceClientBuilder()
            .WithHttpAddress($"https://127.0.0.1:{server.Port}")
            .WithTlsClientCertificate(pki.ClientCertFile, pki.ClientKeyFile)
            .WithTlsRootCertificate(pki.CaCertFile)
            .Build();

        var response = await client.SearchAsync(new SearchRequest("test"));

        Assert.That(response.Results, Is.Empty);
    }

    [Test]
    public void Test_MutualTls_HandshakeFails_WithoutClientCertificate()
    {
        using var pki = new TestPki();

        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await using var server = new MtlsTestServer(pki.ServerCert, pki.CaCert, requireClientCert: true);

            using var client = new SpiceClientBuilder()
                .WithHttpAddress($"https://127.0.0.1:{server.Port}")
                .WithTlsRootCertificate(pki.CaCertFile)
                .Build();

            await client.SearchAsync(new SearchRequest("test"));
        });
    }

    [Test]
    public async Task Test_Tls_Succeeds_WithoutClientCertificate_WhenServerDoesNotRequireOne()
    {
        using var pki = new TestPki();
        await using var server = new MtlsTestServer(pki.ServerCert, pki.CaCert, requireClientCert: false);

        using var client = new SpiceClientBuilder()
            .WithHttpAddress($"https://127.0.0.1:{server.Port}")
            .WithTlsRootCertificate(pki.CaCertFile)
            .Build();

        var response = await client.SearchAsync(new SearchRequest("test"));

        Assert.That(response.Results, Is.Empty);
    }

    [Test]
    public void Test_Build_ThrowsWhenClientKeyFileMissingButCertFileProvided()
    {
        using var pki = new TestPki();

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var client = new SpiceClientBuilder()
                .WithTlsClientCertificate(pki.ClientCertFile, "")
                .Build();
        });

        Assert.That(ex!.Message, Does.Contain("TlsClientKeyFile"));
    }

    [Test]
    public void Test_Build_ThrowsWhenClientCertificateFileDoesNotExist()
    {
        // The exact exception type is a platform/BCL implementation detail (a missing
        // directory surfaces differently than a missing file) — what matters is that a
        // nonexistent certificate file fails loudly rather than silently skipping mTLS.
        Assert.Catch(() =>
        {
            using var client = new SpiceClientBuilder()
                .WithTlsClientCertificate("/nonexistent/cert.pem", "/nonexistent/key.pem")
                .Build();
        });
    }

    [Test]
    public void Test_Build_ThrowsWhenRootCertificateFileDoesNotExist()
    {
        Assert.Catch(() =>
        {
            using var client = new SpiceClientBuilder()
                .WithTlsRootCertificate("/nonexistent/ca.pem")
                .Build();
        });
    }
}
