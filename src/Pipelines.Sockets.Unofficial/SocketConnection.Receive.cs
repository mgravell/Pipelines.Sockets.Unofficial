using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        SocketAwaitable _readerAwaitable;
        private async void DoReceiveAsync()
        {
            _receive.Writer.OnReaderCompleted(
                (ex, state) => ((Socket)state).Shutdown(SocketShutdown.Receive), Socket);
            Exception error = null;
            DebugLog("starting receive loop");
            try
            {
                var args = CreateArgs(_receiveOptions.ReaderScheduler, out _readerAwaitable);
                while (true)
                {
                    if (ZeroLengthReads && Socket.Available == 0)
                    {
                        DebugLog($"awaiting zero-length receive...");

                        Helpers.Incr(Counter.OpenReceiveReadAsync);
                        var receive = ReceiveAsync(Socket, args, default, Name);
                        Helpers.Incr(receive.IsCompleted ? Counter.SocketZeroLengthReceiveSync : Counter.SocketZeroLengthReceiveAsync);
                        await receive;
                        Helpers.Decr(Counter.OpenReceiveReadAsync);
                        DebugLog($"zero-length receive complete; now {Socket.Available} bytes available");

                        // this *could* be because data is now available, or it *could* be because of
                        // the EOF; we can't really trust Available, so now we need to do a non-empty
                        // read to find out which
                    }

                    var buffer = _receive.Writer.GetMemory(1);
                    DebugLog($"leased {buffer.Length} bytes from pipe");
                    try
                    {
                        DebugLog($"initiating socket receive...");
                        Helpers.Incr(Counter.OpenReceiveReadAsync);
                        var receive = ReceiveAsync(Socket, args, buffer, Name);
                        Helpers.Incr(receive.IsCompleted ? Counter.SocketReceiveSync : Counter.SocketReceiveAsync);
                        DebugLog(receive.IsCompleted ? "receive is sync" : "receive is async");
                        var bytesReceived = await receive;
                        Helpers.Decr(Counter.OpenReceiveReadAsync);
                        DebugLog($"received {bytesReceived} bytes ({args.BytesTransferred}, {args.SocketError})");
                        
                        _receive.Writer.Advance(bytesReceived);

                        if (bytesReceived == 0)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        // commit?
                    }

                    DebugLog("flushing pipe");
                    Helpers.Incr(Counter.OpenReceiveFlushAsync);
                    var flushTask = _receive.Writer.FlushAsync();
                    Helpers.Incr(flushTask.IsCompleted ? Counter.SocketPipeFlushSync : Counter.SocketPipeFlushAsync);

                    FlushResult result;
                    if (flushTask.IsCompletedSuccessfully)
                    {
                        result = flushTask.Result;
                        DebugLog("pipe flushed (sync)");
                    }
                    else
                    {
                        result = await flushTask;
                        DebugLog("pipe flushed (async)");
                    }
                    Helpers.Decr(Counter.OpenReceiveFlushAsync);

                    if (result.IsCanceled || result.IsCompleted)
                    {
                        // Pipe consumer is shut down, do we stop writing
                        DebugLog(result.IsCanceled ? "canceled" : "complete");
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
            //return error;
        }

        private static SocketAwaitable ReceiveAsync(Socket socket, SocketAsyncEventArgs args, Memory<byte> buffer, string name)
        {
#if SOCKET_STREAM_BUFFERS
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
            Helpers.DebugLog(name, $"## {nameof(socket.ReceiveAsync)} <={buffer.Length}");
            if (!socket.ReceiveAsync(args)) SocketAwaitable.OnCompleted(args);

            return GetAwaitable(args);
        }
    }
}