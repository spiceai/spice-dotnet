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
using Spice.Params;
using NUnit.Framework;

namespace SpiceTest;

/// <summary>
/// Unit tests for Param class edge cases and advanced scenarios.
/// </summary>
[TestFixture]
public class ParamEdgeCasesTest
{
    // ============ Constructor Tests ============

    [Test]
    public void Test_Param_Constructor_WithNullValue_AndNullType()
    {
        var param = new Param(null, null);

        Assert.That(param.Value, Is.Null);
        Assert.That(param.Type, Is.Null);
        Assert.That(param.HasExplicitType, Is.False);
    }

    [Test]
    public void Test_Param_Constructor_WithValue_AndNullType()
    {
        var param = new Param(42, null);

        Assert.That(param.Value, Is.EqualTo(42));
        Assert.That(param.Type, Is.Null);
        Assert.That(param.HasExplicitType, Is.False);
    }

    [Test]
    public void Test_Param_Constructor_WithNullValue_AndExplicitType()
    {
        var param = new Param(null, new Int32Type());

        Assert.That(param.Value, Is.Null);
        Assert.That(param.Type, Is.TypeOf<Int32Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    // ============ String Edge Cases ============

    [Test]
    public void Test_Param_String_EmptyString()
    {
        var param = Param.String("");
        Assert.That(param.Value, Is.EqualTo(""));
        Assert.That(param.Type, Is.TypeOf<StringType>());
    }

    [Test]
    public void Test_Param_String_Whitespace()
    {
        var param = Param.String("   ");
        Assert.That(param.Value, Is.EqualTo("   "));
        Assert.That(param.Type, Is.TypeOf<StringType>());
    }

    [Test]
    public void Test_Param_String_Unicode()
    {
        var param = Param.String("こんにちは世界 🌍");
        Assert.That(param.Value, Is.EqualTo("こんにちは世界 🌍"));
        Assert.That(param.Type, Is.TypeOf<StringType>());
    }

    [Test]
    public void Test_Param_String_NewLines()
    {
        var param = Param.String("line1\nline2\rline3\r\nline4");
        Assert.That(param.Value, Is.EqualTo("line1\nline2\rline3\r\nline4"));
    }

    [Test]
    public void Test_Param_String_SpecialCharacters()
    {
        var param = Param.String("SELECT * FROM table WHERE name = 'O''Brien'; --");
        Assert.That(param.Value, Is.EqualTo("SELECT * FROM table WHERE name = 'O''Brien'; --"));
    }

    // ============ Binary Edge Cases ============

    [Test]
    public void Test_Param_Binary_EmptyArray()
    {
        var param = Param.Binary(Array.Empty<byte>());
        Assert.That(param.Value, Is.EqualTo(Array.Empty<byte>()));
        Assert.That(param.Type, Is.TypeOf<BinaryType>());
    }

    [Test]
    public void Test_Param_Binary_LargeArray()
    {
        var largeBytes = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeBytes);
        
        var param = Param.Binary(largeBytes);
        Assert.That(param.Value, Is.EqualTo(largeBytes));
    }

    // ============ Numeric Edge Cases ============

    [Test]
    public void Test_Param_Int32_MinValue()
    {
        var param = Param.Int32(int.MinValue);
        Assert.That(param.Value, Is.EqualTo(int.MinValue));
    }

    [Test]
    public void Test_Param_Int32_MaxValue()
    {
        var param = Param.Int32(int.MaxValue);
        Assert.That(param.Value, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void Test_Param_Int64_MinValue()
    {
        var param = Param.Int64(long.MinValue);
        Assert.That(param.Value, Is.EqualTo(long.MinValue));
    }

    [Test]
    public void Test_Param_Int64_MaxValue()
    {
        var param = Param.Int64(long.MaxValue);
        Assert.That(param.Value, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void Test_Param_Float_SpecialValues()
    {
        Assert.That(Param.Float(float.NaN).Value, Is.EqualTo(float.NaN));
        Assert.That(Param.Float(float.PositiveInfinity).Value, Is.EqualTo(float.PositiveInfinity));
        Assert.That(Param.Float(float.NegativeInfinity).Value, Is.EqualTo(float.NegativeInfinity));
        Assert.That(Param.Float(float.Epsilon).Value, Is.EqualTo(float.Epsilon));
    }

    [Test]
    public void Test_Param_Double_SpecialValues()
    {
        Assert.That(Param.Double(double.NaN).Value, Is.EqualTo(double.NaN));
        Assert.That(Param.Double(double.PositiveInfinity).Value, Is.EqualTo(double.PositiveInfinity));
        Assert.That(Param.Double(double.NegativeInfinity).Value, Is.EqualTo(double.NegativeInfinity));
        Assert.That(Param.Double(double.Epsilon).Value, Is.EqualTo(double.Epsilon));
    }

    [Test]
    public void Test_Param_Decimal128_MaxPrecision()
    {
        var param = Param.Decimal128(12345678901234567890.123456789m, 38, 9);
        Assert.That(param.Type, Is.TypeOf<Decimal128Type>());
        
        var decType = (Decimal128Type)param.Type!;
        Assert.That(decType.Precision, Is.EqualTo(38));
        Assert.That(decType.Scale, Is.EqualTo(9));
    }

    [Test]
    public void Test_Param_Decimal128_NegativeValue()
    {
        var param = Param.Decimal128(-99999.99999m);
        Assert.That(param.Value, Is.EqualTo(-99999.99999m));
    }

    [Test]
    public void Test_Param_Decimal256_LargePrecision()
    {
        var param = Param.Decimal256(123.456m, 76, 20);
        Assert.That(param.Type, Is.TypeOf<Decimal256Type>());
        
        var decType = (Decimal256Type)param.Type!;
        Assert.That(decType.Precision, Is.EqualTo(76));
        Assert.That(decType.Scale, Is.EqualTo(20));
    }

    // ============ Date/Time Edge Cases ============

    [Test]
    public void Test_Param_Date32_EpochDate()
    {
        var param = Param.Date32(new DateTime(1970, 1, 1));
        Assert.That(param.Value, Is.EqualTo(0));
    }

    [Test]
    public void Test_Param_Date32_BeforeEpoch()
    {
        var param = Param.Date32(new DateTime(1969, 12, 31));
        Assert.That((int)param.Value!, Is.LessThan(0));
    }

    [Test]
    public void Test_Param_Date32_FarFuture()
    {
        var param = Param.Date32(new DateTime(2100, 12, 31));
        Assert.That((int)param.Value!, Is.GreaterThan(0));
    }

    [Test]
    public void Test_Param_Timestamp_WithDifferentTimeUnits()
    {
        var dateTime = new DateTime(2023, 6, 15, 12, 30, 45, DateTimeKind.Utc);
        
        var paramSeconds = Param.Timestamp(dateTime, TimeUnit.Second);
        var paramMillis = Param.Timestamp(dateTime, TimeUnit.Millisecond);
        var paramMicros = Param.Timestamp(dateTime, TimeUnit.Microsecond);
        var paramNanos = Param.Timestamp(dateTime, TimeUnit.Nanosecond);
        
        Assert.That(paramSeconds.Type, Is.TypeOf<TimestampType>());
        Assert.That(paramMillis.Type, Is.TypeOf<TimestampType>());
        Assert.That(paramMicros.Type, Is.TypeOf<TimestampType>());
        Assert.That(paramNanos.Type, Is.TypeOf<TimestampType>());
        
        // Verify units
        Assert.That(((TimestampType)paramSeconds.Type!).Unit, Is.EqualTo(TimeUnit.Second));
        Assert.That(((TimestampType)paramMillis.Type!).Unit, Is.EqualTo(TimeUnit.Millisecond));
        Assert.That(((TimestampType)paramMicros.Type!).Unit, Is.EqualTo(TimeUnit.Microsecond));
        Assert.That(((TimestampType)paramNanos.Type!).Unit, Is.EqualTo(TimeUnit.Nanosecond));
    }

    [Test]
    public void Test_Param_Timestamp_WithTimezone()
    {
        var dateTime = new DateTime(2023, 6, 15, 12, 30, 45, DateTimeKind.Utc);
        var param = Param.Timestamp(dateTime, TimeUnit.Microsecond, "America/New_York");
        
        var tsType = (TimestampType)param.Type!;
        Assert.That(tsType.Timezone, Is.EqualTo("America/New_York"));
    }

    [Test]
    public void Test_Param_Duration_Zero()
    {
        var param = Param.Duration(TimeSpan.Zero);
        Assert.That(param.Value, Is.EqualTo(0L));
    }

    [Test]
    public void Test_Param_Duration_NegativeTimeSpan()
    {
        var param = Param.Duration(TimeSpan.FromSeconds(-60));
        Assert.That((long)param.Value!, Is.LessThan(0));
    }

#if NET6_0_OR_GREATER
    [Test]
    public void Test_Param_Time64_FromTimeOnly_Midnight()
    {
        var param = Param.Time64(TimeOnly.MinValue);
        Assert.That(param.Value, Is.EqualTo(0L));
    }

    [Test]
    public void Test_Param_Time64_FromTimeOnly_EndOfDay()
    {
        var param = Param.Time64(new TimeOnly(23, 59, 59, 999));
        Assert.That((long)param.Value!, Is.GreaterThan(0));
    }
#endif

    // ============ Interval Tests ============

    [Test]
    public void Test_Param_MonthInterval_ZeroMonths()
    {
        var param = Param.MonthInterval(0);
        Assert.That(param.Value, Is.EqualTo(0));
    }

    [Test]
    public void Test_Param_MonthInterval_NegativeMonths()
    {
        var param = Param.MonthInterval(-12);
        Assert.That(param.Value, Is.EqualTo(-12));
    }

    [Test]
    public void Test_Param_DayTimeInterval_ZeroValues()
    {
        var param = Param.DayTimeInterval(0, 0);
        Assert.That(param.Type, Is.TypeOf<IntervalType>());
    }

    [Test]
    public void Test_Param_MonthDayNanoInterval_AllPositive()
    {
        var param = Param.MonthDayNanoInterval(12, 30, 1_000_000_000L);
        Assert.That(param.Type, Is.TypeOf<IntervalType>());
        
        var tuple = (ValueTuple<int, int, long>)param.Value!;
        Assert.That(tuple.Item1, Is.EqualTo(12));
        Assert.That(tuple.Item2, Is.EqualTo(30));
        Assert.That(tuple.Item3, Is.EqualTo(1_000_000_000L));
    }

    // ============ FixedSizeBinary Tests ============

    [Test]
    public void Test_Param_FixedSizeBinary_MatchingSize()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var param = Param.FixedSizeBinary(bytes, 4);
        
        Assert.That(param.Value, Is.EqualTo(bytes));
        var fixedType = (FixedSizeBinaryType)param.Type!;
        Assert.That(fixedType.ByteWidth, Is.EqualTo(4));
    }

    [Test]
    public void Test_Param_FixedSizeBinary_DifferentSize()
    {
        // Note: The type allows specifying a different byte width than the actual array size
        // The server should validate this
        var bytes = new byte[] { 0x01, 0x02 };
        var param = Param.FixedSizeBinary(bytes, 4);
        
        var fixedType = (FixedSizeBinaryType)param.Type!;
        Assert.That(fixedType.ByteWidth, Is.EqualTo(4));
    }

    // ============ From() Method Tests ============

    [Test]
    public void Test_Param_From_NullValue()
    {
        var param = Param.From(null);
        Assert.That(param.Value, Is.Null);
        Assert.That(param.Type, Is.Null);
        Assert.That(param.HasExplicitType, Is.False);
    }

    [Test]
    public void Test_Param_From_VariousTypes()
    {
        // All these should have inferred types (Type = null)
        Assert.That(Param.From(42).HasExplicitType, Is.False);
        Assert.That(Param.From("hello").HasExplicitType, Is.False);
        Assert.That(Param.From(3.14).HasExplicitType, Is.False);
        Assert.That(Param.From(true).HasExplicitType, Is.False);
        Assert.That(Param.From(DateTime.Now).HasExplicitType, Is.False);
    }

    // ============ WithType() Method Tests ============

    [Test]
    public void Test_Param_WithType_CustomType()
    {
        // Use WithType to force a specific Arrow type
        var param = Param.WithType(42, new Int64Type());
        
        Assert.That(param.Value, Is.EqualTo(42));
        Assert.That(param.Type, Is.TypeOf<Int64Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_WithType_OverrideDefaultType()
    {
        // String value but force Binary type (for testing purposes)
        var param = Param.WithType("test", new BinaryType());
        
        Assert.That(param.Value, Is.EqualTo("test"));
        Assert.That(param.Type, Is.TypeOf<BinaryType>());
    }
}
