using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public static partial class StreamConnection
    {
        private sealed class AsyncStreamPipe : IMeasuredDuplexPipe
        {
            [Conditional("VERBOSE")]
            private void DebugLog(string message = null, [CallerMemberName] string caller = null) => Helpers.DebugLog(Name, message, caller);

            private readonly Pipe _readPipe, _writePipe;
            private readonly Stream _inner;
            private string Name { get; }

            public override string ToString() => Name;

            public AsyncStreamPipe(Stream stream, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions, bool read, bool write, string name)
            {
                if (sendPipeOptions == null) sendPipeOptions = PipeOptions.Default;
                if (receivePipeOptions == null) receivePipeOptions = PipeOptions.Default;
                if (stream == null) Throw.ArgumentNull(nameof(stream));
                _inner = stream;
                if (!(read || write)) Throw.Argument("At least one of read/write must be set");
                if (read && write)
                {
                    string preamble = "";
                    switch(stream)
                    {
                        case MemoryStream ms: // extra guidance in this case; there's a reaily available alternative
                            preamble = "You probably want `new Pipe()` instead - a `Pipe` is broadly comparable to a MemoryStream with separate read/write heads. ";
                            goto ThrowNonDuplexStream;
                        case FileStream fs:
                            // others here?
                            ThrowNonDuplexStream:
                            Throw.Argument(preamble + $"`{stream.GetType().Name}` is not a duplex stream and cannot be used in this context (you can still create a reader/writer).", nameof(stream));
                            return;
                    }
                }

                if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
                Name = name ?? GetType().Name;
                if (read)
                {
                    if (!stream.CanRead) Throw.InvalidOperation("Cannot create a read pipe over a non-readable stream");
                    _readPipe = new Pipe(receivePipeOptions);
                    receivePipeOptions.ReaderScheduler.Schedule(obj => ((AsyncStreamPipe)obj).CopyFromStreamToReadPipe().PipelinesFireAndForget(), this);
                }
                if (write)
                {
                    if (!stream.CanWrite) Throw.InvalidOperation("Cannot create a write pipe over a non-writable stream");
                    _writePipe = new Pipe(sendPipeOptions);
                    sendPipeOptions.WriterScheduler.Schedule(obj => ((AsyncStreamPipe)obj).CopyFromWritePipeToStream().PipelinesFireAndForget(), this);
                }
            }

            public PipeWriter Output
            {
                get
                {
                    var result = _writePipe?.Writer;
                    if (result == null) Throw.InvalidOperation("Cannot write to this pipe");
                    return result;
                }
            }
            public PipeReader Input
            {
                get
                {
                    var result = _readPipe?.Reader;
                    if (result == null) Throw.InvalidOperation("Cannot read from this pipe");
                    return result;
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
                        var memory = writer.GetMemory(1);
#if SOCKET_STREAM_BUFFERS
                        int read = await _inner.ReadAsync(memory).ConfigureAwait(false);
#else
                        var arr = memory.GetArray();
                        int read = await _inner.ReadAsync(arr.Array, arr.Offset, arr.Count).ConfigureAwait(false);
#endif
                        if (read <= 0) break;
                        writer.Advance(read);
                        Interlocked.Add(ref _totalBytesSent, read);

                        // need to flush regularly, a: to respect backoffs, and b: to awaken the reader
                        var flush = await writer.FlushAsync().ConfigureAwait(false);
                        if (flush.IsCompleted || flush.IsCanceled) break;
                    }
                }
                catch (Exception ex)
                {
                    err = ex;
                }
                writer.Complete(err);
            }

            private long _totalBytesSent, _totalBytesReceived;

            long IMeasuredDuplexPipe.TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
            long IMeasuredDuplexPipe.TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);

            private async Task CopyFromWritePipeToStream()
            {
                var reader = _writePipe.Reader;
                try
                {
                    while (true)
                    {
                        DebugLog(nameof(reader.ReadAsync));
                        // ask to be awakened by work
                        var pending = reader.ReadAsync();
                        if (!pending.IsCompleted)
                        {
                            // then: not currently anything to do synchronously; this
                            // would be a great time to flush! this *could*
                            // result in over-flushing if reader and writer
                            // are *just about* in sync, but... it'll do
                            DebugLog($"flushing stream...");
                            await _inner.FlushAsync().ConfigureAwait(false);
                            DebugLog($"flushed");
                        }
                        var result = await pending;
                        ReadOnlySequence<byte> buffer;
                        do
                        {
                            buffer = result.Buffer;
                            DebugLog($"complete: {result.IsCompleted}; canceled: {result.IsCanceled}; bytes: {buffer.Length}");
                            if (!buffer.IsEmpty)
                            {
                                await WriteBuffer(_inner, buffer, Name).ConfigureAwait(false);
                                Interlocked.Add(ref _totalBytesReceived, buffer.Length);
                                DebugLog($"bytes written; marking consumed");
                            }
                            reader.AdvanceTo(buffer.End);
                        } while (!(buffer.IsEmpty && result.IsCompleted)
                            && reader.TryRead(out result));

                        if (result.IsCanceled) break;
                        if (buffer.IsEmpty && result.IsCompleted) break; // that's all, folks
                    }
                    try { reader.Complete(null); } catch { }
                }
                catch (Exception ex)
                {
                    try { reader.Complete(ex); } catch { }
                }
            }

            private static Task WriteBuffer(Stream target, in ReadOnlySequence<byte> data, string name)
            {
                static async Task WriteBufferAwaited(Stream ttarget, ReadOnlySequence<byte> ddata, string nname)
                {
                    foreach (var segment in ddata)
                    {
                        Helpers.DebugLog(nname, $"writing {segment.Length} bytes to '{ttarget}'...");
#if SOCKET_STREAM_BUFFERS
                        await ttarget.WriteAsync(segment);
#else
                        var arr = segment.GetArray();
                        await ttarget.WriteAsync(arr.Array, arr.Offset, arr.Count).ConfigureAwait(false);
#endif
                        Helpers.DebugLog(nname, $"write complete");
                    }
                }
                if (data.IsSingleSegment)
                {
                    Helpers.DebugLog(name, $"writing {data.Length} bytes to '{target}'...");
#if SOCKET_STREAM_BUFFERS
                    var vt = target.WriteAsync(data.First);
                    return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
#else
                    var arr = data.First.GetArray();
                    return target.WriteAsync(arr.Array, arr.Offset, arr.Count);
#endif
                }
                else
                {
                    return WriteBufferAwaited(target, data, name);
                }
            }
        }
    }
}
