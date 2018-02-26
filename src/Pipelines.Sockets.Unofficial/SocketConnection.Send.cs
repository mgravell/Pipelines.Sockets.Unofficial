using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        private async Task<Exception> DoSend()
        {
            Exception error = null;
            DebugLog("starting send loop");
            try
            {
                SocketAsyncEventArgs args = null;
                while (true)
                {
                    DebugLog("awaiting data from pipe...");
                    var result = await _send.Reader.ReadAsync();
                    var buffer = result.Buffer;

                    if (result.IsCanceled)
                    {
                        DebugLog("cancelled");
                        break;
                    }

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            if (args == null) args = CreateArgs();
                            DebugLog($"sending {buffer.Length} bytes over socket...");
                            await SendAsync(Socket, args, buffer);
                        }
                        else if (result.IsCompleted)
                        {
                            DebugLog("completed");
                            break;
                        }
                    }
                    finally
                    {
                        DebugLog("advancing");
                        _send.Reader.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = null;
            }
            catch (ObjectDisposedException)
            {
                DebugLog("fail: disposed");
                error = null;
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
                // Make sure to close the connection only after the _aborted flag is set.
                // Without this, the RequestsCanBeAbortedMidRead test will sometimes fail when
                // a BadHttpRequestException is thrown instead of a TaskCanceledException.
                _sendAborted = true;
                try
                {
                    DebugLog($"shutting down socket-send");
                    Socket.Shutdown(SocketShutdown.Send);
                }
                catch { }

                // close *both halves* of the send pipe; we're not
                // listening *and* we don't want anyone trying to write
                DebugLog($"marking {nameof(Output)} as complete");
                try { _send.Writer.Complete(error); } catch { }
                try { _send.Reader.Complete(error); } catch { }
            }
            DebugLog(error == null ? "exiting with success" : $"exiting with failure: {error.Message}");
            return error;
        }

        private static SocketAwaitable SendAsync(Socket socket, SocketAsyncEventArgs args, ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                return SendAsync(socket, args, buffer.First);
            }

#if NETCOREAPP2_1
            if (!_eventArgs.MemoryBuffer.Equals(Memory<byte>.Empty))
#else
            if (args.Buffer != null)
#endif
            {
                args.SetBuffer(null, 0, 0);
            }

            args.BufferList = GetBufferList(args, buffer);

            if (!socket.SendAsync(args)) OnCompleted(args);

            return GetAwaitable(args);
        }

        private static SocketAwaitable SendAsync(Socket socket, SocketAsyncEventArgs args, ReadOnlyMemory<byte> memory)
        {
            // The BufferList getter is much less expensive then the setter.
            if (args.BufferList != null)
            {
                args.BufferList = null;
            }

#if NETCOREAPP2_1
            _eventArgs.SetBuffer(MemoryMarshal.AsMemory(memory));
#else
            var segment = memory.GetArray();

            args.SetBuffer(segment.Array, segment.Offset, segment.Count);
#endif
            if (!socket.SendAsync(args)) OnCompleted(args);

            return GetAwaitable(args);
        }

        private static List<ArraySegment<byte>> GetBufferList(SocketAsyncEventArgs args, ReadOnlySequence<byte> buffer)
        {
            Debug.Assert(!buffer.IsEmpty);
            Debug.Assert(!buffer.IsSingleSegment);

            var list = (List<ArraySegment<byte>>)args.BufferList;

            if (list == null)
            {
                list = new List<ArraySegment<byte>>();
            }
            else
            {
                // Buffers are pooled, so it's OK to root them until the next multi-buffer write.
                list.Clear();
            }

            foreach (var b in buffer)
            {
                list.Add(b.GetArray());
            }

            return list;
        }
    }
}