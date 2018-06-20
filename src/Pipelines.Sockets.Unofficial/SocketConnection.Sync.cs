using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        private Exception DoSendSync()
        {
            Exception error = null;
            Helpers.DebugLog("starting send loop");
            try
            {
                var waitSignal = new AutoResetEvent(false);
                Action setSignal = () => waitSignal.Set();
                while (true)
                {
                    Helpers.DebugLog("awaiting data from pipe...");
                    if (!_send.Reader.TryRead(out var result))
                    {
                        var t = _send.Reader.ReadAsync();
                        if (t.IsCompletedSuccessfully)
                        {
                            result = t.Result;
                        }
                        else
                        {
                            var awaiter = t.GetAwaiter();
                            awaiter.UnsafeOnCompleted(setSignal);
                            waitSignal.WaitOne();
                            result = awaiter.GetResult();
                        }
                    }

                    var buffer = result.Buffer;

                    if (result.IsCanceled)
                    {
                        Helpers.DebugLog("cancelled");
                        break;
                    }

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            Helpers.DebugLog($"sending {buffer.Length} bytes over socket...");
                            Send(Socket, buffer);
                        }
                        else if (result.IsCompleted)
                        {
                            Helpers.DebugLog("completed");
                            break;
                        }
                    }
                    finally
                    {
                        Helpers.DebugLog("advancing");
                        _send.Reader.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                Helpers.DebugLog($"fail: {ex.SocketErrorCode}");
                error = null;
            }
            catch (ObjectDisposedException)
            {
                Helpers.DebugLog("fail: disposed");
                error = null;
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
                // Make sure to close the connection only after the _aborted flag is set.
                // Without this, the RequestsCanBeAbortedMidRead test will sometimes fail when
                // a BadHttpRequestException is thrown instead of a TaskCanceledException.
                _sendAborted = true;
                try
                {
                    Helpers.DebugLog($"shutting down socket-send");
                    Socket.Shutdown(SocketShutdown.Send);
                }
                catch { }

                // close *both halves* of the send pipe; we're not
                // listening *and* we don't want anyone trying to write
                Helpers.DebugLog($"marking {nameof(Output)} as complete");
                try { _send.Writer.Complete(error); } catch { }
                try { _send.Reader.Complete(error); } catch { }
            }
            Helpers.DebugLog(error == null ? "exiting with success" : $"exiting with failure: {error.Message}");
            return error;
        }

        private Exception DoReceiveSync()
        {
            Exception error = null;
            Helpers.DebugLog("starting receive loop");
            try
            {
                var waitSignal = new AutoResetEvent(false);
                Action setSignal = () => waitSignal.Set();

                var args = CreateArgs(_pipeOptions.ReaderScheduler);
                while (true)
                {
                    if (ZeroLengthReads && Socket.Available == 0)
                    {
                        Helpers.DebugLog($"awaiting zero-length receive...");
                        Receive(Socket, default);
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
                        var bytesReceived = Receive(Socket, buffer);
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
                        var awaiter = flushTask.GetAwaiter();
                        awaiter.UnsafeOnCompleted(setSignal);
                        waitSignal.WaitOne();
                        result = awaiter.GetResult();
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

        private static int Send(Socket socket, ReadOnlySequence<byte> buffer)
            => buffer.IsSingleSegment ? SendSingle(socket, buffer.First) : SendMulti(socket, buffer);
           
        private static int SendSingle(Socket socket, ReadOnlyMemory<byte> segment)
        {
#if NETCOREAPP2_1
            return socket.Send(segment.Span);
#else
            var arr = segment.GetArray();
            return socket.Send(arr.Array, arr.Offset, arr.Count, SocketFlags.None);
#endif
        }
        private static int SendMulti(Socket socket, ReadOnlySequence<byte> buffer)
        {
            var buffers = GetBufferList(null, buffer);
            var bytes = socket.Send(buffers);
            RecycleSpareBuffer(buffers);
            return bytes;
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
