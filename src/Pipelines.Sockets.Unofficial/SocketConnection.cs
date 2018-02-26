// Licensed under the Apache License, Version 2.0.

using System;
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
        public PipeReader Input => _receive.Reader;

        public PipeWriter Output => _send.Writer;

        internal Socket Socket { get; private set; }

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

        
        void Start()
        {
            _receiveTask = DoReceive();
            _sendTask = DoSend();
        }
        Task _receiveTask, _sendTask;

        
        private SocketConnection(Socket socket, PipeOptions options)
        {
            Socket = socket;
            _send = new Pipe(options);
            _receive = new Pipe(options);
        }

    }
}