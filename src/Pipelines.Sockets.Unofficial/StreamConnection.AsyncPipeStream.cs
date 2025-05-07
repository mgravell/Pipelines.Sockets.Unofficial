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
        /// <summary>
        /// Exposes a Stream as a duplex pipe
        /// </summary>
        public sealed partial class AsyncPipeStream : Stream
        {
            private string Name { get; }
            /// <summary>
            /// Gets a string representation of this object
            /// </summary>
            public override string ToString() => Name;

            private AsyncReadResult _pendingRead;
            private AsyncWriteResult _pendingWrite;
            private FlushToken _flushInstance;
            private AsyncReadResult PendingRead => _pendingRead ??= new AsyncReadResult();
            private AsyncWriteResult PendingWrite => _pendingWrite ??= new AsyncWriteResult();
            private FlushToken FlushInstance => _flushInstance ??= new FlushToken();

            private Action _processDataFromAwaiter;
            private Action ProcessDataFromAwaiter => _processDataFromAwaiter ??= ProcessDataFromAwaiterImpl;

            private readonly PipeReader _reader;
            private readonly PipeWriter _writer;

            internal AsyncPipeStream(PipeReader reader, PipeWriter writer, string name)
            {
                if (reader is null && writer is null)
                    Throw.ArgumentNull("At least one of reader/writer must be provided");
                _reader = reader;
                _writer = writer;

                if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
                Name = name.Trim();
            }
            /// <summary>
            /// Gets whether read operations are available
            /// </summary>
            public override bool CanRead => _reader is not null;
            /// <summary>
            /// Gets whether write operations are available
            /// </summary>
            public override bool CanWrite => _writer is not null;
            /// <summary>
            /// Gets whether the stream can timeout
            /// </summary>
            public override bool CanTimeout => false;
            /// <summary>
            /// Gets whether the seek operations are supported on this stream
            /// </summary>
            public override bool CanSeek => false;
            /// <summary>
            /// Change the position of the stream
            /// </summary>
            public override long Seek(long offset, SeekOrigin origin) { Throw.NotSupported(); return default; }
            /// <summary>
            /// Query the length of the stream
            /// </summary>
            public override long Length { get { Throw.NotSupported(); return default; } }
            /// <summary>
            /// Get or set the position of the stream
            /// </summary>
            public override long Position
            {
                get { Throw.NotSupported(); return default; }
                set => Throw.NotSupported();
            }
            /// <summary>
            /// Specify the length of the stream
            /// </summary>
            public override void SetLength(long value) => Throw.NotSupported();

            private void AssertCanRead() { if (_reader is null) Throw.InvalidOperation("Cannot read"); }
            private void AssertCanWrite() { if (_writer is null) Throw.InvalidOperation("Cannot write"); }

            [Conditional("VERBOSE")]
            private void DebugLog(string message = null, [CallerMemberName] string caller = null) => Helpers.DebugLog(Name, message, caller);

            /// <summary>
            /// Read a buffer from the stream
            /// </summary>
            public override int Read(byte[] buffer, int offset, int count)
            {
                DebugLog();
                AssertCanRead();
                Helpers.Incr(Counter.PipeStreamRead);
                return Read(new Memory<byte>(buffer, offset, count));
            }
#pragma warning disable RCS1231 // Make parameter ref read-only.
            private int Read(Memory<byte> memory)
#pragma warning restore RCS1231 // Make parameter ref read-only.
            {
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    pendingRead.AssertAvailable();
                    if (memory.IsEmpty) return 0;

                    if (_reader.TryRead(out var result))
                    {
                        return ConsumeBytes(result, memory.Span);
                    }
                    var pending = _reader.ReadAsync();
                    if (pending.IsCompleted)
                    {
                        return ConsumeBytes(pending.Result, memory.Span);
                    }
                    pendingRead.Init(null, null, memory, null, PendingAsyncMode.Synchronous);
                    pendingRead.ReadAwaiter = pending.GetAwaiter();
                    pendingRead.ReadAwaiter.UnsafeOnCompleted(ProcessDataFromAwaiter);
                    if (!pendingRead.IsCompleted) Monitor.Wait(pendingRead.SyncLock);
                    return pendingRead.ConsumeBytesReadAndReset();
                }
            }
            /// <summary>
            /// Reads a single byte
            /// </summary>
            public override int ReadByte()
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamReadByte);
                AssertCanRead();
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    pendingRead.AssertAvailable();
                    ReadOnlySequence<byte> buffer;
                    if (_reader.TryRead(out var readResult) && !(buffer = readResult.Buffer).IsEmpty)
                    {
                        var b = buffer.First.Span[0];
                        _reader.AdvanceTo(buffer.GetPosition(1));
                        return b;
                    }
                }
                var arr = ArrayPool<byte>.Shared.Rent(1);
                int bytes = Read(new Memory<byte>(arr, 0, 1));
                var result = bytes <= 0 ? -1 : arr[0];
                ArrayPool<byte>.Shared.Return(arr);
                return result;
            }
            /// <summary>
            /// Write a buffer to the stream
            /// </summary>
            public override void Write(byte[] buffer, int offset, int count)
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamWrite);
                var from = new ReadOnlySpan<byte>(buffer, offset, count);
                WriteImpl(from);
                FlushImpl();
            }

