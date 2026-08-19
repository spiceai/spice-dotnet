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

using NUnit.Framework;

namespace SpiceTest;

/// <summary>
/// Unit tests for the netstandard2.0 <c>ArgumentException.ThrowIfNullOrWhiteSpace</c> polyfill.
/// </summary>
/// <remarks>
/// These pin the polyfill to the same exception-type split as the in-box .NET 8+ API:
/// <see cref="ArgumentNullException"/> for a null argument, <see cref="ArgumentException"/>
/// (and not the null subtype) for an empty or whitespace-only one.
/// </remarks>
[TestFixture]
public class ThrowHelperTest
{
    [Test]
    public void ThrowIfNullOrWhiteSpace_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => System.ThrowHelper.ThrowIfNullOrWhiteSpace(null, "arg"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ThrowIfNullOrWhiteSpace_EmptyOrWhitespace_ThrowsArgumentExceptionNotArgumentNullException(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => System.ThrowHelper.ThrowIfNullOrWhiteSpace(value, "arg"));
        Assert.That(ex, Is.Not.InstanceOf<ArgumentNullException>());
    }

    [Test]
    public void ThrowIfNullOrWhiteSpace_NonEmptyValue_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => System.ThrowHelper.ThrowIfNullOrWhiteSpace("value", "arg"));
    }
}
