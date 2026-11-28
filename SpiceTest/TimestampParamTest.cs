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
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;
using Spice.Adbc;
using Spice.Params;

namespace SpiceTest;

/// <summary>
/// Timestamp parameters must bind the exact value asked for, or fail loudly.
/// These build the one-element Arrow array that the ADBC client sends.
/// </summary>
[TestFixture]
public class TimestampParamTest
{
    private static readonly MethodInfo CreateArrowArray = typeof(SpiceAdbcClient)
        .GetMethod("CreateArrowArray", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static long Bound(Param p)
    {
        var array = (TimestampArray)CreateArrowArray.Invoke(null, new[] { p.Value, p.Type! })!;
        Assert.That(array.IsNull(0), Is.False);
        return array.GetValue(0)!.Value;
    }

    [Test]
    public void RawNanosecondValue_IsBoundExactly()
    {
        Assert.That(Bound(Param.Timestamp(1L, TimeUnit.Nanosecond)), Is.EqualTo(1L));
        Assert.That(Bound(Param.Timestamp(1_762_000_000_123_456_789L, TimeUnit.Nanosecond)),
            Is.EqualTo(1_762_000_000_123_456_789L));
        Assert.That(Bound(Param.Timestamp(-1L, TimeUnit.Nanosecond)), Is.EqualTo(-1L));
    }

    [TestCase(TimeUnit.Second, 1_762_000_000L)]
    [TestCase(TimeUnit.Millisecond, 1_762_000_000_123L)]
    [TestCase(TimeUnit.Microsecond, 1_762_000_000_123_456L)]
    [TestCase(TimeUnit.Microsecond, -1L)]
    [TestCase(TimeUnit.Second, 253_402_214_400L)] // 9999-12-31, as a raw value
    public void RawValue_IsBoundExactly(TimeUnit unit, long value)
    {
        Assert.That(Bound(Param.Timestamp(value, unit)), Is.EqualTo(value));
    }

    [Test]
    public void DateTimeBeyondNanosecondRange_Throws()
    {
        var farFuture = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentOutOfRangeException>(() => Param.Timestamp(farFuture, TimeUnit.Nanosecond));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Param.Timestamp(new DateTimeOffset(farFuture), TimeUnit.Nanosecond));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Param.Timestamp(new DateTime(1600, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeUnit.Nanosecond));
    }

    [Test]
    public void RawDateTimeWithExplicitType_BeyondNanosecondRange_Throws()
    {
        // new Param(DateTime, TimestampType) hands the DateTime itself to the ADBC array builder,
        // which converts it there rather than in Param.Timestamp.
        var nanos = new TimestampType(TimeUnit.Nanosecond, "UTC");
        var farFuture = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        foreach (var value in new object[] { farFuture, new DateTimeOffset(farFuture) })
        {
            var ex = Assert.Throws<TargetInvocationException>(() => Bound(new Param(value, nanos)));
            Assert.That(ex!.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>(), value.GetType().Name);
        }
    }

    [Test]
    public void RawDateTimeWithExplicitType_IsBoundExactly()
    {
        var utc = new DateTime(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc).AddTicks(1234567);
        var offset = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-7)).AddTicks(1234567);
        var nanos = new TimestampType(TimeUnit.Nanosecond, "UTC");
        Assert.That(Bound(new Param(utc, nanos)), Is.EqualTo(1_793_521_800_123_456_700L));
        Assert.That(Bound(new Param(offset, nanos)), Is.EqualTo(1_793_521_800_123_456_700L));

        var justBefore = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);
        Assert.That(Bound(new Param(justBefore, new TimestampType(TimeUnit.Second, "UTC"))), Is.EqualTo(-1L));
    }

    [Test]
    public void NanosecondRangeEdges_AreBound()
    {
        // Arrow nanosecond timestamps cover 1677-09-21T00:12:43.145224192Z to 2262-04-11T23:47:16.854775807Z;
        // DateTime has 100 ns ticks, so the edges inside the range are those rounded inward.
        var max = new DateTime(2262, 4, 11, 23, 47, 16, DateTimeKind.Utc).AddTicks(8_547_758);
        Assert.That(Bound(Param.Timestamp(max, TimeUnit.Nanosecond)), Is.EqualTo(9_223_372_036_854_775_800L));
        var min = new DateTime(1677, 9, 21, 0, 12, 43, DateTimeKind.Utc).AddTicks(1_452_242);
        Assert.That(Bound(Param.Timestamp(min, TimeUnit.Nanosecond)), Is.EqualTo(-9_223_372_036_854_775_800L));
    }

    [TestCase(TimeUnit.Second, -1L)]
    [TestCase(TimeUnit.Millisecond, -1L)]
    [TestCase(TimeUnit.Microsecond, -1L)]
    public void PreEpochSubUnit_FloorsTowardEarlier(TimeUnit unit, long expected)
    {
        // 1969-12-31T23:59:59.9999999Z is one tick before the epoch: it belongs to the
        // second (millisecond, microsecond) that starts before the epoch, not the epoch itself.
        var justBefore = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);
        Assert.That(Bound(Param.Timestamp(justBefore, unit)), Is.EqualTo(expected));
    }

    [Test]
    public void DateTimeAndDateTimeOffset_BindTheSameInstant()
    {
        var utc = new DateTime(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc).AddTicks(1234567);
        var offset = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-7)).AddTicks(1234567);
        foreach (var unit in new[] { TimeUnit.Second, TimeUnit.Millisecond, TimeUnit.Microsecond, TimeUnit.Nanosecond })
        {
            Assert.That(Bound(Param.Timestamp(offset, unit)), Is.EqualTo(Bound(Param.Timestamp(utc, unit))), unit.ToString());
        }
        Assert.That(Bound(Param.Timestamp(utc, TimeUnit.Nanosecond)), Is.EqualTo(1_793_521_800_123_456_700L));
    }
}
