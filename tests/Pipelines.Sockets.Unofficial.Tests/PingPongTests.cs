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
        static readonly PipeOptions PipeOptions, InlineSend, InlineReceive, InlineBoth;


        static PingPongTests()
        {
            // PipeOptions = PipeOptions.Default;

            var pool = new DedicatedThreadPoolPipeScheduler("Custom thread pool");
            //var pool = PipeScheduler.Inline;
            //var pool = PipeScheduler.ThreadPool;
            PipeOptions = new PipeOptions(readerScheduler: pool, writerScheduler: pool, useSynchronizationContext: false);

            InlineSend = new PipeOptions(PipeOptions.Pool, PipeOptions.ReaderScheduler, PipeScheduler.Inline, PipeOptions.PauseWriterThreshold, PipeOptions.ResumeWriterThreshold, PipeOptions.MinimumSegmentSize, PipeOptions.UseSynchronizationContext);
            InlineReceive = new PipeOptions(PipeOptions.Pool, PipeScheduler.Inline, PipeOptions.WriterScheduler, PipeOptions.PauseWriterThreshold, PipeOptions.ResumeWriterThreshold, PipeOptions.MinimumSegmentSize, PipeOptions.UseSynchronizationContext);
            InlineBoth = new PipeOptions(PipeOptions.Pool, PipeScheduler.Inline, PipeScheduler.Inline, PipeOptions.PauseWriterThreshold, PipeOptions.ResumeWriterThreshold, PipeOptions.MinimumSegmentSize, PipeOptions.UseSynchronizationContext);
        }

        public const int LoopCount = 5000;
        public static string Scheduler =>
            ReferenceEquals(PipeOptions.ReaderScheduler, PipeOptions.WriterScheduler)
            ? PipeOptions.ReaderScheduler.ToString()
            : $"{PipeOptions.ReaderScheduler} / {PipeOptions.WriterScheduler}";
        private TestTextWriter Log { get; }
        public static bool RunTLS => PipeOptions.ReaderScheduler != PipeScheduler.ThreadPool;

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
                SocketConnection.SetRecommendedClientOptions(client);
                client.Connect(listener.LocalEndPoint);
                Socket server = listener.Accept();
                SocketConnection.SetRecommendedServerOptions(server);
                return (client, server);
            }
        }



        [Fact]
        public async Task Basic_Pipelines_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();

            using (client)
            using (server)
            {
                var clientPipe = SocketConnection.Create(client, InlineReceive, InlineSend, name: "socket client");
                var serverPipe = SocketConnection.Create(server, InlineReceive, InlineSend, name: "socket server");

                await PingPong(clientPipe, serverPipe, LoopCount);
            }
            Log.DebugLog("All good!");
        }

        [Fact]
        public async Task Basic_NetworkStream_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();

            using (client)
            using (server)
            {
                var clientStream = new NetworkStream(client);
                var serverStream = new NetworkStream(server);
                await PingPong(clientStream, serverStream, LoopCount);
            }
            Log.DebugLog("All good!");
        }

        [Fact]
        public async Task Basic_NetworkStream_Pipelines_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();

            using (client)
            using (server)
            {
                var clientStream = new NetworkStream(client);
                var serverStream = new NetworkStream(server);

                var clientPipe = StreamConnector.GetDuplex(clientStream, name: "client");
                var serverPipe = StreamConnector.GetDuplex(serverStream, name: "server");
                await PingPong(clientPipe, serverPipe, LoopCount);
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

                await PingPong(clientStream, serverPipe, LoopCount);
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

                await PingPong(clientRevert, serverPipe, LoopCount);
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

                await PingPong(clientPipe, serverStream, LoopCount);
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

                await PingPong(clientPipe, serverRevert, LoopCount);
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

                await PingPong(clientRevert, serverRevert, LoopCount);
            }
            Log.DebugLog("All good!");
        }
        static readonly RemoteCertificateValidationCallback IgnoreAllCertificateErrors = delegate { return true; };

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
                var serverSsl = new SslStream(serverStream, false, IgnoreAllCertificateErrors, null, EncryptionPolicy.RequireEncryption);

                var clientStream = StreamConnector.GetDuplex(clientPipe, name: "stream client");
                var clientSsl = new SslStream(clientStream, false, IgnoreAllCertificateErrors, null, EncryptionPolicy.RequireEncryption);
                
                var serverAuth = serverSsl.AuthenticateAsServerAsync(SomeCertificate);
                var clientAuth = clientSsl.AuthenticateAsClientAsync("somesite");

                await serverAuth;
                await clientAuth;
                
                var serverRevert = StreamConnector.GetDuplex(serverSsl, PipeOptions, name: "revert server");
                var clientRevert = StreamConnector.GetDuplex(clientSsl, PipeOptions, name: "revert client");



                await PingPong(clientRevert, serverRevert, LoopCount);
            }
            Log.DebugLog("All good!");
        }
        static readonly X509Certificate SomeCertificate = X509Certificate.CreateFromCertFile("somesite.pfx");
        [Fact]
        public async Task ServerClient_SslStream_PingPong()
        {
            Log.DebugLog();
            var (client, server) = CreateConnectedSocketPair();
            using (client)
            using (server)
            {
                var serverSsl = new SslStream(new NetworkStream(server), false, IgnoreAllCertificateErrors, null, EncryptionPolicy.RequireEncryption);
                var clientSsl = new SslStream(new NetworkStream(client), false, IgnoreAllCertificateErrors, null, EncryptionPolicy.RequireEncryption);

                
                var serverAuth = serverSsl.AuthenticateAsServerAsync(SomeCertificate);
                var clientAuth = clientSsl.AuthenticateAsClientAsync("somesite");

                await serverAuth;
                await clientAuth;

                await PingPong(clientSsl, serverSsl, LoopCount);
            }
            Log.DebugLog("All good!");
        }

        private async Task PingPong(IDuplexPipe client, IDuplexPipe server, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Log.DebugLogVerboseWriteLine();
                Log.DebugLogVerbose($"Test {i}...");

                Log.DebugLogVerbose("Client sending...");
                var expected = await WritePing(client.Output, i);

                Log.DebugLogVerbose("Server reading...");
                string s = await ReadLine(server.Input);
                Log.DebugLogVerbose($"Server received: '{s}'");
                Assert.Equal(expected, s);


                GC.KeepAlive(server.Output);
                Log.DebugLogVerbose("Server sending...");
                expected = await WritePong(server.Output, i);

                Log.DebugLogVerbose("Client reading...");
                s = await ReadLine(client.Input);
                Log.DebugLogVerbose($"Client received: '{s}'");
                Assert.Equal(expected, s);
            }

        }

        private async Task PingPong(IDuplexPipe client, Stream server, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Log.DebugLogVerboseWriteLine();
                Log.DebugLogVerbose($"Test {i}...");

                Log.DebugLogVerbose("Client sending...");
                var expected = await WritePing(client.Output, i);

                Log.DebugLogVerbose("Server reading...");
                string s = await ReadLine(server);
                Log.DebugLogVerbose($"Server received: '{s}'");
                Assert.Equal(expected, s);

                Log.DebugLogVerbose("Server sending...");
                expected = await WritePong(server, i);

                Log.DebugLogVerbose("Client reading...");
                s = await ReadLine(client.Input);
                Log.DebugLogVerbose($"Client received: '{s}'");
                Assert.Equal(expected, s);
            }
        }

        private async Task PingPong(Stream client, IDuplexPipe server, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Log.DebugLogVerboseWriteLine();
                Log.DebugLogVerbose($"Test {i}...");

                Log.DebugLogVerbose("Client sending...");
                var expected = await WritePing(client, i);

                Log.DebugLogVerbose("Server reading...");
                string s = await ReadLine(server.Input);
                Log.DebugLogVerbose($"Server received: '{s}'");
                Assert.Equal(expected, s);

                Log.DebugLogVerbose("Server sending...");
                expected = await WritePong(server.Output, i);

                Log.DebugLogVerbose("Client reading...");
                s = await ReadLine(client);
                Log.DebugLogVerbose($"Client received: '{s}'");
                Assert.Equal(expected, s);
            }
        }

        private async Task PingPong(Stream client, Stream server, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Log.DebugLogVerboseWriteLine();
                Log.DebugLogVerbose($"Test {i}...");

                Log.DebugLogVerbose("Client sending...");
                var expected = await WritePing(client, i);

                Log.DebugLogVerbose("Server reading...");
                string s = await ReadLine(server);
                Log.DebugLogVerbose($"Server received: '{s}'");
                Assert.Equal(expected, s);

                Log.DebugLogVerbose("Server sending...");
                expected = await WritePong(server, i);

                Log.DebugLogVerbose("Client reading...");
                s = await ReadLine(client);
                Log.DebugLogVerbose($"Client received: '{s}'");
                Assert.Equal(expected, s);
            }
        }
        static byte[] GetPayload(string message)
        {
            var utf8 = Encoding.UTF8;
            var bytes = utf8.GetBytes(message);
            var len = utf8.GetByteCount(message);
            var arr = new byte[len + 1];
            utf8.GetBytes(message, 0, message.Length, arr, 0);
            arr[arr.Length - 1] = (byte)'\n';
            return arr;
        }
        async ValueTask<string> WritePing(PipeWriter writer, int i)
        {
            var s = PINGPONGPREFIX + "PING:" + i;
            var bytes = GetPayload(s);
            await writer.WriteAsync(bytes);
            await writer.FlushAsync();
            return s;
        }
        const string PINGPONGPREFIX = "afkjakjasdaskhdkjhdakjhdaksdhaksjdhaksdhkaj hdkjahd akjdh akshf sjgf sjfgsjdhfg sjhgfs jfgsdfjhdsf gjsdfgjs dgfjh sdfgsjhfgsdjfgsjdfgsj hfgdsjfgsdj hfgdsdjfg sdjfg sd";
        async ValueTask<string> WritePong(PipeWriter writer, int i)
        {
            var s = PINGPONGPREFIX + "PONG:" + i;
            var bytes = GetPayload(s);
            await writer.WriteAsync(bytes);
            await writer.FlushAsync();
            return s;
        }
        async Task WriteLine(PipeWriter writer, string message)
        {
            var bytes = GetPayload(message);
            await writer.WriteAsync(bytes);
            await writer.FlushAsync();
        }
        async ValueTask<string> WritePing(Stream stream, int i)
        {
            var s = "PING:" + i;
            var bytes = GetPayload(s);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
            return s;
        }
        async ValueTask<string> WritePong(Stream stream, int i)
        {
            var s = "PONG:" + i;
            var bytes = GetPayload(s);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
            return s;
        }

        static async Task WriteLine(Stream stream, string message)
        {
            var bytes = GetPayload(message);
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

                if (bytes <= 0 || buffer[0] == (byte)'\n') break;
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

        internal static unsafe string ReadString(ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                
                var span = buffer.First.Span;
                if (span.IsEmpty) return "";
                fixed (byte* ptr = &span[0])
                {
                    return Encoding.UTF8.GetString(ptr, span.Length);
                }
            }
            var decoder = Encoding.UTF8.GetDecoder();
            int charCount = 0;
            foreach (var segment in buffer)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;

                fixed (byte* bPtr = &span[0])
                {
                    charCount += decoder.GetCharCount(bPtr, span.Length, false);
                }
            }

            decoder.Reset();

            string s = new string((char)0, charCount);
            fixed (char* sPtr = s)
            {
                char* cPtr = sPtr;
                foreach (var segment in buffer)
                {
                    var span = segment.Span;
                    if (span.IsEmpty) continue;

                    fixed (byte* bPtr = &span[0])
                    {
                        var written = decoder.GetChars(bPtr, span.Length, cPtr, charCount, false);
                        cPtr += written;
                        charCount -= written;
                    }
                }
            }
            return s;
        }

    }
}
