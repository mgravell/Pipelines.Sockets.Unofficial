﻿#if SOCKET_STREAM_BUFFERS
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class StreamConnector
    {
        partial class AsyncPipeStream
        {
            /// <summary>
            /// Write a span to the pipe
            /// </summary>
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamWriteSpan);
                WriteImpl(buffer);
                FlushImpl();
            }
            /// <summary>
            /// Red a span from the pipe
            /// </summary>
            public override int Read(Span<byte> buffer)
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamReadSpan);
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    pendingRead.AssertAvailable();
                    if (buffer.IsEmpty) return 0;

                    if (_reader.TryRead(out var result))
                    {
                        return ConsumeBytes(result, buffer);
                    }
                }
                // slow way, then
                var arr = ArrayPool<byte>.Shared.Rent(buffer.Length);
                int bytes = Read(new Memory<byte>(arr, 0, buffer.Length));
                if(bytes != 0)
                {
                    new Span<byte>(arr, 0, bytes).CopyTo(buffer);
                }
                return bytes;
            }

            /// <summary>
            /// Read a span from the pipe
            /// </summary>
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                DebugLog("init");
                Helpers.Incr(Counter.PipeStreamReadAsyncMemory);
                return ReadAsyncImpl(buffer, cancellationToken);
            }
            /// <summary>
            /// Write a span to the pipe
            /// </summary>
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamWriteAsyncMemory);
                WriteImpl(buffer.Span);

                var flush = _writer.FlushAsync(cancellationToken);
                return flush.IsCompletedSuccessfully ? default : new ValueTask(flush.AsTask());
            }
        }
    }
}
#endif