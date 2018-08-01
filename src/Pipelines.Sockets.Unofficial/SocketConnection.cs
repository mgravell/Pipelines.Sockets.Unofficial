using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public enum PipeShutdownKind
    {
        None = 0, // important this stays zero for default value, etc
        PipeDisposed = 1,

        ReadEndOfStream = 2,
        ReadDisposed = 4,
        ReadIOException = 5,
        ReadException = 6,
        ReadSocketError = 7,

        WriteEndOfStream = 8,
        WriteDisposed = 9,
        WriteIOException = 10,
        WriteException = 11,
        WriteSocketError = 12,        
    }
    /// <summary>
    /// Reperesents a duplex pipe over managed sockets
    /// </summary>
    public sealed partial class SocketConnection : IDuplexPipe, IDisposable
    {
#if DEBUG
        ~SocketConnection() => Helpers.Incr(Counter.SocketConnectionCollectedWithoutDispose);
#endif

        private int _socketShutdownKind;
        public PipeShutdownKind ShutdownKind => (PipeShutdownKind)Thread.VolatileRead(ref _socketShutdownKind);
        public SocketError SocketError {get; private set;}

        private bool TrySetShutdown(PipeShutdownKind kind) => kind != PipeShutdownKind.None &&
            Interlocked.CompareExchange(ref _socketShutdownKind, (int)kind, 0) == 0;

        private bool TrySetShutdown(PipeShutdownKind kind, SocketError socketError)
        {
            bool win = TrySetShutdown(kind);
            if (win) SocketError = socketError;
            return win;
        }

        /// <summary>
        /// Set recommended socket options for client sockets
        /// </summary>
        public static void SetRecommendedClientOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch (Exception ex) { Helpers.DebugLog(nameof(SocketConnection), ex.Message); }

            try { SetFastLoopbackOption(socket); } catch (Exception ex) { Helpers.DebugLog(nameof(SocketConnection), ex.Message); }
        }

        /// <summary>
        /// Set recommended socket options for server sockets
        /// </summary>
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
            try { Socket?.Close(); } catch { }
            try { _readerAwaitable?.TryComplete(0, SocketError.Shutdown); } catch { }
            try { _writerAwaitable?.TryComplete(0, SocketError.Shutdown); } catch { }

            try { Socket?.Dispose(); } catch { }
            // Socket = null;
        }
        /// <summary>
        /// Connection for receiving data
        /// </summary>
        public PipeReader Input => _receive.Reader;

        /// <summary>
        /// Connection for sending data
        /// </summary>
        public PipeWriter Output => _send.Writer;
        private string Name { get; }
        /// <summary>
        /// Gets a string representation of this object
        /// </summary>
        public override string ToString() => Name;
        void RunThreadAsTask(object state, Action<object> callback, string name)
        {
            if (!string.IsNullOrWhiteSpace(Name)) name = Name + ":" + name;
#pragma warning disable IDE0017
            var thread = new Thread(tuple =>
            {
                var t = (Tuple<object, Action<object>, TaskCompletionSource<Exception>>)tuple;
                //try { t.Item3?.TrySetResult(t.Item2(t.Item1)); }
                //catch (Exception ex) { t.Item3.TrySetException(ex); }

                t.Item2(t.Item1);
            });
            thread.IsBackground = true;
#pragma warning restore IDE0017
            if (string.IsNullOrWhiteSpace(name)) name = callback.Method.Name;
            if (!string.IsNullOrWhiteSpace(name)) thread.Name = name;

            TaskCompletionSource<Exception> tcs = null; // new TaskCompletionSource<Exception>();
            thread.Start(Tuple.Create(state, callback, tcs));
            //return tcs.Task;
        }

        /// <summary>
        /// The underlying socket for this connection
        /// </summary>
        public Socket Socket { get; }

        private Pipe _send, _receive;
        // TODO: flagify
#pragma warning disable CS0414, CS0649
        private volatile bool _sendAborted, _receiveAborted;
#pragma warning restore CS0414, CS0649

        private static SocketAsyncEventArgs CreateArgs(PipeScheduler scheduler, out SocketAwaitable awaitable)
        {
            awaitable = new SocketAwaitable(scheduler);
            var args = new SocketAsyncEventArgs { UserToken = awaitable };
            args.Completed += SocketAwaitable.Callback;
            return args;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SocketAwaitable GetAwaitable(SocketAsyncEventArgs args)
            => (SocketAwaitable)args.UserToken;

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions pipeOptions = null,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            var conn = new SocketConnection(socket, pipeOptions, pipeOptions, socketConnectionOptions, name);
            return conn;
        }

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            var conn = new SocketConnection(socket, sendPipeOptions, receivePipeOptions, socketConnectionOptions, name);
            return conn;
        }
        private SocketConnection(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions, SocketConnectionOptions socketConnectionOptions, string name = null)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name.Trim();
            if (sendPipeOptions == null) sendPipeOptions = PipeOptions.Default;
            if (receivePipeOptions == null) receivePipeOptions = PipeOptions.Default;

            Socket = socket;
            SocketConnectionOptions = socketConnectionOptions;
            _send = new Pipe(sendPipeOptions);
            _receive = new Pipe(receivePipeOptions);

            _receiveOptions = receivePipeOptions;
            _sendOptions = sendPipeOptions;

            sendPipeOptions.ReaderScheduler.Schedule(s_DoSendAsync, this);
            receivePipeOptions.ReaderScheduler.Schedule(s_DoReceiveAsync, this);
        }

        private static void DoReceiveAsync(object s) => ((SocketConnection)s).DoReceiveAsync();
        private static readonly Action<object> s_DoReceiveAsync = DoReceiveAsync;
        private static void DoSendAsync(object s) => ((SocketConnection)s).DoSendAsync();
        private static readonly Action<object> s_DoSendAsync = DoSendAsync;

        private PipeOptions _receiveOptions, _sendOptions;

        static List<ArraySegment<byte>> _spareBuffer;
        private static List<ArraySegment<byte>> GetSpareBuffer()
        {
            var existing = Interlocked.Exchange(ref _spareBuffer, null);
            existing?.Clear();
            return existing;
        }
        private static void RecycleSpareBuffer(List<ArraySegment<byte>> value)
        {
            if (value != null) Interlocked.Exchange(ref _spareBuffer, value);
        }

    }
}