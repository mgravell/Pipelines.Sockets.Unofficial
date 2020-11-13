using BenchmarkDotNet.Running;
using System;

namespace Benchmark
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {   // if no args, we're probably using Ctrl+F5 in the IDE; enlargen thyself!
#pragma warning disable CA1416 // windows only
                try { Console.WindowWidth = Console.LargestWindowWidth - 20; } catch { }
#pragma warning restore CA1416 // windows only
            }
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
