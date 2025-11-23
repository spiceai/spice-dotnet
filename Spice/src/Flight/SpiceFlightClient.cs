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
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Client;
using Grpc.Core;
using Grpc.Net.Client;
using Polly;
using Polly.Retry;
using Spice.Auth;
using Spice.Config;
using Spice.Errors;

namespace Spice.Flight;

internal class SpiceFlightClient : IDisposable
{
    private readonly FlightClient _flightClient;
    private readonly GrpcChannel _channel;
    private readonly HttpClient? _httpClient;
    private readonly AsyncRetryPolicy _retryPolicy;

    private static GrpcChannelOptions GetGrpcChannelOptions(string? appId, string? apiKey, string? userAgent)
    {
        var options = new GrpcChannelOptions();

        if (appId == null || apiKey == null) return options;


        options.Credentials = ChannelCredentials.SecureSsl;
        options.HttpClient = new HttpClient
        {
            DefaultRequestHeaders =
            {
                Authorization = AuthHeaderBuilder.BasicAuth(appId, apiKey)
            }
        };

        // Prepend the user-supplied user agent (if any) with the Spice user agent
        var uaString = string.IsNullOrEmpty(userAgent) ? SpiceUserAgent.agent() : $"{userAgent} {SpiceUserAgent.agent()}";
        options.HttpClient.DefaultRequestHeaders.Add("User-Agent", uaString);

        return options;
    }

    private static Metadata.Entry? GetAuthToken(Metadata responseHeaders, Metadata trailers)
    {
        return responseHeaders.Get("authorization") ?? trailers.Get("authorization");
    }

    internal SpiceFlightClient(string address, int maxRetries, string? appId, string? apiKey, string? userAgent)
    {
        _retryPolicy = Policy.Handle<RpcException>(ex =>
                ex.Status.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Aborted
                    or StatusCode.Internal or StatusCode.Unknown)
            .WaitAndRetryAsync(retryCount: maxRetries,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(retryAttempt * 1.5),
                onRetry: (_, timespan, retryAttempt, _) =>
                {
                    Console.WriteLine(
                        $"Request failed. Waiting {timespan} before next retry. Retry attempt {retryAttempt}");
                });

        var options = GetGrpcChannelOptions(appId, apiKey, userAgent);
        _httpClient = options.HttpClient;

        _channel = GrpcChannel.ForAddress(address, options);
        _flightClient = new FlightClient(_channel);

        if (appId != null && apiKey != null)
        {
            AuthenticateAsync().GetAwaiter().GetResult();
        }
    }

    private async Task AuthenticateAsync()
    {
        var stream = _flightClient.Handshake();

        var headers = await stream.ResponseHeadersAsync.ConfigureAwait(false);
        var token = GetAuthToken(headers, stream.GetTrailers());
        
        if (token == null || _httpClient == null)
        {
            throw new SpiceException(SpiceStatus.FailedToAuthenticate, "Failed to authenticate");
        }

        _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(token.Value);
    }

    internal async Task<FlightClientRecordBatchStreamReader> Query(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            throw new ArgumentException("No SQL provided");
        }

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var descriptor = FlightDescriptor.CreateCommandDescriptor(sql);
            var flightInfo = await _flightClient.GetInfo(descriptor);

            var endpoint = flightInfo.Endpoints.FirstOrDefault();
            if (endpoint == null) throw new Exception("Failed to get endpoint");

            var stream = _flightClient.GetStream(endpoint.Ticket);
            return stream.ResponseStream;
        });
    }

    internal async Task<FlightClientRecordBatchStreamReader> Query(string sql, IDictionary<string, object> parameters)
    {
        if (string.IsNullOrEmpty(sql))
        {
            throw new ArgumentException("No SQL provided");
        }

        if (parameters == null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            // Create a parameterized query command using Flight SQL
            var commandBytes = SerializeParameterizedQuery(sql, parameters);
            var descriptor = FlightDescriptor.CreateCommandDescriptor(commandBytes);
            var flightInfo = await _flightClient.GetInfo(descriptor);

            var endpoint = flightInfo.Endpoints.FirstOrDefault();
            if (endpoint == null) throw new Exception("Failed to get endpoint");

            var stream = _flightClient.GetStream(endpoint.Ticket);
            return stream.ResponseStream;
        });
    }

    private static byte[] SerializeParameterizedQuery(string sql, IDictionary<string, object> parameters)
    {
        // Use Arrow Flight SQL protocol for parameterized queries
        // Format: SQL followed by parameter binding information
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        
        // Write SQL query
        writer.Write(sql);
        
        // Write parameter count
        writer.Write(parameters.Count);
        
        // Write each parameter name and value
        foreach (var param in parameters)
        {
            writer.Write(param.Key);
            WriteParameterValue(writer, param.Value);
        }
        
        return memoryStream.ToArray();
    }

    private static void WriteParameterValue(BinaryWriter writer, object value)
    {
        // Write type indicator and value
        switch (value)
        {
            case null:
                writer.Write((byte)0); // NULL
                break;
            case string s:
                writer.Write((byte)1); // STRING
                writer.Write(s);
                break;
            case int i:
                writer.Write((byte)2); // INT32
                writer.Write(i);
                break;
            case long l:
                writer.Write((byte)3); // INT64
                writer.Write(l);
                break;
            case double d:
                writer.Write((byte)4); // DOUBLE
                writer.Write(d);
                break;
            case float f:
                writer.Write((byte)5); // FLOAT
                writer.Write(f);
                break;
            case bool b:
                writer.Write((byte)6); // BOOLEAN
                writer.Write(b);
                break;
            case DateTime dt:
                writer.Write((byte)7); // TIMESTAMP
                writer.Write(dt.ToBinary());
                break;
            case byte[] bytes:
                writer.Write((byte)8); // BINARY
                writer.Write(bytes.Length);
                writer.Write(bytes);
                break;
            default:
                throw new ArgumentException($"Unsupported parameter type: {value.GetType().Name}");
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // FlightClient doesn't implement IDisposable, but its underlying channel does
            _channel?.Dispose();
            _httpClient?.Dispose();
        }

        _disposed = true;
    }
}