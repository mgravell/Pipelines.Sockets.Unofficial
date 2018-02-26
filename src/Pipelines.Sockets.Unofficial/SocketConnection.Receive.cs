using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        private async Task DoReceive()
        {
            Exception error = null;

            try
            {
                var args = CreateArgs();
                while (true)
                {
                    var buffer = _receive.Writer.GetMemory();

                    try
                    {
                        var bytesReceived = await ReceiveAsync(Socket, args, buffer);

                        if (bytesReceived == 0)
                        {
                            break;
                        }

                        _receive.Writer.Advance(bytesReceived);
                    }
                    finally
                    {
                        // commit?
                    }

                    var flushTask = _receive.Writer.FlushAsync();

                    if (!flushTask.IsCompleted)
                    {
                        await flushTask;
                    }

                    var result = flushTask.GetAwaiter().GetResult();
                    if (result.IsCompleted)
                    {
                        // Pipe consumer is shut down, do we stop writing
                        break;
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                error = new ConnectionResetException(ex.Message, ex);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                             ex.SocketErrorCode == SocketError.Interrupted ||
                                             ex.SocketErrorCode == SocketError.InvalidArgument)
            {
                if (!_receiveAborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                }
            }
            catch (ObjectDisposedException)
            {
                if (!_receiveAborted)
                {
                    error = new ConnectionAbortedException();
                }
            }
            catch (IOException ex)
            {
                error = ex;
            }
            catch (Exception ex)
            {
                error = new IOException(ex.Message, ex);
            }
            finally
            {
                if (_receiveAborted)
                {
                    error = error ?? new ConnectionAbortedException();
                }
                try
                {
                    Socket.Shutdown(SocketShutdown.Receive);
                }
                catch { }
                Input.Complete(error);
            }
        }

        private static SocketAwaitable ReceiveAsync(Socket socket, SocketAsyncEventArgs args, Memory<byte> buffer)
        {
#if NETCOREAPP2_1
            _eventArgs.SetBuffer(buffer);
#else

            var segment = buffer.GetArray();
            args.SetBuffer(segment.Array, segment.Offset, segment.Count);
#endif
            if (!socket.ReceiveAsync(args)) OnCompleted(args);

            return GetAwaitable(args);
        }
    }
}