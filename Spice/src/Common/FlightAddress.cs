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

namespace Spice.Common;

internal static class FlightAddress
{
    /// <summary>
    /// Converts a Flight address into the http:// or https:// form that
    /// <c>GrpcChannel.ForAddress</c> accepts.
    /// </summary>
    /// <remarks>
    /// <c>grpc://</c> and <c>grpc+tcp://</c> map to <c>http://</c>, and <c>grpc+tls://</c> maps to
    /// <c>https://</c>. <c>http://</c> and <c>https://</c> pass through unchanged. An address with no
    /// scheme gets <c>https://</c> when <paramref name="useTls"/> is set and <c>http://</c> otherwise.
    /// </remarks>
    /// <param name="address">The Flight address as configured</param>
    /// <param name="useTls">Whether TLS was requested for the connection</param>
    /// <param name="schemeRequiresTls">Set when the address scheme itself requires TLS</param>
    /// <returns>The address with an http:// or https:// scheme</returns>
    /// <exception cref="ArgumentException">Thrown when the scheme is not supported or the address has no host.</exception>
    public static string Normalize(string address, bool useTls, out bool schemeRequiresTls)
    {
        var trimmed = address.Trim();
        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        string scheme;
        string rest;
        if (schemeEnd < 0)
        {
            scheme = useTls ? "https" : "http";
            rest = trimmed;
        }
        else
        {
            scheme = trimmed.Substring(0, schemeEnd).ToLowerInvariant();
            rest = trimmed.Substring(schemeEnd + 3);
        }

        if (string.IsNullOrWhiteSpace(rest))
        {
            throw new ArgumentException($"Flight address '{address}' has no host.", nameof(address));
        }

        switch (scheme)
        {
            case "http":
            case "grpc":
            case "grpc+tcp":
                schemeRequiresTls = false;
                return "http://" + rest;
            case "https":
            case "grpc+tls":
                schemeRequiresTls = true;
                return "https://" + rest;
            default:
                throw new ArgumentException(
                    $"Unsupported Flight address scheme '{scheme}' in '{address}'. Use grpc://, grpc+tls://, grpc+tcp://, http://, or https://.",
                    nameof(address));
        }
    }
}
