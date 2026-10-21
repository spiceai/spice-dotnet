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
/// The state of one runtime component, as reported by <c>/v1/status</c>.
/// </summary>
/// <remarks>
/// The runtime reports these as plain strings such as <c>"Ready"</c>. A value this version of the
/// SDK does not recognize maps to <see cref="Unknown"/> rather than failing, so a newer runtime can
/// add a state without breaking an older client.
/// </remarks>
public enum ComponentStatus
{
    /// <summary>A state this version of the SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>The component is initializing and not yet ready.</summary>
    Initializing,

    /// <summary>The component is ready to accept connections.</summary>
    Ready,

    /// <summary>The component is disabled and not running.</summary>
    Disabled,

    /// <summary>An error occurred in the component.</summary>
    Error,

    /// <summary>The component is refreshing its state.</summary>
    Refreshing,

    /// <summary>The component is shutting down.</summary>
    ShuttingDown,

    /// <summary>The component is configured but not loaded yet.</summary>
    NotLoaded,
}
