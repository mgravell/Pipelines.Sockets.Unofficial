using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        public static async Task<SocketConnection> ConnectAsync(EndPoint endpoint, PipeOptions options)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var args = CreateArgs();

            await ConnectAsync(socket, args, endpoint);
            
            var conn = new SocketConnection(socket, options);
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
