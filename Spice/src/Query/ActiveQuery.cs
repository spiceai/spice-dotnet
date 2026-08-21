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

using System.Text.Json.Serialization;

namespace Spice.Query;

/// <summary>
/// A synchronous query currently running on the Spice runtime.
/// </summary>
/// <remarks>
/// Synchronous queries are the ones started by <see cref="SpiceClient.SqlAsync"/>,
/// <see cref="SpiceClient.SqlWithParamsAsync"/>, FlightSQL, NSQL, and search. The runtime does not
/// return a query's ID to the client that submitted it, so <see cref="SpiceClient.ListActiveQueriesAsync"/>
/// is how to find the ID that <see cref="SpiceClient.CancelActiveQueryAsync"/> needs.
/// </remarks>
public class ActiveQuery
{
    /// <summary>
    /// The ID the runtime assigned to this query. Pass this to
    /// <see cref="SpiceClient.CancelActiveQueryAsync"/> to cancel it.
    /// </summary>
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;

    /// <summary>
    /// The protocol the query arrived on: <c>"http"</c>, <c>"flight"</c>, <c>"flightsql"</c>, or
    /// <c>"internal"</c>.
    /// </summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    /// <summary>
    /// The query's SQL, truncated by the runtime for display.
    /// </summary>
    [JsonPropertyName("sql_preview")]
    public string SqlPreview { get; set; } = string.Empty;

    /// <summary>
    /// When the query started, in milliseconds since the Unix epoch.
    /// </summary>
    [JsonPropertyName("started_at_ms")]
    public long StartedAtMs { get; set; }

    /// <summary>
    /// The query's start time.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset StartedAt => DateTimeOffset.FromUnixTimeMilliseconds(StartedAtMs);
}

/// <summary>
/// The wire envelope returned by <c>GET /v1/sql/active</c>.
/// </summary>
internal sealed class ActiveQueriesResponse
{
    [JsonPropertyName("queries")]
    public List<ActiveQuery> Queries { get; set; } = new();

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}

/// <summary>
/// The wire envelope returned by <c>POST /v1/sql/{id}/cancel</c>.
/// </summary>
internal sealed class CancelActiveQueryResponse
{
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
