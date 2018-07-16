using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
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
            var addressFamily = endpoint.AddressFamily == AddressFamily.Unspecified ? AddressFamily.InterNetwork : endpoint.AddressFamily;
            var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;
            if (socket == null)
            {
                socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            }
            if (sendPipeOptions == null) sendPipeOptions = PipeOptions.Default;
            if (receivePipeOptions == null) receivePipeOptions = PipeOptions.Default;

            SetRecommendedClientOptions(socket);

            using (var args = CreateArgs(receivePipeOptions.ReaderScheduler, out _))
            {
                Helpers.DebugLog(name, $"connecting to {endpoint}...");

                await ConnectAsync(socket, args, endpoint);
            }

            Helpers.DebugLog(name, "connected");

            var connection = Create(socket, sendPipeOptions, receivePipeOptions, connectionOptions, name);

            if (onConnected != null) await onConnected(connection);

            return connection;
        }

        internal static void SetFastLoopbackOption(Socket socket)
        {
            // SIO_LOOPBACK_FAST_PATH (https://msdn.microsoft.com/en-us/library/windows/desktop/jj841212%28v=vs.85%29.aspx)
            // Speeds up localhost operations significantly. OK to apply to a socket that will not be hooked up to localhost,
            // or will be subject to WFP filtering.
            const int SIO_LOOPBACK_FAST_PATH = -1744830448;

            // windows only
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Win8/Server2012+ only
                var osVersion = Environment.OSVersion.Version;
                if (osVersion.Major > 6 || (osVersion.Major == 6 && osVersion.Minor >= 2))
                {
                    byte[] optionInValue = BitConverter.GetBytes(1);
                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
                }
            }
        }

        private static SocketAwaitable ConnectAsync(Socket socket, SocketAsyncEventArgs args, EndPoint endpoint)
        {
            args.RemoteEndPoint = endpoint;
            if (!socket.ConnectAsync(args)) SocketAwaitable.OnCompleted(args);
            return GetAwaitable(args);
        }

    }
}
