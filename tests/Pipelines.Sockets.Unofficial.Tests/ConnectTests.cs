using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.IO;
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
        [Fact]
        public void CanCheckDependencies()
        {
            SocketConnection.AssertDependencies();
        }

        private ITestOutputHelper Output { get; }

        public ConnectTests(ITestOutputHelper output)
        {
            Output = output;
        }

        [Fact]
        public async Task Connect()
        {
            var timeout = Task.Delay(6000);
            var code = ConnectImpl();
            var first = await Task.WhenAny(timeout, code);
            if (first == timeout) Throw.Timeout("unknown timeout");
            await first; // check outcome
        }

        private async Task ConnectImpl()
        {
            int port = 16320 + new Random().Next(100);
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);
            object waitForRunning = new object();
            Task<string> server;
            Output.WriteLine("Starting server...");
            lock (waitForRunning)
            {
                server = Task.Run(() => SyncEchoServer(waitForRunning, endpoint));
                if (!Monitor.Wait(waitForRunning, 5000))
                    Throw.Timeout("Server didn't start");
            }

            if (server.IsFaulted)
            {
                await server; // early exit if broken
            }

            string actual;
            Output.WriteLine("connecting...");
            using var conn = await SocketConnection.ConnectAsync(endpoint,
                connectionOptions: SocketConnectionOptions.ZeroLengthReads).ConfigureAwait(false);
            var data = Encoding.ASCII.GetBytes("Hello, world!");
            Output.WriteLine("sending message...");
            await conn.Output.WriteAsync(data).ConfigureAwait(false);
          
            Assert.True(conn.Output.CanGetUnflushedBytes, "conn.Output.CanGetUnflushedBytes");

            Output.WriteLine("completing output");
            conn.Output.Complete();

            Output.WriteLine("awaiting server...");
            actual = await server;

            Assert.Equal("Hello, world!", actual);

            string returned;
            Output.WriteLine("buffering response...");
            while (true)
            {
                var result = await conn.Input.ReadAsync().ConfigureAwait(false);

                var buffer = result.Buffer;
                Output.WriteLine($"received {buffer.Length} bytes");
                if (result.IsCompleted)
                {
                    returned = Encoding.ASCII.GetString(result.Buffer.ToArray());
                    Output.WriteLine($"received: '{returned}'");
                    break;
                }

                Output.WriteLine("advancing");
                conn.Input.AdvanceTo(buffer.Start, buffer.End);
            }

            Assert.Equal("!dlrow ,olleH", returned);

            Output.WriteLine("disposing");
        }

        private Task<string> SyncEchoServer(object ready, IPEndPoint endpoint)
        {
            try
            {
                var listener = new TcpListener(endpoint);
                Output.WriteLine($"[Server] starting on {endpoint}...");
                listener.Start();
                lock (ready)
                {
                    Monitor.PulseAll(ready);
                }
                Output.WriteLine("[Server] running; waiting for connection...");
                string s;
                using (var socket = listener.AcceptSocket())
                {
                    Output.WriteLine($"[Server] accepted connection");
                    using var ns = new NetworkStream(socket);
                    using (var reader = new StreamReader(ns, Encoding.ASCII, false, 1024, true))
                    using (var writer = new StreamWriter(ns, Encoding.ASCII, 1024, true))
                    {
                        s = reader.ReadToEnd();
                        Output.WriteLine($"[Server] received '{s}'; replying in reverse...");
                        char[] chars = s.ToCharArray();
                        Array.Reverse(chars);
                        var t = new string(chars);
                        writer.Write(t);
                    }
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                Output.WriteLine($"[Server] shutting down");
                listener.Stop();
                return Task.FromResult(s);
            }
            catch (Exception ex)
            {
                Output.WriteLine($"[Server] faulted: {ex.Message}");
                lock (ready)
                {
                    Monitor.PulseAll(ready);
                }
                return Task.FromException<string>(ex);
            }
        }
    }
}
