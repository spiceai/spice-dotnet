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
using Spice.Config;
using Spice.Flight;

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
    /// Gets or sets the maximum number of retries. This property is internal set and defaults to 3.
    /// </summary>
    public int MaxRetries { get; internal set; } = 3;

    private SpiceFlightClient? FlightClient { get; set; }


    internal void Init()
    {
        FlightClient = new SpiceFlightClient(FlightAddress, MaxRetries, AppId, ApiKey, UserAgent);
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
    /// Runs a parameterized query against the Flight endpoint using Flight SQL prepared statements.
    /// </summary>
    /// <returns>A task representing asynchronous operation, with a result of type <see cref="FlightClientRecordBatchStreamReader"/></returns>
    /// <param name="sql">Parameterized SQL query to be executed. Use named parameters like :param_name or $param_name</param>
    /// <param name="parameters">Dictionary of parameter names and their values</param>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.ArgumentNullException">Thrown when parameters is null</exception>
    /// <exception cref="Spice.Errors.SpiceException">Spice exception</exception>
    /// <exception cref="Grpc.Core.RpcException">gRPC exception</exception>
    public Task<FlightClientRecordBatchStreamReader> Query(string sql, IDictionary<string, object> parameters)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        if (FlightClient == null) throw new InvalidOperationException("FlightClient not initialized");

        return FlightClient.Query(sql, parameters);
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
        }

        _disposed = true;
    }
}