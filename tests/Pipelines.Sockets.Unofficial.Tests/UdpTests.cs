using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class UdpTests
    {
        public UdpTests(ITestOutputHelper log) => _log = log;

        private readonly ITestOutputHelper _log;

        private void Log(string message)
        {
            if (_log != null)
            {
                lock (_log) { _log.WriteLine(message); }
            }
        }

        [Fact]
        public async Task Basics()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 10134);
            Action<string> log = Log;
            var server = DatagramConnection.Create(endpoint, Marshaller.UTF8, name: "server", log: log);
            var client = DatagramConnection.Create(endpoint, Marshaller.UTF8, name: "client", log: log);
            {
                var serverShutdown = Task.Run(() => RunPingServer(server));

                // TODO: create a frame-builder that provides IBufferWriter<byte>
                // API for constructing frames using the pool
                var bytes = Encoding.ASCII.GetBytes("hello");
                var frame = new Frame(bytes);

                Log($"Client sending {frame.Payload.Length} bytes");
                await client.Output.WriteAsync(frame);
                Log($"Client sent, awaiting reply");

                using (var reply = await client.Input.ReadAsync())
                {
                    Log($"Client received {reply.Payload.Length} bytes");
                    // lazy, just for test
                    var s = Encoding.ASCII.GetString(reply.Payload.ToArray());
                    Assert.Equal("hello", s);
                }

                await serverShutdown;
            }
        }

        private async Task RunPingServer(IDuplexChannel<Frame> channel)
        {
            try
            {
                Assert.NotNull(channel);
                Assert.NotNull(channel.Input);

                Log("Server starting...");
                while (await channel.Input.WaitToReadAsync())
                {
                    Log("Server reading frames...");
                    while (channel.Input.TryRead(out var frame))
                    {
                        Log($"Server received {frame.Payload.Length} bytes");
                        await channel.Output.WriteAsync(frame);
                        Log($"Server sent reply");
                    }
                }
                Log("Server exiting");
            }
            catch (Exception ex)
            {
                Log($"Server stack: {ex.StackTrace}");
                Log($"Server faulted: {ex.Message}");
                throw;
            }
        }
    }
}
