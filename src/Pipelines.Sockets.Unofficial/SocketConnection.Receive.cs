using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        private async Task<Exception> DoReceiveAsync()
        {
            Exception error = null;
            Helpers.DebugLog("starting receive loop");
            try
            {
                var args = CreateArgs(_pipeOptions.ReaderScheduler);
                while (true)
                {
                    if (ZeroLengthReads && Socket.Available == 0)
                    {
                        Helpers.DebugLog($"awaiting zero-length receive...");
                        await ReceiveAsync(Socket, args, default);
                        Helpers.DebugLog($"zero-length receive complete; now {Socket.Available} bytes available");

                        // this *could* be because data is now available, or it *could* be because of
                        // the EOF; we can't really trust Available, so now we need to do a non-empty
                        // read to find out which
                    }

                    var buffer = _receive.Writer.GetMemory(1);
                    Helpers.DebugLog($"leased {buffer.Length} bytes from pool");
                    try
                    {
                        Helpers.DebugLog($"awaiting socket receive...");
                        var bytesReceived = await ReceiveAsync(Socket, args, buffer);
                        Helpers.DebugLog($"received {bytesReceived} bytes");

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

                    Helpers.DebugLog("flushing pipe");
                    var flushTask = _receive.Writer.FlushAsync();

                    FlushResult result;
                    if (flushTask.IsCompletedSuccessfully)
                    {
                        result = flushTask.Result;
                        Helpers.DebugLog("pipe flushed (sync)");
                    }
                    else
                    {
                        result = await flushTask;
                        Helpers.DebugLog("pipe flushed (async)");
                    }

                    if (result.IsCompleted)
                    {
                        // Pipe consumer is shut down, do we stop writing
                        Helpers.DebugLog("complete");
                        break;
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                Helpers.DebugLog($"fail: {ex.SocketErrorCode}");
                error = new ConnectionResetException(ex.Message, ex);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                             ex.SocketErrorCode == SocketError.Interrupted ||
                                             ex.SocketErrorCode == SocketError.InvalidArgument)
            {
                Helpers.DebugLog($"fail: {ex.SocketErrorCode}");
                if (!_receiveAborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                }
            }
            catch (ObjectDisposedException)
            {
                Helpers.DebugLog($"fail: disposed");
                if (!_receiveAborted)
                {
                    error = new ConnectionAbortedException();
                }
            }
            catch (IOException ex)
            {
                Helpers.DebugLog($"fail - io: {ex.Message}");
                error = ex;
            }
            catch (Exception ex)
            {
                Helpers.DebugLog($"fail: {ex.Message}");
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
                    Helpers.DebugLog($"shutting down socket-receive");
                    Socket.Shutdown(SocketShutdown.Receive);
                }
                catch { }

                // close the *writer* half of the receive pipe; we won't
                // be writing any more, but callers can still drain the
                // pipe if they choose
                Helpers.DebugLog($"marking {nameof(Input)} as complete");
                try { _receive.Writer.Complete(error); } catch { }
            }

            Helpers.DebugLog(error == null ? "exiting with success" : $"exiting with failure: {error.Message}");
            return error;
        }

        private static SocketAwaitable ReceiveAsync(Socket socket, SocketAsyncEventArgs args, Memory<byte> buffer)
        {
#if NETCOREAPP2_1
            args.SetBuffer(buffer);
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