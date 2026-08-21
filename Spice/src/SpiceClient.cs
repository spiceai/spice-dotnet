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
using Spice.Datasets;
using Spice.Flight;
using Spice.Http;
using Spice.Nsql;
using Spice.Query;
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
    /// Runs sql against the Flight endpoint and streams the results back synchronously.
    /// </summary>
    /// <remarks>
    /// Use <see cref="QueryAsync"/> instead to submit sql for asynchronous execution on the
    /// runtime and poll for completion, which requires the runtime to be running in
    /// distributed/scheduler mode.
    /// </remarks>
    /// <returns>A task representing asynchronus operation, with a result of type <see cref="FlightClientRecordBatchStreamReader"/></returns>
    /// <param name="sql">SQL to be executed against Spice</param>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="Spice.Errors.SpiceException">Spice exception</exception>
    /// <exception cref="Grpc.Core.RpcException">gRPC exception</exception>
    public Task<FlightClientRecordBatchStreamReader> SqlAsync(string sql)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (FlightClient == null) throw new InvalidOperationException("FlightClient not initialized");

        return FlightClient.SqlAsync(sql);
    }

    /// <summary>
    /// Executes a parameterized SQL query synchronously using ADBC (Arrow Database Connectivity).
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
    /// var result = await client.SqlWithParamsAsync(
    ///     "SELECT * FROM table WHERE id = $1 AND name = $2",
    ///     123, "test");
    ///
    /// // With explicit types
    /// var result = await client.SqlWithParamsAsync(
    ///     "SELECT * FROM table WHERE id = $1 AND amount = $2",
    ///     Param.Int32(123), Param.Double(99.99));
    /// </code>
    /// </example>
    /// </summary>
    /// <remarks>
    /// Use <see cref="QueryWithParamsAsync"/> instead to submit the parameterized query for
    /// asynchronous execution on the runtime, which requires distributed/scheduler mode.
    /// </remarks>
    /// <param name="sql">SQL query with positional parameter placeholders ($1, $2, etc.)</param>
    /// <param name="parameters">The parameter values (can be plain values or Param instances)</param>
    /// <returns>A task representing the asynchronous operation, with an IArrowArrayStream result</returns>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when the client is not initialized</exception>
    public Task<IArrowArrayStream?> SqlWithParamsAsync(string sql, params object?[] parameters)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (AdbcClient == null) throw new InvalidOperationException("AdbcClient not initialized");

        return AdbcClient.SqlWithParamsAsync(sql, parameters);
    }

    /// <summary>
    /// Refreshes a dataset in the Spice runtime using the dataset's configured refresh settings.
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
    /// <example>
    /// <code>
    /// // Refresh only the recent rows, appending them to the accelerated data.
    /// await client.RefreshDatasetAsync("taxi_trips", new RefreshOptions()
    ///     .WithRefreshSql("SELECT * FROM taxi_trips WHERE tip_amount &gt; 10.0")
    ///     .WithRefreshMode(RefreshMode.Append));
    /// </code>
    /// </example>
    /// <param name="datasetName">The name of the dataset to refresh</param>
    /// <param name="options">Overrides for this refresh, or null to use the dataset configuration</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when datasetName is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task RefreshDatasetAsync(string datasetName, RefreshOptions? options)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (HttpClient == null) throw new InvalidOperationException("HttpClient not initialized");

        return HttpClient.RefreshDatasetAsync(datasetName, options);
    }

    /// <summary>
    /// Checks whether the Spice runtime is healthy, i.e. the process is up and serving HTTP.
    /// This is the liveness signal; use <see cref="IsSpiceReadyAsync"/> to find out whether the
    /// runtime has finished loading and can serve queries.
    ///
    /// <para>
    /// A probe reports false rather than throwing when the runtime is unreachable. To bound how
    /// long it waits, pass a token from a <see cref="CancellationTokenSource"/> with a timeout.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    /// if (!await client.IsSpiceHealthyAsync(cts.Token))
    /// {
    ///     // The runtime is not up yet.
    /// }
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the probe</param>
    /// <returns>A task that resolves to true when the runtime reports healthy, false otherwise</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the client is not initialized</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled</exception>
    public Task<bool> IsSpiceHealthyAsync(CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (HttpClient == null) throw new InvalidOperationException("HttpClient not initialized");

        return HttpClient.IsSpiceHealthyAsync(cancellationToken);
    }

    /// <summary>
    /// Checks whether the Spice runtime is ready to serve queries. The runtime reports ready once
    /// every component — datasets, accelerations, models — has finished loading, which makes this
    /// the check to gate application startup on.
    ///
    /// <para>
    /// On Spice.ai Cloud this endpoint is authenticated; configure an API key on the builder.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the probe</param>
    /// <returns>A task that resolves to true when the runtime reports ready, false otherwise</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the client is not initialized</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled</exception>
    public Task<bool> IsSpiceReadyAsync(CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (HttpClient == null) throw new InvalidOperationException("HttpClient not initialized");

        return HttpClient.IsSpiceReadyAsync(cancellationToken);
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

    /// <summary>
    /// Submits sql to the Spice runtime for asynchronous execution and returns a handle for
    /// polling status and retrieving results.
    /// </summary>
    /// <remarks>
    /// Async queries require the runtime to be running in distributed/scheduler mode; against
    /// a single-node runtime the runtime returns an error explaining that async queries are
    /// only available in cluster mode. Use <see cref="SqlAsync"/> for synchronous, streaming queries.
    /// </remarks>
    /// <example>
    /// <code>
    /// var query = await client.QueryAsync("SELECT * FROM taxi_trips");
    /// await query.WaitAsync();
    /// using var results = await query.GetResultsAsync();
    /// </code>
    /// </example>
    /// <param name="sql">SQL to run asynchronously</param>
    /// <param name="cancellationToken">Token to cancel the submission</param>
    /// <returns>A handle for polling status and retrieving results</returns>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when the client is not initialized</exception>
    public Task<AsyncQuery> QueryAsync(string sql, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (FlightClient == null) throw new InvalidOperationException("FlightClient not initialized");

        return FlightClient.QueryAsync(sql, null, cancellationToken);
    }

    /// <summary>
    /// Submits a parameterized query for asynchronous execution. Parameters are bound
    /// positionally ($1, $2, ...) and sent as a JSON array, so each must be a JSON-encodable value.
    /// </summary>
    /// <remarks>
    /// Async queries require the runtime to be running in distributed/scheduler mode. Use
    /// <see cref="SqlWithParamsAsync"/> for synchronous, streaming parameterized queries.
    /// </remarks>
    /// <param name="sql">SQL query with positional parameter placeholders ($1, $2, etc.)</param>
    /// <param name="parameters">The parameter values</param>
    /// <returns>A handle for polling status and retrieving results</returns>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when the client is not initialized</exception>
    public Task<AsyncQuery> QueryWithParamsAsync(string sql, params object?[] parameters)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (FlightClient == null) throw new InvalidOperationException("FlightClient not initialized");

        object? boundParameters = parameters.Length > 0 ? parameters : null;
        return FlightClient.QueryAsync(sql, boundParameters, CancellationToken.None);
    }

    /// <summary>
    /// Answers a natural-language query by having the runtime's configured LLM generate SQL,
    /// then running it.
    /// </summary>
    /// <remarks>
    /// The generated SQL is returned in <see cref="NsqlResponse.SQL"/>. The runtime executes
    /// it read-only and retries generation when the query fails to run, so a thrown exception
    /// means generation or execution failed repeatedly.
    ///
    /// <para>
    /// NSQL requires an LLM model configured in the Spicepod. See
    /// https://docs.spice.ai/features/text-to-sql for how to configure one.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = await client.NsqlAsync(new NsqlRequest("top 5 customers by revenue"));
    /// Console.WriteLine(result.SQL);
    /// </code>
    /// </example>
    /// <param name="request">The natural-language query to answer</param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The generated SQL alongside the rows it returned</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="System.ArgumentException">Thrown when the query text is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task<NsqlResponse> NsqlAsync(NsqlRequest request, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (HttpClient == null) throw new InvalidOperationException("HttpClient not initialized");

        return HttpClient.NsqlAsync(request, cancellationToken);
    }

    /// <summary>
    /// Translates a natural-language query into SQL without running it.
    /// </summary>
    /// <remarks>
    /// Use it to inspect or edit the query before running it, or to run it through
    /// <see cref="SqlAsync"/> or <see cref="SqlWithParamsAsync"/> so the results arrive as Arrow
    /// rather than decoded JSON.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sql = await client.NsqlGenerateSqlAsync(new NsqlRequest("how many orders"));
    /// var result = await client.SqlAsync(sql);
    /// </code>
    /// </example>
    /// <param name="request">The natural-language query to translate</param>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The generated SQL</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="System.ArgumentException">Thrown when the query text is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task<string> NsqlGenerateSqlAsync(NsqlRequest request, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (HttpClient == null) throw new InvalidOperationException("HttpClient not initialized");

        return HttpClient.NsqlGenerateSqlAsync(request, cancellationToken);
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