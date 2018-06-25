using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System.Reflection;

namespace Benchmarks
{
    public static class Program
    {
        public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
    }

    internal class Config : ManualConfig
    {
        public Config()
        {
            Job Get(Job j) => j.With(RunStrategy.Monitoring)
                               .WithLaunchCount(1)
                               .WithWarmupCount(1)
                               .WithTargetCount(5)
                               .WithInvocationCount(5);

            Add(new MemoryDiagnoser());
            Add(Get(Job.Clr));
            Add(Get(Job.Core));
        }
    }
}
