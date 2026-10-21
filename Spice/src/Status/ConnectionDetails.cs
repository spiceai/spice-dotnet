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

namespace Spice.Status;

/// <summary>
/// One of the runtime's network-facing components — <c>http</c>, <c>flight</c>, <c>metrics</c>, or
/// <c>opentelemetry</c> — with the address it listens on and its current state.
/// </summary>
public class ConnectionDetails
{
    /// <summary>
    /// The component's name, such as <c>"http"</c> or <c>"flight"</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The address the component listens on, such as <c>"127.0.0.1:50051"</c>, or <c>"N/A"</c> when
    /// it is disabled.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The component's state.
    /// </summary>
    public ComponentStatus Status { get; set; }

    /// <summary>
    /// The state exactly as the runtime reported it. Useful when <see cref="Status"/> is
    /// <see cref="ComponentStatus.Unknown"/>.
    /// </summary>
    public string StatusText { get; set; } = string.Empty;
}
