using BenchmarkDotNet.Running;
using System;

namespace Benchmark
{
    internal static class Program
    {
        static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

        public static int AssertIs(this int actual, int expected)
        {
            if (actual != expected) throw new InvalidOperationException($"expected {expected} but was {actual}");
            return actual;
        }

        public static long AssertIs(this long actual, long expected)
        {
            if (actual != expected) throw new InvalidOperationException($"expected {expected} but was {actual}");
            return actual;
        }
    }
}
