// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public sealed partial class SocketConnection : IDuplexPipe, IDisposable
    {
        public void Dispose()
        {
            Socket?.Dispose();
            Socket = null;
        }
        public PipeReader Input
        {
            get
            {
                if (_receiveTask == null) _receiveTask = DoReceive();
                return _receive.Reader;
            }
        }

        public PipeWriter Output
        {
            get
            {
                if (_sendTask == null) _sendTask = DoSend();
                return _send.Writer;
            }
        }

        public Socket Socket { get; private set; }

        private Pipe _send, _receive;
        // TODO: flagify
        private volatile bool _sendAborted, _receiveAborted;

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

        public static SocketConnection Create(Socket socket, PipeOptions options = null
#if DEBUG
            , TextWriter log = null
#endif
            )
        {
            var conn = new SocketConnection(socket, options);
#if DEBUG
            conn._log = log;
#endif
            return conn;
        }

        Task _receiveTask, _sendTask;

        
        private SocketConnection(Socket socket, PipeOptions options)
        {
            if (options == null) options = GetDefaultOptions();
            Socket = socket;
            _send = new Pipe(options);
            _receive = new Pipe(options);
        }

    }
}