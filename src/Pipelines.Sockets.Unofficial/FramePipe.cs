using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Diagnostics;

#pragma warning disable CS1591
namespace Pipelines.Sockets.Unofficial
{
    public interface IDuplexChannel<TWrite, TRead>
    {
        ChannelReader<TRead> Input { get; }
        ChannelWriter<TWrite> Output { get; }
    }
    public interface IDuplexChannel<T> : IDuplexChannel<T, T> { }

    public readonly struct Frame : IDisposable
    {
        public override string ToString() => $"{Payload.Length} bytes, flags: {_socketFlags}";
        public override bool Equals(object obj) => throw new NotSupportedException();
        public override int GetHashCode() => throw new NotSupportedException();

        private readonly SocketFlags _socketFlags;
        private readonly ReadOnlySequence<byte> _payload;
        private readonly object _onDispose;
        public Frame(ReadOnlySequence<byte> payload, Action<ReadOnlySequence<byte>> onDispose = null, SocketFlags socketFlags = SocketFlags.None)
        {
            _payload = payload;
            _socketFlags = socketFlags;
            _onDispose = onDispose;
        }
        public Frame(ReadOnlyMemory<byte> payload, Action<ReadOnlyMemory<byte>> onDispose = null, SocketFlags socketFlags = SocketFlags.None)
        {
            _payload = new ReadOnlySequence<byte>(payload);
            _socketFlags = socketFlags;
            _onDispose = onDispose;
        }
        public Frame(ReadOnlyMemory<byte> payload, IDisposable onDispose, SocketFlags socketFlags = SocketFlags.None)
        {
            _payload = new ReadOnlySequence<byte>(payload);
            _socketFlags = socketFlags;
            _onDispose = onDispose;
        }
        public Frame(IMemoryOwner<byte> payload, SocketFlags socketFlags = SocketFlags.None)
        {
            _payload = new ReadOnlySequence<byte>(payload.Memory);
            _socketFlags = socketFlags;
            _onDispose = payload;
        }
        public Frame(ArraySegment<byte> payload, Action<ArraySegment<byte>> onDispose = null, SocketFlags socketFlags = SocketFlags.None)
        {
            var memory = new ReadOnlyMemory<byte>(payload.Array, payload.Offset, payload.Count);
            _payload = new ReadOnlySequence<byte>(memory);
            _socketFlags = socketFlags;
            _onDispose = onDispose;
        }

        public SocketFlags SocketFlags => _socketFlags;
        public ReadOnlySequence<byte> Payload => _payload;
        public void Dispose()
        {
            switch (_onDispose)
            {
                case null: break;
                case IMemoryOwner<byte> mo: mo.Dispose(); break;
                case Action<ReadOnlySequence<byte>> aros: aros(_payload); break;
                case Action<ReadOnlyMemory<byte>> aas: aas(_payload.First); break;
                case Action<ArraySegment<byte>> aas: aas(_payload.First.GetArray()); break;
            }
        }
    }

    public sealed class FrameConnectionOptions
    {
        public static FrameConnectionOptions Default { get; } = new FrameConnectionOptions();

        public FrameConnectionOptions(MemoryPool<byte> pool = null,
            PipeScheduler readerScheduler = null, PipeScheduler writerScheduler = null,
            int maximumFrameSize = 65535, int blockSize = 16 * 1024,
            bool useSynchronizationContext = true,
            BoundedChannelOptions channelOptions = null)
        {
            Pool = pool ?? MemoryPool<byte>.Shared;
            ReaderScheduler = readerScheduler ?? PipeScheduler.ThreadPool;
            WriterScheduler = writerScheduler ?? PipeScheduler.ThreadPool;
            MaximumFrameSize = maximumFrameSize;
            BlockSize = blockSize;
            ChannelOptions = channelOptions;
            UseSynchronizationContext = useSynchronizationContext;
        }

        public MemoryPool<byte> Pool { get; }
        public PipeScheduler ReaderScheduler { get; }
        public PipeScheduler WriterScheduler { get; }
        public int MaximumFrameSize { get; }
        public int BlockSize { get; }
        public BoundedChannelOptions ChannelOptions { get; }
        public bool UseSynchronizationContext { get; }
    }
    public abstract class SocketFrameConnection : IDuplexChannel<Frame>, IDisposable
    {
        public abstract ChannelReader<Frame> Input { get; }

        public abstract ChannelWriter<Frame> Output { get; }

