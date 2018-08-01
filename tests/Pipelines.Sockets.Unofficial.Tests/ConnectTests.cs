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
        private ITestOutputHelper Output { get; }
        private TestTextWriter Log { get; }

        public ConnectTests(ITestOutputHelper output)
        {
            Output = output;
            Log = TestTextWriter.Create(output);
        }

        [Fact]
        public async Task Connect()
        {
            var timeout = Task.Delay(5000);
            var code = ConnectImpl();
            var first = await Task.WhenAny(timeout, code).ConfigureAwait(false);
            if (first == timeout) throw new TimeoutException();
        }

        private async Task ConnectImpl()
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
            Log?.DebugLog("connecting...");
            using (var conn = await SocketConnection.ConnectAsync(endpoint,
                connectionOptions: SocketConnectionOptions.ZeroLengthReads).ConfigureAwait(false))
            {
                var data = Encoding.ASCII.GetBytes("Hello, world!");
                Log?.DebugLog("sending message...");
                await conn.Output.WriteAsync(data).ConfigureAwait(false);
                Log?.DebugLog("completing output");
                conn.Output.Complete();

                Log?.DebugLog("awaiting server...");
                actual = await server;

                Assert.Equal("Hello, world!", actual);

                string returned;
                Log?.DebugLog("buffering response...");
                while (true)
                {
                    var result = await conn.Input.ReadAsync().ConfigureAwait(false);

                    var buffer = result.Buffer;
                    Log?.DebugLog($"received {buffer.Length} bytes");
                    if (result.IsCompleted)
                    {
                        returned = Encoding.ASCII.GetString(result.Buffer.ToArray());
                        Log?.DebugLog($"received: '{returned}'");
                        break;
                    }

                    Log?.DebugLog("advancing");
                    conn.Input.AdvanceTo(buffer.Start, buffer.End);
                }

                Assert.Equal("!dlrow ,olleH", returned);

                Log?.DebugLog("disposing");
            }
        }

        private Task<string> SyncEchoServer(object ready, IPEndPoint endpoint)
        {
            var listener = new TcpListener(endpoint);
            Log?.DebugLog($"[Server] starting on {endpoint}...");
            listener.Start();
            Output.WriteLine("[Server] running; waiting for connection...");
            lock (ready)
            {
                Monitor.Pulse(ready);
            }
            string s;
            using (var socket = listener.AcceptSocket())
            {
                Output.WriteLine($"[Server] accepted connection");
                using (var ns = new NetworkStream(socket))
                {
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
            }
            Output.WriteLine($"[Server] shutting down");
            listener.Stop();
            return Task.FromResult(s);
        }
    }
}
