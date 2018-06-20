// Licensed under the Apache License, Version 2.0.

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
    /// <summary>
    /// Reperesents a duplex pipe over managed sockets
    /// </summary>
    public sealed partial class SocketConnection : IDuplexPipe, IDisposable
    {
#if DEBUG
        public static void SetLog(System.IO.TextWriter writer) => Helpers.Log = writer;
#endif

        [Conditional("VERBOSE")]
        private void DebugLog(string message, [CallerMemberName] string caller = null) => Helpers.DebugLog(Name, message, caller);

        /// <summary>
        /// Release any resources held by this instance
        /// </summary>
        public void Dispose()
        {
            Socket?.Dispose();
            // Socket = null;
        }
        /// <summary>
        /// Connection for receiving data
        /// </summary>
        public PipeReader Input
        {
            get
            {
                if (_receiveTask == null) _receiveTask = HasFlag(SocketConnectionOptions.SyncReader)
                        ? RunThreadAsTask(this, c => c.DoReceiveSync(), nameof(DoReceiveSync)) : DoReceiveAsync();
                return _receive.Reader;
            }
        }

        /// <summary>
        /// Connection for sending data
        /// </summary>
        public PipeWriter Output
        {
            get
            {
                if (_sendTask == null)
                {
                    _sendTask = HasFlag(SocketConnectionOptions.SyncWriter)
                        ? RunThreadAsTask(this, c => c.DoSendSync(), nameof(DoSendSync)) : DoSendAsync();
                }
                return _send.Writer;
            }
        }
        private string Name { get; }
        public override string ToString() => Name;
        Task<Exception> RunThreadAsTask(SocketConnection connection, Func<SocketConnection, Exception> callback, string name)
        {
            if (!string.IsNullOrWhiteSpace(Name)) name = Name + ":" + name;
            var thread = new Thread(tuple =>
            {
                var t = (Tuple<SocketConnection, Func<SocketConnection, Exception>, TaskCompletionSource<Exception>>)tuple;
                try { t.Item3?.TrySetResult(t.Item2(t.Item1)); }
                catch (Exception ex) { t.Item3.TrySetException(ex); }
                
            });
            thread.IsBackground = true;
            if (string.IsNullOrWhiteSpace(name)) name = callback.Method.Name;
            if (!string.IsNullOrWhiteSpace(name)) thread.Name = name;
            
            var tcs = new TaskCompletionSource<Exception>();
            thread.Start(Tuple.Create(connection, callback, tcs));
            return tcs.Task;
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

        private static SocketAsyncEventArgs CreateArgs(PipeScheduler scheduler)
        {
            if (ReferenceEquals(scheduler, PipeScheduler.Inline)) scheduler = null;
            var args = new SocketAsyncEventArgs { UserToken = new SocketAwaitable(scheduler) };
            args.Completed += (_, e) => OnCompleted(e);
            return args;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SocketAwaitable GetAwaitable(SocketAsyncEventArgs args)
            => (SocketAwaitable)args.UserToken;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OnCompleted(SocketAsyncEventArgs args)
            => ((SocketAwaitable)args.UserToken).Complete(args.BytesTransferred, args.SocketError);

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions pipeOptions = null,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            var conn = new SocketConnection(socket, pipeOptions, socketConnectionOptions, name);
            return conn;
        }

        Task _receiveTask, _sendTask;


        private SocketConnection(Socket socket, PipeOptions pipeOptions, SocketConnectionOptions socketConnectionOptions, string name = null)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name.Trim();
            if (pipeOptions == null) pipeOptions = GetDefaultOptions();
            _pipeOptions = pipeOptions;
            Socket = socket;
            SocketConnectionOptions = socketConnectionOptions;
            _send = new Pipe(pipeOptions);
            _receive = new Pipe(pipeOptions);
            
        }
        private readonly PipeOptions _pipeOptions;

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