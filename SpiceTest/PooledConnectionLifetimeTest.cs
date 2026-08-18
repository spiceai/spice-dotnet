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

using System.Net.Http;
using System.Reflection;
using NUnit.Framework;
using Spice.Http;

namespace SpiceTest;

/// <summary>
/// Regression coverage for setting <see cref="SocketsHttpHandler.PooledConnectionLifetime"/>
/// on the mTLS-configured HTTP handler, so a long-lived connection re-resolves DNS instead of
/// getting stuck on a stale IP behind a load balancer.
/// </summary>
[TestFixture]
public class PooledConnectionLifetimeTest
{
    private static readonly TimeSpan ExpectedLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reaches into SpiceHttpClient's private HttpClient field, then into HttpMessageInvoker's
    /// private handler field, to inspect the SocketsHttpHandler actually wired up. There is no
    /// public surface for this — the fix's whole point is an internal transport setting.
    /// </summary>
    private static SocketsHttpHandler? GetSocketsHandler(SpiceHttpClient spiceHttpClient)
    {
        var httpClientField = typeof(SpiceHttpClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(httpClientField, Is.Not.Null, "SpiceHttpClient._httpClient field should exist");
        var httpClient = (HttpClient)httpClientField!.GetValue(spiceHttpClient)!;

        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(handlerField, Is.Not.Null, "HttpMessageInvoker._handler field should exist");
        return handlerField!.GetValue(httpClient) as SocketsHttpHandler;
    }

    [Test]
    public void Test_PooledConnectionLifetime_SetWhenClientCertificateConfigured()
    {
        using var pki = new MtlsTest.TestPki();
        using var httpClient = new SpiceHttpClient(
            "https://127.0.0.1:1", appId: null, apiKey: null, userAgent: null,
            tlsClientCertFile: pki.ClientCertFile, tlsClientKeyFile: pki.ClientKeyFile, tlsRootCertFile: pki.CaCertFile);

        var handler = GetSocketsHandler(httpClient);

        Assert.That(handler, Is.Not.Null, "expected a SocketsHttpHandler when mTLS is configured");
        Assert.That(handler!.PooledConnectionLifetime, Is.EqualTo(ExpectedLifetime));
    }

    [Test]
    public void Test_PooledConnectionLifetime_SetWhenOnlyRootCertificateConfigured()
    {
        using var pki = new MtlsTest.TestPki();
        using var httpClient = new SpiceHttpClient(
            "https://127.0.0.1:1", appId: null, apiKey: null, userAgent: null,
            tlsClientCertFile: null, tlsClientKeyFile: null, tlsRootCertFile: pki.CaCertFile);

        var handler = GetSocketsHandler(httpClient);

        Assert.That(handler, Is.Not.Null, "expected a SocketsHttpHandler when a custom CA is configured");
        Assert.That(handler!.PooledConnectionLifetime, Is.EqualTo(ExpectedLifetime));
    }

    [Test]
    public void Test_PooledConnectionLifetime_NotAppliedWithoutTlsConfiguration()
    {
        using var httpClient = new SpiceHttpClient("http://127.0.0.1:1", appId: null, apiKey: null, userAgent: null);

        var handler = GetSocketsHandler(httpClient);

        Assert.That(handler, Is.Null,
            "a plain HttpClient() shouldn't expose a SocketsHttpHandler directly — this SDK only sets " +
            "PooledConnectionLifetime on the handler it builds for mTLS/custom-CA configurations");
    }
}
