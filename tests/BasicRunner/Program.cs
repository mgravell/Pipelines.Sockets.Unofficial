using Pipelines.Sockets.Unofficial;
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

        static async Task RunTest(Func<Task> method, string label)
        {
            Console.WriteLine();
            Console.WriteLine();
            for (int i = 0; i < 10; i++)
            {
#if DEBUG
                if (i == 1) DebugCounters.Reset();
#endif
                var watch = Stopwatch.StartNew();
                await method();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\t{Rate(watch)}\t{label}");

#if DEBUG
                if (i == 1)
                {
                    var summary = DebugCounters.GetSummary();
                    Console.WriteLine(summary);
                }
#endif
            }
        }
        static async Task Main()
        {            
            Thread.CurrentThread.Name = nameof(Main);
            Console.WriteLine($"Loop count: {PingPongTests.LoopCount}");
            Console.WriteLine($"Scheduler: {PingPongTests.Scheduler}");
            var parent = new PingPongTests(Console.Out);
            
            //await RunTest(parent.Basic_Pipelines_PingPong, "Socket=>Pipelines=>PingPong");
            //await RunTest(parent.Basic_NetworkStream_PingPong, "Socket=>NetworkStream=>PingPong");
            //await RunTest(parent.Basic_NetworkStream_Pipelines_PingPong, "Socket=>NetworkStream=>Pipelines=>PingPong");

            if (PingPongTests.RunTLS)
            {
                await RunTest(parent.ServerClientDoubleInverted_SslStream_PingPong, "Socket=>Pipelines=>Inverter=>SslStream=>Inverter=>PingPong");
            //    await RunTest(parent.ServerClient_SslStream_PingPong, "Socket=>NetworkStream=>SslStream=>PingPong");
             //   await RunTest(parent.ServerClient_SslStream_Inverter_PingPong, "Socket=>NetworkStream=>SslStream=>Inverter=>PingPong");
            }

        }
    }
}

