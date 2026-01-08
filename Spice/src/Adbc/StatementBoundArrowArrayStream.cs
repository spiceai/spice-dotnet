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

using System;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Adbc;
using Apache.Arrow.Ipc;

namespace Spice.Adbc;

/// <summary>
/// A wrapper around IArrowArrayStream that keeps the ADBC statement alive
/// for the lifetime of the stream. When this wrapper is disposed, it disposes
/// both the underlying stream and the statement in the correct order.
/// </summary>
internal sealed class StatementBoundArrowArrayStream : IArrowArrayStream
{
    private readonly IArrowArrayStream _innerStream;
    private readonly AdbcStatement _statement;
    private bool _disposed;

    /// <summary>
    /// Creates a new StatementBoundArrowArrayStream that wraps the given stream
    /// and keeps the statement alive until disposal.
    /// </summary>
    /// <param name="innerStream">The underlying Arrow array stream</param>
    /// <param name="statement">The ADBC statement to keep alive and dispose with the stream</param>
    public StatementBoundArrowArrayStream(IArrowArrayStream innerStream, AdbcStatement statement)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _statement = statement ?? throw new ArgumentNullException(nameof(statement));
    }

    /// <inheritdoc/>
    public Schema Schema => _innerStream.Schema;

    /// <inheritdoc/>
    public ValueTask<RecordBatch?> ReadNextRecordBatchAsync(CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
#endif
        return _innerStream.ReadNextRecordBatchAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose in the correct order:
        // 1. First dispose the stream (finishes reading/closes the stream)
        // 2. Then dispose the statement (releases server-side resources)
        try
        {
            _innerStream.Dispose();
        }
        finally
        {
            _statement.Dispose();
        }
    }
}