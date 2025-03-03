using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// When possible, determines how the pipe first reached a close state
    /// </summary>
    public enum PipeShutdownKind
    {
        // 0**: things to do with the pipe
        /// <summary>
        /// The pipe is still open
        /// </summary>
        None = 0, // important this stays zero for default value, etc
        /// <summary>
        /// The pipe itself was disposed
        /// </summary>
        PipeDisposed = 1,

        // 1**: things to do with the read loop
        /// <summary>
        /// The socket-reader reached a natural EOF from the socket
        /// </summary>
        ReadEndOfStream = 100,
        /// <summary>
        /// The socket-reader encountered a dispose failure
        /// </summary>
        ReadDisposed = 101,
        /// <summary>
        /// The socket-reader encountered an IO failure
        /// </summary>
        ReadIOException = 102,
        /// <summary>
        /// The socket-reader encountered a general failure
        /// </summary>
        ReadException = 103,
        /// <summary>
        /// The socket-reader encountered a socket failure - the SocketError may be populated
        /// </summary>
        ReadSocketError = 104,
        /// <summary>
        /// When attempting to flush incoming data, the pipe indicated that it was complete
        /// </summary>
        ReadFlushCompleted = 105,
        /// <summary>
        /// When attempting to flush incoming data, the pipe indicated cancelation
        /// </summary>
        ReadFlushCanceled = 106,

        // 2**: things to do with the write loop
        /// <summary>
        /// The socket-writerreached a natural EOF from the pipe
        /// </summary>
        WriteEndOfStream = 200,
        /// <summary>
        /// The socket-writer encountered a dispose failure
        /// </summary>
        WriteDisposed = 201,
        /// <summary>
        /// The socket-writer encountered an IO failure
        /// </summary>
        WriteIOException = 203,
        /// <summary>
        /// The socket-writer encountered a general failure
        /// </summary>
        WriteException = 204,
        /// <summary>
        /// The socket-writer encountered a socket failure - the SocketError may be populated
        /// </summary>
        WriteSocketError = 205,

        // 3**: things to do with the reader/writer themselves
        /// <summary>
        /// The input's reader was completed
        /// </summary>
        InputReaderCompleted = 300,
        /// <summary>
        /// The input's writer was completed
        /// </summary>
        InputWriterCompleted = 301,
        /// <summary>
        /// The output's reader was completed
        /// </summary>
        OutputReaderCompleted = 302,
        /// <summary>
        /// The input's writer was completed
        /// </summary>
        OutputWriterCompleted = 303,
        /// <summary>
        /// An application defined exit was triggered by the client
        /// </summary>
        ProtocolExitClient = 400,
        /// <summary>
        /// An application defined exit was triggered by the server
        /// </summary>
        ProtocolExitServer = 401,
    }

    /// <summary>
    /// Reperesents a duplex pipe over managed sockets
    /// </summary>
    public sealed partial class SocketConnection : IMeasuredDuplexPipe, IDisposable
    {
#if DEBUG
        ~SocketConnection() => Helpers.Incr(Counter.SocketConnectionCollectedWithoutDispose);
#endif

        /// <summary>
        /// Check that all dependencies are available
        /// </summary>
        public static void AssertDependencies() => Helpers.AssertDependencies();

        private int _socketShutdownKind;
        /// <summary>
        /// When possible, determines how the pipe first reached a close state
        /// </summary>
        public PipeShutdownKind ShutdownKind => (PipeShutdownKind)Thread.VolatileRead(ref _socketShutdownKind);
        /// <summary>
        /// When the ShutdownKind relates to a socket error, may contain the socket error code
        /// </summary>
        public SocketError SocketError { get; private set; }

        private bool TrySetShutdown(PipeShutdownKind kind) => kind != PipeShutdownKind.None
            && Interlocked.CompareExchange(ref _socketShutdownKind, (int)kind, 0) == 0;

        /// <summary>
        /// Try to signal the pipe shutdown reason as being due to an application protocol event
        /// </summary>
        /// <param name="kind">The kind of shutdown; only protocol-related reasons will succeed</param>
        /// <returns>True if successful</returns>
        public bool TrySetProtocolShutdown(PipeShutdownKind kind)
        {
            switch (kind)
            {
                case PipeShutdownKind.ProtocolExitClient:
                case PipeShutdownKind.ProtocolExitServer:
                    return TrySetShutdown(kind);
                default:
                    return false;
            }
        }
        private bool TrySetShutdown(PipeShutdownKind kind, SocketError socketError)
        {
            bool win = TrySetShutdown(kind);
            if (win) SocketError = socketError;
            return win;
        }

        /// <summary>
        /// Set recommended socket options for client sockets
        /// </summary>
        /// <param name="socket">The socket to set options against</param>
        public static void SetRecommendedClientOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch (Exception ex) { Helpers.DebugLog(nameof(SocketConnection), ex.Message); }
        }

        /// <summary>
        /// Set recommended socket options for server sockets
        /// </summary>
        /// <param name="socket">The socket to set options against</param>
        public static void SetRecommendedServerOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch (Exception ex) { Helpers.DebugLog(nameof(SocketConnection), ex.Message); }
        }

