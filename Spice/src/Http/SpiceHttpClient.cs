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

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Spice.Auth;
using Spice.Common;
using Spice.Datasets;
using Spice.Query;
using Spice.Search;

namespace Spice.Http;

/// <summary>
/// HTTP client for Spice runtime operations.
/// </summary>
internal class SpiceHttpClient : ISpiceHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly string _httpAddress;
    private bool _disposed;

    internal SpiceHttpClient(string httpAddress, string? appId, string? apiKey, string? userAgent,
        string? tlsClientCertFile = null, string? tlsClientKeyFile = null, string? tlsRootCertFile = null)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(httpAddress);
#else
        ThrowHelper.ThrowIfNullOrWhiteSpace(httpAddress, nameof(httpAddress));
#endif

        _httpAddress = httpAddress;

#if NET8_0_OR_GREATER
        if (tlsClientCertFile != null || tlsRootCertFile != null)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            if (tlsClientCertFile != null && tlsClientKeyFile != null)
            {
                var clientCert = ClientCertificateLoader.LoadForClientAuth(tlsClientCertFile, tlsClientKeyFile);
                handler.SslOptions.ClientCertificates =
                    new System.Security.Cryptography.X509Certificates.X509Certificate2Collection { clientCert };
            }
            if (tlsRootCertFile != null)
            {
#pragma warning disable SYSLIB0057
                var caCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(tlsRootCertFile);
#pragma warning restore SYSLIB0057
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                    if (cert == null || chain == null) return false;
                    chain.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(caCert);
                    return chain.Build(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert));
                };
            }
            _httpClient = new HttpClient(handler);
        }
        else
#endif
        {
            _httpClient = new HttpClient();
        }

        // Set authorization if credentials provided
        if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthHeaderBuilder.BasicAuth(appId!, apiKey!);
        }

        // Set user agent
        if (!string.IsNullOrEmpty(userAgent))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
        }
    }

    /// <summary>
    /// Runs the query against the HTTP endpoint (for future use).
    /// </summary>
    /// <param name="sql">SQL to be executed against Spice</param>
    /// <returns>A task representing asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task<string> QueryAsync(string sql)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        ThrowHelper.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
#endif
        throw new NotImplementedException("HTTP query endpoint not yet implemented");
    }

    /// <summary>
    /// Refreshes a dataset in the Spice runtime.
    /// </summary>
    /// <param name="datasetName">The name of the dataset to refresh</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when datasetName is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task RefreshDatasetAsync(string datasetName) => RefreshDatasetAsync(datasetName, null);

    /// <summary>
    /// Refreshes a dataset in the Spice runtime, overriding the dataset's configured
    /// refresh settings for this refresh only.
    /// </summary>
    /// <param name="datasetName">The name of the dataset to refresh</param>
    /// <param name="options">Overrides for this refresh, or null to use the dataset configuration</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when datasetName is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public async Task RefreshDatasetAsync(string datasetName, RefreshOptions? options)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetName);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        ThrowHelper.ThrowIfNullOrWhiteSpace(datasetName, nameof(datasetName));