        public static SocketFrameConnection Create(
            EndPoint endpoint,
            FrameConnectionOptions sendOptions = null,
            FrameConnectionOptions receiveOptions = null,
            string name = null,
            SocketConnectionOptions connectionOptions = SocketConnectionOptions.None
#if DEBUG
            , Action<string> log = null
#endif
            )
        {
            var addressFamily = endpoint.AddressFamily == AddressFamily.Unspecified ? AddressFamily.InterNetwork : endpoint.AddressFamily;
            const ProtocolType protocolType = ProtocolType.Udp; // needs linux test addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Udp;

            var socket = new Socket(addressFamily, SocketType.Dgram, protocolType);
            SocketConnection.SetRecommendedServerOptions(socket); // fine for client too
            return new DefaultSocketFrameConnection(socket, name, sendOptions, receiveOptions, connectionOptions
#if DEBUG
                , log
#endif
                );
        }

        private readonly SocketConnectionOptions _options;
        private readonly string _name;
#if DEBUG
        private readonly Action<string> _log;
#endif
        [Conditional("DEBUG")]
        protected internal void DebugLog(string message)
        {
            if (_log != null) _log.Invoke("[" + _name + "]: " + message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasFlag(SocketConnectionOptions option) => (option & _options) != 0;

        protected internal bool InlineWrites
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasFlag(SocketConnectionOptions.InlineWrites);
        }
        protected internal bool InlineReads
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasFlag(SocketConnectionOptions.InlineReads);
        }

        private int _socketShutdownKind;
        protected bool TrySetShutdown(PipeShutdownKind kind, [CallerMemberName] string caller = null)
        {
            DebugLog($"Shutting down: {kind} from {caller}");
            return kind != PipeShutdownKind.None
            && Interlocked.CompareExchange(ref _socketShutdownKind, (int)kind, 0) == 0;
        }

        protected bool TrySetShutdown(PipeShutdownKind kind, SocketError socketError, [CallerMemberName] string caller = null)
        {
            bool win = TrySetShutdown(kind, caller);
            if (win)
            {
                SocketError = socketError;
                DebugLog($"Socket error: {socketError}");
            }
            return win;
        }

        /// <summary>
        /// When possible, determines how the pipe first reached a close state
        /// </summary>
        public PipeShutdownKind ShutdownKind => (PipeShutdownKind)Thread.VolatileRead(ref _socketShutdownKind);
        /// <summary>
        /// When the ShutdownKind relates to a socket error, may contain the socket error code
        /// </summary>
        public SocketError SocketError { get; protected set; }

        protected SocketFrameConnection(string name, SocketConnectionOptions options, Action<string> log)
        {
            _name = string.IsNullOrWhiteSpace(name) ? GetType().FullName : name.Trim();
            _options = options;
            _log = log;
        }

        public override string ToString() => _name;

        public abstract void Dispose();

        private sealed class DefaultSocketFrameConnection : SocketFrameConnection
        {
            private static readonly BoundedChannelOptions s_DefaultSendChannelOptions = new BoundedChannelOptions(capacity: 1024)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = true };
            private static readonly BoundedChannelOptions s_DefaultReceiveChannelOptions = new BoundedChannelOptions(capacity: 1024)
            { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = true };

            private readonly Socket _socket;
            private readonly FrameConnectionOptions _sendOptions;
            private readonly FrameConnectionOptions _receiveOptions;
            private readonly Channel<Frame> _sendToSocket;
            private readonly Channel<Frame> _receivedFromSocket;
            internal DefaultSocketFrameConnection(Socket socket, string name,
                FrameConnectionOptions sendOptions, FrameConnectionOptions receiveOptions,
                SocketConnectionOptions connectionOptions
#if DEBUG
                , Action<string> log
#endif
                ) : base(name, connectionOptions
#if DEBUG
                    , log
#endif
                    )
            {
                _socket = socket;
                _sendOptions = sendOptions ?? FrameConnectionOptions.Default;
                _receiveOptions = receiveOptions ?? FrameConnectionOptions.Default;

                _sendToSocket = Channel.CreateBounded<Frame>(_sendOptions.ChannelOptions ?? s_DefaultSendChannelOptions);
                _receivedFromSocket = Channel.CreateBounded<Frame>(_receiveOptions.ChannelOptions ?? s_DefaultReceiveChannelOptions);

                _sendOptions.ReaderScheduler.Schedule(s_DoSendAsync, this);
                _receiveOptions.ReaderScheduler.Schedule(s_DoReceiveAsync, this);
            }

            public override void Dispose()
            {
                try { _socket.Shutdown(SocketShutdown.Both); } catch { }
                try { _socket.Close(); } catch { }
                try { _socket.Dispose(); } catch { }
                try { _sendToSocket.Writer.TryComplete(); } catch { }
                try { _receivedFromSocket.Writer.TryComplete(); } catch { }
            }

            public override ChannelReader<Frame> Input => _receivedFromSocket.Reader;
            public override ChannelWriter<Frame> Output => _sendToSocket.Writer;

            static readonly Action<object> s_DoSendAsync = state => _ = ((DefaultSocketFrameConnection)state).DoSendAsync();
            static readonly Action<object> s_DoReceiveAsync = state => _ = ((DefaultSocketFrameConnection)state).DoReceiveAsync();

