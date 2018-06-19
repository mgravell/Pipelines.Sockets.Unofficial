using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class StreamConnector
    {
        private sealed class AsyncStreamPipe : IDuplexPipe
        {
            private readonly Pipe _readPipe, _writePipe;
            private readonly Stream _inner;
            Task _writeTask, _readTask;

            public AsyncStreamPipe(Stream stream, PipeOptions pipeOptions, bool read, bool write)
            {
                _inner = stream ?? throw new ArgumentNullException(nameof(stream));

                if (!(read || write)) throw new ArgumentException("At least one of read/write must be set");
                if (read)
                {
                    if (!stream.CanWrite) throw new InvalidOperationException("Cannot create a read pipe over a non-writable stream");
                    _readPipe = new Pipe(pipeOptions ?? PipeOptions.Default);
                }
                if (write)
                {
                    if (!stream.CanRead) throw new InvalidOperationException("Cannot create a write pipe over a non-readable stream");
                    _writePipe = new Pipe(pipeOptions ?? PipeOptions.Default);
                }
            }

            public PipeWriter Output
            {
                get
                {
                    if (_writeTask == null)
                    {
                        if (_writePipe == null) throw new InvalidOperationException("Cannot write to this pipe");
                        _writeTask = CopyFromWritePipeToStream();
                    }
                    return _writePipe.Writer;
                }
            }
            public PipeReader Input
            {
                get
                {
                    if (_readTask == null)
                    {
                        if (_readPipe == null) throw new InvalidOperationException("Cannot read from this pipe");
                        _readTask = CopyFromStreamToReadPipe();
                    }
                    return _readPipe.Reader;
                }
            }
            private async Task CopyFromStreamToReadPipe()
            {
                var writer = _readPipe.Writer;
                while (true)
                {
                    var arr = GetArray(writer.GetMemory(1));

                    int read = await _inner.ReadAsync(arr.Array, arr.Offset, arr.Count);

                    if (read <= 0) break;

                    writer.Advance(read);
                    await writer.FlushAsync();
                }
            }
            private async Task CopyFromWritePipeToStream()
            {
                var reader = _writePipe.Reader;
                while (true)
                {
                    if (!reader.TryRead(out var result))
                    {
                        result = await reader.ReadAsync();
                    }
                    if (result.Buffer.IsEmpty && result.IsCompleted)
                        break;
                    await WriteBuffer(_inner, result.Buffer);
                    reader.AdvanceTo(result.Buffer.End);
                }
            }
            static ArraySegment<byte> GetArray(ReadOnlyMemory<byte> memory)
            {
                if (!MemoryMarshal.TryGetArray<byte>(MemoryMarshal.AsMemory<byte>(memory), out var arr))
                    throw new InvalidOperationException("Cannot obtain array");
                return arr;
            }
            static ArraySegment<byte> GetArray(Memory<byte> memory)
            {
                if (!MemoryMarshal.TryGetArray<byte>(memory, out var arr))
                    throw new InvalidOperationException("Cannot obtain array");
                return arr;
            }

            static Task WriteBuffer(Stream target, ReadOnlySequence<byte> data)
            {
                async Task WriteBufferAwaited(Stream ttarget, ReadOnlySequence<byte> ddata)
                {
                    foreach (var segment in ddata)
                    {
                        if (!MemoryMarshal.TryGetArray<byte>(segment, out var arr))
                            throw new InvalidOperationException("Cannot obtain array");

                        await ttarget.WriteAsync(arr.Array, arr.Offset, arr.Count);
                    }
                }
                if (data.IsSingleSegment)
                {
                    var arr = GetArray(data.First);
                    return target.WriteAsync(arr.Array, arr.Offset, arr.Count);
                }
                else
                {
                    return WriteBufferAwaited(target, data);
                }
            }
        }

    }
}
