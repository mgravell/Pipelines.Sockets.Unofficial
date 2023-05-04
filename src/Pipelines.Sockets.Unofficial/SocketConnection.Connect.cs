using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public partial class SocketConnection
    {
        /// <summary>
        /// Open a new or existing socket as a client
        /// </summary>
        public static Task<SocketConnection> ConnectAsync(
            EndPoint endpoint,
            PipeOptions pipeOptions = null,
            SocketConnectionOptions connectionOptions = SocketConnectionOptions.None,
            Func<SocketConnection, Task> onConnected = null,
            Socket socket = null, string name = null)
            => ConnectAsync(endpoint, pipeOptions, pipeOptions, connectionOptions, onConnected, socket, name);

        /// <summary>
        /// Open a new or existing socket as a client
        /// </summary>
        public static async Task<SocketConnection> ConnectAsync(
            EndPoint endpoint,
            PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions connectionOptions = SocketConnectionOptions.None,
            Func<SocketConnection, Task> onConnected = null,
            Socket socket = null, string name = null)
        {
            AssertDependencies();
            var addressFamily = endpoint.AddressFamily == AddressFamily.Unspecified ? AddressFamily.InterNetwork : endpoint.AddressFamily;
            var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;
            if (socket is null)
            {
                socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            }
            if (sendPipeOptions is null) sendPipeOptions = PipeOptions.Default;
            if (receivePipeOptions is null) receivePipeOptions = PipeOptions.Default;

            SetRecommendedClientOptions(socket);

            using (var args = new SocketAwaitableEventArgs((connectionOptions & SocketConnectionOptions.InlineConnect) == 0 ? PipeScheduler.ThreadPool : null))
            {
                args.RemoteEndPoint = endpoint;
                Helpers.DebugLog(name, $"connecting to {endpoint}...");

                if (!socket.ConnectAsync(args)) args.Complete();
                await args;
            }

            Helpers.DebugLog(name, "connected");

            var connection = Create(socket, sendPipeOptions, receivePipeOptions, connectionOptions, name);

            if (onConnected is not null) await onConnected(connection).ConfigureAwait(false);

            return connection;
        }
    }
}
