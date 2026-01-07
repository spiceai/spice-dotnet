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
/// Unit tests for the Param class which provides typed parameter support for parameterized queries.
/// </summary>
[TestFixture]
public class ParamTest
{
    [Test]
    public void Test_Param_Int8_SetsCorrectTypeAndValue()
    {
        var param = Param.Int8(42);

        Assert.That(param.Value, Is.EqualTo((sbyte)42));
        Assert.That(param.Type, Is.TypeOf<Int8Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Int16_SetsCorrectTypeAndValue()
    {
        var param = Param.Int16(1234);

        Assert.That(param.Value, Is.EqualTo((short)1234));
        Assert.That(param.Type, Is.TypeOf<Int16Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Int32_SetsCorrectTypeAndValue()
    {
        var param = Param.Int32(123456);

        Assert.That(param.Value, Is.EqualTo(123456));
        Assert.That(param.Type, Is.TypeOf<Int32Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Int64_SetsCorrectTypeAndValue()
    {
        var param = Param.Int64(9876543210L);

        Assert.That(param.Value, Is.EqualTo(9876543210L));
        Assert.That(param.Type, Is.TypeOf<Int64Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Float_SetsCorrectTypeAndValue()
    {
        var param = Param.Float(3.14f);

        Assert.That(param.Value, Is.EqualTo(3.14f));
        Assert.That(param.Type, Is.TypeOf<FloatType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Double_SetsCorrectTypeAndValue()
    {
        var param = Param.Double(3.14159265359);

        Assert.That(param.Value, Is.EqualTo(3.14159265359));
        Assert.That(param.Type, Is.TypeOf<DoubleType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_String_SetsCorrectTypeAndValue()
    {
        var param = Param.String("Hello, World!");

        Assert.That(param.Value, Is.EqualTo("Hello, World!"));
        Assert.That(param.Type, Is.TypeOf<StringType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Boolean_SetsCorrectTypeAndValue()
    {
        var param = Param.Boolean(true);

        Assert.That(param.Value, Is.EqualTo(true));
        Assert.That(param.Type, Is.TypeOf<BooleanType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Binary_SetsCorrectTypeAndValue()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var param = Param.Binary(bytes);

        Assert.That(param.Value, Is.EqualTo(bytes));
        Assert.That(param.Type, Is.TypeOf<BinaryType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Date32_FromInt_SetsCorrectTypeAndValue()
    {
        var param = Param.Date32(1000); // days since epoch

        Assert.That(param.Value, Is.EqualTo(1000));
        Assert.That(param.Type, Is.TypeOf<Date32Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Date32_FromDateTime_CalculatesDaysSinceEpoch()
    {
        var date = new DateTime(1972, 9, 27); // 1000 days after epoch (roughly)
        var param = Param.Date32(date);

        // 1000 days from 1970-01-01 = 1972-09-27
        var expectedDays = (int)(date.Date - new DateTime(1970, 1, 1)).TotalDays;
        Assert.That(param.Value, Is.EqualTo(expectedDays));
        Assert.That(param.Type, Is.TypeOf<Date32Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

#if NET6_0_OR_GREATER
    [Test]
    public void Test_Param_Date32_FromDateOnly_CalculatesDaysSinceEpoch()
    {
        var date = new DateOnly(2023, 6, 15);
        var param = Param.Date32(date);

        var expectedDays = date.DayNumber - new DateOnly(1970, 1, 1).DayNumber;
        Assert.That(param.Value, Is.EqualTo(expectedDays));
        Assert.That(param.Type, Is.TypeOf<Date32Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }
#endif

    [Test]
    public void Test_Param_Time32_WithSecondUnit_SetsCorrectTypeAndValue()
    {
        var param = Param.Time32(3600, TimeUnit.Second);

        Assert.That(param.Value, Is.EqualTo(3600));
        Assert.That(param.Type, Is.TypeOf<Time32Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Time32_InvalidUnit_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Param.Time32(1000, TimeUnit.Microsecond));
        Assert.Throws<ArgumentException>(() => Param.Time32(1000, TimeUnit.Nanosecond));
    }

    [Test]
    public void Test_Param_Time64_WithMicrosecondUnit_SetsCorrectTypeAndValue()
    {
        var param = Param.Time64(1000000L, TimeUnit.Microsecond);

        Assert.That(param.Value, Is.EqualTo(1000000L));
        Assert.That(param.Type, Is.TypeOf<Time64Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Time64_InvalidUnit_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Param.Time64(1000L, TimeUnit.Second));
        Assert.Throws<ArgumentException>(() => Param.Time64(1000L, TimeUnit.Millisecond));
    }

    [Test]
    public void Test_Param_Timestamp_FromDateTime_SetsCorrectTypeAndValue()
    {
        var dateTime = new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var param = Param.Timestamp(dateTime, TimeUnit.Microsecond);

        Assert.That(param.Type, Is.TypeOf<TimestampType>());
        Assert.That(param.HasExplicitType, Is.True);
        Assert.That(param.Value, Is.Not.Null);
    }

    [Test]
    public void Test_Param_Duration_FromMicroseconds_SetsCorrectTypeAndValue()
    {
        var param = Param.Duration(1000000L);

        Assert.That(param.Value, Is.EqualTo(1000000L));
        Assert.That(param.Type, Is.TypeOf<Int64Type>()); // Duration is stored as Int64
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Duration_FromTimeSpan_ConvertsToMicroseconds()
    {
        var timeSpan = TimeSpan.FromMilliseconds(1000);
        var param = Param.Duration(timeSpan);

        // 1000 ms = 1,000,000 microseconds
        Assert.That(param.Value, Is.EqualTo(1000000L));
        Assert.That(param.Type, Is.TypeOf<Int64Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Decimal128_SetsCorrectTypeAndValue()
    {
        var param = Param.Decimal128(99.99m, 10, 2);

        Assert.That(param.Value, Is.EqualTo(99.99m));
        Assert.That(param.Type, Is.TypeOf<Decimal128Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_Null_SetsNullTypeAndValue()
    {
        var param = Param.Null();

        Assert.That(param.Value, Is.Null);
        Assert.That(param.Type, Is.TypeOf<NullType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_From_InfersType()
    {
        var param = Param.From(123);

        Assert.That(param.Value, Is.EqualTo(123));
        Assert.That(param.Type, Is.Null); // Type is inferred, not set explicitly
        Assert.That(param.HasExplicitType, Is.False);
    }

    [Test]
    public void Test_Param_WithType_SetsExplicitType()
    {
        var param = Param.WithType(123, new Int64Type());

        Assert.That(param.Value, Is.EqualTo(123));
        Assert.That(param.Type, Is.TypeOf<Int64Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_MonthInterval_SetsCorrectTypeAndValue()
    {
        var param = Param.MonthInterval(24); // 2 years

        Assert.That(param.Value, Is.EqualTo(24));
        Assert.That(param.Type, Is.TypeOf<IntervalType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_DayTimeInterval_SetsCorrectTypeAndValue()
    {
        var param = Param.DayTimeInterval(10, 5000); // 10 days, 5000 ms

        Assert.That(param.Type, Is.TypeOf<IntervalType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_UInt8_SetsCorrectTypeAndValue()
    {
        var param = Param.UInt8(200);

        Assert.That(param.Value, Is.EqualTo((byte)200));
        Assert.That(param.Type, Is.TypeOf<UInt8Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_UInt16_SetsCorrectTypeAndValue()
    {
        var param = Param.UInt16(50000);

        Assert.That(param.Value, Is.EqualTo((ushort)50000));
        Assert.That(param.Type, Is.TypeOf<UInt16Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_UInt32_SetsCorrectTypeAndValue()
    {
        var param = Param.UInt32(3000000000u);

        Assert.That(param.Value, Is.EqualTo(3000000000u));
        Assert.That(param.Type, Is.TypeOf<UInt32Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_UInt64_SetsCorrectTypeAndValue()
    {
        var param = Param.UInt64(10000000000000000000uL);

        Assert.That(param.Value, Is.EqualTo(10000000000000000000uL));
        Assert.That(param.Type, Is.TypeOf<UInt64Type>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_LargeString_SetsCorrectTypeAndValue()
    {
        var param = Param.LargeString("Large string content");

        Assert.That(param.Value, Is.EqualTo("Large string content"));
        Assert.That(param.Type, Is.TypeOf<LargeStringType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_LargeBinary_SetsCorrectTypeAndValue()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var param = Param.LargeBinary(bytes);

        Assert.That(param.Value, Is.EqualTo(bytes));
        Assert.That(param.Type, Is.TypeOf<LargeBinaryType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

    [Test]
    public void Test_Param_FixedSizeBinary_SetsCorrectTypeAndValue()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var param = Param.FixedSizeBinary(bytes, 4);

        Assert.That(param.Value, Is.EqualTo(bytes));
        Assert.That(param.Type, Is.TypeOf<FixedSizeBinaryType>());
        Assert.That(param.HasExplicitType, Is.True);
    }

#if NET8_0_OR_GREATER
    [Test]
    public void Test_Param_HalfFloat_SetsCorrectTypeAndValue()
    {
        var param = Param.HalfFloat((Half)1.5);

        Assert.That(param.Value, Is.EqualTo((Half)1.5));
        Assert.That(param.Type, Is.TypeOf<HalfFloatType>());
        Assert.That(param.HasExplicitType, Is.True);
    }
#endif
}
