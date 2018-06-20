using Pipelines.Sockets.Unofficial.Tests;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BasicRunner
{
    class Program
    {
        static async Task Main()
        {
            Thread.CurrentThread.Name = nameof(Main);
            await new PingPongTests(Console.Out).ServerInverted_PingPong();
        }
    }
}

