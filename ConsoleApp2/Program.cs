using PooledAwait;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
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
            Action<string> log = Log;
#endif
            using (var server = DatagramConnection.CreateServer(serverEndpoint,
                Marshaller.StringUtf8,
                Marshaller.Int32Utf8, name: "server"
#if DEBUG
                , log: log
#endif
                ))
            {
                try
                {
                    var serverShutdown = Task.Run(() => RunPingServer(server));
                    Console.WriteLine("Server running...");
                    await serverShutdown;
                }
                catch { }
            }
        }

        private async Task RunPingServer(IFrameChannel<string, int> channel)
        {
            try
            {
                Log("Server starting...");
                while (await channel.Input.WaitToReadAsync())
                {
                    Log("Server reading frames...");
                    while (channel.Input.TryRead(out var frame))
                    {
                        await Amplify(channel.Output, frame.Payload, frame.Peer, frame.Flags);
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

        private async FireAndForget Amplify(ChannelWriter<Frame<string>> output, int count, EndPoint peer, SocketFlags flags)
        {
            Log($"Server received '{count}' from {peer}, flags: {flags}");
            await Task.Yield();
            for(int i = 0; i < count; i++)
            {
                const string PAYLOAD = "lobortis mattis aliquam faucibus purus in massa tempor nec feugiat nisl pretium fusce id velit ut tortor pretium viverra suspendisse potenti nullam ac tortor vitae purus faucibus ornare";
                await output.WriteAsync(new Frame<string>(PAYLOAD, peer: peer, flags: flags));
            }
            Log($"Server sent {count} replies to {peer}");
        }
    }
}
