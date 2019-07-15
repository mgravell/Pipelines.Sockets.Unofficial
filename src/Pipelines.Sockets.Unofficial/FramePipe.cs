using PooledAwait;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

#pragma warning disable CS1591
namespace Pipelines.Sockets.Unofficial
{
    public interface IMarshaller<T>
    {
        T Read(ReadOnlySequence<byte> payload, out Action<T> onDispose);
        void Write(T payload, IBufferWriter<byte> writer);
    }
    public interface IDuplexChannel<TWrite, TRead> : IDisposable
    {
        ChannelReader<TRead> Input { get; }
        ChannelWriter<TWrite> Output { get; }
    }
    public interface IDuplexChannel<T> : IDuplexChannel<T, T> { }

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
    public static class DatagramConnection<TMessage>
    {
        public static IDuplexChannel<Frame<TMessage>> CreateClient<TMarshaller>(
            EndPoint remoteEndpoint,
            TMarshaller marshaller,
            string name = null,
            EndPoint localEndpoint = null
#if DEBUG
            , Action<string> log = null
#endif

            ) where TMarshaller : IMarshaller<TMessage>
        {
            return SocketFrameConnection<TMarshaller, TMessage>.CreateClient(
                remoteEndpoint,
                marshaller, name: name,
                localEndpoint: localEndpoint
                
#if DEBUG
                , log: log
#endif
                );
        }

        public static IDuplexChannel<Frame<TMessage>> CreateServer<TMarshaller>(
            EndPoint endpoint,
            TMarshaller marshaller,
            string name = null
#if DEBUG
            , Action<string> log = null
#endif

        ) where TMarshaller : IMarshaller<TMessage>
        {
            return SocketFrameConnection<TMarshaller, TMessage>.CreateServer(
                endpoint,
                marshaller, name: name
#if DEBUG
                , log: log
#endif
                );
        }
    }

    public readonly struct Frame<T> : IDisposable
    {
        public override string ToString() => Payload?.ToString() ?? "";
        public override bool Equals(object obj) => throw new NotSupportedException();
        public override int GetHashCode() => throw new NotSupportedException();

        public EndPoint Peer { get; }
        public SocketFlags Flags { get; }
        public T Payload { get; }
        public long LocalIndex { get; }
        private readonly Action<T> _onDispose;

        public Frame(T payload, SocketFlags flags = SocketFlags.None, EndPoint peer = null, Action<T> onDispose = null, long localIndex = -1)
        {
            Payload = payload;
            Flags = flags;
            Peer = peer;
            _onDispose = onDispose;
            LocalIndex = localIndex;
        }

        public static implicit operator Frame<T>(T payload) => new Frame<T>(payload);
        public static implicit operator T(Frame<T> frame) => frame.Payload;
        public void Dispose() => _onDispose?.Invoke(Payload);
    }

    public static class Marshaller
    {
        public static StringMarshaller ASCII => new StringMarshaller(Encoding.ASCII);
        public static StringMarshaller UTF8 => default;
        public static CharMemoryMarshaller CharMemoryUTF8 => default;
        
        public readonly struct StringMarshaller : IMarshaller<string>
        {
            private Encoding Encoding
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _encoding ?? Encoding.UTF8;
            }
            public override string ToString() => Encoding.EncodingName;

            public unsafe string Read(ReadOnlySequence<byte> payload, out Action<string> onDispose)
            {
                onDispose = null;
                if (payload.IsEmpty) return "";
                if (!payload.IsSingleSegment) throw new NotImplementedException();
                var span = payload.First.Span;
                fixed (byte* bPtr = span)
                {
                    return Encoding.GetString(bPtr, span.Length);
                }
            }

            public unsafe void Write(string payload, IBufferWriter<byte> writer)
            {
                if (string.IsNullOrEmpty(payload)) return;
                var enc = Encoding;
                var span = writer.GetSpan(enc.GetByteCount(payload));
                fixed (char* cPtr = payload)
                fixed (byte* bPtr = span)
                {
                    int bytes = enc.GetBytes(cPtr, payload.Length, bPtr, span.Length);
                    writer.Advance(bytes);
                }
            }

            private readonly Encoding _encoding;

            public StringMarshaller(Encoding encoding)
            {
                _encoding = encoding;
            }
            public override bool Equals(object obj) => obj is StringMarshaller other && other.Encoding == Encoding;
            public override int GetHashCode() => Encoding.GetHashCode();
        }