#pragma warning disable RCS1231 // Make parameter ref read-only.
            private void WriteImpl(ReadOnlySpan<byte> from)
#pragma warning restore RCS1231 // Make parameter ref read-only.
            {
                AssertCanWrite();
                int offset = 0;
                int count = from.Length;
                while (count > 0)
                {
                    var to = _writer.GetSpan(1);
                    int bytes = Math.Min(count, to.Length);
                    DebugLog($"Writing {bytes} bytes to '{_writer}'...");
                    from.Slice(offset, bytes).CopyTo(to.Slice(0, bytes));
                    _writer.Advance(bytes);
                    offset += bytes;
                    count -= bytes;
                }
                DebugLog($"Total: {from.Length} bytes written to '{_writer}'");
            }
            /// <summary>
            /// Perform an asynchronous write operation
            /// </summary>
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamWriteAsync);
                WriteImpl(new ReadOnlySpan<byte>(buffer, offset, count));
                return FlushAsyncImpl(default);
            }
            /// <summary>
            /// Write a single byte
            /// </summary>
            public override void WriteByte(byte value)
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamWriteByte);
                AssertCanWrite();
                var to = _writer.GetSpan(1);
                to[0] = value;
                _writer.Advance(1);
                FlushImpl();
            }
            /// <summary>
            /// Begin an asynchronous write operation
            /// </summary>
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                DebugLog();
                Helpers.Incr(Counter.PipeStreamBeginWrite);
                Write(buffer, offset, count);
                FlushImpl(); // TODO: use async flush here
                var obj = PendingWrite;
                PendingWrite.AsyncState = state;
                callback(obj);
                return obj;
            }
            /// <summary>
            /// End an asynchronous write operation
            /// </summary>
            public override void EndWrite(IAsyncResult asyncResult) { }

            private sealed class FlushToken
            {
                private bool isSet;
                public void Reset()
                {
                    lock (this) { isSet = false; }
                }

                public readonly Action Set;
                public FlushToken() => Set = () =>
                {
                    lock (this)
                    {
                        isSet = true;
                        Monitor.PulseAll(this);
                    }
                };

                public void Wait()
                {
                    lock (this)
                    {
                        while (!isSet) Monitor.Wait(this);
                    }
                }
            }
            /// <summary>
            /// Signal that the written data should be read; this may awaken the reader if inactive,
            /// and suspend the writer if the backlog is too large
            /// </summary>
            public override void Flush()
            {
                AssertCanWrite();
                DebugLog($"Flushing {_writer}");
                Helpers.Incr(Counter.PipeStreamFlush);
                FlushImpl();
            }

            private void FlushImpl()
            {
                var flush = _writer.FlushAsync();
                if (flush.IsCompleted)
                {
                    _ = flush.Result; // (important for recycling IValueTaskSource etc)
                    return;
                }

                var inst = FlushInstance;
                inst.Reset();
                var awaiter = flush.GetAwaiter();
                awaiter.OnCompleted(inst.Set);
                inst.Wait();
                awaiter.GetResult();
            }
            /// <summary>
            /// Signal that the written data should be read; this may awaken the reader if inactive,
            /// and suspend the writer if the backlog is too large
            /// </summary>
            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                AssertCanWrite();
                DebugLog($"Flushing {_writer}");
                Helpers.Incr(Counter.PipeStreamFlushAsync);
                return FlushAsyncImpl(cancellationToken);
            }
            private Task FlushAsyncImpl(in CancellationToken cancellationToken)
            {
                var flush = _writer.FlushAsync(cancellationToken);
                return flush.IsCompletedSuccessfully ? Task.CompletedTask : flush.AsTask();
            }

            private class AsyncWriteResult : IAsyncResult
            {
                private static readonly ManualResetEvent _alwaysSet = new ManualResetEvent(true);
                public bool IsCompleted => true;
                public WaitHandle AsyncWaitHandle => _alwaysSet;
                public object AsyncState { get; internal set; }
                public bool CompletedSynchronously => true;
            }

            /// <summary>
            /// Close the stream
            /// </summary>
            public override void Close()
            {
                CloseWrite();
                CloseRead();
                //try { _reader?.CancelPendingRead(); } catch { }
                //try { _writer?.CancelPendingFlush(); } catch { }
            }

            /// <summary>
            /// Signals that writing is complete; no more data will be written
            /// </summary>
            public void CloseWrite()
            {
                try { _writer?.Complete(); } catch { }
            }
            /// <summary>
            /// Signals that reading is complete; no more data will be read
            /// </summary>
            public void CloseRead()
            {
                try { _reader?.Complete(); } catch { }
            }

            /// <summary>
            /// Begin an asynchronous read operation
            /// </summary>
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                DebugLog("init");
                AssertCanRead();
                Helpers.Incr(Counter.PipeStreamBeginRead);
                var memory = new Memory<byte>(buffer, offset, count);
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    pendingRead.Init(callback, state, memory, null, PendingAsyncMode.AsyncCallback);
                    ReadResult result = default;
                    bool complete;
                    if (count == 0 || _reader.TryRead(out result))
                    {
                        complete = true;
                    }
                    else
                    {
                        var pending = _reader.ReadAsync();
                        if (pending.IsCompleted)
                        {
                            result = pending.Result;
                            complete = true;
                        }
                        else
                        {
                            pendingRead.ReadAwaiter = pending.GetAwaiter();
                            pendingRead.ReadAwaiter.UnsafeOnCompleted(ProcessDataFromAwaiter);
                            complete = false;
                        }
                    }

                    if (complete)
                    {
                        pendingRead.CompletedSynchronously = true;
                        ConsumeBytesAndExecuteContiuations(result);
                    }
                    return pendingRead;
                }
            }
            /// <summary>
            /// End an asynchronous read operation
            /// </summary>
            public override int EndRead(IAsyncResult asyncResult)
            {
                DebugLog();
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    return pendingRead.ConsumeBytesReadAndReset();
                }
            }
            private int ConsumeBytes(ReadResult from, Span<byte> to)
            {
                int bytesRead = 0;
                if (!from.IsCanceled)
                {
                    var buffer = from.Buffer;
                    int remaining = to.Length;
                    if (buffer.IsSingleSegment)
                    {
                        var segSpan = buffer.First.Span;
                        bytesRead = Math.Min(segSpan.Length, remaining);
                        segSpan.Slice(0, bytesRead).CopyTo(to);
                    }
                    else
                    {
                        if (remaining != 0)
                        {
                            foreach (var segment in buffer)
                            {
                                var segSpan = segment.Span;
                                int take = Math.Min(segSpan.Length, remaining);
                                segSpan.Slice(0, take).CopyTo(to);
                                to = to.Slice(take);
                                bytesRead += take;
                                remaining -= take;
                            }
                        }
                    }
                    var end = buffer.GetPosition(bytesRead);
                    _reader.AdvanceTo(end);
                }

                PendingRead.SetBytesRead(bytesRead, !from.IsCanceled);
                return bytesRead;
            }

            /// <summary>
            /// Perform an asynchronous read operation
            /// </summary>
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                DebugLog("init");
                Helpers.Incr(Counter.PipeStreamReadAsync);
                return ReadAsyncImpl(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
            }

