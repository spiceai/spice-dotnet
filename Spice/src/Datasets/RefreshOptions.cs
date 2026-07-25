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

using System.Globalization;
using System.Text.Json;

namespace Spice.Datasets;

/// <summary>
/// The refresh mode to use for a single on-demand dataset refresh.
/// </summary>
/// <remarks>
/// On-demand refreshes apply to the <c>full</c> and <c>append</c> refresh modes only.
/// Datasets accelerated with <c>changes</c> mode are kept up to date by change data
/// capture and are not refreshed through this API.
/// </remarks>
public enum RefreshMode
{
    /// <summary>
    /// Replace the accelerated data with the full result of the refresh query.
    /// </summary>
    Full,

    /// <summary>
    /// Append newly returned rows to the accelerated data.
    /// </summary>
    Append,
}

/// <summary>
/// Optional overrides for a single on-demand dataset acceleration refresh.
/// </summary>
/// <remarks>
/// Every option is optional. Any option left unset falls back to the value configured
/// for the dataset in the Spicepod. An instance with no options set requests a refresh
/// using the dataset's existing configuration.
/// </remarks>
/// <example>
/// <code>
/// var options = new RefreshOptions()
///     .WithRefreshSql("SELECT * FROM taxi_trips WHERE tip_amount &gt; 10.0")
///     .WithRefreshMode(RefreshMode.Append)
///     .WithMaxJitter(TimeSpan.FromSeconds(10));
///
/// await client.RefreshDatasetAsync("taxi_trips", options);
/// </code>
/// </example>
public sealed class RefreshOptions
{
    /// <summary>
    /// The SQL statement used for this refresh. Defaults to the <c>refresh_sql</c>
    /// configured for the dataset, if any.
    /// </summary>
    public string? RefreshSql { get; set; }

    /// <summary>
    /// The refresh mode to use for this refresh. Defaults to the <c>refresh_mode</c>
    /// configured for the dataset, or <see cref="Datasets.RefreshMode.Full"/>.
    /// </summary>
    public RefreshMode? RefreshMode { get; set; }

    /// <summary>
    /// The maximum amount of jitter to add before starting this refresh. Defaults to the
    /// <c>refresh_jitter_max</c> configured for the dataset, or 10% of the refresh check interval.
    /// </summary>
    public TimeSpan? MaxJitter { get; set; }

    /// <summary>
    /// Sets the SQL statement used for this refresh.
    /// </summary>
    /// <param name="refreshSql">The refresh SQL statement.</param>
    /// <returns>The current instance of <see cref="RefreshOptions"/> for method chaining.</returns>
    /// <exception cref="System.ArgumentException">Thrown when refreshSql is null or whitespace.</exception>
    public RefreshOptions WithRefreshSql(string refreshSql)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshSql);
#else
        ThrowHelper.ThrowIfNullOrWhiteSpace(refreshSql, nameof(refreshSql));
#endif
        RefreshSql = refreshSql;
        return this;
    }

    /// <summary>
    /// Sets the refresh mode to use for this refresh.
    /// </summary>
    /// <param name="refreshMode">The refresh mode.</param>
    /// <returns>The current instance of <see cref="RefreshOptions"/> for method chaining.</returns>
    public RefreshOptions WithRefreshMode(RefreshMode refreshMode)
    {
        RefreshMode = refreshMode;
        return this;
    }

    /// <summary>
    /// Sets the maximum amount of jitter to add before starting this refresh.
    /// </summary>
    /// <param name="maxJitter">The maximum jitter. Must not be negative.</param>
    /// <returns>The current instance of <see cref="RefreshOptions"/> for method chaining.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">Thrown when maxJitter is negative.</exception>
    public RefreshOptions WithMaxJitter(TimeSpan maxJitter)
    {
        if (maxJitter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxJitter), maxJitter, "maxJitter must not be negative.");
        }

        MaxJitter = maxJitter;
        return this;
    }

    /// <summary>
    /// Serializes the options to the JSON request body expected by the Spice runtime.
    /// Unset options are omitted so the runtime falls back to the dataset configuration.
    /// </summary>
    internal string ToJson()
    {
        var body = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(RefreshSql))
        {
            body["refresh_sql"] = RefreshSql!;
        }

        if (RefreshMode.HasValue)
        {
            body["refresh_mode"] = ToWireValue(RefreshMode.Value);
        }

        if (MaxJitter.HasValue)
        {
            body["refresh_jitter_max"] = FormatDuration(MaxJitter.Value);
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// Maps a <see cref="Datasets.RefreshMode"/> to the value understood by the runtime.
    /// </summary>
    internal static string ToWireValue(RefreshMode refreshMode) => refreshMode switch
    {
        Datasets.RefreshMode.Full => "full",
        Datasets.RefreshMode.Append => "append",
        _ => throw new ArgumentOutOfRangeException(nameof(refreshMode), refreshMode, "Unsupported refresh mode."),
    };

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a duration string the runtime can parse (for example "10s" or "1500ms").
    /// </summary>
    internal static string FormatDuration(TimeSpan value)
    {
        var totalMilliseconds = (long)value.TotalMilliseconds;

        return totalMilliseconds % 1000 == 0
            ? string.Concat((totalMilliseconds / 1000).ToString(CultureInfo.InvariantCulture), "s")
            : string.Concat(totalMilliseconds.ToString(CultureInfo.InvariantCulture), "ms");
    }
}
