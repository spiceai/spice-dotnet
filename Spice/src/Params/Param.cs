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

using Apache.Arrow.Types;

namespace Spice.Params;

/// <summary>
/// Represents a query parameter with an optional explicit Arrow type.
/// If Type is null, the type will be inferred from the value.
/// 
/// Use the static factory methods to create parameters with explicit types,
/// or pass simple .NET values directly to SqlWithParams for automatic type inference.
/// 
/// Example usage:
/// <code>
/// // With type inference
/// await client.SqlWithParams("SELECT * FROM table WHERE id = $1", 123);
/// 
/// // With explicit type
/// await client.SqlWithParams("SELECT * FROM table WHERE id = $1", Param.Int32(123));
/// </code>
/// </summary>
public sealed class Param
{
    /// <summary>
    /// The value of the parameter.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// The explicit Arrow type for this parameter. If null, type will be inferred.
    /// </summary>
    public IArrowType? Type { get; }

    /// <summary>
    /// Creates a new parameter with the specified value and optional explicit type.
    /// </summary>
    /// <param name="value">The parameter value</param>
    /// <param name="type">The explicit Arrow type (null for type inference)</param>
    public Param(object? value, IArrowType? type = null)
    {
        Value = value;
        Type = type;
    }

    /// <summary>
    /// Returns true if this parameter has an explicit type annotation.
    /// </summary>
    public bool HasExplicitType => Type != null;

    // ============ Integer Types ============

    /// <summary>Creates a parameter with Int8 (sbyte) type.</summary>
    public static Param Int8(sbyte value) => new(value, new Int8Type());

    /// <summary>Creates a parameter with UInt8 (byte) type.</summary>
    public static Param UInt8(byte value) => new(value, new UInt8Type());

    /// <summary>Creates a parameter with Int16 (short) type.</summary>
    public static Param Int16(short value) => new(value, new Int16Type());

    /// <summary>Creates a parameter with UInt16 (ushort) type.</summary>
    public static Param UInt16(ushort value) => new(value, new UInt16Type());

    /// <summary>Creates a parameter with Int32 (int) type.</summary>
    public static Param Int32(int value) => new(value, new Int32Type());

    /// <summary>Creates a parameter with UInt32 (uint) type.</summary>
    public static Param UInt32(uint value) => new(value, new UInt32Type());

    /// <summary>Creates a parameter with Int64 (long) type.</summary>
    public static Param Int64(long value) => new(value, new Int64Type());

    /// <summary>Creates a parameter with UInt64 (ulong) type.</summary>
    public static Param UInt64(ulong value) => new(value, new UInt64Type());

    // ============ Floating Point Types ============

    /// <summary>Creates a parameter with Float (float) type.</summary>
    public static Param Float(float value) => new(value, new FloatType());

    /// <summary>Creates a parameter with Double (double) type.</summary>
    public static Param Double(double value) => new(value, new DoubleType());

#if NET8_0_OR_GREATER
    /// <summary>Creates a parameter with HalfFloat (Half) type.</summary>
    public static Param HalfFloat(Half value) => new(value, new HalfFloatType());
#endif

    // ============ String and Binary Types ============

    /// <summary>Creates a parameter with String (Utf8) type.</summary>
    public static Param String(string value) => new(value, new StringType());

    /// <summary>Creates a parameter with LargeString type for strings larger than 2GB.</summary>
    public static Param LargeString(string value) => new(value, new LargeStringType());

    /// <summary>Creates a parameter with Binary type.</summary>
    public static Param Binary(byte[] value) => new(value, new BinaryType());

    /// <summary>Creates a parameter with LargeBinary type for data larger than 2GB.</summary>
    public static Param LargeBinary(byte[] value) => new(value, new LargeBinaryType());

    /// <summary>Creates a parameter with FixedSizeBinary type.</summary>
    public static Param FixedSizeBinary(byte[] value, int byteWidth) => new(value, new FixedSizeBinaryType(byteWidth));

    // ============ Boolean Type ============

    /// <summary>Creates a parameter with Boolean type.</summary>
    public static Param Boolean(bool value) => new(value, new BooleanType());

    // ============ Date/Time Types ============

    /// <summary>Creates a parameter with Date32 type (days since epoch).</summary>
    public static Param Date32(int daysSinceEpoch) => new(daysSinceEpoch, new Date32Type());

    /// <summary>Creates a parameter with Date32 type from a DateTime.</summary>
    public static Param Date32(DateTime date)
    {
        var daysSinceEpoch = (int)(date.Date - new DateTime(1970, 1, 1)).TotalDays;
        return new Param(daysSinceEpoch, new Date32Type());
    }

#if NET6_0_OR_GREATER
    /// <summary>Creates a parameter with Date32 type from a DateOnly.</summary>
    public static Param Date32(DateOnly date)
    {
        var daysSinceEpoch = date.DayNumber - new DateOnly(1970, 1, 1).DayNumber;
        return new Param(daysSinceEpoch, new Date32Type());
    }
#endif

    /// <summary>Creates a parameter with Date64 type (milliseconds since epoch).</summary>
    public static Param Date64(long millisecondsSinceEpoch) => new(millisecondsSinceEpoch, new Date64Type());

