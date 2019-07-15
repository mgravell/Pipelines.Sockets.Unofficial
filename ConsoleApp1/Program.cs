using System;
using System.Net;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Tests
{
    static class Program
    {
        static Task Main()
        {
            var client = new TestClient();
            return client.Basics();
        }
    }
    public class TestClient
    {
        
        private void Log(string message)
        {
            lock (this)
            {
                Console.WriteLine(message);
            }
        }

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

                var reply = await client.Input.ReadAsync();
                {
                    Log($"Client received '{reply}'");
                }

                await serverShutdown;
            }
        }

        private async Task RunPingServer(IDuplexChannel<Frame<string>> channel)
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
                throw;
            }
        }
    }
}
