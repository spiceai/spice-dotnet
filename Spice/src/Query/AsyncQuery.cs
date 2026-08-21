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
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Grpc.Core;
using Spice.Flight;

namespace Spice.Query;

/// <summary>
/// A handle to a query submitted for asynchronous execution via
/// <see cref="SpiceClient.QueryAsync"/> or <see cref="SpiceClient.QueryWithParamsAsync"/>.
/// </summary>
/// <remarks>
/// Not safe for concurrent use from multiple threads.
/// </remarks>
public sealed class AsyncQuery
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly SpiceFlightClient _client;

    internal AsyncQuery(SpiceFlightClient client, string queryId, QueryStatus status)
    {
        _client = client;
        Id = queryId;
        Status = status;
    }

    /// <summary>
    /// The server-assigned query identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The status as of the last poll. Set on construction and refreshed by
    /// <see cref="GetStatusAsync"/>, <see cref="WaitAsync"/>, <see cref="GetResultsAsync"/>
    /// and <see cref="CancelAsync"/>.
    /// </summary>
    public QueryStatus Status { get; private set; }

    /// <summary>
    /// Polls the runtime once and returns the query's current status.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the request</param>
    /// <returns>The current status</returns>
    public async Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await PollStatusAsync(cancellationToken).ConfigureAwait(false);
        return response.Status;
    }

    /// <summary>
    /// Polls the runtime until the query reaches a terminal status (Succeeded, Failed,
    /// Cancelled, or Closed) or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel waiting</param>
    /// <returns>The terminal status</returns>
    public async Task<QueryStatus> WaitAsync(CancellationToken cancellationToken = default)
    {
        var response = await WaitForTerminalAsync(cancellationToken).ConfigureAwait(false);
        return response.Status;
    }

    /// <summary>
    /// Waits for the query to complete and returns its results as an Apache Arrow stream.
    /// The caller must dispose the returned stream.
    /// </summary>
    /// <remarks>
    /// If the query does not succeed, this throws an <see cref="InvalidOperationException"/>
    /// describing the terminal status, including the runtime's error message when available.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel waiting and fetching</param>
    /// <returns>The query's results</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the query did not succeed</exception>
    public async Task<IArrowArrayStream> GetResultsAsync(CancellationToken cancellationToken = default)
    {
        var statusResponse = await WaitForTerminalAsync(cancellationToken).ConfigureAwait(false);

        if (statusResponse.Status != QueryStatus.Succeeded)
        {
            var reason = statusResponse.Error?.ToString();
            var detail = string.IsNullOrEmpty(reason) ? $"status: {statusResponse.Status}" : reason;
            throw new InvalidOperationException($"Async query {Id} did not succeed ({detail}).");
        }

        var chunkCount = statusResponse.Result?.TotalChunkCount ?? 0;
        // Always fetch at least once so a genuinely empty result still yields a schema.
        var fetchCount = Math.Max(chunkCount, 1);

        Schema? schema = null;
        var batches = new List<RecordBatch>();

        for (var i = 0; i < fetchCount; i++)
        {
            byte[] chunkBytes;
            try
            {
                chunkBytes = await _client.DoActionAsync(
                    AsyncQueryActions.GetResult,
                    new GetAsyncQueryResultRequest { QueryId = Id, ChunkIndex = i },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException) when (i == 0 && chunkCount == 0)
            {
                // A missing chunk 0 on a genuinely empty result is not an error.
                break;
            }

            using var chunkStream = new MemoryStream(chunkBytes);
            using var reader = new ArrowStreamReader(chunkStream);

            RecordBatch? batch;
            while ((batch = await reader.ReadNextRecordBatchAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                schema ??= reader.Schema;
                batches.Add(batch);
            }
        }

        schema ??= new Schema.Builder().Build();
        return new InMemoryRecordBatchStream(schema, batches);
    }

    /// <summary>
    /// Requests cancellation of the query.
    /// </summary>
    /// <remarks>
    /// Best-effort: a query that has already reached a terminal status is not cancelled,
    /// which is not reported as an error. Inspect <see cref="Status"/> after this call to
    /// observe the outcome.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the request</param>
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        var body = await _client.DoActionAsync(
            AsyncQueryActions.Cancel,
            new CancelAsyncQueryRequest { QueryId = Id },
            cancellationToken).ConfigureAwait(false);

        var response = JsonSerializer.Deserialize<CancelAsyncQueryResponse>(body)
            ?? throw new InvalidOperationException("Failed to parse the cancel-async-query response.");

        Status = response.Status;
    }

    private async Task<GetAsyncQueryStatusResponse> PollStatusAsync(CancellationToken cancellationToken)
    {
        var body = await _client.DoActionAsync(
            AsyncQueryActions.GetStatus,
            new GetAsyncQueryStatusRequest { QueryId = Id },
            cancellationToken).ConfigureAwait(false);

        var response = JsonSerializer.Deserialize<GetAsyncQueryStatusResponse>(body)
            ?? throw new InvalidOperationException("Failed to parse the async query status response.");

        Status = response.Status;
        return response;
    }

    private async Task<GetAsyncQueryStatusResponse> WaitForTerminalAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await PollStatusAsync(cancellationToken).ConfigureAwait(false);
            if (response.Status.IsTerminal())
            {
                return response;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
