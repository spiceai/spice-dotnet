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

using Spice.Config;

namespace Spice;

public class SpiceClientBuilder
{
    private readonly SpiceClient _spiceClient = new();

    /// <summary>
    /// Sets the client's flight address.
    /// </summary>
    /// <param name="flightAddress">Flight address the client will query</param>
    /// <returns>The current instance of <see cref="SpiceClientBuilder"/> for method chaining.</returns>
    /// <exception cref="System.ArgumentException">Thrown when flightAddress is null or whitespace.</exception>
    public SpiceClientBuilder WithFlightAddress(string flightAddress)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(flightAddress);
#else
        ThrowHelper.ThrowIfNullOrWhiteSpace(flightAddress, nameof(flightAddress));
#endif
        _spiceClient.FlightAddress = flightAddress;
        return this;
    }

    /// <summary>
    /// Sets the client's Api Key.
    /// </summary>
    /// <param name="apiKey">The Spice Cloud api key</param>
    /// <returns>The current instance of <see cref="SpiceClientBuilder"/> for method chaining.</returns>
    /// <exception cref="System.ArgumentException">Thrown when the apiKey is in wrong format.</exception>
    public SpiceClientBuilder WithApiKey(string apiKey)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
#else
        ThrowHelper.ThrowIfNullOrWhiteSpace(apiKey, nameof(apiKey));
#endif
        
        var parts = apiKey.Split('|');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new ArgumentException("apiKey must be in format 'appId|key'", nameof(apiKey));
        }

        _spiceClient.AppId = parts[0];
        _spiceClient.ApiKey = apiKey;
        
        return this;
    }

    /// <summary>
    /// Sets the client's flight address to default Spice Cloud address. 
    /// </summary>
    /// <returns>The current instance of <see cref="SpiceClientBuilder"/> for method chaining.</returns>
    public SpiceClientBuilder WithSpiceCloud()
    {
        _spiceClient.FlightAddress = SpiceDefaultConfigCloud.FlightAddress;
        return this;
    }

    /// <summary>
    /// Sets the client's max retries count. 
    /// </summary>
    /// <param name="maxRetries">Max retries for request</param>
    /// <returns>The current instance of <see cref="SpiceClientBuilder"/> for method chaining.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">Thrown when maxRetries is negative.</exception>
    public SpiceClientBuilder WithMaxRetries(int maxRetries)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
#else
        ThrowHelper.ThrowIfNegative(maxRetries, nameof(maxRetries));
#endif
        _spiceClient.MaxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// Sets the client's user agent.
    /// </summary>
    /// <param name="userAgent">User agent string</param>
    /// <returns>The current instance of <see cref="SpiceClientBuilder"/> for method chaining.</returns>
    /// <exception cref="System.ArgumentException">Thrown when userAgent is null or whitespace.</exception>
    public SpiceClientBuilder WithUserAgent(string userAgent)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
#else
        ThrowHelper.ThrowIfNullOrWhiteSpace(userAgent, nameof(userAgent));
#endif
        _spiceClient.UserAgent = userAgent;
        return this;
    }

    /// <summary>
    /// Initiates <see cref="SpiceClient" /> with provided parameters.
    /// </summary>
    /// <returns>The current instance of <see cref="SpiceClient"/> for method chaining.</returns>
    /// <exception cref="Spice.Errors.SpiceException">Spice exception</exception>
    /// <exception cref="Grpc.Core.RpcException">gRPC exception</exception>
    public SpiceClient Build()
    {
        _spiceClient.Init();
        return _spiceClient;
    }
}