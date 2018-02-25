using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class ConnectTests
    {
        ITestOutputHelper Output { get; }
        public ConnectTests(ITestOutputHelper output)
        {
            Output = output;
        }
        [Fact]
        public async Task Connect()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 9080);
            object waitForRunning = new object();
            Task<string> server;
            lock (waitForRunning)
            {
                server = Task.Run(() => SyncEchoServer(waitForRunning, endpoint));
                if (!Monitor.Wait(waitForRunning, 5000))
                    throw new TimeoutException("Server didn't start");
            }

            string actual;
            using (var conn = await SocketConnection.ConnectAsync(endpoint, DefaultOptions))
            {
                var data = Encoding.ASCII.GetBytes("Hello, world!");
                await conn.Output.WriteAsync(data);
                conn.Output.Complete();

                actual = await server;
            }
            Assert.Equal("Hello, world!", actual);
        }

        static PipeOptions DefaultOptions { get; } = new PipeOptions(MemoryPool<byte>.Shared);

        Task<string> SyncEchoServer(object ready, IPEndPoint endpoint)
        {
            var listener = new TcpListener(endpoint);
            Output.WriteLine($"Server starting on {endpoint}...");
            listener.Start();
            Output.WriteLine("Server running; waiting for connection...");
            lock (ready)
            {
                Monitor.Pulse(ready);
            }
            string s;
            using (var socket = listener.AcceptSocket())
            {
                Output.WriteLine($"Server accepted connection");
                using (var ns = new NetworkStream(socket))
                {
                    using (var reader = new StreamReader(ns, Encoding.ASCII, false, 1024, true))
                    using (var writer = new StreamWriter(ns, Encoding.ASCII, 1024, true))
                    {
                        s = reader.ReadToEnd();
                        Output.WriteLine($"Server received '{s}'; replying in kind...");
                        writer.Write(s);
                    }
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                
            }
            Output.WriteLine($"Server shutting down");
            listener.Stop();
            return Task.FromResult(s);
        }
    }
}
