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

using Apache.Arrow;
using Apache.Arrow.Ipc;

namespace Spice.Query;

/// <summary>
/// An <see cref="IArrowArrayStream"/> over record batches that have already been fully
/// decoded into memory, used to hand back the chunks fetched for an async query's results.
/// </summary>
internal sealed class InMemoryRecordBatchStream : IArrowArrayStream
{
    private readonly IReadOnlyList<RecordBatch> _batches;
    private int _index;

    internal InMemoryRecordBatchStream(Schema schema, IReadOnlyList<RecordBatch> batches)
    {
        Schema = schema;
        _batches = batches;
    }

    public Schema Schema { get; }

    public ValueTask<RecordBatch> ReadNextRecordBatchAsync(CancellationToken cancellationToken = default)
    {
        var batch = _index < _batches.Count ? _batches[_index++] : null;
        return new ValueTask<RecordBatch>(batch!);
    }

    public void Dispose()
    {
        foreach (var batch in _batches)
        {
            batch.Dispose();
        }
    }
}