#pragma warning disable RCS1231 // Make parameter ref read-only.
            private ValueTask<int> ReadAsyncImpl(Memory<byte> memory, in CancellationToken cancellationToken)
#pragma warning restore RCS1231 // Make parameter ref read-only.
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssertCanRead();
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    pendingRead.AssertAvailable();

                    if (memory.IsEmpty) return new ValueTask<int>(0);

                    DebugLog(nameof(_reader.TryRead));
                    if (_reader.TryRead(out ReadResult result))
                    {
                        DebugLog("sync consume");
                        return new ValueTask<int>(ConsumeBytes(result, memory.Span));
                    }
                    else
                    {
                        DebugLog(nameof(_reader.ReadAsync));
                        var pending = _reader.ReadAsync(cancellationToken);
                        if (pending.IsCompleted)
                        {
                            DebugLog("sync consume");
                            return new ValueTask<int>(ConsumeBytes(pending.Result, memory.Span));
                        }

                        DebugLog("setting completion data for a task");
                        var tcs = new TaskCompletionSource<int>();
                        pendingRead.Init(null, null, memory, tcs, PendingAsyncMode.Task);
                        pendingRead.ReadAwaiter = pending.GetAwaiter();
                        pendingRead.ReadAwaiter.UnsafeOnCompleted(ProcessDataFromAwaiter);
                        return new ValueTask<int>(tcs.Task);
                    }
                }
            }
            internal enum PendingAsyncMode
            {
                None,
                Synchronous,
                Task,
                AsyncCallback
            }

            internal void ProcessDataFromAwaiterImpl()
            {
                DebugLog();
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    var result = pendingRead.ReadAwaiter.GetResult();
                    pendingRead.ReadAwaiter = default;
                    ConsumeBytesAndExecuteContiuations(result);
                }
            }
            private void ConsumeBytesAndExecuteContiuations(ReadResult result)
            {
                var pendingRead = PendingRead;
                DebugLog($"complete: {result.IsCompleted}; canceled: {result.IsCanceled}; bytes: {result.Buffer.Length}");
                int bytes = ConsumeBytes(result, pendingRead.Memory.Span);
                DebugLog($"consumed: {bytes} (max: {pendingRead.Memory.Length}); mode: {pendingRead.AsyncMode}");
                switch (pendingRead.AsyncMode)
                {
                    case PendingAsyncMode.AsyncCallback:
                        pendingRead.ExecuteCallback();
                        break;
                    case PendingAsyncMode.Task:
                        var task = pendingRead.TaskSource;
                        pendingRead.TaskSource = null;

                        // need to reset state *before* setting result, because: direct continuations
                        bytes = pendingRead.ConsumeBytesReadAndReset();
                        if (task is null)
                        {
                            DebugLog("no task!");
                        }
                        else
                        {
                            bool wasSet;
                            if (result.IsCanceled)
                            {
                                DebugLog("setting task cancelation...");
                                wasSet = task.TrySetCanceled();
                            }
                            else
                            {
                                DebugLog($"setting task result ({bytes})...");
                                wasSet = task.TrySetResult(bytes);
                            }
                            DebugLog(wasSet ? "task outcome set" : "unable to set task outcome");
                        }
                        break;
                    case PendingAsyncMode.Synchronous:
                        Monitor.PulseAll(pendingRead.SyncLock);
                        break;
                }
            }

            private class AsyncReadResult : IAsyncResult
            {
                internal ValueTaskAwaiter<ReadResult> ReadAwaiter;
                private int BytesRead { get; set; }

                internal object SyncLock => this;
#pragma warning disable RCS1231 // Make parameter ref read-only.
                internal void Init(AsyncCallback callback, object asyncState, Memory<byte> memory,
#pragma warning restore RCS1231 // Make parameter ref read-only.
                    TaskCompletionSource<int> tcs, PendingAsyncMode asyncMode)
                {
                    AssertAvailable();
                    AsyncMode = asyncMode;
                    Memory = memory;
                    Callback = callback;
                    AsyncState = asyncState;
                    TaskSource = tcs;
                    IsCompleted = CompletedSynchronously = false;
                    BytesRead = -1;
                }

                internal PendingAsyncMode AsyncMode { get; private set; }
                internal void AssertAvailable()
                {
                    if (AsyncMode != PendingAsyncMode.None)
                        Throw.InvalidOperation($"A {AsyncMode} operation is already in progress");
                }
                internal void ExecuteCallback()
                {
                    var tmp = Callback;
                    Callback = null;
                    tmp?.Invoke(this);
                }

                internal void SetBytesRead(int bytesRead, bool complete)
                {
                    BytesRead = bytesRead;
                    IsCompleted = complete;
                    _waitHandle?.Set();
                }
                internal int ConsumeBytesReadAndReset()
                {
                    if (AsyncMode == PendingAsyncMode.None)
                    {
                        Throw.InvalidOperation("No read in progress");
                    }
                    AsyncMode = PendingAsyncMode.None;
                    _waitHandle?.Reset();
                    return BytesRead;
                }
                internal Memory<byte> Memory { get; private set; }
                private AsyncCallback Callback { get; set; }
                internal TaskCompletionSource<int> TaskSource { get; set; }
                public bool IsCompleted { get; internal set; }

                private ManualResetEvent _waitHandle;
                public WaitHandle AsyncWaitHandle => _waitHandle ??= new ManualResetEvent(IsCompleted);
                public object AsyncState { get; private set; }
                public bool CompletedSynchronously { get; internal set; }
            }
        }
    }
}
