using BenchmarkDotNet.Running;
using System;
using System.Runtime.InteropServices;

namespace Benchmark
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (args == null || args.Length == 0))
            {   // if no args, we're probably using Ctrl+F5 in the IDE; enlargen thyself!
                try { Console.WindowWidth = Console.LargestWindowWidth - 20; } catch { }
            }
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
