using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        public static async Task<SocketConnection> ConnectAsync(EndPoint endpoint, PipeOptions options
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

            var conn = new SocketConnection(socket, options);
#if DEBUG
            conn._log = log;
#endif
            conn.Start();            
            return conn;
        }

        private static SocketAwaitable ConnectAsync(Socket socket, SocketAsyncEventArgs args, EndPoint endpoint)
        {
            args.RemoteEndPoint = endpoint;
            if (!socket.ConnectAsync(args)) OnCompleted(args);
            return GetAwaitable(args);
        }

    }
}
