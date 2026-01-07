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

using System.Reflection;
using Apache.Arrow.Types;
using NUnit.Framework;

namespace SpiceTest;

/// <summary>
/// Unit tests for Arrow type inference from .NET types.
/// These tests verify the internal InferArrowType method behavior.
/// </summary>
[TestFixture]
public class TypeInferenceTest
{
    // Access to the internal InferArrowType method for testing
    private static IArrowType InferArrowType(object? value)
    {
        // Use reflection to access the internal static method
        var adbcClientType = typeof(Spice.SpiceClient).Assembly
            .GetType("Spice.Adbc.SpiceAdbcClient");
        
        Assert.That(adbcClientType, Is.Not.Null, "SpiceAdbcClient type should exist");
        
        var method = adbcClientType!.GetMethod("InferArrowType", 
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.That(method, Is.Not.Null, "InferArrowType method should exist");
        
        return (IArrowType)method!.Invoke(null, new[] { value })!;
    }

    // ============ Null Type ============

    [Test]
    public void Test_InferArrowType_Null_ReturnsNullType()
    {
        var result = InferArrowType(null);
        Assert.That(result, Is.TypeOf<NullType>());
    }

    // ============ Boolean Type ============

    [Test]
    public void Test_InferArrowType_Bool_ReturnsBooleanType()
    {
        var result = InferArrowType(true);
        Assert.That(result, Is.TypeOf<BooleanType>());
    }

    [Test]
    public void Test_InferArrowType_BoolFalse_ReturnsBooleanType()
    {
        var result = InferArrowType(false);
        Assert.That(result, Is.TypeOf<BooleanType>());
    }

    // ============ Integer Types ============

    [Test]
    public void Test_InferArrowType_SByte_ReturnsInt8Type()
    {
        var result = InferArrowType((sbyte)42);
        Assert.That(result, Is.TypeOf<Int8Type>());
    }

    [Test]
    public void Test_InferArrowType_Byte_ReturnsUInt8Type()
    {
        var result = InferArrowType((byte)42);
        Assert.That(result, Is.TypeOf<UInt8Type>());
    }

    [Test]
    public void Test_InferArrowType_Short_ReturnsInt16Type()
    {
        var result = InferArrowType((short)1234);
        Assert.That(result, Is.TypeOf<Int16Type>());
    }

    [Test]
    public void Test_InferArrowType_UShort_ReturnsUInt16Type()
    {
        var result = InferArrowType((ushort)1234);
        Assert.That(result, Is.TypeOf<UInt16Type>());
    }

    [Test]
    public void Test_InferArrowType_Int_ReturnsInt32Type()
    {
        var result = InferArrowType(123456);
        Assert.That(result, Is.TypeOf<Int32Type>());
    }

    [Test]
    public void Test_InferArrowType_UInt_ReturnsUInt32Type()
    {
        var result = InferArrowType(123456u);
        Assert.That(result, Is.TypeOf<UInt32Type>());
    }

    [Test]
    public void Test_InferArrowType_Long_ReturnsInt64Type()
    {
        var result = InferArrowType(9876543210L);
        Assert.That(result, Is.TypeOf<Int64Type>());
    }

    [Test]
    public void Test_InferArrowType_ULong_ReturnsUInt64Type()
    {
        var result = InferArrowType(9876543210UL);
        Assert.That(result, Is.TypeOf<UInt64Type>());
    }

    // ============ Floating Point Types ============

    [Test]
    public void Test_InferArrowType_Float_ReturnsFloatType()
    {
        var result = InferArrowType(3.14f);
        Assert.That(result, Is.TypeOf<FloatType>());
    }

    [Test]
    public void Test_InferArrowType_Double_ReturnsDoubleType()
    {
        var result = InferArrowType(3.14159265359);
        Assert.That(result, Is.TypeOf<DoubleType>());
    }

#if NET8_0_OR_GREATER
    [Test]
    public void Test_InferArrowType_Half_ReturnsHalfFloatType()
    {
        var result = InferArrowType((Half)1.5);
        Assert.That(result, Is.TypeOf<HalfFloatType>());
    }
#endif

    // ============ Decimal Type ============

    [Test]
    public void Test_InferArrowType_Decimal_ReturnsDecimal128Type()
    {
        var result = InferArrowType(99.99m);
        Assert.That(result, Is.TypeOf<Decimal128Type>());
    }

    // ============ String and Binary Types ============

    [Test]
    public void Test_InferArrowType_String_ReturnsStringType()
    {
        var result = InferArrowType("Hello, World!");
        Assert.That(result, Is.TypeOf<StringType>());
    }

    [Test]
    public void Test_InferArrowType_EmptyString_ReturnsStringType()
    {
        var result = InferArrowType("");
        Assert.That(result, Is.TypeOf<StringType>());
    }

    [Test]
    public void Test_InferArrowType_ByteArray_ReturnsBinaryType()
    {
        var result = InferArrowType(new byte[] { 1, 2, 3 });
        Assert.That(result, Is.TypeOf<BinaryType>());
    }

    [Test]
    public void Test_InferArrowType_EmptyByteArray_ReturnsBinaryType()
    {
        var result = InferArrowType(new byte[0]);
        Assert.That(result, Is.TypeOf<BinaryType>());
    }

    // ============ DateTime Types ============

    [Test]
    public void Test_InferArrowType_DateTime_ReturnsTimestampType()
    {
        var result = InferArrowType(DateTime.Now);
        Assert.That(result, Is.TypeOf<TimestampType>());
    }

    [Test]
    public void Test_InferArrowType_DateTimeOffset_ReturnsTimestampType()
    {
        var result = InferArrowType(DateTimeOffset.Now);
        Assert.That(result, Is.TypeOf<TimestampType>());
    }

    [Test]
    public void Test_InferArrowType_TimeSpan_ReturnsInt64Type()
    {
        // TimeSpan is mapped to Int64 (microseconds) since DurationType isn't directly constructable in C#
        var result = InferArrowType(TimeSpan.FromSeconds(60));
        Assert.That(result, Is.TypeOf<Int64Type>());
    }

#if NET6_0_OR_GREATER
    [Test]
    public void Test_InferArrowType_DateOnly_ReturnsDate32Type()
    {
        var result = InferArrowType(new DateOnly(2023, 6, 15));
        Assert.That(result, Is.TypeOf<Date32Type>());
    }

    [Test]
    public void Test_InferArrowType_TimeOnly_ReturnsTime64Type()
    {
        var result = InferArrowType(new TimeOnly(12, 30, 45));
        Assert.That(result, Is.TypeOf<Time64Type>());
    }
#endif

    // ============ Edge Cases ============

    [Test]
    public void Test_InferArrowType_IntegerBoundary_MinValue()
    {
        Assert.That(InferArrowType(int.MinValue), Is.TypeOf<Int32Type>());
        Assert.That(InferArrowType(long.MinValue), Is.TypeOf<Int64Type>());
        Assert.That(InferArrowType(short.MinValue), Is.TypeOf<Int16Type>());
    }

    [Test]
    public void Test_InferArrowType_IntegerBoundary_MaxValue()
    {
        Assert.That(InferArrowType(int.MaxValue), Is.TypeOf<Int32Type>());
        Assert.That(InferArrowType(long.MaxValue), Is.TypeOf<Int64Type>());
        Assert.That(InferArrowType(short.MaxValue), Is.TypeOf<Int16Type>());
    }

    [Test]
    public void Test_InferArrowType_FloatingPointSpecialValues()
    {
        Assert.That(InferArrowType(float.NaN), Is.TypeOf<FloatType>());
        Assert.That(InferArrowType(float.PositiveInfinity), Is.TypeOf<FloatType>());
        Assert.That(InferArrowType(float.NegativeInfinity), Is.TypeOf<FloatType>());
        Assert.That(InferArrowType(double.NaN), Is.TypeOf<DoubleType>());
        Assert.That(InferArrowType(double.PositiveInfinity), Is.TypeOf<DoubleType>());
        Assert.That(InferArrowType(double.NegativeInfinity), Is.TypeOf<DoubleType>());
    }

    [Test]
    public void Test_InferArrowType_UnsupportedType_ThrowsArgumentException()
    {
        // Custom objects should throw
        Assert.Throws<TargetInvocationException>(() => InferArrowType(new object()));
        Assert.Throws<TargetInvocationException>(() => InferArrowType(new List<int> { 1, 2, 3 }));
    }
}
