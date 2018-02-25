// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public sealed class SocketConnection : IDuplexPipe, IDisposable
    {
        public void Dispose()
        {
            Socket?.Dispose();
            Socket = null;
        }
        public PipeReader Input => _receive.Reader;

        public PipeWriter Output => _send.Writer;

        internal Socket Socket { get; private set; }

        private Pipe _send, _receive;
        private SocketSender _sender;
        private SocketReceiver _receiver;
        private volatile bool _aborted;

        
        public static async Task<SocketConnection> ConnectAsync(EndPoint endpoint, PipeOptions options)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            SocketAwaitable completion = new SocketAwaitable();
            var args = new SocketAsyncEventArgs {
                RemoteEndPoint = endpoint,
                UserToken = completion
            };
            args.Completed += (s, e) => ((SocketAwaitable)e.UserToken).Complete();

            if (socket.ConnectAsync(args)) await completion;

            if (args.SocketError != SocketError.Success)
                throw new SocketException((int)args.SocketError);

            var conn = new SocketConnection(socket, options);
            conn.Start();
            return conn;
        }

        void Start()
        {
            _receiveTask = DoReceive();
            _sendTask = DoSend();
        }
        Task _receiveTask, _sendTask;

        
        private SocketConnection(Socket socket, PipeOptions options)
        {
            Socket = socket;
            _send = new Pipe(options);
            _receive = new Pipe(options);

            _sender = new SocketSender(socket);
            _receiver = new SocketReceiver(socket);
        }

        private const int MinAllocBufferSize = 2048;


        private async Task DoReceive()
        {
            Exception error = null;

            try
            {
                while (true)
                {
                    // Ensure we have some reasonable amount of buffer space
                    var buffer = _receive.Writer.GetMemory(MinAllocBufferSize);

                    try
                    {
                        var bytesReceived = await _receiver.ReceiveAsync(buffer);

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
                if (!_aborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                }
            }
            catch (ObjectDisposedException)
            {
                if (!_aborted)
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
                if (_aborted)
                {
                    error = error ?? new ConnectionAbortedException();
                }

                Input.Complete(error);
            }
        }

        private async Task<Exception> DoSend()
        {
            Exception error = null;

            try
            {
                while (true)
                {
                    // Wait for data to write from the pipe producer
                    var result = await _send.Reader.ReadAsync();
                    var buffer = result.Buffer;

                    if (result.IsCanceled)
                    {
                        break;
                    }

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            await _sender.SendAsync(buffer);
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        _send.Reader.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                error = null;
            }
            catch (ObjectDisposedException)
            {
                error = null;
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
                // Make sure to close the connection only after the _aborted flag is set.
                // Without this, the RequestsCanBeAbortedMidRead test will sometimes fail when
                // a BadHttpRequestException is thrown instead of a TaskCanceledException.
                _aborted = true;
                Socket.Shutdown(SocketShutdown.Send);
            }

            return error;
        }
    }
}