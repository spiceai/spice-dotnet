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
    public static bool IsCloudAddress(string address)
    {
        // Match FlightAddress.Normalize, which trims before connecting, so auth follows the destination.
        address = address.Trim();
        if (string.Equals(address, Config.SpiceDefaultConfigCloud.FlightAddress, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var host = address;
        var schemeEnd = host.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            host = host.Substring(schemeEnd + 3);
        }

        for (var i = 0; i < host.Length; i++)
        {
            if (host[i] == ':' || host[i] == '/')
            {
                host = host.Substring(0, i);
                break;
            }
        }

        host = host.TrimEnd('.').ToLowerInvariant();
        return host == "spiceai.io" || host.EndsWith(".spiceai.io", StringComparison.Ordinal);
    }

    public static AuthenticationHeaderValue BasicAuth(string appId, string apiKey)
    {
        return AuthenticationHeaderValue.Parse("Basic " +
                                               Convert.ToBase64String(Encoding
                                                   .UTF8
                                                   .GetBytes($"{appId}:{apiKey}")));
    }
}