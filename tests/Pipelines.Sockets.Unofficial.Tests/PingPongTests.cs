using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
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

        static readonly PipeOptions PipeOptions;
        static PingPongTests()
        {
            var pool = new DedicatedThreadPoolPipeScheduler("MyPool");
            var options = new PipeOptions(readerScheduler: pool, writerScheduler: pool, useSynchronizationContext: false);
            PipeOptions = options;
        }
        [Fact]
        public async Task Basic_PingPong()
        {
            var (client, server) = CreateConnectedSocketPair();
            Assert.NotNull(client);
            Assert.NotNull(server);
            
            var clientPipe = SocketConnection.Create(client, PipeOptions);
            var serverPipe = SocketConnection.Create(server, PipeOptions);

            await PingPong(clientPipe, serverPipe);
        }

        [Fact]
        public async Task ClientInverted_PingPong()
        {
            var (client, server) = CreateConnectedSocketPair();
            Assert.NotNull(client);
            Assert.NotNull(server);

            var clientPipe = SocketConnection.Create(client, PipeOptions);
            var serverPipe = SocketConnection.Create(server, PipeOptions);

            var clientStream = StreamConnector.GetDuplex(clientPipe);

            await PingPong(clientStream, serverPipe);
        }

        [Fact]
        public async Task ServerInverted_PingPong()
        {
            var (client, server) = CreateConnectedSocketPair();
            Assert.NotNull(client);
            Assert.NotNull(server);

            var clientPipe = SocketConnection.Create(client, PipeOptions);
            var serverPipe = SocketConnection.Create(server, PipeOptions);

            var serverStream = StreamConnector.GetDuplex(serverPipe);

            await PingPong(clientPipe, serverStream);
        }

        private async Task PingPong(IDuplexPipe client, IDuplexPipe server)
        {
            Log.DebugLog("Client sending...");
            await Write(client.Output, "PING");
            Log.DebugLog("Client sent; completing output...");
            client.Output.Complete();
            Log.DebugLog("Client completed output");

            Log.DebugLog("Server reading...");
            string s = await ReadToEnd(server.Input);
            Log.DebugLog($"Server received: '{s}'");
            Assert.Equal("PING", s);

            Log.DebugLog("Server sending...");
            await Write(server.Output, "PONG");
            Log.DebugLog("Server sent; completing output...");
            server.Output.Complete();
            Log.DebugLog("Server completed output");

            Log.DebugLog("Client reading...");
            s = await ReadToEnd(client.Input);
            Log.DebugLog($"Client received: '{s}'");
            Assert.Equal("PONG", s);
        }

        private async Task PingPong(IDuplexPipe client, StreamConnector.AsyncPipeStream server)
        {
            Log.DebugLog("Client sending...");
            await Write(client.Output, "PING");
            Log.DebugLog("Client sent; completing output...");
            client.Output.Complete();
            Log.DebugLog("Client completed output");

            Log.DebugLog("Server reading...");
            string s = await ReadToEnd(server);
            Log.DebugLog($"Server received: '{s}'");
            Assert.Equal("PING", s);

            Log.DebugLog("Server sending...");
            await Write(server, "PONG");
            Log.DebugLog("Server sent; completing output...");
            server.CloseWrite();
            Log.DebugLog("Server completed output");

            Log.DebugLog("Client reading...");
            s = await ReadToEnd(client.Input);
            Log.DebugLog($"Client received: '{s}'");
            Assert.Equal("PONG", s);
        }

        private async Task PingPong(StreamConnector.AsyncPipeStream client, IDuplexPipe server)
        {
            Log.DebugLog("Client sending...");
            await Write(client, "PING");
            Log.DebugLog("Client sent; completing output...");
            client.CloseWrite();
            Log.DebugLog("Client completed output");

            Log.DebugLog("Server reading...");
            string s = await ReadToEnd(server.Input);
            Log.DebugLog($"Server received: '{s}'");
            Assert.Equal("PING", s);

            Log.DebugLog("Server sending...");
            await Write(server.Output, "PONG");
            Log.DebugLog("Server sent; completing output...");
            server.Output.Complete();
            Log.DebugLog("Server completed output");

            Log.DebugLog("Client reading...");
            s = await ReadToEnd(client);
            Log.DebugLog($"Client received: '{s}'");
            Assert.Equal("PONG", s);
        }

        async static Task Write(PipeWriter writer, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await writer.WriteAsync(bytes);
        }

        async static Task Write(Stream stream, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        async static Task<string> ReadToEnd(Stream stream)
        {
            using (var sr = new StreamReader(stream))
            {
                return await sr.ReadToEndAsync();
            }
        }
        async static Task<string> ReadToEnd(PipeReader reader)
        {
            var sb = new StringBuilder();
            string result = null;
            while (result == null)
            {
                var inp = await reader.ReadAsync();
                var buffer = inp.Buffer;
                SequencePosition consumed = buffer.Start;
                if (inp.IsCompleted)
                {
                    result = ReadString(buffer);
                    consumed = buffer.End;
                }
                reader.AdvanceTo(inp.Buffer.Start);


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
