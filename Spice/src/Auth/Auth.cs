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
using System.Text;

namespace Spice.Auth;

internal static class AuthHeaderBuilder
{
    /// <summary>
    /// Builds the header a Spice OSS runtime secured with an API key accepts:
    /// <c>Authorization: Bearer &lt;key&gt;</c>.
    /// </summary>
    public static AuthenticationHeaderValue BearerAuth(string apiKey)
    {
        return new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Returns the app ID of a well-formed Spice Cloud key (<c>&lt;appId&gt;|&lt;secret&gt;</c>), or
    /// null when the key is a plain key, as configured on a Spice OSS runtime.
    /// </summary>
    public static string? GetCloudAppId(string apiKey)
    {
        var parts = apiKey.Split('|');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        return parts[0];
    }

    /// <summary>
    /// Reports whether a Flight address points at Spice Cloud: the configured Cloud Flight
    /// address, <c>spiceai.io</c>, or any subdomain of it.
    /// </summary>
    /// <remarks>
    /// Both addresses are compared in their normalized form (see <see cref="Common.FlightAddress.Normalize"/>),
    /// so scheme aliases such as <c>grpc+tls://</c> and <c>https://</c> for the same endpoint match.
    /// </remarks>
    /// <param name="address">The Flight address</param>
    /// <param name="useTls">Whether TLS was requested, used for an address with no scheme</param>
    public static bool IsCloudAddress(string address, bool useTls = false)
    {
        return IsCloudAddress(address, useTls, Config.SpiceDefaultConfigCloud.FlightAddress);
    }

    /// <summary>
    /// Reports whether a Flight address points at Spice Cloud, given the configured Cloud Flight address.
    /// </summary>
    internal static bool IsCloudAddress(string address, bool useTls, string cloudFlightAddress)
    {
        // Match FlightAddress.Normalize, which trims before connecting, so auth follows the destination.
        address = address.Trim();
        if (string.Equals(NormalizeForComparison(address, useTls),
                NormalizeForComparison(cloudFlightAddress, true),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Read the host the client will actually connect to, so user info such as
        // "spiceai.io:password@localhost" can't make a local address look like Cloud.
        string host;
        try
        {
            var normalized = Common.FlightAddress.Normalize(address, useTls, out _);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                return false;
            }

            host = uri.IdnHost;
        }
        catch (ArgumentException)
        {
            return false;
        }

        host = host.TrimEnd('.').ToLowerInvariant();
        return host == "spiceai.io" || host.EndsWith(".spiceai.io", StringComparison.Ordinal);
    }

    private static string NormalizeForComparison(string address, bool useTls)
    {
        try
        {
            var normalized = Common.FlightAddress.Normalize(address, useTls, out _);
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                // Uri.Port fills in the scheme's default port, so ":443" and no port compare equal.
                return $"{uri.Scheme}://{uri.IdnHost.TrimEnd('.')}:{uri.Port}";
            }

            return normalized.TrimEnd('/');
        }
        catch (ArgumentException)
        {
            return address.Trim().TrimEnd('/');
        }
    }

    public static AuthenticationHeaderValue BasicAuth(string appId, string apiKey)
    {
        return AuthenticationHeaderValue.Parse("Basic " +
                                               Convert.ToBase64String(Encoding
                                                   .UTF8
                                                   .GetBytes($"{appId}:{apiKey}")));
    }
}