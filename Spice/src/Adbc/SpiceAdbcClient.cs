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

using System.Runtime.InteropServices;
using Apache.Arrow;
using Apache.Arrow.Adbc;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Polly.Retry;
using Spice.Common;
using Spice.Params;

namespace Spice.Adbc;

/// <summary>
/// ADBC client wrapper for executing parameterized queries against Spice.ai.
/// Uses the ADBC FlightSQL driver for prepare/bind/execute pattern.
/// </summary>
internal sealed class SpiceAdbcClient : IDisposable
{
    private AdbcDatabase? _database;
    private AdbcConnection? _connection;
    private readonly object _initLock = new();
    private readonly AsyncRetryPolicy _retryPolicy;

    private readonly string _flightAddress;
    private readonly string? _appId;
    private readonly string? _apiKey;
    private readonly string? _userAgent;
    private readonly bool _useTls;

    /// <summary>
    /// Creates a new ADBC client with the specified configuration.
    /// The connection is lazily initialized on first use.
    /// </summary>
    public SpiceAdbcClient(string flightAddress, int maxRetries, string? appId, string? apiKey, string? userAgent, bool useTls)
    {
        _flightAddress = flightAddress;
        _appId = appId;
        _apiKey = apiKey;
        _userAgent = userAgent;
        _useTls = useTls;

        // Create retry policy that excludes permanent failures like "Statement does not support Prepare"
        _retryPolicy = RetryPolicyFactory.CreateGeneralRetryPolicy<AdbcException>(
            maxRetries,
            additionalPredicate: ShouldRetryAdbcException,
            onRetry: (ex, ts, attempt) => RetryPolicyFactory.LogRetry("ADBC", ex, ts, attempt));
    }