        public readonly struct CharMemoryMarshaller : IMarshaller<ReadOnlyMemory<char>>
        {
            private readonly Encoding _encoding;
            private Encoding Encoding
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _encoding ?? Encoding.UTF8;
            }
            public override string ToString() => Encoding.EncodingName;
            public override bool Equals(object obj) => obj is CharMemoryMarshaller other && other.Encoding == Encoding;
            public override int GetHashCode() => Encoding.GetHashCode();
            public unsafe ReadOnlyMemory<char> Read(ReadOnlySequence<byte> payload, out Action<ReadOnlyMemory<char>> onDispose)
            {
                if (payload.IsEmpty)
                {
                    onDispose = null;
                    return Array.Empty<char>();
                }
                if (!payload.IsSingleSegment) throw new NotImplementedException();
                var bSpan = payload.First.Span;
                var enc = Encoding;

                var arr = ArrayPool<char>.Shared.Rent(enc.GetMaxCharCount(bSpan.Length));
                fixed (byte* bPtr = bSpan)
                fixed (char* cPtr = arr)
                {
                    var charCount = enc.GetChars(bPtr, bSpan.Length, cPtr, arr.Length);
                    onDispose = s_ReleaseToPool;
                    return new ReadOnlyMemory<char>(arr, 0, charCount);
                }
            }
            static readonly Action<ReadOnlyMemory<char>> s_ReleaseToPool = memory =>
            {
                if (MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array != null)
                    ArrayPool<char>.Shared.Return(segment.Array);
            };

            public unsafe void Write(ReadOnlyMemory<char> payload, IBufferWriter<byte> writer)
            {
                if (payload.IsEmpty) return;
                var enc = Encoding;
                var cSpan = payload.Span;
                
                fixed (char* cPtr = cSpan)
                {
                    var bSpan = writer.GetSpan(enc.GetByteCount(cPtr, cSpan.Length));
                    fixed (byte* bPtr = bSpan)
                    {
                        int bytes = enc.GetBytes(cPtr, payload.Length, bPtr, cSpan.Length);
                        writer.Advance(bytes);
                    }
                }
            }