#if DEBUG
#pragma warning disable CS1591
        public static void SetLog(System.IO.TextWriter writer) => Helpers.Log = writer;
#pragma warning restore CS1591
#endif

        [Conditional("VERBOSE")]
        private void DebugLog(string message, [CallerMemberName] string caller = null, [CallerLineNumber] int lineNumber = 0) => Helpers.DebugLog(Name, message, $"{caller}#{lineNumber}");

        /// <summary>
        /// Release any resources held by this instance
        /// </summary>
        public void Dispose()
        {
            TrySetShutdown(PipeShutdownKind.PipeDisposed);
#if DEBUG
            GC.SuppressFinalize(this);
#endif
            try { Socket.Shutdown(SocketShutdown.Receive); } catch { }
            try { Socket.Shutdown(SocketShutdown.Send); } catch { }
            try { Socket.Close(); } catch { }
            try { Socket.Dispose(); } catch { }

            // make sure that the async operations end ... can be twitchy!
            try { _readerArgs?.Abort(); } catch { }
            try { _writerArgs?.Abort(); } catch { }
        }

        /// <summary>
        /// Connection for receiving data
        /// </summary>
        public PipeReader Input => _input;

        /// <summary>
        /// Connection for sending data
        /// </summary>
        public PipeWriter Output => _output;
        private string Name { get; }

        /// <summary>
        /// Gets a string representation of this object
        /// </summary>
        public override string ToString() => Name;

        /// <summary>
        /// The underlying socket for this connection
        /// </summary>
        public Socket Socket { get; }

        /// <summary>
        /// Obtain performance monitoring counters about this connection
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Browsable(false)]
        public Counters GetCounters()
        {
            int available;
            try
            {
                available = Socket?.Available ?? 0;
            }
            catch { available = 0; }

            long sendLength = Counters.GetPipeLength(_sendToSocket),
                receiveLength = Counters.GetPipeLength(_receiveFromSocket);

            return new Counters(available, sendLength, receiveLength);
        }

        /// <summary>
        /// Exposes performance monitoring counters about a connection
        /// </summary>
        public readonly struct Counters
        {
            private static readonly Func<Pipe, long> s_pipeLengthReader;
            static Counters()
            {
                try
                {
                    // theoretically there's a problem here on x86; I'm... "comfortable enough" with it
                    // not to try to do anything more clever, though - if an x86 client has gone over 2GiB
                    // then they deserve a clap - a slow clap
                    var method = typeof(Pipe).GetProperty("Length",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetGetMethod(true);
                    if (method is null)
                    {
                        s_pipeLengthReader = _ => 0L;
                    }
                    else
                    {
                        s_pipeLengthReader = (Func<Pipe, long>)Delegate.CreateDelegate(typeof(Func<Pipe, long>), method);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    s_pipeLengthReader = _ => 0L;
                }
            }

            /// <summary>
            /// Get the number of bytes currently held in a pipe instance
            /// </summary>
            public static long GetPipeLength(Pipe pipe)
            {
                if (pipe is null) return 0;
                try
                {
                    return s_pipeLengthReader(pipe);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    return 0;
                }
            }

            /// <summary>
            /// The number of bytes available on the socket that have not yet been consumed into the pipe
            /// </summary>
            public long BytesAvailableOnSocket { get; }
            /// <summary>
            /// The number of bytes available on the send pipe that have not yet been sent to the socket
            /// </summary>
            public long BytesWaitingToBeSent { get; }
            /// <summary>
            /// The number of bytes available on the receive pipe, i.e. they have been processed from the socket, but not yet read
            /// </summary>
            public long BytesWaitingToBeRead { get; }
            internal Counters(int available, long sendLength, long receiveLength)
            {
                BytesAvailableOnSocket = available;
                BytesWaitingToBeSent = sendLength;
                BytesWaitingToBeRead = receiveLength;
            }
        }

        private readonly Pipe _sendToSocket, _receiveFromSocket;
        private readonly PipeReader _input; // was _receiveFromSocket.Reader;
        private readonly PipeWriter _output; // was _sendToSocket.Writer;

        // TODO: flagify and fully implement
#pragma warning disable CS0414, CS0649, IDE0044, IDE0051, IDE0052
        private volatile bool _sendAborted, _receiveAborted;
#pragma warning restore CS0414, CS0649, IDE0044, IDE0051, IDE0052

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions pipeOptions = null,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            AssertDependencies();
            return new SocketConnection(socket, pipeOptions, pipeOptions, socketConnectionOptions, name);
        }

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            AssertDependencies();
            return new SocketConnection(socket, sendPipeOptions, receivePipeOptions, socketConnectionOptions, name);
        }

        private SocketConnection(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions, SocketConnectionOptions socketConnectionOptions, string name = null)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name.Trim();
            if (sendPipeOptions is null) sendPipeOptions = PipeOptions.Default;
            if (receivePipeOptions is null) receivePipeOptions = PipeOptions.Default;

            if (socket is null) Throw.ArgumentNull(nameof(socket));
            Socket = socket;
            SocketConnectionOptions = socketConnectionOptions;
            _sendToSocket = new Pipe(sendPipeOptions);
            _receiveFromSocket = new Pipe(receivePipeOptions);
            _receiveOptions = receivePipeOptions;
            _sendOptions = sendPipeOptions;

            _input = new WrappedReader(_receiveFromSocket.Reader, this);
            _output = new WrappedWriter(_sendToSocket.Writer, this);

            sendPipeOptions.ReaderScheduler.Schedule(s_DoSendAsync, this);
            receivePipeOptions.ReaderScheduler.Schedule(s_DoReceiveAsync, this);
        }

        private void InputReaderCompleted(Exception ex)
        {
            TrySetShutdown(ex, this, PipeShutdownKind.InputReaderCompleted);
            try { this.Socket.Shutdown(SocketShutdown.Receive); }
            catch { }
        }
        private void OutputWriterCompleted(Exception ex)
        {
            TrySetShutdown(ex, this, PipeShutdownKind.OutputWriterCompleted);
        }
        private sealed class WrappedReader : PipeReader
        {
            private readonly PipeReader _reader;
            private readonly SocketConnection _connection;

            public WrappedReader(PipeReader reader, SocketConnection connection)
            {
                _reader = reader;
                _connection = connection;
            }

            public override void Complete(Exception exception = null)
            {
                _connection.InputReaderCompleted(exception);
                _reader.Complete(exception);
            }
            public override void AdvanceTo(SequencePosition consumed)
                => _reader.AdvanceTo(consumed);
            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
                => _reader.AdvanceTo(consumed, examined);
            public override void CancelPendingRead()
                => _reader.CancelPendingRead();
            public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
                => _reader.ReadAsync(cancellationToken);
            public override bool TryRead(out ReadResult result)
                => _reader.TryRead(out result);

            // note - consider deprecated: https://github.com/dotnet/corefx/issues/38362
            [Obsolete]
            public override void OnWriterCompleted(Action<Exception, object> callback, object state)
                => _reader.OnWriterCompleted(callback, state);
        }

        private sealed class WrappedWriter : PipeWriter
        {
            private readonly PipeWriter _writer;
            private readonly SocketConnection _connection;

            public WrappedWriter(PipeWriter writer, SocketConnection connection)
            {
                _writer = writer;
                _connection = connection;
            }
            public override void Complete(Exception exception = null)
            {
                _connection.OutputWriterCompleted(exception);
                _writer.Complete(exception);
            }
            public override void Advance(int bytes)
                => _writer.Advance(bytes);
            public override void CancelPendingFlush()
                => _writer.CancelPendingFlush();
            public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
                => _writer.FlushAsync(cancellationToken);
            public override Memory<byte> GetMemory(int sizeHint = 0)
                => _writer.GetMemory(sizeHint);
            public override Span<byte> GetSpan(int sizeHint = 0)
                => _writer.GetSpan(sizeHint);
            public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
                => _writer.WriteAsync(source, cancellationToken);

            // note - consider deprecated: https://github.com/dotnet/corefx/issues/38362
            [Obsolete]
            public override void OnReaderCompleted(Action<Exception, object> callback, object state)
                => _writer.OnReaderCompleted(callback, state);
        }

        private static bool TrySetShutdown(Exception ex, SocketConnection connection, PipeShutdownKind kind)
        {
            try
            {
                return ex is SocketException se ? connection.TrySetShutdown(kind, se.SocketErrorCode)
                    : connection.TrySetShutdown(kind);
            }
            catch
            {
                return false;
            }
        }

        private static void DoReceiveAsync(object s) => ((SocketConnection)s).DoReceiveAsync().PipelinesFireAndForget();
        private static readonly Action<object> s_DoReceiveAsync = DoReceiveAsync;
        private static void DoSendAsync(object s) => ((SocketConnection)s).DoSendAsync().PipelinesFireAndForget();
        private static readonly Action<object> s_DoSendAsync = DoSendAsync;

        private readonly PipeOptions _receiveOptions, _sendOptions;

        private static List<ArraySegment<byte>> _spareBuffer;

        private static List<ArraySegment<byte>> GetSpareBuffer()
        {
            var existing = Interlocked.Exchange(ref _spareBuffer, null);
            existing?.Clear();
            return existing;
        }

        private static void RecycleSpareBuffer(SocketAwaitableEventArgs args)
        {
            // note: the BufferList getter is much less expensive then the setter.
            if (args?.BufferList is List<ArraySegment<byte>> list)
            {
                args.BufferList = null; // see #26 - don't want it being reused by the next piece of IO
                Interlocked.Exchange(ref _spareBuffer, list);
            }
        }
    }
}