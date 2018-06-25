using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Tests;
using System.IO;
using System.Threading.Tasks;

namespace Benchmarks
{
    [Config(typeof(Config))]
    public class BasicTests
    {
        public static PingPongTests PPTests { get; } = new PingPongTests(TextWriter.Null);

        [Benchmark(Baseline = true, Description = "Socket=>Pipelines=>PingPong", OperationsPerInvoke = PingPongTests.LoopCount)]
        public Task BasicPipelines() => PPTests.Basic_Pipelines_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>PingPong", OperationsPerInvoke = PingPongTests.LoopCount)]
        public Task BasicNetworkStream() => PPTests.Basic_NetworkStream_PingPong();

        [Benchmark(Description = "Socket=>Pipelines=>TRW=>PingPong", OperationsPerInvoke = PingPongTests.LoopCount)]
        public Task BasicPipelinesText() => PPTests.Basic_Pipelines_Text_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>TRW=>PingPong", OperationsPerInvoke = PingPongTests.LoopCount)]
        public Task BasicNetworkStreamText() => PPTests.Basic_NetworkStream_Text_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>Pipelines=>PingPong", OperationsPerInvoke = PingPongTests.LoopCount)]
        public Task BasicNetworkStreamPipelines() => PPTests.Basic_NetworkStream_Pipelines_PingPong();
    }
}