    /// <summary>Creates a parameter with Date64 type from a DateTime.</summary>
    public static Param Date64(DateTime date)
    {
        var msSinceEpoch = (long)(date - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        return new Param(msSinceEpoch, new Date64Type());
    }

    /// <summary>Creates a parameter with Time32 type.</summary>
    public static Param Time32(int value, TimeUnit unit)
    {
        if (unit != TimeUnit.Second && unit != TimeUnit.Millisecond)
        {
            throw new ArgumentException("Time32 only supports Second or Millisecond units", nameof(unit));
        }
        return new Param(value, new Time32Type(unit));
    }

    /// <summary>Creates a parameter with Time64 type.</summary>
    public static Param Time64(long value, TimeUnit unit)
    {
        if (unit != TimeUnit.Microsecond && unit != TimeUnit.Nanosecond)
        {
            throw new ArgumentException("Time64 only supports Microsecond or Nanosecond units", nameof(unit));
        }
        return new Param(value, new Time64Type(unit));
    }

#if NET6_0_OR_GREATER
    /// <summary>Creates a parameter with Time64 type from a TimeOnly.</summary>
    public static Param Time64(TimeOnly time, TimeUnit unit = TimeUnit.Microsecond)
    {
        long value = unit switch
        {
            TimeUnit.Microsecond => time.Ticks / 10,
            TimeUnit.Nanosecond => time.Ticks * 100,
            _ => throw new ArgumentException("Time64 only supports Microsecond or Nanosecond units", nameof(unit))
        };
        return new Param(value, new Time64Type(unit));
    }
#endif

    /// <summary>Creates a parameter with Timestamp type.</summary>
    public static Param Timestamp(long value, TimeUnit unit, string? timezone = null) =>
        new(value, new TimestampType(unit, timezone ?? TimeZoneInfo.Utc.Id));

    /// <summary>Creates a parameter with Timestamp type from a DateTime.</summary>
    public static Param Timestamp(DateTime dateTime, TimeUnit unit = TimeUnit.Microsecond, string? timezone = null)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ticks = (dateTime.ToUniversalTime() - epoch).Ticks;
        long value = unit switch
        {
            TimeUnit.Second => ticks / TimeSpan.TicksPerSecond,
            TimeUnit.Millisecond => ticks / TimeSpan.TicksPerMillisecond,
            TimeUnit.Microsecond => ticks / 10,
            TimeUnit.Nanosecond => ticks * 100,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        return new Param(value, new TimestampType(unit, timezone ?? TimeZoneInfo.Utc.Id));
    }

    /// <summary>Creates a parameter with Timestamp type from a DateTimeOffset.</summary>
    public static Param Timestamp(DateTimeOffset dateTimeOffset, TimeUnit unit = TimeUnit.Microsecond, string? timezone = null)
    {
        var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ticks = (dateTimeOffset - epoch).Ticks;
        long value = unit switch
        {
            TimeUnit.Second => ticks / TimeSpan.TicksPerSecond,
            TimeUnit.Millisecond => ticks / TimeSpan.TicksPerMillisecond,
            TimeUnit.Microsecond => ticks / 10,
            TimeUnit.Nanosecond => ticks * 100,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        return new Param(value, new TimestampType(unit, timezone ?? TimeZoneInfo.Utc.Id));
    }

    /// <summary>Creates a parameter with Duration type (stored as Int64 microseconds).</summary>
    /// <remarks>Duration is stored as Int64 since Arrow C# doesn't expose DurationType constructor directly.</remarks>
    public static Param Duration(long valueMicroseconds) => new(valueMicroseconds, new Int64Type());

    /// <summary>Creates a parameter with Duration type from a TimeSpan.</summary>
    /// <remarks>Duration is stored as Int64 microseconds since Arrow C# doesn't expose DurationType constructor directly.</remarks>
    public static Param Duration(TimeSpan timeSpan)
    {
        // Store as microseconds (standard duration unit for high precision)
        long valueMicroseconds = timeSpan.Ticks / 10;
        return new Param(valueMicroseconds, new Int64Type());
    }

    // ============ Interval Types ============

    /// <summary>Creates a parameter with YearMonth interval type.</summary>
    public static Param MonthInterval(int months) => new(months, new IntervalType(IntervalUnit.YearMonth));

    /// <summary>Creates a parameter with DayTime interval type.</summary>
    public static Param DayTimeInterval(int days, int milliseconds)
    {
        // Pack days and milliseconds into a long (days in upper 32 bits, ms in lower 32 bits)
        long value = ((long)days << 32) | (uint)milliseconds;
        return new Param(value, new IntervalType(IntervalUnit.DayTime));
    }

    /// <summary>Creates a parameter with MonthDayNano interval type.</summary>
    public static Param MonthDayNanoInterval(int months, int days, long nanoseconds)
    {
        // Return as a tuple that can be unpacked during binding
        return new Param((months, days, nanoseconds), new IntervalType(IntervalUnit.MonthDayNanosecond));
    }

    // ============ Decimal Types ============

    /// <summary>Creates a parameter with Decimal128 type.</summary>
    public static Param Decimal128(decimal value, int precision = 38, int scale = 10) =>
        new(value, new Decimal128Type(precision, scale));

    /// <summary>Creates a parameter with Decimal256 type.</summary>
    public static Param Decimal256(decimal value, int precision = 76, int scale = 10) =>
        new(value, new Decimal256Type(precision, scale));

    // ============ Null Type ============

    /// <summary>Creates a null parameter.</summary>
    public static Param Null() => new(null, new NullType());

    // ============ Generic Constructor ============

    /// <summary>
    /// Creates a parameter with automatic type inference.
    /// Use this when you want to let the SDK infer the Arrow type from the .NET type.
    /// </summary>
    public static Param From(object? value) => new(value, null);

    /// <summary>
    /// Creates a parameter with an explicit Arrow type.
    /// Use this for advanced scenarios where you need precise control over the Arrow type.
    /// </summary>
    public static Param WithType(object? value, IArrowType type) => new(value, type);
}
