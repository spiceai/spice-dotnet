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
using System.Security.Cryptography.X509Certificates;
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

    private static GrpcChannelOptions GetGrpcChannelOptions(string? appId, string? apiKey, string? userAgent, bool useTls, string? tlsClientCertFile = null, string? tlsClientKeyFile = null, string? tlsRootCertFile = null)
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
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };
                if (tlsClientCertFile != null && tlsClientKeyFile != null)
                {
                    var clientCert = ClientCertificateLoader.LoadForClientAuth(tlsClientCertFile, tlsClientKeyFile);
                    handler.SslOptions.ClientCertificates = new X509Certificate2Collection { clientCert };
                }
                if (tlsRootCertFile != null)
                {
                    #pragma warning disable SYSLIB0057
                    var caCert = new X509Certificate2(tlsRootCertFile);
#pragma warning restore SYSLIB0057
                    handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                    {
                        if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                        if (cert == null || chain == null) return false;
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(caCert);
                        return chain.Build(new X509Certificate2(cert));
                    };
                }
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
            var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            if (tlsClientCertFile != null && tlsClientKeyFile != null)
            {
                var clientCert = ClientCertificateLoader.LoadForClientAuth(tlsClientCertFile, tlsClientKeyFile);
                handler.SslOptions.ClientCertificates = new X509Certificate2Collection { clientCert };
            }
            if (tlsRootCertFile != null)
            {
                #pragma warning disable SYSLIB0057
                    var caCert = new X509Certificate2(tlsRootCertFile);
#pragma warning restore SYSLIB0057
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                    if (cert == null || chain == null) return false;
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(caCert);
                    return chain.Build(new X509Certificate2(cert));
                };
            }
            messageHandler = handler;
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

    internal SpiceFlightClient(string address, int maxRetries, string? appId, string? apiKey, string? userAgent, bool useTls, string? tlsClientCertFile = null, string? tlsClientKeyFile = null, string? tlsRootCertFile = null)
    {
        _retryPolicy = RetryPolicyFactory.CreateRpcRetryPolicy(
            maxRetries,
            (ex, ts, attempt) => RetryPolicyFactory.LogRetry("Flight", ex, ts, attempt));

        var options = GetGrpcChannelOptions(appId, apiKey, userAgent, useTls, tlsClientCertFile, tlsClientKeyFile, tlsRootCertFile);
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

        Metadata headers;
        try
        {
            headers = await stream.ResponseHeadersAsync.ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            throw new SpiceException(
                SpiceStatus.FailedToAuthenticate,
                $"Failed to authenticate: {DescribeRpcFailure(ex)}",
                ex);
        }

        // Trailers are only readable once the call has completed. Reading them
        // eagerly throws InvalidOperationException on a handshake that failed,
        // which masks the gRPC status that says what actually went wrong.
        Exception? failure = null;
        var token = headers.Get("authorization");
        if (token == null)
        {
            token = TryGetTrailerToken(stream, out failure);
        }

        if (token == null || _httpClient == null)
        {
            var reason = DescribeAuthFailure(failure);

            throw new SpiceException(
                SpiceStatus.FailedToAuthenticate,
                $"Failed to authenticate: {reason} Check that the API key is valid for this endpoint.",
                failure);
        }

        _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(token.Value);
    }

    /// <summary>
    /// Reads the authorization token from the handshake trailers, if the call
    /// completed far enough for them to be available.
    /// </summary>
    /// <param name="stream">The handshake call</param>
    /// <param name="failure">The failure that prevented reading the trailers, if any</param>
    /// <returns>The token entry, or null</returns>
    internal static Metadata.Entry? TryGetTrailerToken(
        AsyncDuplexStreamingCall<FlightHandshakeRequest, FlightHandshakeResponse> stream,
        out Exception? failure)
    {
        failure = null;
        try
        {
            return stream.GetTrailers().Get("authorization");
        }
        catch (RpcException ex)
        {
            failure = ex;
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // The handshake never completed, so there are no trailers to read.
            failure = ex;
            return null;
        }
    }

    /// <summary>
    /// Renders a gRPC failure as something a caller can act on.
    /// </summary>
    /// <param name="ex">The gRPC exception</param>
    /// <returns>A short description of the failure</returns>
    private static string DescribeRpcFailure(RpcException ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Status.Detail) ? ex.Message : ex.Status.Detail;
        return $"{ex.StatusCode} - {detail}.";
    }

    /// <summary>
    /// Renders why the auth token could not be obtained as something a caller can act on.
    /// </summary>
    /// <param name="failure">The failure captured while trying to read the token, if any</param>
    /// <returns>A short description of the failure</returns>
    internal static string DescribeAuthFailure(Exception? failure)
    {
        return failure switch
        {
            null => "the runtime returned no authorization token.",
            RpcException rpcEx => DescribeRpcFailure(rpcEx),
            _ => "the handshake did not complete before trailers could be read."
        };
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