using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Tests;
using System.IO;
using System.Threading.Tasks;

namespace Benchmarks
{
    [Config(typeof(Config))]
    public class TLSTests
    {
        public static PingPongTests PPTests { get; } = PingPongTests.Create(TextWriter.Null);

        [Benchmark(Description = "Socket=>Pipelines=>Inverter=>SslStream=>Inverter=>PingPong")]
        public Task BasicPipelines() => PPTests.ServerClientDoubleInverted_SslStream_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>SslStream=>PingPong")]
        public Task BasicNetworkStream() => PPTests.ServerClient_SslStream_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>SslStream=>Inverter=>PingPong")]
        public Task BasicPipelinesText() => PPTests.ServerClient_SslStream_Inverter_PingPong();
    }
}
