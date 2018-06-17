// Licensed under the Apache License, Version 2.0.

using System;
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

        static Task<Exception> RunThreadAsTask(SocketConnection connection, Func<SocketConnection, Exception> callback, string name)
        {
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

        private static SocketAsyncEventArgs CreateArgs()
        {
            var args = new SocketAsyncEventArgs { UserToken = new SocketAwaitable() };
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
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None
#if DEBUG
            , TextWriter log = null
#endif
            )
        {
            var conn = new SocketConnection(socket, pipeOptions, socketConnectionOptions);
#if DEBUG
            conn._log = log;
#endif
            return conn;
        }

        Task _receiveTask, _sendTask;


        private SocketConnection(Socket socket, PipeOptions pipeOptions, SocketConnectionOptions socketConnectionOptions)
        {
            if (pipeOptions == null) pipeOptions = GetDefaultOptions();
            Socket = socket;
            SocketConnectionOptions = socketConnectionOptions;
            _send = new Pipe(pipeOptions);
            _receive = new Pipe(pipeOptions);
        }

    }
}