/*
Copyright 2026 The Spice.ai OSS Authors

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

using System.Reflection;
using NUnit.Framework;
using Spice.Adbc;

namespace SpiceTest;

/// <summary>
/// The ADBC client behind SqlWithParamsAsync must get the same TLS material as the Flight
/// and HTTP clients; without it a parameterized query fails certificate verification against
/// a private-CA runtime while every other call on the same client succeeds.
/// </summary>
[TestFixture]
public class AdbcTlsOptionsTest
{
    private const string RootCerts = "adbc.flight.sql.client_option.tls_root_certs";
    private const string CertChain = "adbc.flight.sql.client_option.mtls_cert_chain";
    private const string PrivateKey = "adbc.flight.sql.client_option.mtls_private_key";

    [Test]
    public void Test_RootCertificate_IsPassedToDriver()
    {
        using var pki = new MtlsTest.TestPki();
        using var client = new SpiceAdbcClient("grpc+tls://127.0.0.1:50051", 0, null, null, null, true,
            tlsRootCertFile: pki.CaCertFile);

        var options = client.BuildDatabaseParameters();

        Assert.That(options[RootCerts], Is.EqualTo(File.ReadAllText(pki.CaCertFile)));
        Assert.That(options.ContainsKey(CertChain), Is.False);
        Assert.That(options.ContainsKey(PrivateKey), Is.False);
    }

    [Test]
    public void Test_ClientCertificate_IsPassedToDriver()
    {
        using var pki = new MtlsTest.TestPki();
        using var client = new SpiceAdbcClient("grpc+tls://127.0.0.1:50051", 0, null, null, null, true,
            tlsClientCertFile: pki.ClientCertFile, tlsClientKeyFile: pki.ClientKeyFile, tlsRootCertFile: pki.CaCertFile);

        var options = client.BuildDatabaseParameters();

        Assert.That(options[RootCerts], Is.EqualTo(File.ReadAllText(pki.CaCertFile)));
        Assert.That(options[CertChain], Is.EqualTo(File.ReadAllText(pki.ClientCertFile)));
        Assert.That(options[PrivateKey], Is.EqualTo(File.ReadAllText(pki.ClientKeyFile)));
    }

    [Test]
    public void Test_NoTlsFiles_AddsNoTlsOptions()
    {
        using var client = new SpiceAdbcClient("grpc://127.0.0.1:50051", 0, null, null, null, false);

        var options = client.BuildDatabaseParameters();

        Assert.That(options.ContainsKey(RootCerts), Is.False);
        Assert.That(options.ContainsKey(CertChain), Is.False);
        Assert.That(options.ContainsKey(PrivateKey), Is.False);
    }

    [Test]
    public void Test_SpiceClient_PassesTlsFilesToAdbcClient()
    {
        using var pki = new MtlsTest.TestPki();
        using var client = new Spice.SpiceClientBuilder()
            .WithFlightAddress("grpc+tls://127.0.0.1:50051")
            .WithTlsRootCertificate(pki.CaCertFile)
            .WithTlsClientCertificate(pki.ClientCertFile, pki.ClientKeyFile)
            .Build();

        // AdbcClient is private to SpiceClient; read it the way TypeInferenceTest reaches internals.
        var adbcClient = (SpiceAdbcClient)typeof(Spice.SpiceClient)
            .GetProperty("AdbcClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client)!;
        var options = adbcClient.BuildDatabaseParameters();

        Assert.That(options[RootCerts], Is.EqualTo(File.ReadAllText(pki.CaCertFile)));
        Assert.That(options[CertChain], Is.EqualTo(File.ReadAllText(pki.ClientCertFile)));
        Assert.That(options[PrivateKey], Is.EqualTo(File.ReadAllText(pki.ClientKeyFile)));
    }
}
