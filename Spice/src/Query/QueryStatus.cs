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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spice.Query;

/// <summary>
/// The lifecycle status of an async query, as reported by the Spice runtime.
/// </summary>
[JsonConverter(typeof(QueryStatusJsonConverter))]
public enum QueryStatus
{
    /// <summary>The query has been submitted but has not started running.</summary>
    Pending,

    /// <summary>The query is running.</summary>
    Running,

    /// <summary>The query finished successfully and its results are available.</summary>
    Succeeded,

    /// <summary>The query failed. See <see cref="AsyncQuery.GetResultsAsync"/> for the runtime's error message.</summary>
    Failed,

    /// <summary>The query was cancelled before it finished.</summary>
    Cancelled,

    /// <summary>The query's results have been released by the runtime.</summary>
    Closed,
}

/// <summary>
/// Extension methods for <see cref="QueryStatus"/>.
/// </summary>
public static class QueryStatusExtensions
{
    /// <summary>
    /// Reports whether the status is terminal, i.e. the query will not transition further.
    /// </summary>
    /// <param name="status">The status to check</param>
    /// <returns>True when the query has finished, failed, or been cancelled or closed</returns>
    public static bool IsTerminal(this QueryStatus status) =>
        status is QueryStatus.Succeeded or QueryStatus.Failed or QueryStatus.Cancelled or QueryStatus.Closed;
}

/// <summary>
/// Converts <see cref="QueryStatus"/> to and from the runtime's uppercase wire representation
/// (for example <c>"SUCCEEDED"</c>).
/// </summary>
internal sealed class QueryStatusJsonConverter : JsonConverter<QueryStatus>
{
    public override QueryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "PENDING" => QueryStatus.Pending,
            "RUNNING" => QueryStatus.Running,
            "SUCCEEDED" => QueryStatus.Succeeded,
            "FAILED" => QueryStatus.Failed,
            "CANCELLED" => QueryStatus.Cancelled,
            "CLOSED" => QueryStatus.Closed,
            _ => throw new JsonException($"Unrecognized query status \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, QueryStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            QueryStatus.Pending => "PENDING",
            QueryStatus.Running => "RUNNING",
            QueryStatus.Succeeded => "SUCCEEDED",
            QueryStatus.Failed => "FAILED",
            QueryStatus.Cancelled => "CANCELLED",
            QueryStatus.Closed => "CLOSED",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported query status."),
        });
    }
}
