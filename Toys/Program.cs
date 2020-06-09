using Pipelines.Sockets.Unofficial;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Toys
{
    static class Program
    {
        const int Port = 12542;

        static void Log(string message)
            => Console.WriteLine(message);
        static async Task Main()
        {
            using var server = new EchoServer(Port);

            await Task.Yield();
            SocketConnection.AssertDependencies();



            Log("Connecting...");
            using var connection = await SocketConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, Port));
            Log("Connected");

            Guid guid = Guid.NewGuid();
            Log($"Writing '{guid}'...");
            var output = connection.Output;
            var memory = output.GetMemory(30);
            if (!Utf8Formatter.TryFormat(guid, memory.Span, out var bytes))
                throw new FormatException();
            output.Advance(bytes);

            //Log($"Flushing...");
            //var flushResult = await output.FlushAsync();
            //Log($"IsCompleted:{flushResult.IsCompleted}, IsCanceled:{flushResult.IsCanceled}");

            //Log($"Reading...");
            //var input = connection.Input;
            //while (true)
            //{
            //    Log($"Reading...");
            //    var readResult = await input.ReadAsync();
            //    Log($"IsCompleted:{readResult.IsCompleted}, IsCanceled:{readResult.IsCanceled}, Length:{readResult.Buffer.Length}");
            //    if (readResult.IsCompleted || readResult.IsCanceled) break;

            //    if (readResult.Buffer.Length >= 36)
            //    {
            //        var buffer = readResult.Buffer;
            //        var len = checked((int)buffer.Length);
            //        var arr = ArrayPool<byte>.Shared.Rent(len);
            //        try
            //        {
            //            buffer.CopyTo(arr);
            //            var s = Encoding.UTF8.GetString(arr, 0, len);
            //            Log($"Received: '{s}'");
            //        }
            //        finally
            //        {
            //            ArrayPool<byte>.Shared.Return(arr);
            //        }
            //        input.AdvanceTo(readResult.Buffer.End);
            //        break;
            //    }
            //    else
            //    {
            //        input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            //    }
            //}


            //Log($"Closing output...");
            //output.Complete();
        }

        public class EchoServer : IDisposable
        {
            public int Port { get; }
            TcpListener _listener;
            public EchoServer(int port)
            {
                Port = port;
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                AcceptNextConnection();
            }
            private void AcceptNextConnection()
            {
                try
                {
                    _listener?.BeginAcceptTcpClient(AcceptConnection, null);
                }
                catch { }
            }

            private void AcceptConnection(IAsyncResult ar)
            {
                try
                {
                    var client = _listener.EndAcceptTcpClient(ar);
                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        if (state is TcpClient c)
                        {
                            _ = RunClientAsync(c);
                        }
                    }, client);
                    AcceptNextConnection();
                }
                catch { }
            }
            static async Task RunClientAsync(TcpClient client)
            {
                try
                {
                    using (client)
                    {
                        var stream = client.GetStream();
                        await stream.CopyToAsync(stream);
                    }
                }
                catch { }
            }

            void IDisposable.Dispose()
            {
                var listener = _listener;
                _listener = null;
                listener?.Stop();
            }
        }
    }
}
