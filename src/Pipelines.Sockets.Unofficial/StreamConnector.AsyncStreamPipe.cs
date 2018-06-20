using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class StreamConnector
    {
        private sealed class AsyncStreamPipe : IDuplexPipe
        {


            [Conditional("VERBOSE")]
            private void DebugLog(string message = null, [CallerMemberName] string caller = null) => Helpers.DebugLog(Name, message, caller);

            private readonly Pipe _readPipe, _writePipe;
            private readonly Stream _inner;
            private string Name { get; }
            Task _writeTask, _readTask;

            public override string ToString() => Name;


            public AsyncStreamPipe(Stream stream, PipeOptions pipeOptions, bool read, bool write, string name)
            {
                _inner = stream ?? throw new ArgumentNullException(nameof(stream));
                if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
                Name = name ?? GetType().Name;

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
                Exception err = null;
                var writer = _readPipe.Writer;
                try
                {
                    while (true)
                    {
                        var arr = GetArray(writer.GetMemory(1));

                        int read = await _inner.ReadAsync(arr.Array, arr.Offset, arr.Count);

                        if (read <= 0) break;

                        writer.Advance(read);
                        DebugLog($"");
                        await writer.FlushAsync();
                    }
                }
                catch (Exception ex)
                {
                    err = ex;
                }
                writer.Complete(err);
            }
            private async Task CopyFromWritePipeToStream()
            {
                var reader = _writePipe.Reader;
                while (true)
                {
                    DebugLog(nameof(reader.TryRead));
                    if (!reader.TryRead(out var result))
                    {
                        DebugLog(nameof(reader.ReadAsync));
                        result = await reader.ReadAsync();
                    }
                    DebugLog($"complete: {result.IsCompleted}; canceled: {result.IsCanceled}; bytes: {result.Buffer.Length}");

                    if (result.Buffer.IsEmpty && result.IsCompleted)
                        break;

                    await WriteBuffer(_inner, result.Buffer, Name);
                    DebugLog($"bytes written; marking consumed");
                    reader.AdvanceTo(result.Buffer.End);

                    DebugLog($"flushing stream...");
                    await _inner.FlushAsync();
                    DebugLog($"flushed");
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

            static Task WriteBuffer(Stream target, ReadOnlySequence<byte> data, string name)
            {
                async Task WriteBufferAwaited(Stream ttarget, ReadOnlySequence<byte> ddata, string nname)
                {
                    foreach (var segment in ddata)
                    {
                        var arr = GetArray(segment);
                        Helpers.DebugLog(name, $"writing {arr.Count} bytes to '{target}'...");
                        await ttarget.WriteAsync(arr.Array, arr.Offset, arr.Count);
                        Helpers.DebugLog(name, $"write complete");
                    }
                }
                if (data.IsSingleSegment)
                {
                    var arr = GetArray(data.First);
                    Helpers.DebugLog(name, $"writing {arr.Count} bytes to '{target}'...");
                    return target.WriteAsync(arr.Array, arr.Offset, arr.Count);
                }
                else
                {
                    return WriteBufferAwaited(target, data, name);
                }
            }
        }

    }
}
