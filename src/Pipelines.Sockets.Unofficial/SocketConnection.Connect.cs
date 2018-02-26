using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        private static PipeOptions _defaultOptions;
        private static PipeOptions GetDefaultOptions()
            => _defaultOptions ?? (_defaultOptions = new PipeOptions(MemoryPool<byte>.Shared));
        public static async Task<SocketConnection> ConnectAsync(
            EndPoint endpoint,
            PipeOptions options = null,
            Action<Socket> onConnected = null
#if DEBUG
            , TextWriter log = null
#endif
            )
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var args = CreateArgs();

#if DEBUG
            DebugLog(log, $"connecting to {endpoint}...");
#endif
            await ConnectAsync(socket, args, endpoint);
#if DEBUG
            DebugLog(log, "connected");
#endif
            socket.NoDelay = true;
            onConnected?.Invoke(socket);


            return Create(socket, options
#if DEBUG
            , log: log
#endif
            );
        }

        private static SocketAwaitable ConnectAsync(Socket socket, SocketAsyncEventArgs args, EndPoint endpoint)
        {
            args.RemoteEndPoint = endpoint;
            if (!socket.ConnectAsync(args)) OnCompleted(args);
            return GetAwaitable(args);
        }

    }
}
