using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Pipelines.Sockets.Unofficial
{
    public partial class SocketConnection
    {
        private SocketAwaitable _readerAwaitable;

        /// <summary>
        /// The total number of bytes read from the socket
        /// </summary>
        public long BytesRead { get; private set; }

        /// <summary>
        /// The number of bytes received in the last read
        /// </summary>
        public int LastReceived { get; private set; }

        private async void DoReceiveAsync()
        {
            Exception error = null;
            DebugLog("starting receive loop");
            SocketAsyncEventArgs args = null;
            try
            {
                args = CreateArgs(_receiveOptions.ReaderScheduler, out _readerAwaitable);
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

                    var buffer = _receiveFromSocket.Writer.GetMemory(1);
                    DebugLog($"leased {buffer.Length} bytes from pipe");
                    try
                    {
                        DebugLog($"initiating socket receive...");
                        Helpers.Incr(Counter.OpenReceiveReadAsync);
                        var receive = ReceiveAsync(Socket, args, buffer, Name);
                        Helpers.Incr(receive.IsCompleted ? Counter.SocketReceiveSync : Counter.SocketReceiveAsync);
                        DebugLog(receive.IsCompleted ? "receive is sync" : "receive is async");
                        var bytesReceived = await receive;
                        LastReceived = bytesReceived;
                        Helpers.Decr(Counter.OpenReceiveReadAsync);
                        DebugLog($"received {bytesReceived} bytes ({args.BytesTransferred}, {args.SocketError})");

                        if (bytesReceived <= 0)
                        {
                            _receiveFromSocket.Writer.Advance(0);
                            TrySetShutdown(PipeShutdownKind.ReadEndOfStream);
                            break;
                        }
                        _receiveFromSocket.Writer.Advance(bytesReceived);
                        BytesRead += bytesReceived;
                    }
                    finally
                    {
                        // commit?
                    }

                    DebugLog("flushing pipe");
                    Helpers.Incr(Counter.OpenReceiveFlushAsync);
                    var flushTask = _receiveFromSocket.Writer.FlushAsync();
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

                    if (result.IsCompleted)
                    {
                        TrySetShutdown(PipeShutdownKind.ReadFlushCompleted);
                        break;
                    }
                    if (result.IsCanceled)
                    {
                        TrySetShutdown(PipeShutdownKind.ReadFlushCanceled);
                        break;
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = new ConnectionResetException(ex.Message, ex);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted
                                             || ex.SocketErrorCode == SocketError.ConnectionAborted
                                             || ex.SocketErrorCode == SocketError.Interrupted
                                             || ex.SocketErrorCode == SocketError.InvalidArgument)
            {
                TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                if (!_receiveAborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                }
            }
            catch (SocketException ex)
            {
                TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = ex;
            }
            catch (ObjectDisposedException)
            {
                TrySetShutdown(PipeShutdownKind.ReadDisposed);
                DebugLog($"fail: disposed");
                if (!_receiveAborted)
                {
                    error = new ConnectionAbortedException();
                }
            }
            catch (IOException ex)
            {
                TrySetShutdown(PipeShutdownKind.ReadIOException);
                DebugLog($"fail - io: {ex.Message}");
                error = ex;
            }
            catch (Exception ex)
            {
                TrySetShutdown(PipeShutdownKind.ReadException);
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
                try { _receiveFromSocket.Writer.Complete(error); } catch { }

                if (args != null) try { args.Dispose(); } catch { }
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