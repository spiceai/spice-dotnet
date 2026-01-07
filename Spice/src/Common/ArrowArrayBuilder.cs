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

using Apache.Arrow;

namespace Spice.Common;

/// <summary>
/// Helper methods for creating single-value Arrow arrays.
/// Reduces boilerplate in array creation code.
/// </summary>
internal static class ArrowArrayBuilder
{
    /// <summary>
    /// Creates a single-value Boolean array.
    /// </summary>
    public static IArrowArray BuildBoolean(bool? value)
    {
        var builder = new BooleanArray.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value Int8 array.
    /// </summary>
    public static IArrowArray BuildInt8(sbyte? value)
    {
        var builder = new Int8Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value UInt8 array.
    /// </summary>
    public static IArrowArray BuildUInt8(byte? value)
    {
        var builder = new UInt8Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value Int16 array.
    /// </summary>
    public static IArrowArray BuildInt16(short? value)
    {
        var builder = new Int16Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value UInt16 array.
    /// </summary>
    public static IArrowArray BuildUInt16(ushort? value)
    {
        var builder = new UInt16Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value Int32 array.
    /// </summary>
    public static IArrowArray BuildInt32(int? value)
    {
        var builder = new Int32Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value UInt32 array.
    /// </summary>
    public static IArrowArray BuildUInt32(uint? value)
    {
        var builder = new UInt32Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value Int64 array.
    /// </summary>
    public static IArrowArray BuildInt64(long? value)
    {
        var builder = new Int64Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value UInt64 array.
    /// </summary>
    public static IArrowArray BuildUInt64(ulong? value)
    {
        var builder = new UInt64Array.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value Float array.
    /// </summary>
    public static IArrowArray BuildFloat(float? value)
    {
        var builder = new FloatArray.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value Double array.
    /// </summary>
    public static IArrowArray BuildDouble(double? value)
    {
        var builder = new DoubleArray.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Creates a single-value HalfFloat array.
    /// </summary>
    public static IArrowArray BuildHalfFloat(Half? value)
    {
        var builder = new HalfFloatArray.Builder();
        if (value.HasValue)
            builder.Append(value.Value);
        else
            builder.AppendNull();
        return builder.Build();
    }
#endif

    /// <summary>
    /// Creates a single-value String array.
    /// </summary>
    public static IArrowArray BuildString(string? value)
    {
        var builder = new StringArray.Builder();
        if (value != null)
            builder.Append(value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a single-value Binary array.
    /// </summary>
    public static IArrowArray BuildBinary(byte[]? value)
    {
        var builder = new BinaryArray.Builder();
        if (value != null)
            builder.Append((ReadOnlySpan<byte>)value);
        else
            builder.AppendNull();
        return builder.Build();
    }

    /// <summary>
    /// Creates a Null array with a single null value.
    /// </summary>
    public static IArrowArray BuildNull()
    {
        return new NullArray(1);
    }
}
