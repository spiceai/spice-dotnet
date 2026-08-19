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

using Apache.Arrow.Flight.Client;
using Apache.Arrow.Ipc;
using Spice.Adbc;
using Spice.Config;
using Spice.Flight;
using Spice.Http;
using Spice.Search;

namespace Spice;

public class SpiceClient : IDisposable
{
    /// <summary>
    /// Gets or sets the application ID. This property is internal set and can be null.
    /// </summary>
    public string? AppId { get; internal set; }

    /// <summary>
    /// Gets or sets the API key. This property is internal set and can be null.
    /// </summary>
    public string? ApiKey { get; internal set; }

    /// <summary>
    /// Gets or sets the User-Agent string. This property is internal set and can be null.
    /// </summary>
    public string? UserAgent { get; internal set; }

    /// <summary>
    /// Gets or sets the flight address. This property is internal set and defaults to local flight endpoint.
    /// </summary>
    public string FlightAddress { get; internal set; } = SpiceDefaultConfigLocal.FlightAddress;

    /// <summary>
    /// Gets or sets the HTTP address. This property is internal set and defaults to local HTTP endpoint.
    /// </summary>
    public string HttpAddress { get; internal set; } = SpiceDefaultConfigLocal.HttpAddress;

    /// <summary>
    /// Gets or sets the maximum number of retries. This property is internal set and defaults to 3.
    /// </summary>
    public int MaxRetries { get; internal set; } = 3;

    /// <summary>
    /// Gets or sets whether to use TLS for connections. This property is internal set and defaults to false for local connections.
    /// </summary>
    public bool UseTls { get; internal set; }

    /// <summary>
    /// Gets or sets the path to a PEM-encoded client certificate file for mTLS.
    /// Must be used together with <see cref="TlsClientKeyFile"/>.
    /// </summary>
    public string? TlsClientCertFile { get; internal set; }

    /// <summary>
    /// Gets or sets the path to a PEM-encoded client private key file for mTLS.
    /// Must be used together with <see cref="TlsClientCertFile"/>.
    /// </summary>
    public string? TlsClientKeyFile { get; internal set; }

    /// <summary>
    /// Gets or sets the path to a PEM-encoded CA certificate file for server verification.
    /// When set, this CA is used instead of the system certificate store.
    /// </summary>
    public string? TlsRootCertFile { get; internal set; }

    private SpiceFlightClient? FlightClient { get; set; }
    private SpiceAdbcClient? AdbcClient { get; set; }
    private SpiceHttpClient? HttpClient { get; set; }


    internal void Init()
    {
        // Validate that client cert and key are either both set or both unset
        bool hasCert = !string.IsNullOrEmpty(TlsClientCertFile);
        bool hasKey = !string.IsNullOrEmpty(TlsClientKeyFile);
        if (hasCert != hasKey)
        {
            var missing = hasCert ? nameof(TlsClientKeyFile) : nameof(TlsClientCertFile);
            throw new InvalidOperationException(
                $"Both {nameof(TlsClientCertFile)} and {nameof(TlsClientKeyFile)} must be provided together for mTLS. {missing} is missing.");
        }

        FlightClient = new SpiceFlightClient(FlightAddress, MaxRetries, AppId, ApiKey, UserAgent, UseTls, TlsClientCertFile, TlsClientKeyFile, TlsRootCertFile);
        AdbcClient = new SpiceAdbcClient(FlightAddress, MaxRetries, AppId, ApiKey, UserAgent, UseTls);
        HttpClient = new SpiceHttpClient(HttpAddress, AppId, ApiKey, UserAgent, TlsClientCertFile, TlsClientKeyFile, TlsRootCertFile);
    }

    /// <summary>
    /// Runs the query against the Flight endpoint. 
    /// </summary>
    /// <returns>A task representing asynchronus operation, with a result of type <see cref="FlightClientRecordBatchStreamReader"/></returns>
    /// <param name="sql">SQL to be executed against Spice</param>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="Spice.Errors.SpiceException">Spice exception</exception>
    /// <exception cref="Grpc.Core.RpcException">gRPC exception</exception>
    public Task<FlightClientRecordBatchStreamReader> Query(string sql)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (FlightClient == null) throw new InvalidOperationException("FlightClient not initialized");

        return FlightClient.Query(sql);
    }

    /// <summary>
    /// Executes a parameterized SQL query using ADBC (Arrow Database Connectivity).
    /// This is the recommended method for queries with user input to prevent SQL injection.
    /// Parameters should use positional placeholders ($1, $2, etc.) in the SQL query.
    /// 
    /// <para>
    /// Parameters can be:
    /// <list type="bullet">
    /// <item><description>Simple .NET values (int, string, bool, etc.) - type will be inferred</description></item>
    /// <item><description>Param instances with explicit type annotation using Param factory methods</description></item>
    /// </list>
    /// </para>
    /// 
    /// <example>
    /// <code>
    /// // With automatic type inference
    /// var result = await client.QueryWithParams(
    ///     "SELECT * FROM table WHERE id = $1 AND name = $2",
    ///     123, "test");
    /// 
    /// // With explicit types
    /// var result = await client.QueryWithParams(
    ///     "SELECT * FROM table WHERE id = $1 AND amount = $2",
    ///     Param.Int32(123), Param.Double(99.99));
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="sql">SQL query with positional parameter placeholders ($1, $2, etc.)</param>
    /// <param name="parameters">The parameter values (can be plain values or Param instances)</param>
    /// <returns>A task representing the asynchronous operation, with an IArrowArrayStream result</returns>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when the client is not initialized</exception>
    public Task<IArrowArrayStream?> QueryWithParams(string sql, params object?[] parameters)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (AdbcClient == null) throw new InvalidOperationException("AdbcClient not initialized");

        return AdbcClient.QueryWithParamsAsync(sql, parameters);
    }

    /// <summary>
    /// Refreshes a dataset in the Spice runtime.
    /// </summary>
    /// <param name="datasetName">The name of the dataset to refresh</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when datasetName is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task RefreshDatasetAsync(string datasetName)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (HttpClient == null) throw new InvalidOperationException("HttpClient not initialized");

        return HttpClient.RefreshDatasetAsync(datasetName);
    }

    /// <summary>
    /// Runs a vector, keyword, or hybrid search.
    /// </summary>
    /// <remarks>
    /// Searches datasets that have an embedding column and a loaded embedding model,
    /// returning the documents most similar to the request text. Setting
    /// <see cref="SearchRequest.Keywords"/> pre-filters the embedding column with a
    /// lexical search first, making the search hybrid.
    /// </remarks>
    /// <param name="request">The search to run</param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The matches, ordered by descending score</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="System.ArgumentException">Thrown when the search text is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (HttpClient == null) throw new InvalidOperationException("HttpClient not initialized");

        return HttpClient.SearchAsync(request, cancellationToken);
    }

    private bool _disposed;

    /// <summary>
    /// Releases all resources used by the <see cref="SpiceClient"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="SpiceClient"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            FlightClient?.Dispose();
            AdbcClient?.Dispose();
            HttpClient?.Dispose();
        }

        _disposed = true;
    }
}