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
/// The Flight DoAction action types the runtime serves for async queries. Only available
/// when the runtime is running in distributed/scheduler mode.
/// </summary>
internal static class AsyncQueryActions
{
    internal const string Submit = "SubmitAsyncQuery";
    internal const string GetStatus = "GetAsyncQueryStatus";
    internal const string GetResult = "GetAsyncQueryResult";
    internal const string Cancel = "CancelAsyncQuery";
}

internal sealed class SubmitAsyncQueryRequest
{
    [JsonPropertyName("sql")]
    public string Sql { get; set; } = string.Empty;

    // Positionally-bound parameter values ($1, $2, ...), sent as-is so each must already be a
    // JSON-encodable value.
    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Parameters { get; set; }
}

internal sealed class SubmitAsyncQueryResponse
{
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public QueryStatus Status { get; set; }
}

internal sealed class GetAsyncQueryStatusRequest
{
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;
}

/// <summary>
/// The runtime's description of why an async query did not succeed.
/// </summary>
internal sealed class AsyncQueryError
{
    [JsonPropertyName("error_code")]
    public JsonElement ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    public override string ToString()
    {
        var code = ErrorCode.ValueKind == JsonValueKind.String ? ErrorCode.GetString() : null;
        return string.IsNullOrEmpty(code) ? Message : $"{code}: {Message}";
    }
}

internal sealed class AsyncQueryResultMetadata
{
    [JsonPropertyName("total_row_count")]
    public long TotalRowCount { get; set; }

    [JsonPropertyName("total_chunk_count")]
    public int TotalChunkCount { get; set; }
}

internal sealed class GetAsyncQueryStatusResponse
{
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public QueryStatus Status { get; set; }

    [JsonPropertyName("error")]
    public AsyncQueryError? Error { get; set; }

    [JsonPropertyName("result")]
    public AsyncQueryResultMetadata? Result { get; set; }
}

internal sealed class GetAsyncQueryResultRequest
{
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }
}

internal sealed class CancelAsyncQueryRequest
{
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;
}

internal sealed class CancelAsyncQueryResponse
{
    [JsonPropertyName("query_id")]
    public string QueryId { get; set; } = string.Empty;

    [JsonPropertyName("cancelled")]
    public bool Cancelled { get; set; }

    [JsonPropertyName("status")]
    public QueryStatus Status { get; set; }
}
