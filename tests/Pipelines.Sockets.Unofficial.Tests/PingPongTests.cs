using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class PingPongTests
    {
        private TestTextWriter Log { get; }

        public PingPongTests(TextWriter output)
        {
            Log = new TestTextWriter(output);
        }
        public PingPongTests(ITestOutputHelper output)
        {
            Log = new TestTextWriter(output);
        }
        protected (Socket Client, Socket Server) CreateConnectedSocketPair()
        {
            Log.DebugLog("Connecting...");
            using (Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);

                Log.DebugLog("Connected");
                Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                client.Connect(listener.LocalEndPoint);
                Socket server = listener.Accept();

                return (client, server);
            }
        }

        static readonly PipeOptions PipeOptions, HardFlushPipeOptions;
        static PingPongTests()
        {
            var pool = new DedicatedThreadPoolPipeScheduler("MyPool");
            PipeOptions = new PipeOptions(readerScheduler: pool, writerScheduler: pool, useSynchronizationContext: false);
            HardFlushPipeOptions = new PipeOptions(readerScheduler: pool, writerScheduler: pool, useSynchronizationContext: false, resumeWriterThreshold: 0);
        }

        const int LOOP = 20;
        [Fact]
        public async Task Basic_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();

            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, PipeOptions, name: "socket client");
                var serverPipe = SocketConnection.Create(server, PipeOptions, name: "socket server");

                await PingPong(clientPipe, serverPipe, LOOP);
            }
            Log.DebugLog("All good!");
        }

        [Fact]
        public async Task ClientInverted_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();
            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, PipeOptions, name: "socket client");
                var serverPipe = SocketConnection.Create(server, PipeOptions, name: "socket server");

                var clientStream = StreamConnector.GetDuplex(clientPipe, name: "stream client");

                await PingPong(clientStream, serverPipe, LOOP);
            }
            Log.DebugLog("All good!");
        }

        [Fact]
        public async Task ClientDoubleInverted_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();
            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, PipeOptions, name: "socket client");
                var serverPipe = SocketConnection.Create(server, PipeOptions, name: "socket server");

                var clientStream = StreamConnector.GetDuplex(clientPipe, name: "stream client");

                var clientRevert = StreamConnector.GetDuplex(clientStream, name: "revert client");

                await PingPong(clientRevert, serverPipe, LOOP);
            }
            Log.DebugLog("All good!");
        }

        [Fact]
        public async Task ServerInverted_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();
            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, PipeOptions, name: "socket client");
                var serverPipe = SocketConnection.Create(server, PipeOptions, name: "socket server");

                var serverStream = StreamConnector.GetDuplex(serverPipe, name: "stream server");

                await PingPong(clientPipe, serverStream, LOOP);
            }
            Log.DebugLog("All good!");
        }


        [Fact]
        public async Task ServerDoubleInverted_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();
            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, PipeOptions, name: "socket client");
                var serverPipe = SocketConnection.Create(server, PipeOptions, name: "socket server");

                var serverStream = StreamConnector.GetDuplex(serverPipe, name: "stream server");
                var serverRevert = StreamConnector.GetDuplex(serverStream, PipeOptions, name: "revert server");

                await PingPong(clientPipe, serverRevert, LOOP);
            }
            Log.DebugLog("All good!");
        }

        [Fact]
        public async Task ServerClientDoubleInverted_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();
            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, PipeOptions, name: "socket client");
                var serverPipe = SocketConnection.Create(server, PipeOptions, name: "socket server");

                var serverStream = StreamConnector.GetDuplex(serverPipe, name: "stream server");
                var serverRevert = StreamConnector.GetDuplex(serverStream, PipeOptions, name: "revert server");

                var clientStream = StreamConnector.GetDuplex(clientPipe, name: "stream client");
                var clientRevert = StreamConnector.GetDuplex(clientStream, PipeOptions, name: "revert client");

                await PingPong(clientRevert, serverRevert, LOOP);
            }
            Log.WriteLine("All good!");
        }

        [Fact]
        public async Task ServerClientDoubleInverted_SslStream_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();
            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, PipeOptions, name: "socket client");
                var serverPipe = SocketConnection.Create(server, PipeOptions, name: "socket server");

                var serverStream = StreamConnector.GetDuplex(serverPipe, name: "stream server");
                var serverSsl = new SslStream(serverStream);
                
                var clientStream = StreamConnector.GetDuplex(clientPipe, name: "stream client");
                var clientSsl = new SslStream(clientStream);

                X509Certificate cert = null;
                var serverAuth = serverSsl.AuthenticateAsServerAsync(cert);
                var clientAuth = clientSsl.AuthenticateAsClientAsync("foo");

                await serverAuth;
                await clientAuth;

                var serverRevert = StreamConnector.GetDuplex(serverSsl, PipeOptions, name: "revert server");
                var clientRevert = StreamConnector.GetDuplex(clientSsl, PipeOptions, name: "revert client");



                await PingPong(clientRevert, serverRevert, LOOP);
            }
            Log.WriteLine("All good!");
        }

        private async Task PingPong(IDuplexPipe client, IDuplexPipe server, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Log.DebugLogWriteLine();
                Log.DebugLog($"Test {i}...");

                Log.DebugLog("Client sending...");
                await WriteLine(client.Output, $"PING:{i}");

                Log.DebugLog("Server reading...");
                string s = await ReadLine(server.Input);
                Log.DebugLogVerbose($"Server received: '{s}'");
                Assert.Equal($"PING:{i}", s);       


                GC.KeepAlive(server.Output);
                Log.DebugLog("Server sending...");
                await WriteLine(server.Output, $"PONG:{i}");

                Log.DebugLog("Client reading...");
                s = await ReadLine(client.Input);
                Log.DebugLogVerbose($"Client received: '{s}'");
                Assert.Equal($"PONG:{i}", s);
            }
        }

        private async Task PingPong(IDuplexPipe client, StreamConnector.AsyncPipeStream server, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Log.DebugLogWriteLine();
                Log.DebugLog($"Test {i}...");

                Log.DebugLogVerbose("Client sending...");
                await WriteLine(client.Output, $"PING:{i}");

                Log.DebugLogVerbose("Server reading...");
                string s = await ReadLine(server);
                Log.DebugLogVerbose($"Server received: '{s}'");
                Assert.Equal($"PING:{i}", s);

                Log.DebugLogVerbose("Server sending...");
                await WriteLine(server, $"PONG:{i}");

                Log.DebugLogVerbose("Client reading...");
                s = await ReadLine(client.Input);
                Log.DebugLogVerbose($"Client received: '{s}'");
                Assert.Equal($"PONG:{i}", s);
            }
        }

        private async Task PingPong(StreamConnector.AsyncPipeStream client, IDuplexPipe server, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Log.DebugLogWriteLine();
                Log.DebugLog($"Test {i}...");

                Log.DebugLogVerbose("Client sending...");
                await WriteLine(client, $"PING:{i}");

                Log.DebugLogVerbose("Server reading...");
                string s = await ReadLine(server.Input);
                Log.DebugLogVerbose($"Server received: '{s}'");
                Assert.Equal($"PING:{i}", s);

                Log.DebugLogVerbose("Server sending...");
                await WriteLine(server.Output, $"PONG:{i}");

                Log.DebugLogVerbose("Client reading...");
                s = await ReadLine(client);
                Log.DebugLogVerbose($"Client received: '{s}'");
                Assert.Equal($"PONG:{i}", s);
            }
        }

        async Task WriteLine(PipeWriter writer, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            await writer.WriteAsync(bytes);
            await writer.FlushAsync();
        }

        static async Task WriteLine(Stream stream, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        async Task<string> ReadLine(Stream stream)
        {
            var ms = new MemoryStream();
            var buffer = new byte[1];

            int bytes;
            while (true)
            {
                bytes = await stream.ReadAsync(buffer, 0, 1);

                if (bytes <= 0 || buffer[0] ==(byte) '\n') break;
                ms.WriteByte(buffer[0]);
            }
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
        async static Task<string> ReadLine(PipeReader reader)
        {
            var sb = new StringBuilder();
            string result = null;
            while (result == null)
            {
                var inp = await reader.ReadAsync();
                var buffer = inp.Buffer;

                var split = buffer.PositionOf((byte)'\n');

                SequencePosition consumed = buffer.Start;
                if (split != null)
                {
                    var contents = buffer.Slice(0, split.Value);
                    result = ReadString(contents);
                    consumed = buffer.GetPosition(1, split.Value);
                }
                reader.AdvanceTo(consumed);
            }
            return result;
        }

        private static unsafe string ReadString(ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                var span = buffer.First.Span;
                if (span.Length == 0) return "";

                fixed (byte* ptr = &span[0])
                {
                    return Encoding.UTF8.GetString(ptr, span.Length);
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