    /// <summary>
    /// Determines if an ADBC exception should be retried.
    /// Returns false for permanent failures that should not be retried.
    /// </summary>
    private static bool ShouldRetryAdbcException(AdbcException ex)
    {
        // Don't retry if the driver doesn't support a feature
#if NETSTANDARD2_0
        if (ex.Message.IndexOf("does not support", StringComparison.OrdinalIgnoreCase) >= 0)
#else
        if (ex.Message.Contains("does not support", StringComparison.OrdinalIgnoreCase))
#endif
        {
            return false;
        }

        // Don't retry if it's a NotImplemented error
#if NETSTANDARD2_0
        if (ex.Message.IndexOf("NotImplemented", StringComparison.OrdinalIgnoreCase) >= 0)
#else
        if (ex.Message.Contains("NotImplemented", StringComparison.OrdinalIgnoreCase))
#endif
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Initializes the ADBC connection if not already initialized.
    /// This is thread-safe and called lazily on first parameterized query.
    /// </summary>
    private void InitializeIfNeeded()
    {
        if (_database != null && _connection != null)
        {
            return;
        }

        lock (_initLock)
        {
            if (_database != null && _connection != null)
            {
                return;
            }

            // Format the URI for ADBC FlightSQL driver
            // The driver expects grpc:// or grpc+tls:// schemes
            var uri = _flightAddress;

            // Convert http/https schemes to grpc/grpc+tls
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
#if NETSTANDARD2_0
                uri = "grpc+tls://" + uri.Substring(8);
#else
                uri = string.Concat("grpc+tls://", uri.AsSpan(8));
#endif
            }
            else if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
#if NETSTANDARD2_0
                uri = "grpc://" + uri.Substring(7);
#else
                uri = string.Concat("grpc://", uri.AsSpan(7));
#endif
            }
            else if (!uri.StartsWith("grpc://", StringComparison.OrdinalIgnoreCase) &&
                     !uri.StartsWith("grpc+tls://", StringComparison.OrdinalIgnoreCase) &&
                     !uri.StartsWith("grpc+tcp://", StringComparison.OrdinalIgnoreCase))
            {
                // No scheme provided - add grpc or grpc+tls based on TLS setting
                uri = _useTls ? string.Concat("grpc+tls://", uri) : string.Concat("grpc://", uri);
            }

            // Build database parameters for the ADBC FlightSQL driver
            var databaseParams = new Dictionary<string, string>
            {
                { "uri", uri }
            };

            // Add authentication if available
            if (!string.IsNullOrEmpty(_appId) && !string.IsNullOrEmpty(_apiKey))
            {
                databaseParams["username"] = _appId!;
                databaseParams["password"] = _apiKey!;
            }

            // Add user agent header
            databaseParams["adbc.flight.sql.rpc.call_header.user-agent"] = UserAgentHelper.BuildUserAgent(_userAgent);

            // Load the ADBC FlightSQL driver from the application base directory
            var driverPath = ResolveNativeDriverPath();
            var driver = AdbcDriverLoader.LoadDriver(driverPath, "AdbcDriverFlightsqlInit");
            _database = driver.Open(databaseParams);
            _connection = _database.Connect(new Dictionary<string, string>());
        }
    }

    /// <summary>
    /// Executes a parameterized SQL query using the ADBC prepare/bind/execute pattern.
    /// </summary>
    /// <param name="sql">SQL query with positional placeholders ($1, $2, etc.)</param>
    /// <param name="parameters">The parameter values (can be plain values or Param instances)</param>
    /// <returns>An ArrowArrayStream with the query results</returns>
    public Task<IArrowArrayStream?> SqlWithParamsAsync(string sql, params object?[] parameters)
    {
        if (string.IsNullOrEmpty(sql))
        {
            throw new ArgumentException("No SQL provided", nameof(sql));
        }

        return _retryPolicy.ExecuteAsync(() =>
        {
            InitializeIfNeeded();

            var statement = _connection!.CreateStatement();
            try
            {
                statement.SqlQuery = sql;

                // Prepare the statement
                statement.Prepare();

                // Bind parameters if provided
                if (parameters.Length > 0)
                {
                    var parameterBatch = CreateParameterBatch(parameters);
                    statement.Bind(parameterBatch, parameterBatch.Schema);
                }

                // Execute the query
                var result = statement.ExecuteQuery();

                // Wrap the stream to keep the statement alive for the stream's lifetime
                var wrappedStream = new StatementBoundArrowArrayStream(result.Stream!, statement);
                return Task.FromResult<IArrowArrayStream?>(wrappedStream);
            }
            catch
            {
                // If anything fails before we wrap the stream, dispose the statement
                statement.Dispose();
                throw;
            }
        });
    }

    /// <summary>
    /// Creates a RecordBatch containing the parameter values.
    /// </summary>
    private static RecordBatch CreateParameterBatch(object?[] parameters)
    {
        var numParams = parameters.Length;
        var fields = new Field[numParams];
        var arrays = new IArrowArray[numParams];

        for (var i = 0; i < numParams; i++)
        {
            var param = parameters[i];
            object? value;
            IArrowType arrowType;

            // Extract value and type from Param or infer
            if (param is Param p)
            {
                value = p.Value;
                arrowType = p.Type ?? InferArrowType(p.Value);
            }
            else
            {
                value = param;
                arrowType = InferArrowType(param);
            }

            var fieldName = $"${i + 1}";
            fields[i] = new Field(fieldName, arrowType, nullable: true);
            arrays[i] = CreateArrowArray(value, arrowType);
        }

        var schema = new Schema(fields, null);
        return new RecordBatch(schema, arrays, 1);
    }

    /// <summary>
    /// Infers the Arrow type from a .NET value.
    /// </summary>
    private static IArrowType InferArrowType(object? value)
    {
        return value switch
        {
            null => new NullType(),
            bool => new BooleanType(),
            sbyte => new Int8Type(),
            byte => new UInt8Type(),
            short => new Int16Type(),
            ushort => new UInt16Type(),
            int => new Int32Type(),
            uint => new UInt32Type(),
            long => new Int64Type(),
            ulong => new UInt64Type(),
            float => new FloatType(),
            double => new DoubleType(),
            decimal => new Decimal128Type(38, 10),
            string => new StringType(),
            byte[] => new BinaryType(),
            DateTime => new TimestampType(TimeUnit.Microsecond, TimeZoneInfo.Utc.Id),
            DateTimeOffset => new TimestampType(TimeUnit.Microsecond, TimeZoneInfo.Utc.Id),
            TimeSpan => new Int64Type(), // Duration stored as microseconds in Int64
#if NET6_0_OR_GREATER
            DateOnly => new Date32Type(),
            TimeOnly => new Time64Type(TimeUnit.Microsecond),
            Half => new HalfFloatType(),
#endif
            _ => throw new ArgumentException($"Unsupported parameter type: {value.GetType().FullName}")
        };
    }

    /// <summary>
    /// Creates an Arrow array containing a single value of the specified type.
    /// </summary>
    private static IArrowArray CreateArrowArray(object? value, IArrowType arrowType)
    {
        return arrowType switch
        {
            NullType => CreateNullArray(),
            BooleanType => CreateBooleanArray(value as bool?),
            Int8Type => CreateInt8Array(value is sbyte sb ? sb : null),
            UInt8Type => CreateUInt8Array(value is byte b ? b : null),
            Int16Type => CreateInt16Array(value is short s ? s : null),
            UInt16Type => CreateUInt16Array(value is ushort us ? us : null),
            Int32Type => CreateInt32Array(ConvertToInt32(value)),
            UInt32Type => CreateUInt32Array(value is uint ui ? ui : null),
            Int64Type => CreateInt64Array(ConvertToInt64(value)),
            UInt64Type => CreateUInt64Array(value is ulong ul ? ul : null),
            FloatType => CreateFloatArray(value is float f ? f : null),
            DoubleType => CreateDoubleArray(value is double d ? d : null),
#if NET8_0_OR_GREATER
            HalfFloatType => CreateHalfFloatArray(value is Half h ? h : null),
#endif
            StringType => CreateStringArray(value as string),
            LargeStringType => CreateStringArray(value as string), // Use StringArray for both
            BinaryType => CreateBinaryArray(value as byte[]),
            LargeBinaryType => CreateBinaryArray(value as byte[]), // Use BinaryArray for both
            Date32Type => CreateDate32Array(value),
            Date64Type => CreateDate64Array(value),
            Time32Type t32 => CreateTime32Array(value, t32.Unit),
            Time64Type t64 => CreateTime64Array(value, t64.Unit),
            TimestampType ts => CreateTimestampArray(value, ts),
            Decimal128Type dec128 => CreateDecimal128Array(value, dec128),
            Decimal256Type dec256 => CreateDecimal256Array(value, dec256),
            _ => throw new ArgumentException($"Unsupported Arrow type: {arrowType.GetType().Name}")
        };
    }

    private static int? ConvertToInt32(object? value)
    {
        return value switch
        {
            int i => i,
            DateTime dt => (int)(dt.Date - new DateTime(1970, 1, 1)).TotalDays,
#if NET6_0_OR_GREATER
            DateOnly dateOnly => dateOnly.DayNumber - new DateOnly(1970, 1, 1).DayNumber,
#endif
            _ => null
        };
    }

    private static long? ConvertToInt64(object? value)
    {
        return value switch
        {
            long l => l,
            TimeSpan ts => ts.Ticks / 10, // Convert to microseconds
            DateTime dt => (long)((dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10),
            DateTimeOffset dto => (long)((dto - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).Ticks / 10),
            _ => null
        };
    }

    // ============ Array Builders (delegating to common helper) ============

    private static IArrowArray CreateNullArray() => ArrowArrayBuilder.BuildNull();

    private static IArrowArray CreateBooleanArray(bool? value) => ArrowArrayBuilder.BuildBoolean(value);

    private static IArrowArray CreateInt8Array(sbyte? value) => ArrowArrayBuilder.BuildInt8(value);

    private static IArrowArray CreateUInt8Array(byte? value) => ArrowArrayBuilder.BuildUInt8(value);

    private static IArrowArray CreateInt16Array(short? value) => ArrowArrayBuilder.BuildInt16(value);

    private static IArrowArray CreateUInt16Array(ushort? value) => ArrowArrayBuilder.BuildUInt16(value);

    private static IArrowArray CreateInt32Array(int? value) => ArrowArrayBuilder.BuildInt32(value);

    private static IArrowArray CreateUInt32Array(uint? value) => ArrowArrayBuilder.BuildUInt32(value);

    private static IArrowArray CreateInt64Array(long? value) => ArrowArrayBuilder.BuildInt64(value);

    private static IArrowArray CreateUInt64Array(ulong? value) => ArrowArrayBuilder.BuildUInt64(value);

    private static IArrowArray CreateFloatArray(float? value) => ArrowArrayBuilder.BuildFloat(value);

    private static IArrowArray CreateDoubleArray(double? value) => ArrowArrayBuilder.BuildDouble(value);

#if NET8_0_OR_GREATER
    private static IArrowArray CreateHalfFloatArray(Half? value) => ArrowArrayBuilder.BuildHalfFloat(value);
#endif

    private static IArrowArray CreateStringArray(string? value) => ArrowArrayBuilder.BuildString(value);

    private static IArrowArray CreateBinaryArray(byte[]? value) => ArrowArrayBuilder.BuildBinary(value);

    private static Date32Array CreateDate32Array(object? value)
    {
        var builder = new Date32Array.Builder();
        if (value == null)
        {
            builder.AppendNull();
        }
        else if (value is int daysSinceEpoch)
        {
            var dt = new DateTime(1970, 1, 1).AddDays(daysSinceEpoch);
            builder.Append(dt);
        }
        else if (value is DateTime dt)
        {
            builder.Append(dt);
        }
#if NET6_0_OR_GREATER
        else if (value is DateOnly dateOnly)
        {
            builder.Append(dateOnly.ToDateTime(TimeOnly.MinValue));
        }
#endif
        else
        {
            builder.AppendNull();
        }
        return builder.Build();
    }

    private static Date64Array CreateDate64Array(object? value)
    {
        var builder = new Date64Array.Builder();
        if (value == null)
        {
            builder.AppendNull();
        }
        else if (value is long msSinceEpoch)
        {
            var dto = DateTimeOffset.FromUnixTimeMilliseconds(msSinceEpoch);
            builder.Append(dto);
        }
        else if (value is DateTime dt)
        {
            builder.Append(new DateTimeOffset(dt));
        }
        else
        {
            builder.AppendNull();
        }
        return builder.Build();
    }

    private static Time32Array CreateTime32Array(object? value, TimeUnit unit)
    {
        var builder = new Time32Array.Builder(new Time32Type(unit));
        if (value is int intValue)
        {
            builder.Append(intValue);
        }
        else
        {
            builder.AppendNull();
        }
        return builder.Build();
    }

    private static Time64Array CreateTime64Array(object? value, TimeUnit unit)
    {
        var builder = new Time64Array.Builder(new Time64Type(unit));
        if (value is long longValue)
        {
            builder.Append(longValue);
        }
#if NET6_0_OR_GREATER
        else if (value is TimeOnly timeOnly)
        {
            long ticks = unit switch
            {
                TimeUnit.Microsecond => timeOnly.Ticks / 10,
                TimeUnit.Nanosecond => timeOnly.Ticks * 100,
                _ => throw new ArgumentException($"Invalid time unit for Time64: {unit}")
            };
            builder.Append(ticks);
        }
#endif
        else
        {
            builder.AppendNull();
        }
        return builder.Build();
    }

    private static TimestampArray CreateTimestampArray(object? value, TimestampType type)
    {
        var builder = new TimestampArray.Builder(type);
        if (value == null)
        {
            builder.AppendNull();
        }
        else if (value is long longValue)
        {
            // longValue is already in the correct unit, convert to DateTimeOffset
            long ticks = type.Unit switch
            {
                TimeUnit.Second => longValue * TimeSpan.TicksPerSecond,
                TimeUnit.Millisecond => longValue * TimeSpan.TicksPerMillisecond,
                TimeUnit.Microsecond => longValue * 10,
                TimeUnit.Nanosecond => longValue / 100,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type.Unit, "Unsupported timestamp time unit")
            };
            var dto = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(ticks);
            builder.Append(dto);
        }
        else if (value is DateTime dt)
        {
            builder.Append(new DateTimeOffset(dt.ToUniversalTime()));
        }
        else if (value is DateTimeOffset dto)
        {
            builder.Append(dto);
        }
        else
        {
            builder.AppendNull();
        }
        return builder.Build();
    }

    private static Decimal128Array CreateDecimal128Array(object? value, Decimal128Type type)
    {
        var builder = new Decimal128Array.Builder(type);
        if (value is decimal dec)
        {
            builder.Append(dec);
        }
        else
        {
            builder.AppendNull();
        }
        return builder.Build();
    }

    private static Decimal256Array CreateDecimal256Array(object? value, Decimal256Type type)
    {
        var builder = new Decimal256Array.Builder(type);
        if (value is decimal dec)
        {
            builder.Append(dec);
        }
        else
        {
            builder.AppendNull();
        }
        return builder.Build();
    }

    // ============ Disposal ============

    /// <summary>
    /// Resolves the path to the native ADBC FlightSQL driver based on the current platform.
    /// Uses standard .NET conventions: AppContext.BaseDirectory and runtimes/{rid}/native/ layout.
    /// </summary>
    private static string ResolveNativeDriverPath()
    {
        var rid = GetRuntimeIdentifier();
        var driverFileName = GetDriverFileName();
        var driverPath = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", driverFileName);

        if (!File.Exists(driverPath))
        {
            throw new FileNotFoundException(
                $"Could not find native ADBC FlightSQL driver at {driverPath}. " +
                $"Ensure the SpiceAI NuGet package is properly installed.",
                driverPath);
        }

        return driverPath;
    }

    /// <summary>
    /// Gets the runtime identifier for the current platform.
    /// </summary>
    private static string GetRuntimeIdentifier()
    {
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"linux-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"osx-{arch}";

        throw new PlatformNotSupportedException(
            $"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// Gets the platform-specific driver filename.
    /// </summary>
    private static string GetDriverFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "libadbc_driver_flightsql.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "libadbc_driver_flightsql.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libadbc_driver_flightsql.dylib";

        throw new PlatformNotSupportedException(
            $"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            DisposalHelper.SafeDisposeAll(_connection, _database);
        }

        _disposed = true;
    }
}
