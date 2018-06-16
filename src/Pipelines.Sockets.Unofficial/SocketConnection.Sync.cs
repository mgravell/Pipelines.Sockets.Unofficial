using System;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Pipelines.Sockets.Unofficial
{
    interface ISyncDuplexPipe : IDuplexPipe
    {
        int Read();
    }
    partial class SocketConnection : ISyncDuplexPipe
    {
        int ISyncDuplexPipe.Read()
        {
            if (ZeroLengthReads && Socket.Available == 0)
            {
                DebugLog($"sync zero-length receive...");
                Receive(Socket, default);
                DebugLog($"zero-length receive complete; now {Socket.Available} bytes available");
            }
            var buffer = _receive.Writer.GetMemory(1);
            DebugLog($"leased {buffer.Length} bytes from pool");

            DebugLog($"awaiting socket receive...");
            var bytesReceived = Receive(Socket, buffer);
            DebugLog($"received {bytesReceived} bytes");

            if (bytesReceived > 0)
            {
                _receive.Writer.Advance(bytesReceived);
            }

            // should I flush here? if I don't flush, is it readable?
            return bytesReceived;
        }

        private static int Receive(Socket socket, Memory<byte> buffer)
        {

#if NETCOREAPP2_1
            return socket.Receive(buffer.Span);
#else
            if (buffer.IsEmpty)
            {
                return socket.Receive(Array.Empty<byte>());
            }
            else
            {
                var segment = buffer.GetArray();
                return socket.Receive(segment.Array, segment.Offset, segment.Count, SocketFlags.None);
            }
#endif
        }

    }
}
