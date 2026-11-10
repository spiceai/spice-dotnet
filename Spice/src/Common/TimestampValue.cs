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

namespace Spice.Common;

/// <summary>
/// Converts .NET instants to Arrow timestamp values: the count of
/// <see cref="TimeUnit"/>s since 1970-01-01T00:00:00Z.
/// </summary>
internal static class TimestampValue
{
    private const long TicksPerMicrosecond = 10;
    private const long NanosecondsPerTick = 100;
    private const long UnixEpochTicks = 621_355_968_000_000_000; // 1970-01-01T00:00:00Z

    private const string NanosecondRange =
        "nanosecond timestamps cover 1677-09-21T00:12:43.145224192Z to 2262-04-11T23:47:16.854775807Z; " +
        "use TimeUnit.Microsecond for instants outside that range";

    /// <summary>
    /// The Arrow value of <paramref name="instant"/> in <paramref name="unit"/>.
    /// A sub-unit remainder floors toward the earlier instant, so a time just
    /// before the epoch stays before it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The instant does not fit a 64-bit count of <paramref name="unit"/>s
    /// (only possible for nanoseconds), or the unit is unknown.
    /// </exception>
    public static long FromInstant(DateTimeOffset instant, TimeUnit unit, string paramName)
    {
        long ticks = instant.UtcTicks - UnixEpochTicks;
        switch (unit)
        {
            case TimeUnit.Second:
                return FloorDiv(ticks, TimeSpan.TicksPerSecond);
            case TimeUnit.Millisecond:
                return FloorDiv(ticks, TimeSpan.TicksPerMillisecond);
            case TimeUnit.Microsecond:
                return FloorDiv(ticks, TicksPerMicrosecond);
            case TimeUnit.Nanosecond:
                try
                {
                    return checked(ticks * NanosecondsPerTick);
                }
                catch (OverflowException)
                {
                    throw new ArgumentOutOfRangeException(paramName, instant,
                        $"Timestamp {instant.UtcDateTime:O} is out of range for TimeUnit.Nanosecond: {NanosecondRange}.");
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported timestamp time unit");
        }
    }

    private static long FloorDiv(long value, long divisor)
    {
        long quotient = value / divisor;
        return (value % divisor) < 0 ? quotient - 1 : quotient;
    }
}