            public CharMemoryMarshaller(Encoding encoding)
            {
                _encoding = encoding;
            }
        }
    }

    internal abstract class SocketFrameConnection<TMarshaller, TMessage> : IDuplexChannel<Frame<TMessage>>, IDisposable
        where TMarshaller : IMarshaller<TMessage>
    {
        public abstract ChannelReader<Frame<TMessage>> Input { get; }

        public abstract ChannelWriter<Frame<TMessage>> Output { get; }

        public static SocketFrameConnection<TMarshaller, TMessage> CreateClient(
            EndPoint remoteEndpoint,
            TMarshaller marshaller,
            FrameConnectionOptions sendOptions = null,
            FrameConnectionOptions receiveOptions = null,
            string name = null,
            SocketConnectionOptions connectionOptions = SocketConnectionOptions.None,
            EndPoint localEndpoint = null
#if DEBUG
            , Action<string> log = null
#endif
            )
        {
            var addressFamily = remoteEndpoint.AddressFamily == AddressFamily.Unspecified ? AddressFamily.InterNetwork : remoteEndpoint.AddressFamily;
            const ProtocolType protocolType = ProtocolType.Udp; // needs linux test addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Udp;

            var socket = new Socket(addressFamily, SocketType.Dgram, protocolType);
            socket.EnableBroadcast = true;
            SocketConnection.SetRecommendedClientOptions(socket);
            socket.Bind(localEndpoint ?? EphemeralEndpoint);
            
            // SocketConnection.SetRecommendedServerOptions(socket); // fine for client too
            return new DefaultSocketFrameConnection(socket, remoteEndpoint, name, marshaller, sendOptions, receiveOptions, connectionOptions, false
#if DEBUG
                , log
#endif
                );
        }

        static readonly EndPoint EphemeralEndpoint = new IPEndPoint(IPAddress.Any, 0);

        public static SocketFrameConnection<TMarshaller, TMessage> CreateServer(
            EndPoint endpoint,
            TMarshaller marshaller,
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
            socket.EnableBroadcast = true;
            //socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            //socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.PacketInformation, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.ReuseAddress, true);
            SocketConnection.SetRecommendedServerOptions(socket);
            socket.Bind(endpoint);

            return new DefaultSocketFrameConnection(socket, endpoint, name, marshaller, sendOptions, receiveOptions, connectionOptions, true
#if DEBUG
                , log
#endif
                );
        }

        private readonly EndPoint _endpoint;
        private readonly SocketConnectionOptions _options;
        private readonly string _name;
        private readonly TMarshaller _marshaller;
#if DEBUG
        private readonly Action<string> _log;
#endif
        [Conditional("DEBUG")]
        protected internal void DebugLog(string message)
        {
#if DEBUG
            if (_log != null) _log.Invoke("[" + _name + "]: " + message);
#endif
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
                DebugLog($"Socket error: {socketError} from {caller}");
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

        protected SocketFrameConnection(EndPoint endpoint, string name, SocketConnectionOptions options,
            TMarshaller marshaller
#if DEBUG
            , Action<string> log
#endif
            )
        {
            _endpoint = endpoint;
            _name = string.IsNullOrWhiteSpace(name) ? GetType().FullName : name.Trim();
            _options = options;
            _marshaller = marshaller;
#if DEBUG
            _log = log;
#endif
        }

        public override string ToString() => _name;

        public abstract void Dispose();

        private sealed class DefaultSocketFrameConnection : SocketFrameConnection<TMarshaller, TMessage>,
            IBufferWriter<byte>
        {
            private readonly bool _isServer;
            private static readonly BoundedChannelOptions s_DefaultChannelOptions = new BoundedChannelOptions(capacity: 1024)
            { SingleReader = false, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = true };

            private readonly Socket _socket;
            private readonly FrameConnectionOptions _sendOptions;
            private readonly FrameConnectionOptions _receiveOptions;
            private readonly Channel<Frame<TMessage>> _sendToSocket;
            private readonly Channel<Frame<TMessage>> _receivedFromSocket;
            internal DefaultSocketFrameConnection(
                Socket socket, EndPoint endpoint, string name, TMarshaller marshaller,
                FrameConnectionOptions sendOptions, FrameConnectionOptions receiveOptions,
                SocketConnectionOptions connectionOptions, bool isServer
#if DEBUG
                , Action<string> log
#endif
                ) : base(endpoint, name, connectionOptions, marshaller
#if DEBUG
                    , log
#endif
                    )
            {
                _isServer = isServer;
                _socket = socket;
                _sendOptions = sendOptions ?? FrameConnectionOptions.Default;
                _receiveOptions = receiveOptions ?? FrameConnectionOptions.Default;

                _sendToSocket = Channel.CreateBounded<Frame<TMessage>>(_sendOptions.ChannelOptions ?? s_DefaultChannelOptions);
                _receivedFromSocket = Channel.CreateBounded<Frame<TMessage>>(_receiveOptions.ChannelOptions ?? s_DefaultChannelOptions);

                _ = DoSendAsync();
                _ = DoReceiveAsync();
            }

            public override void Dispose()
            {
                try { _socket.Shutdown(SocketShutdown.Both); } catch { }
                try { _socket.Close(); } catch { }
                try { _socket.Dispose(); } catch { }
                try { _sendToSocket.Writer.TryComplete(); } catch { }
                try { _receivedFromSocket.Writer.TryComplete(); } catch { }
            }

            public override ChannelReader<Frame<TMessage>> Input => _receivedFromSocket.Reader;
            public override ChannelWriter<Frame<TMessage>> Output => _sendToSocket.Writer;

            byte[] _writeBuffer;
            int _writeOffset;
            Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint)
                => new Span<byte>(_writeBuffer, _writeOffset, _sendOptions.MaximumFrameSize - _writeOffset);
            Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
                => new Memory<byte>(_writeBuffer, _writeOffset, _sendOptions.MaximumFrameSize - _writeOffset);

            void IBufferWriter<byte>.Advance(int bytes)
            {
                if (bytes < 0 | (bytes + _writeOffset) > _sendOptions.MaximumFrameSize)
                    throw new ArgumentOutOfRangeException(nameof(bytes));
                _writeOffset += bytes;
            }


            void RentWriteBuffer()
            {
                _writeBuffer = ArrayPool<byte>.Shared.Rent(_sendOptions.MaximumFrameSize);
            }
            void ResetWriteBuffer()
            {
                _writeOffset = 0;
            }
            void ReturnWriteBuffer()
            {
                if (_writeBuffer != null)
                    ArrayPool<byte>.Shared.Return(_writeBuffer);
                _writeBuffer = null;
            }
            private async FireAndForget DoSendAsync()
            {
                await _sendOptions.ReaderScheduler.Yield();
                Exception error = null;
                SocketAwaitableEventArgs args = null;
                try
                {
                    DebugLog("Starting send loop...");
                    do
                    {
                        if (_sendToSocket.Reader.TryRead(out var frame))
                        {
                            DebugLog("Processing sync send frames...");
                            bool isFirst = true;
                            RentWriteBuffer();
                            do
                            {
                                using (frame)
                                {
                                    ResetWriteBuffer();
                                    _marshaller.Write(frame.Payload, this);

                                    if (_writeOffset == 0) continue; // empty payload
                                    if (args == null)
                                    {
                                        args = new SocketAwaitableEventArgs(InlineWrites ? null : _sendOptions.ReaderScheduler);

                                    }
                                    args.SocketFlags = frame.Flags;
                                    args.RemoteEndPoint = frame.Peer ?? _endpoint;
                                }

                                if (isFirst) args.SetBuffer(_writeBuffer, 0, _writeOffset);
                                else args.SetBuffer(0, _writeOffset);

                                DebugLog($"Sending {frame} to {args.RemoteEndPoint}, flags: {args.SocketFlags}");
                                if (_socket.SendToAsync(args))
                                {
                                    // will complete asynchronously
                                }
                                else
                                {
                                    // completed synchronously - need to mark completed
                                    args.Complete();
                                }
                                int bytes = await args;
                                DebugLog($"Sent: {bytes} bytes");
                            } while (_sendToSocket.Reader.TryRead(out frame));
                            ReturnWriteBuffer();
                        }
                        DebugLog("Awaiting async send frames...");
                    } while (await _sendToSocket.Reader.WaitToReadAsync());
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
                    ReturnWriteBuffer();
                }
            }

            async PooledValueTask OnReceivedAsync(byte[] payload, int bytes, SocketFlags flags, EndPoint peer, long localIndex)
            {
                try
                {
                    await Task.Yield();
                    DebugLog("Deserializing payload...");
                    var message = _marshaller.Read(new ReadOnlySequence<byte>(payload, 0, bytes), out var onDispose);
                    DebugLog("Writing received message to channel...");
                    await _receivedFromSocket.Writer.WriteAsync(new Frame<TMessage>(message, flags, peer, onDispose, localIndex));
                }
                catch (Exception ex)
                {
                    DebugLog(ex.Message);
                    TrySetShutdown(PipeShutdownKind.ReadException);
                }
                finally
                {
                    if (payload != null)
                        ArrayPool<byte>.Shared.Return(payload);
                }
            }

            private async FireAndForget DoReceiveAsync()
            {
                await _receiveOptions.ReaderScheduler.Yield();
                Exception error = null;
                var args = new SocketAwaitableEventArgs(InlineReads ? null : _receiveOptions.WriterScheduler);
                args.RemoteEndPoint = _endpoint;
                byte[] rented = null;
                ValueTask pending = default;
                long localIndex = 0;
                try
                {
                    DebugLog("Starting receive loop...");
                    while (true)
                    {
                        rented = ArrayPool<byte>.Shared.Rent(_receiveOptions.MaximumFrameSize);
                        DebugLog($"Rented buffer; {rented.Length} available; allowing {_receiveOptions.MaximumFrameSize}");
                        args.SetBuffer(rented, 0, _receiveOptions.MaximumFrameSize);
                        args.SocketFlags = SocketFlags.None;

                        bool isPending;
                        DebugLog($"Receiving from {args.RemoteEndPoint}...");
                        // args.RemoteEndPoint = _endpoint;
                        isPending = _socket.ReceiveFromAsync(args);

                        int bytes;
                        if (isPending)
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
                            bytes = args.GetResult();
                        }
                        DebugLog($"Received: {bytes} bytes");
                        if (bytes <= 0) break;

                        // we'll have at most one message deserializing while we fetch new data
                        await pending; // wait for the last queued item to finish (backlog etc)
                        pending = OnReceivedAsync(rented, bytes, args.SocketFlags, args.RemoteEndPoint, localIndex++); // don't await the new one yet
                        rented = null; // ownership transferred
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
                    if (rented != null) try { ArrayPool<byte>.Shared.Return(rented); } catch { }
                }
            }
        }
    }

}
