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
            DebugLog("starting receive loop");
            try
            {
                var args = CreateArgs();
                while (true)
                {
                    if (ZeroLengthReads && Socket.Available == 0)
                    {
                        DebugLog($"awaiting zero-length receive...");
                        await ReceiveAsync(Socket, args, default);
                        DebugLog($"zero-length receive complete; now {Socket.Available} bytes available");
                    }

                    var buffer = _receive.Writer.GetMemory();
                    DebugLog($"leased {buffer.Length} bytes from pool");
                    try
                    {
                        DebugLog($"awaiting socket receive...");
                        var bytesReceived = await ReceiveAsync(Socket, args, buffer);
                        DebugLog($"received {bytesReceived} bytes");

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

                    DebugLog("flushing pipe");
                    var flushTask = _receive.Writer.FlushAsync();

                    if (!flushTask.IsCompleted)
                    {
                        await flushTask;
                        DebugLog("pipe flushed (async)");
                    }
                    else
                    {
                        DebugLog("pipe flushed (sync)");
                    }


                    var result = flushTask.GetAwaiter().GetResult();
                    if (result.IsCompleted)
                    {
                        // Pipe consumer is shut down, do we stop writing
                        DebugLog("complete");
                        break;
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = new ConnectionResetException(ex.Message, ex);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                             ex.SocketErrorCode == SocketError.Interrupted ||
                                             ex.SocketErrorCode == SocketError.InvalidArgument)
            {
                DebugLog($"fail: {ex.SocketErrorCode}");
                if (!_receiveAborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                }
            }
            catch (ObjectDisposedException)
            {
                DebugLog($"fail: disposed");
                if (!_receiveAborted)
                {
                    error = new ConnectionAbortedException();
                }
            }
            catch (IOException ex)
            {
                DebugLog($"fail - io: {ex.Message}");
                error = ex;
            }
            catch (Exception ex)
            {
                DebugLog($"fail: {ex.Message}");
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
                    DebugLog($"shutting down socket-receive");
                    Socket.Shutdown(SocketShutdown.Receive);
                }
                catch { }

                // close the *writer* half of the receive pipe; we won't
                // be writing any more, but callers can still drain the
                // pipe if they choose
                DebugLog($"marking {nameof(Input)} as complete");
                try { _receive.Writer.Complete(error); } catch { }
            }

            DebugLog(error == null ? "exiting with success" : $"exiting with failure: {error.Message}");
        }

        private static SocketAwaitable ReceiveAsync(Socket socket, SocketAsyncEventArgs args, Memory<byte> buffer)
        {
#if NETCOREAPP2_1
            _eventArgs.SetBuffer(buffer);
#else

            if (buffer.IsEmpty)
            {
                // zero-length; retain existing buffer if possible
                if (args.Buffer == null)
                {
                    args.SetBuffer(Array.Empty<byte>(), 0, 0);
                }
                else
                {   // it doesn't matter that the old buffer may still be in
                    // use somewhere; we're reading zero bytes!
                    args.SetBuffer(args.Offset, 0);
                }
            }
            else
            {
                var segment = buffer.GetArray();
                args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            }
#endif
            if (!socket.ReceiveAsync(args)) OnCompleted(args);

            return GetAwaitable(args);
        }
    }
}