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
            var server = DatagramConnection<string>.Create(endpoint, Marshaller.UTF8, name: "server", log: log);
            var client = DatagramConnection<string>.Create(endpoint, Marshaller.UTF8, name: "client", log: log);
            {
                var serverShutdown = Task.Run(() => RunPingServer(server));

                const string message = "hello";
                Log($"Client sending '{message}'");
                await client.Output.WriteAsync(message);
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

        private async Task RunPingServer(IDuplexChannel<string> channel)
        {
            try
            {
                Assert.NotNull(channel);
                Assert.NotNull(channel.Input);

                Log("Server starting...");
                while (await channel.Input.WaitToReadAsync())
                {
                    Log("Server reading frames...");
                    while (channel.Input.TryRead(out var message))
                    {
                        Log($"Server received {message} bytes");
                        await channel.Output.WriteAsync(message);
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
