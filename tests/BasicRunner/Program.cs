using Pipelines.Sockets.Unofficial.Tests;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BasicRunner
{
    static class Program
    {
        static async Task Main()
        {
            Thread.CurrentThread.Name = nameof(Main);
            Console.WriteLine($"Loop count: {PingPongTests.LoopCount}");
            var parent = new PingPongTests(Console.Out);
            Stopwatch watch;

            for (int i = 0; i < 20; i++)
            {
                watch = Stopwatch.StartNew();
                await parent.Basic_Pipelines_PingPong();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\tSocket=>Pipelines=>PingPong");

                watch = Stopwatch.StartNew();
                await parent.Basic_NetworkStream_PingPong();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\tSocket=>NetworkStream=>PingPong");

                watch = Stopwatch.StartNew();
                await parent.Basic_NetworkStream_Pipelines_PingPong();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\tSocket=>NetworkStream=>Pipelines=>PingPong");

                
                //watch = Stopwatch.StartNew();
                //await parent.ServerClientDoubleInverted_SslStream_PingPong();
                //watch.Stop();
                //Console.WriteLine($"{watch.ElapsedMilliseconds}ms\tSocket=>Pipelines=>Inverter=>SslStream=>Inverter=>PingPong");

                //watch = Stopwatch.StartNew();
                //await parent.ServerClient_SslStream_PingPong();
                //watch.Stop();
                //Console.WriteLine($"{watch.ElapsedMilliseconds}ms\tSocket=>NetworkStream=>SslStream=>PingPong");
            }
        }
    }
}

