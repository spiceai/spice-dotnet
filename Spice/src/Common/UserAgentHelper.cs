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

/// <summary>
/// Helper class for building user agent strings consistently across clients.
/// </summary>
internal static class UserAgentHelper
{
    /// <summary>
    /// Builds a user agent string that combines a custom user agent with the Spice SDK user agent.
    /// </summary>
    /// <param name="customUserAgent">Optional custom user agent to prepend.</param>
    /// <returns>The combined user agent string.</returns>
    public static string BuildUserAgent(string? customUserAgent)
    {
        var sdkAgent = Config.SpiceUserAgent.agent();
        return string.IsNullOrEmpty(customUserAgent)
            ? sdkAgent
            : $"{customUserAgent} {sdkAgent}";
    }
}
