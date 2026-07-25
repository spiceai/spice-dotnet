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
}
