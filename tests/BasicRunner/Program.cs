using Pipelines.Sockets.Unofficial.Tests;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BasicRunner
{
    static class Program
    {
        static string Rate(Stopwatch watch)
        {
            var seconds = ((decimal)watch.ElapsedMilliseconds) / 1000;
            return seconds <= 0 ? "inf" : $"{(PingPongTests.LoopCount / seconds):0}/s";

        }
        static async Task Main()
        {
            
            Thread.CurrentThread.Name = nameof(Main);
            Console.WriteLine($"Loop count: {PingPongTests.LoopCount}");
            Console.WriteLine($"Scheduler: {PingPongTests.Scheduler}");
            var parent = new PingPongTests(Console.Out);
            Stopwatch watch;

            bool runTLS = PingPongTests.RunTLS;
            for (int i = 0; i < 20; i++)
            {
                watch = Stopwatch.StartNew();
                await parent.Basic_Pipelines_PingPong();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\t{Rate(watch)}\tSocket=>Pipelines=>PingPong");

                watch = Stopwatch.StartNew();
                await parent.Basic_NetworkStream_PingPong();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\t{Rate(watch)}\tSocket=>NetworkStream=>PingPong");

                watch = Stopwatch.StartNew();
                await parent.Basic_NetworkStream_Pipelines_PingPong();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\t{Rate(watch)}\tSocket=>NetworkStream=>Pipelines=>PingPong");

                if (runTLS)
                {
                    watch = Stopwatch.StartNew();
                    await parent.ServerClientDoubleInverted_SslStream_PingPong();
                    watch.Stop();
                    Console.WriteLine($"{watch.ElapsedMilliseconds}ms\t{Rate(watch)}\tSocket=>Pipelines=>Inverter=>SslStream=>Inverter=>PingPong");

                    watch = Stopwatch.StartNew();
                    await parent.ServerClient_SslStream_PingPong();
                    watch.Stop();
                    Console.WriteLine($"{watch.ElapsedMilliseconds}ms\t{Rate(watch)}\tSocket=>NetworkStream=>SslStream=>PingPong");
                }
            }
        }
    }
}

