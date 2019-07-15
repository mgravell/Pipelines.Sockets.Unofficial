using System;
using System.Diagnostics;
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
            // var clientEndpoint = new IPEndPoint(IPAddress.Loopback, 10135);
#if DEBUG
            Action<string> log = null;
#endif
            using (var server = DatagramConnection<ReadOnlyMemory<char>>.CreateServer(serverEndpoint, Marshaller.CharMemoryUTF8, name: "server"
#if DEBUG
                , log: log
#endif
                ))
            using (var client = DatagramConnection<ReadOnlyMemory<char>>.CreateClient(serverEndpoint, Marshaller.CharMemoryUTF8, name: "client" // , localEndpoint: clientEndpoint
#if DEBUG
                , log: log
#endif
                ))
            {
                const int SEND = 10000;
                var serverShutdown = Task.Run(() => RunPingServer(server));
                var receiveShutdown = Task.Run(async () =>
                {
                    int count = 0;
                    while (await client.Input.WaitToReadAsync())
                    {
                        while (client.Input.TryRead(out var frame))
                        {
                            using (frame) { }

                            count++;
                            if ((count % 250) == 0)
                            {
                                Console.WriteLine($"{count}, {frame.LocalIndex}");
                            }
                            
                            if (count >= SEND - 1)
                            {
                                Console.WriteLine("Got all or most of them");
                                client.Dispose();
                                return;
                            }
                        }
                    }
                });

                const string message = "hello";


                var memory = message.AsMemory();
                for (int i = 0; i < SEND; i++)
                {
                    Log($"Client sending '{message}'");
                    await client.Output.WriteAsync(memory);
                    Log($"Client sent, awaiting reply");
                }
                client.Output.TryComplete();

                var reply = await client.Input.ReadAsync();
                {
                    Log($"Client received '{reply}'");
                }

                await receiveShutdown;
                await serverShutdown;
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
                throw;
            }
        }
    }
}
