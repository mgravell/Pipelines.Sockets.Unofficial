using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    static partial class StreamConnector
    {
        public static IDuplexPipe GetDuplex(Stream stream, PipeOptions pipeOptions = null)
            => new AsyncStreamPipe(stream, pipeOptions, true, true);

        public static PipeReader GetReader(Stream stream, PipeOptions pipeOptions = null)
            => new AsyncStreamPipe(stream, pipeOptions, true, false).Input;

        public static PipeWriter GetWriter(Stream stream, PipeOptions pipeOptions = null)
            => new AsyncStreamPipe(stream, pipeOptions, false, true).Output;
        
        public static Stream GetDuplex(PipeReader reader, PipeWriter writer)
            => new AsyncPipeStream(reader, writer);

        public static Stream GetDuplex(IDuplexPipe pipe)
            => new AsyncPipeStream(pipe.Input, pipe.Output);

        public static Stream GetReader(PipeWriter writer)
            => new AsyncPipeStream(null, writer);

        public static Stream GetWriter(PipeReader reader)
            => new AsyncPipeStream(reader, null);



        class AsyncStreamPipe : IDuplexPipe
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

        class AsyncPipeStream : Stream
        {
            private AsyncReadResult _pendingRead;
            private AsyncWriteResult _pendingWrite;
            private FlushToken _flushInstance;
            private AsyncReadResult PendingRead => _pendingRead ?? (_pendingRead = new AsyncReadResult());
            private AsyncWriteResult PendingWrite => _pendingWrite ?? (_pendingWrite = new AsyncWriteResult());
            private FlushToken FlushInstance => _flushInstance ?? (_flushInstance = new FlushToken());

            private Action _processDataFromAwaiter;
            private Action ProcessDataFromAwaiter => _processDataFromAwaiter ?? (_processDataFromAwaiter = ProcessDataFromAwaiterImpl);


            private readonly PipeReader _reader;
            private readonly PipeWriter _writer;

            public AsyncPipeStream(PipeReader reader, PipeWriter writer)
            {
                if (reader == null && writer == null)
                    throw new ArgumentNullException("At least one of reader/writer must be provided");
                _reader = reader;
                _writer = writer;
            }
            public override bool CanRead => _reader != null;
            public override bool CanWrite => _writer != null;
            public override bool CanTimeout => false;
            public override bool CanSeek => false;
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void SetLength(long value) => throw new NotSupportedException();

            private void AssertCanRead() { if (_reader == null) throw new InvalidOperationException("Cannot read"); }
            private void AssertCanWrite() { if (_writer == null) throw new InvalidOperationException("Cannot write"); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                AssertCanRead();
                var memory = new Memory<byte>(buffer, offset, count);
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    pendingRead.AssertAvailable();
                    if (count == 0) return 0;
                    ReadResult result;

                    if (_reader.TryRead(out result))
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
            public override int ReadByte()
            {
                AssertCanRead();
                var arr = ArrayPool<byte>.Shared.Rent(1);
                int bytes = Read(arr, 0, 1);
                var result = bytes <= 0 ? -1 : arr[0];
                ArrayPool<byte>.Shared.Return(arr);
                return result;
            }
            public override void Write(byte[] buffer, int offset, int count)
            {
                var from = new Span<byte>(buffer, offset, count);
                Write(from);
            }
            private void Write(Span<byte> from)
            {
                AssertCanWrite();
                int offset = 0;
                int count = from.Length;
                while (count > 0)
                {
                    var to = _writer.GetSpan(1);
                    int bytes = Math.Min(count, to.Length);

                    from.Slice(offset, bytes).CopyTo(to.Slice(0, bytes));
                    _writer.Advance(bytes);
                    offset += bytes;
                    count -= bytes;
                }
            }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
            public override void WriteByte(byte value)
            {
                Span<byte> from = stackalloc byte[1] { value };
                Write(from);
            }
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                Write(buffer, offset, count);
                var obj = PendingWrite;
                PendingWrite.AsyncState = state;
                callback(obj);
                return obj;
            }
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

            public override void Flush()
            {
                AssertCanWrite();
                var flush = _writer.FlushAsync();
                if (flush.IsCompleted) return;

                var inst = FlushInstance;
                inst.Reset();
                var awaiter = flush.GetAwaiter();
                awaiter.OnCompleted(inst.Set);
                inst.Wait();
                awaiter.GetResult();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                var flush = _writer.FlushAsync(cancellationToken);
                return flush.IsCompletedSuccessfully ? Task.CompletedTask : flush.AsTask();
            }

            class AsyncWriteResult : IAsyncResult
            {
                private static readonly ManualResetEvent _alwaysSet = new ManualResetEvent(true);
                public bool IsCompleted => true;
                public WaitHandle AsyncWaitHandle => _alwaysSet;
                public object AsyncState { get; internal set; }
                public bool CompletedSynchronously => true;
            }

            public override void Close()
            {
                try { _reader?.CancelPendingRead(); } catch { }
                try { _reader?.Complete(); } catch { }
                try { _writer?.CancelPendingFlush(); } catch { }
                try { _writer?.Complete(); } catch { }
            }


            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                AssertCanRead();
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

            public override int EndRead(IAsyncResult asyncResult)
            {
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    return pendingRead.ConsumeBytesReadAndReset();
                }
            }
            int ConsumeBytes(ReadResult from, Span<byte> to)
            {
                int bytesRead = 0;
                if (!from.IsCanceled)
                {
                    var buffer = from.Buffer;

                    int remaining = to.Length;

                    if (remaining != 0)
                    {
                        foreach (var segment in buffer)
                        {
                            var segSpan = segment.Span;
                            int take = Math.Min(segSpan.Length, remaining);
                            segSpan.CopyTo(to);
                            to = to.Slice(take);
                            bytesRead += take;
                            remaining -= take;
                        }
                    }
                    var end = buffer.GetPosition(bytesRead);
                    _reader.AdvanceTo(end);
                }

                PendingRead.SetBytesRead(bytesRead, !from.IsCanceled);
                return bytesRead;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                AssertCanRead();
                var memory = new Memory<byte>(buffer, offset, count);
                var pendingRead = PendingRead;
                lock (pendingRead.SyncLock)
                {
                    pendingRead.AssertAvailable();

                    if (count == 0) return Task.FromResult(0);
                    else if (_reader.TryRead(out ReadResult result))
                    {
                        return Task.FromResult(ConsumeBytes(result, memory.Span));
                    }
                    else
                    {
                        var pending = _reader.ReadAsync();
                        if (pending.IsCompleted)
                        {
                            return Task.FromResult(ConsumeBytes(pending.Result, memory.Span));
                        }
                        var tcs = new TaskCompletionSource<int>();
                        pendingRead.Init(null, null, memory, tcs, PendingAsyncMode.Task);

                        pendingRead.ReadAwaiter = pending.GetAwaiter();
                        pendingRead.ReadAwaiter.UnsafeOnCompleted(ProcessDataFromAwaiter);
                        return tcs.Task;
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
                int bytes = ConsumeBytes(result, pendingRead.Memory.Span);

                switch (pendingRead.AsyncMode)
                {
                    case PendingAsyncMode.AsyncCallback:
                        pendingRead.ExecuteCallback();
                        break;
                    case PendingAsyncMode.Task:
                        var task = pendingRead.TaskSource;
                        pendingRead.TaskSource = null;
                        if (result.IsCanceled) task.TrySetCanceled();
                        else task.TrySetResult(bytes);
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
                internal void Init(AsyncCallback callback, object asyncState, Memory<byte> memory,
                    TaskCompletionSource<int> tcs, PendingAsyncMode asyncMode)
                {
                    AssertAvailable();
                    AsyncMode = asyncMode;
                    Memory = memory;
                    Callback = callback;
                    AsyncState = asyncState;
                    TaskSource = null;
                    IsCompleted = CompletedSynchronously = false;
                    BytesRead = -1;
                }

                internal PendingAsyncMode AsyncMode { get; private set; }
                internal void AssertAvailable()
                {
                    if (AsyncMode != PendingAsyncMode.None)
                        throw new InvalidOperationException($"A {AsyncMode} operation is already in progress");
                }
                internal void ExecuteCallback()
                {
                    var tmp = Callback;
                    Callback = null;
                    tmp?.Invoke(this);
                }

                internal void SetBytesRead(int bytesRead, bool complete)
                {
                    if (AsyncMode == PendingAsyncMode.None)
                    {
                        throw new InvalidOperationException("No read in progress");
                    }
                    BytesRead = bytesRead;
                    IsCompleted = complete;
                    _waitHandle?.Set();
                }
                internal int ConsumeBytesReadAndReset()
                {
                    if (AsyncMode == PendingAsyncMode.None)
                    {
                        throw new InvalidOperationException("No read in progress");
                    }
                    AsyncMode = PendingAsyncMode.None;
                    _waitHandle?.Reset();
                    return BytesRead;
                }

                internal Memory<byte> Memory { get; private set; }
                private AsyncCallback Callback { get; set; }
                internal TaskCompletionSource<int> TaskSource { get; set; }
                public bool IsCompleted { get; internal set; }

                ManualResetEvent _waitHandle;
                public WaitHandle AsyncWaitHandle => _waitHandle ?? (_waitHandle = new ManualResetEvent(IsCompleted));
                public object AsyncState { get; private set; }
                public bool CompletedSynchronously { get; internal set; }
            }
        }
    }

}
