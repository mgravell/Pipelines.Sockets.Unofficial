using System;
using System.Buffers;
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
            Func<SocketConnection, Task> onConnected = null,
            Socket socket = null
#if DEBUG
            , System.IO.TextWriter log = null
#endif
            )
        {
            if (socket == null)
            {
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            }
            try { socket.NoDelay = true; } catch { }
            try { SetFastLoopbackOption(socket); } catch { }
            var args = CreateArgs();

#if DEBUG
            DebugLog(log, $"connecting to {endpoint}...");
#endif
            await ConnectAsync(socket, args, endpoint);
#if DEBUG
            DebugLog(log, "connected");
#endif

            var connection = Create(socket, options
#if DEBUG
            , log: log
#endif
            );

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
            if (!socket.ConnectAsync(args)) OnCompleted(args);
            return GetAwaitable(args);
        }

    }
}
