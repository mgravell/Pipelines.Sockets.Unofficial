using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Tests
{
    static class Program
    {
        static Task Main()
        {
            var client = new TestServer();
            return client.Basics();
        }
    }
    public class TestServer
    {

        [Conditional("DEBUG")]
        private void Log(string message)
        {
            lock (this)
            {
                Console.WriteLine(message);
            }
        }

        public async Task Basics()
        {
            var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 10134);
#if DEBUG
            Action<string> log = null;
#endif
            using (var server = DatagramConnection<ReadOnlyMemory<char>>.CreateServer(serverEndpoint, Marshaller.CharMemoryUTF8, name: "server"
#if DEBUG
                , log: log
#endif
                ))
            {
                try
                {
                    var serverShutdown = Task.Run(() => RunPingServer(server));

                    await serverShutdown;
                }
                catch { }
            }
        }

        private async Task RunPingServer(IDuplexChannel<Frame<ReadOnlyMemory<char>>> channel)
        {
            try
            {
                Log("Server starting...");
                while (await channel.Input.WaitToReadAsync())
                {
                    Log("Server reading frames...");
                    while (channel.Input.TryRead(out var frame))
                    {
                        Log($"Server received '{frame.Payload}' from {frame.Peer}, flags: {frame.Flags}");
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
            }
        }
    }
}
