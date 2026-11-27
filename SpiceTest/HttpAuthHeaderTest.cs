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
using System.Text;
using Spice.Http;
using Spice.Search;

namespace SpiceTest;

/// <summary>
/// The HTTP client must send the configured key as X-API-Key, including Spice Cloud keys
/// (appId|secret) sent to a Spice Cloud address. Spice runtimes and the Cloud data plane accept
/// only X-API-Key or Authorization: Bearer on HTTP; 0.5.0 sent Basic for Cloud keys and every
/// authenticated HTTP call failed with 401.
/// </summary>
public class HttpAuthHeaderTest
{
    private const string CloudHttpAddress = "https://us-east-1-dev-aws-data.spiceai.io";
    private static readonly string[] SearchDatasets = { "customer" };
    private static readonly string[] ExpectedPaths =
        { "/v1/ready", "/v1/status", "/v1/datasets/orders/acceleration/refresh", "/v1/search" };

    [TestCase("app|secret", CloudHttpAddress)]
    [TestCase("app|secret", "https://data.spiceai.io")]
    [TestCase("app|secret", "http://localhost:8090")]
    [TestCase("plain-key", CloudHttpAddress)]
    [TestCase("plain-key", "http://localhost:8090")]
    public async Task AuthenticatedHttpCalls_SendXApiKey_AndNoAuthorization(string apiKey, string httpAddress)
    {
        var handler = new ApiKeyOnlyRuntime(apiKey);
        using var client = new SpiceHttpClient(httpAddress, apiKey, userAgent: null, handler);

        var ready = await client.IsSpiceReadyAsync();
        var status = await client.GetRuntimeStatusAsync();
        await client.RefreshDatasetAsync("orders");
        var search = await client.SearchAsync(new SearchRequest("furiously") { Datasets = SearchDatasets, Limit = 5 });

        Assert.Multiple(() =>
        {
            Assert.That(ready, Is.True);
            Assert.That(status, Is.Empty);
            Assert.That(search.Results, Is.Empty);
            Assert.That(handler.Requests.Select(r => r.Path),
                Is.EqualTo(ExpectedPaths));
            foreach (var request in handler.Requests)
            {
                Assert.That(request.ApiKey, Is.EqualTo(apiKey), request.Path);
                Assert.That(request.Authorization, Is.Null, $"{request.Path} must not send an Authorization header");
            }
        });
    }

    [Test]
    public async Task NoApiKey_SendsNoCredentialHeaders()
    {
        var handler = new ApiKeyOnlyRuntime(expectedKey: null);
        using var client = new SpiceHttpClient(CloudHttpAddress, apiKey: null, userAgent: null, handler);

        Assert.That(await client.IsSpiceReadyAsync(), Is.True);
        var request = handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.ApiKey, Is.Null);
            Assert.That(request.Authorization, Is.Null);
        });
    }

    private sealed record SeenRequest(string Path, string? ApiKey, string? Authorization);

    /// <summary>
    /// Answers like a Spice runtime secured with an API key: X-API-Key or Bearer is checked,
    /// anything else is 401.
    /// </summary>
    private sealed class ApiKeyOnlyRuntime : HttpMessageHandler
    {
        private readonly string? _expectedKey;

        public ApiKeyOnlyRuntime(string? expectedKey) => _expectedKey = expectedKey;

        public ConcurrentQueue<SeenRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var apiKey = request.Headers.TryGetValues("X-API-Key", out var keys) ? keys.Single() : null;
            var authorization = request.Headers.Authorization?.ToString();
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(new SeenRequest(path, apiKey, authorization));

            var presented = apiKey ?? (request.Headers.Authorization?.Scheme == "Bearer" ? request.Headers.Authorization.Parameter : null);
            if (_expectedKey != null && presented != _expectedKey)
            {
                return Task.FromResult(Respond(HttpStatusCode.Unauthorized, "unauthorized"));
            }

            return Task.FromResult(path switch
            {
                "/v1/ready" => Respond(HttpStatusCode.OK, "ready"),
                "/v1/status" => Respond(HttpStatusCode.OK, "[]"),
                "/v1/search" => Respond(HttpStatusCode.OK, """{"results":[],"duration_ms":1}"""),
                _ => Respond(HttpStatusCode.Created, "{}"),
            });
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