            private async Task DoSendAsync()
            {
                Exception error = null;
                SocketAwaitableEventArgs args = null;
                try
                {
                    DebugLog("Starting send loop...");
                    while (await _sendToSocket.Reader.WaitToReadAsync())
                    {
                        DebugLog("Reading sync frames...");
                        while (_sendToSocket.Reader.TryRead(out var frame))
                        {
                            DebugLog($"Received {frame}");

                            using (frame)
                            {
                                if (args == null)
                                {
                                    args = new SocketAwaitableEventArgs(InlineWrites ? null : _sendOptions.ReaderScheduler)
                                    {
                                        BufferList = new List<ArraySegment<byte>>()
                                    };
                                }
                                SetBuffer(args, frame);
                                if (_socket.SendAsync(args))
                                {
                                    // will complete asynchronously
                                }
                                else
                                {
                                    // completed synchronously - need to mark completed
                                    args.Complete();
                                }
                                await args;
                            }
                        }
                    }
                    TrySetShutdown(PipeShutdownKind.WriteEndOfStream);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    TrySetShutdown(PipeShutdownKind.WriteSocketError, ex.SocketErrorCode);
                    error = null;
                }
                catch (SocketException ex)
                {
                    TrySetShutdown(PipeShutdownKind.WriteSocketError, ex.SocketErrorCode);
                    error = ex;
                }
                catch (ObjectDisposedException)
                {
                    TrySetShutdown(PipeShutdownKind.WriteDisposed);
                    error = null;
                }
                catch (IOException ex)
                {
                    TrySetShutdown(PipeShutdownKind.WriteIOException);
                    error = ex;
                }
                catch (Exception ex)
                {
                    TrySetShutdown(PipeShutdownKind.WriteException);
                    error = new IOException(ex.Message, ex);
                }
                finally
                {
                    DebugLog("Exited send loop");
                    if (error != null) DebugLog(error.Message);
                    try { _socket.Shutdown(SocketShutdown.Send); } catch { }
                    try { _sendToSocket.Writer.TryComplete(error); } catch { }
                    if (args != null) try { args.Dispose(); } catch { }
                }
            }


            private sealed class CountedLease : IDisposable
            {
                private IMemoryOwner<byte> _memoryOwner;
                int _refCount;
                public CountedLease(IMemoryOwner<byte> memoryOwner)
                {
                    _memoryOwner = memoryOwner;
                    _refCount = 1;
                }

                public Memory<byte> Memory => _memoryOwner.Memory;
                public void AddRef() => Interlocked.Increment(ref _refCount);
                public void Release()
                {
                    if (Interlocked.Decrement(ref _refCount) == 0)
                    {
                        var tmp = Interlocked.Exchange(ref _memoryOwner, null);
                        tmp?.Dispose();
                    }
                }

                void IDisposable.Dispose() => Release();
            }

            // TODO: merge CountedLeaseSegment and CountedLease

            sealed class CountedLeaseSegment : ReadOnlySequenceSegment<byte>
            {
                private readonly CountedLease _lease;
                public CountedLeaseSegment(CountedLease lease, long runningIndex)
                {
                    lease.AddRef();
                    Memory = lease.Memory;
                    RunningIndex = runningIndex;
                    _lease = lease;
                }
                internal void SetNext(CountedLeaseSegment next)
                {
                    Next = next;
                }

                internal static readonly Action<ReadOnlySequence<byte>> ReleaseChain = ros =>
                {
                    var start = ros.Start;
                    var node = start.GetObject() as CountedLeaseSegment;
                    long remaining = ros.Length + start.GetInteger();

                    while(remaining > 0)
                    {
                        remaining -= node.Memory.Length;
                        node._lease.Release();
                        node = (CountedLeaseSegment)node.Next;
                    }
                };
            }

