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

        //[Fact]
        //public async Task Basics()
        //{
        //    const int Port = 10134;
        //    Action<string> log = Log;
        //    using (var server = DatagramConnection<string>.Create(new IPEndPoint(IPAddress.Any, Port), Marshaller.UTF8, name: "server", log: log))
        //    using (var client = DatagramConnection<string>.Create(new IPEndPoint(IPAddress.Loopback, Port), Marshaller.UTF8, name: "client", log: log))
        //    {
        //        var serverShutdown = Task.Run(() => RunPingServer(server));

        //        const string message = "hello";
        //        Log($"Client sending '{message}'");
        //        await client.Output.WriteAsync(message);
        //        Log($"Client sent, awaiting reply");

        //        var reply = await client.Input.ReadAsync();
        //        {
        //            Log($"Client received '{reply}'");
        //            Assert.Equal("hello", reply);
        //        }

        //        await serverShutdown;
        //    }
        //}

        private async Task RunPingServer(IDuplexChannel<Frame<string>> channel)
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
                        using (frame)
                        {
                            string message = frame;
                            Log($"Server received {message} bytes");
                            await channel.Output.WriteAsync(message);
                            Log($"Server sent reply");
                        }
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
