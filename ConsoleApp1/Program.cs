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
//            using (var server = DatagramConnection<ReadOnlyMemory<char>>.CreateServer(serverEndpoint, Marshaller.CharMemoryUTF8, name: "server"
//#if DEBUG
//                , log: log
//#endif
//                ))
            using (var client = DatagramConnection<int>.CreateClient(serverEndpoint, Marshaller.Int32Utf8, name: "client" // , localEndpoint: clientEndpoint
#if DEBUG
                , log: log
#endif
                ))
            {
                try
                {
                    const int ITEMCOUNT = 100000;
                    // var serverShutdown = Task.Run(() => RunPingServer(server));
                    var receiveShutdown = Task.Run(async () =>
                    {
                        try
                        {
                            int count = 0;
                            var start = DateTime.UtcNow;
                            while (await client.Input.WaitToReadAsync())
                            {
                                while (client.Input.TryRead(out var frame))
                                {
                                    using (frame) { }

                                    count++;
                                    if ((count % 5000) == 0)
                                    {
                                        var now = DateTime.UtcNow;
                                        var totalBytes = client.TotalBytesReceived;
                                        double megabytes = ((double)totalBytes) / (1024 * 1024);
                                        var time = now - start;
                                        Console.WriteLine($"{count} [vs {frame.Payload + 1}]: {megabytes} MB in {time.TotalMilliseconds}ms; {megabytes / time.TotalSeconds} MB/s");
                                    }

                                    if (count >= ((double)ITEMCOUNT * 0.9))
                                    {
                                        Console.WriteLine("Got enough of them");
                                        client.Dispose();
                                        // server.Dispose();
                                        return;
                                    }
                                }
                            }
                        }
                        catch { }
                    });

                    // request data
                    await client.Output.WriteAsync(ITEMCOUNT);
                    client.Output.TryComplete();

                    await receiveShutdown;
                    // await serverShutdown;
                }
                catch { }
            }
        }

        //private async Task RunPingServer(IDuplexChannel<Frame<ReadOnlyMemory<char>>> channel)
        //{
        //    try
        //    {
        //        Log("Server starting...");
        //        while (await channel.Input.WaitToReadAsync())
        //        {
        //            Log("Server reading frames...");
        //            while (channel.Input.TryRead(out var frame))
        //            {
        //                Log($"Server received '{frame.Payload}' from {frame.Peer}, flags: {frame.Flags}");
        //                await channel.Output.WriteAsync(frame);
        //                Log($"Server sent reply");
        //            }
        //        }
        //        Log("Server exiting");
        //    }
        //    catch (Exception ex)
        //    {
        //        Log($"Server stack: {ex.StackTrace}");
        //        Log($"Server faulted: {ex.Message}");
        //    }
        //}
    }
}