            private async Task DoReceiveAsync()
            {
                Exception error = null;
                LinkedList<CountedLease> chunks = new LinkedList<CountedLease>();
                var args = new SocketAwaitableEventArgs(InlineReads ? null : _receiveOptions.WriterScheduler)
                {
                    BufferList = new List<ArraySegment<byte>>()
                };
                try
                {
                    DebugLog("Starting receive loop...");
                    int bytesAvailable = 0, start = 0;
                    while (true)
                    {
                        DebugLog($"Preparing buffer; {bytesAvailable} available, need {_receiveOptions.MaximumFrameSize}");
                        // make sure we have sufficient capacity for the receive frame
                        while (bytesAvailable < _receiveOptions.MaximumFrameSize)
                        {
                            var lease = _receiveOptions.Pool.Rent(Math.Min(_receiveOptions.BlockSize, _receiveOptions.Pool.MaxBufferSize));
                            chunks.AddLast(new CountedLease(lease));
                            bytesAvailable += lease.Memory.Length;
                        }
                        DebugLog($"Buffer prepared; {bytesAvailable} available");

                        SetBuffer(args, chunks, start);

                        DebugLog($"Args configured; calling ReceiveAsync");
                        int bytes;
                        DebugLog($"Socket: {_socket}");
                        if (_socket.ReceiveAsync(args))
                        {
                            // will complete asynchronously
                            DebugLog($"Receiving asynchronously...");
                            bytes = await args;
                        }
                        else
                        {
                            // completed synchronously - need to mark completed
                            args.Complete();
                            DebugLog($"Receive completed synchronously");
                            bytes = await args;
                        }
                        DebugLog($"Received: {bytes} bytes");
                        if (bytes <= 0) break;

                        // create a payload
                        var frame = CreateFrame(chunks, start, bytes, args.SocketFlags);
                        await _receivedFromSocket.Writer.WriteAsync(frame);

                        // release the chunks that are full
                        while (bytes > 0)
                        {
                            var firstChunk = chunks.First.Value;
                            int len = firstChunk.Memory.Length;
                            if (bytes >= len)
                            {
                                // fully used; drop it
                                chunks.RemoveFirst();
                                bytes -= len;
                                start = 0;
                            }
                            else
                            {
                                start += bytes;
                                break;
                            }
                        }
                    }
                    TrySetShutdown(PipeShutdownKind.ReadEndOfStream);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                    error = new ConnectionResetException(ex.Message, ex);
                }
                catch (SocketException ex)
                {
                    TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                    error = ex;
                }
                catch (ObjectDisposedException)
                {
                    TrySetShutdown(PipeShutdownKind.ReadDisposed);
                }
                catch (IOException ex)
                {
                    TrySetShutdown(PipeShutdownKind.ReadIOException);
                    error = ex;
                }
                catch (Exception ex)
                {
                    TrySetShutdown(PipeShutdownKind.ReadException);
                    error = new IOException(ex.Message, ex);
                }
                finally
                {
                    DebugLog("Exited receive loop");
                    if (error != null) DebugLog(error.Message);
                    try { _socket.Shutdown(SocketShutdown.Receive); } catch { }
                    try { _receivedFromSocket.Writer.Complete(error); } catch { }
                    try { args.Dispose(); } catch { }
                    try
                    {
                        foreach (var chunk in chunks)
                        {
                            try { chunk.Release(); } catch { }
                        }
                    }
                    catch { }
                }
            }

            private Frame CreateFrame(LinkedList<CountedLease> chunks, int start, int bytes, SocketFlags socketFlags)
            {
                if (bytes == 0)
                {
                    return new Frame(new ReadOnlyMemory<byte>(Array.Empty<byte>()), socketFlags: socketFlags);
                }
                var node = chunks.First;

                var lease = node.Value;
                var mem = lease.Memory;
                int len = mem.Length - start;
                if (len >= bytes)
                {
                    lease.AddRef();
                    return new Frame(mem.Slice(start, bytes), lease, socketFlags);
                }

                // multi-segment
                var firstSegment = new CountedLeaseSegment(lease, 0L);
                var endSegment = firstSegment;
                var runningIndex = len;
                bytes -= len;

                while (bytes > 0)
                {
                    node = node.Next;
                    lease = node.Value;
                    mem = lease.Memory;
                    len = Math.Min(mem.Length, bytes);
                    var newSegment = new CountedLeaseSegment(lease, runningIndex);
                    runningIndex += len;
                    bytes -= len;

                    endSegment.SetNext(newSegment);
                    endSegment = newSegment;
                }
                return new Frame(new ReadOnlySequence<byte>(firstSegment, start, endSegment, len), CountedLeaseSegment.ReleaseChain, socketFlags);
            }

            private void SetBuffer(SocketAwaitableEventArgs args, LinkedList<CountedLease> chunks, int start)
            {
                var list = args.BufferList;
                list.Clear();
                foreach (var chunk in chunks)
                {
                    var segment = chunk.Memory.GetArray();
                    if (start != 0)
                    {
                        segment = new ArraySegment<byte>(segment.Array, segment.Offset + start, segment.Count - start);
                        start = 0;
                    }
                    list.Add(segment);
                }
            }
        }

        private static void SetBuffer(SocketAwaitableEventArgs args, Frame frame)
        {
            args.SocketFlags = frame.SocketFlags;
            var payload = frame.Payload;
            var list = args.BufferList;
            list.Clear();
            if (payload.IsEmpty)
            {
                // nothing to do
            }
            else if (payload.IsSingleSegment)
            {
                list.Add(payload.First.GetArray());
            }
            else
            {
                foreach (var segment in payload)
                {
                    list.Add(segment.GetArray());
                }
            }
        }
    }

}
