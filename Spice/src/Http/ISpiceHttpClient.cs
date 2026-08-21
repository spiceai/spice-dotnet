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

using Spice.Datasets;
using Spice.Nsql;
using Spice.Query;
using Spice.Search;

namespace Spice.Http;

/// <summary>
/// Interface for HTTP operations against Spice runtime.
/// </summary>
public interface ISpiceHttpClient : IDisposable
{
    /// <summary>
    /// Runs the query against the HTTP endpoint (for future use).
    /// </summary>
    /// <param name="sql">SQL to be executed against Spice</param>
    /// <returns>A task representing asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task<string> QueryAsync(string sql);

    /// <summary>
    /// Refreshes a dataset in the Spice runtime.
    /// </summary>
    /// <param name="datasetName">The name of the dataset to refresh</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when datasetName is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task RefreshDatasetAsync(string datasetName);

    /// <summary>
    /// Refreshes a dataset in the Spice runtime, overriding the dataset's configured
    /// refresh settings for this refresh only.
    /// </summary>
    /// <param name="datasetName">The name of the dataset to refresh</param>
    /// <param name="options">Overrides for this refresh, or null to use the dataset configuration</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when datasetName is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task RefreshDatasetAsync(string datasetName, RefreshOptions? options);

    /// <summary>
    /// Checks whether the Spice runtime is healthy by calling the <c>/health</c> endpoint.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the probe</param>
    /// <returns>A task that resolves to true when the runtime reports healthy, false otherwise</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled</exception>
    Task<bool> IsSpiceHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the Spice runtime is ready to serve queries by calling the <c>/v1/ready</c> endpoint.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the probe</param>
    /// <returns>A task that resolves to true when the runtime reports ready, false otherwise</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled</exception>
    Task<bool> IsSpiceReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a vector, keyword, or hybrid search against datasets with an embedding column.
    /// </summary>
    /// <param name="request">The search to run</param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The matches, ordered by descending score</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="System.ArgumentException">Thrown when the search text is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the synchronous queries currently running, by calling <c>GET /v1/sql/active</c>.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The running queries, empty when none are running</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task<IReadOnlyList<ActiveQuery>> ListActiveQueriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a running synchronous query by ID, by calling <c>POST /v1/sql/{id}/cancel</c>.
    /// </summary>
    /// <param name="queryId">The query ID, from <see cref="ListActiveQueriesAsync"/></param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when queryId is null, empty, or not a valid UUID</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task CancelActiveQueryAsync(string queryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a natural-language query by having the runtime's configured LLM generate SQL,
    /// then running it, via the <c>/v1/nsql</c> endpoint.
    /// </summary>
    /// <param name="request">The natural-language query to answer</param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The generated SQL alongside the rows it returned</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="System.ArgumentException">Thrown when the query text is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task<NsqlResponse> NsqlAsync(NsqlRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates a natural-language query into SQL without running it, via the
    /// <c>/v1/nsql</c> endpoint.
    /// </summary>
    /// <param name="request">The natural-language query to translate</param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The generated SQL</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="System.ArgumentException">Thrown when the query text is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    Task<string> NsqlGenerateSqlAsync(NsqlRequest request, CancellationToken cancellationToken = default);
}