#endif

        var url = $"{_httpAddress}/v1/datasets/{datasetName}/acceleration/refresh";
        using var content = new StringContent(options?.ToJson() ?? "{}", Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Checks whether the Spice runtime is healthy by calling the <c>/health</c> endpoint.
    /// The endpoint is unauthenticated and returns 200 with a body of "ok" when the runtime is up.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the probe</param>
    /// <returns>A task that resolves to true when the runtime reports healthy, false otherwise</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled</exception>
    public Task<bool> IsSpiceHealthyAsync(CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        return ProbeAsync($"{_httpAddress}/health", "ok", cancellationToken);
    }

    /// <summary>
    /// Checks whether the Spice runtime is ready to serve queries by calling the <c>/v1/ready</c> endpoint.
    /// The runtime returns 200 with a body of "ready" once every component has loaded, and 503 until then.
    /// On Spice.ai Cloud this endpoint is authenticated and requires an API key on the client.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the probe</param>
    /// <returns>A task that resolves to true when the runtime reports ready, false otherwise</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled</exception>
    public Task<bool> IsSpiceReadyAsync(CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        return ProbeAsync($"{_httpAddress}/v1/ready", "ready", cancellationToken);
    }

    /// <summary>
    /// Issues a probe request and reports whether the runtime returned success with the expected body.
    /// A probe never surfaces a transport failure as an exception — an unreachable or unhealthy runtime
    /// is a false result, which is the state the caller is asking about. Cancellation requested by the
    /// caller is propagated, so a probe can participate in a wider timeout.
    /// </summary>
    /// <param name="url">Absolute URL of the endpoint to probe</param>
    /// <param name="expectedBody">Body the runtime returns when the probe passes, compared case-insensitively</param>
    /// <param name="cancellationToken">Token used to cancel the probe</param>
    private async Task<bool> ProbeAsync(string url, string expectedBody, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

#if NET8_0_OR_GREATER
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            // Match the body exactly rather than by substring: /v1/ready answers "not ready"
            // when the runtime is still loading, which contains the success token.
            return string.Equals(body.Trim(), expectedBody, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The request timed out rather than being cancelled by the caller.
            return false;
        }
        catch (HttpRequestException)
        {
            // The runtime is unreachable, which is the answer the probe is looking for.
            return false;
        }
    }

    /// <summary>
    /// Options used to serialize search requests and deserialize search responses.
    /// </summary>
    private static readonly JsonSerializerOptions SearchJsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    /// <summary>
    /// Runs a vector, keyword, or hybrid search against datasets with an embedding column.
    /// </summary>
    /// <param name="request">The search to run</param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The matches, ordered by descending score</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="System.ArgumentException">Thrown when the search text is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text, $"{nameof(request)}.{nameof(request.Text)}");
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        ThrowHelper.ThrowIfNull(request, nameof(request));
        ThrowHelper.ThrowIfNullOrWhiteSpace(request.Text, $"{nameof(request)}.{nameof(request.Text)}");
#endif

        var url = $"{_httpAddress}/v1/search";
        var json = JsonSerializer.Serialize(request, SearchJsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

#if NET8_0_OR_GREATER
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        // netstandard2.0 has no CancellationToken overload for ReadAsStringAsync.
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

        if (!response.IsSuccessStatusCode)
        {
            // Surface the runtime's own message — it names what the caller needs to fix.
            throw new HttpRequestException(
                $"Search failed with status {(int)response.StatusCode}: {ExtractErrorMessage(body)}");
        }

        return JsonSerializer.Deserialize<SearchResponse>(body, SearchJsonOptions) ?? new SearchResponse();
    }

    /// <summary>
    /// Lists the synchronous queries currently running, by calling <c>GET /v1/sql/active</c>.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The running queries, empty when none are running</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public async Task<IReadOnlyList<ActiveQuery>> ListActiveQueriesAsync(CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif

        var url = $"{_httpAddress}/v1/sql/active";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

#if NET8_0_OR_GREATER
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException(
                "Listing active queries failed: the configured API key does not allow listing queries, use a key with write access.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GET {url} failed with status {(int)response.StatusCode} ({response.StatusCode}): {ExtractErrorMessage(body)}");
        }

        var decoded = JsonSerializer.Deserialize<ActiveQueriesResponse>(body);
        return decoded?.Queries ?? new List<ActiveQuery>();
    }

    /// <summary>
    /// Cancels a running synchronous query by ID, by calling <c>POST /v1/sql/{id}/cancel</c>.
    /// </summary>
    /// <param name="queryId">The query ID, from <see cref="ListActiveQueriesAsync"/></param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when queryId is null, empty, or not a valid UUID</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public async Task CancelActiveQueryAsync(string queryId, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        ThrowHelper.ThrowIfNullOrWhiteSpace(queryId, nameof(queryId));
#endif

        // queryId is caller input that reaches the runtime as a URL path segment. Reject
        // anything that is not a canonical UUID here, rather than building a path from it:
        // a value like "." or ".." is unreserved and survives escaping, so a proxy or server
        // that resolves dot segments could route this POST somewhere the caller never named.
        if (!IsValidQueryId(queryId))
        {
            throw new ArgumentException(
                $"Query ID \"{queryId}\" is not a valid UUID. Use the QueryId from {nameof(ListActiveQueriesAsync)}.",
                nameof(queryId));
        }

        var url = $"{_httpAddress}/v1/sql/{Uri.EscapeDataString(queryId)}/cancel";
        using var response = await _httpClient.PostAsync(url, content: null, cancellationToken).ConfigureAwait(false);

#if NET8_0_OR_GREATER
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

        switch (response.StatusCode)
        {
            case System.Net.HttpStatusCode.OK:
                return;
            case System.Net.HttpStatusCode.BadRequest:
                throw new ArgumentException(
                    $"Query ID \"{queryId}\" is not a valid UUID. Use the QueryId from {nameof(ListActiveQueriesAsync)}.",
                    nameof(queryId));
            case System.Net.HttpStatusCode.Forbidden:
                throw new HttpRequestException(
                    "Cancelling the query failed: the configured API key does not allow cancelling queries, use a key with write access.");
            case System.Net.HttpStatusCode.NotFound:
                throw new HttpRequestException(
                    $"No active query \"{queryId}\" found: it may have already finished, or it was submitted under a different API key.");
            default:
                throw new HttpRequestException(
                    $"POST {url} failed with status {(int)response.StatusCode} ({response.StatusCode}): {ExtractErrorMessage(body)}");
        }
    }

    /// <summary>
    /// Reports whether queryId has the exact canonical hyphenated shape the runtime parses
    /// as a UUID (36 characters, lowercase or uppercase hex, no surrounding whitespace or
    /// braces) — the IDs this SDK cancels always come from <see cref="ListActiveQueriesAsync"/>,
    /// so anything looser cannot name a running query.
    /// </summary>
    private static bool IsValidQueryId(string queryId) =>
        queryId.Length == 36 && Guid.TryParseExact(queryId, "D", out _);

    /// <summary>
    /// Pulls the runtime's error message out of a failed response body, falling back to
    /// the raw body when it is not the expected shape.
    /// </summary>
    /// <param name="body">The response body</param>
    /// <returns>The message to report</returns>
    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(no response body)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through and report the body verbatim.
        }

        return body;
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SpiceHttpClient"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="SpiceHttpClient"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _httpClient?.Dispose();
        }

        _disposed = true;
    }
}
