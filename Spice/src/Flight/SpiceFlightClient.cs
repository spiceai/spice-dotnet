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
using Polly.Retry;
using Spice.Auth;
using Spice.Common;
using Spice.Errors;

namespace Spice.Flight;

internal class SpiceFlightClient : IDisposable
{
    private readonly FlightClient _flightClient;
    private readonly GrpcChannel _channel;
    private readonly HttpClient? _httpClient;
    private readonly AsyncRetryPolicy _retryPolicy;

    private static GrpcChannelOptions GetGrpcChannelOptions(string? appId, string? apiKey, string? userAgent, bool useTls)
    {
        var options = new GrpcChannelOptions();

        if (appId == null || apiKey == null)
        {
            // For non-authenticated connections, set credentials based on TLS preference
            if (!useTls)
            {
                options.Credentials = ChannelCredentials.Insecure;
            }
#if NET8_0_OR_GREATER
            else
            {
                // Configure HttpHandler for TLS on macOS (.NET 8.0+)
                var handler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true,
                    // Force periodic connection recycling to trigger DNS re-resolution.
                    // Without this, HTTP/2 connections are kept alive indefinitely and
                    // the client can get stuck on stale IPs when backend targets change
                    // (e.g. AWS ALB target rotation).
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };
                options.HttpHandler = handler;
            }
#endif
            return options;
        }

        // Set TLS credentials for authenticated connections
        options.Credentials = useTls ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure;
        
        // Configure HttpHandler for TLS on macOS (.NET 8.0+)
        HttpMessageHandler messageHandler;
#if NET8_0_OR_GREATER
        if (useTls)
        {
            messageHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                // Force periodic connection recycling to trigger DNS re-resolution.
                // Without this, HTTP/2 connections are kept alive indefinitely and
                // the client can get stuck on stale IPs when backend targets change
                // (e.g. AWS ALB target rotation).
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
        }
        else
#endif
        {
            messageHandler = new HttpClientHandler();
        }
        
        options.HttpClient = new HttpClient(messageHandler)
        {
            DefaultRequestHeaders =
            {
                Authorization = AuthHeaderBuilder.BasicAuth(appId, apiKey)
            }
        };

        // Add user agent header using common helper
        options.HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgentHelper.BuildUserAgent(userAgent));

        return options;
    }

    private static Metadata.Entry? GetAuthToken(Metadata responseHeaders, Metadata trailers)
    {
        return responseHeaders.Get("authorization") ?? trailers.Get("authorization");
    }

    internal SpiceFlightClient(string address, int maxRetries, string? appId, string? apiKey, string? userAgent, bool useTls)
    {
        _retryPolicy = RetryPolicyFactory.CreateRpcRetryPolicy(
            maxRetries,
            (ex, ts, attempt) => RetryPolicyFactory.LogRetry("Flight", ex, ts, attempt));

        var options = GetGrpcChannelOptions(appId, apiKey, userAgent, useTls);
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

            var endpoints = flightInfo.Endpoints;
            if (endpoints.Count == 0)
            {
                throw new InvalidOperationException("Failed to get endpoint from flight info");
            }

            var stream = _flightClient.GetStream(endpoints[0].Ticket);
            return stream.ResponseStream;
        });
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