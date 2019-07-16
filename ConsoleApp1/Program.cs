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
            using (var client = DatagramConnection.CreateClient(serverEndpoint,
                Marshaller.Int32Utf8,
                Marshaller.Memory,
                name: "client" // , localEndpoint: clientEndpoint
#if DEBUG
                , log: log
#endif
                ))
            {
                try
                {
                    const int ITEMCOUNT = 100000;
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
                                        Console.WriteLine($"{count} [vs {frame.LocalIndex + 1}]: {megabytes} MB in {time.TotalMilliseconds}ms; {megabytes / time.TotalSeconds} MB/s");
                                    }

                                    if (count >= ((double)ITEMCOUNT * 0.95))
                                    {
                                        Console.WriteLine("Got enough of them");
                                        client.Dispose();
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
                }
                catch { }
            }
        }
    }
}
