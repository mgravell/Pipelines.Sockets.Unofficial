using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Tests;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Benchmarks
{
    [Config(typeof(Config))]
    public class BasicTests
    {
        public static PingPongTests PPTests { get; } = new PingPongTests(TextWriter.Null);

        [Benchmark(Baseline = true, Description = "Socket=>Pipelines=>PingPong")]
        public Task BasicPipelines() => PPTests.Basic_Pipelines_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>PingPong")]
        public Task BasicNetworkStream() => PPTests.Basic_NetworkStream_PingPong();

        [Benchmark(Description = "Socket=>Pipelines=>TRW=>PingPong")]
        public Task BasicPipelinesText() => PPTests.Basic_Pipelines_Text_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>TRW=>PingPong")]
        public Task BasicNetworkStreamText() => PPTests.Basic_NetworkStream_Text_PingPong();

        [Benchmark(Description = "Socket=>NetworkStream=>Pipelines=>PingPong")]
        public Task BasicNetworkStreamPipelines() => PPTests.Basic_NetworkStream_Pipelines_PingPong();
    }


    [Config(typeof(Config))]
    public class TextTests
    {
        const string path = "t8.shakespeare.txt";
        static readonly Encoding encoding = Encoding.UTF8;
        [Benchmark(Baseline = true, Description = "StreamReader/FileStream")]
        public async ValueTask<string> TestFileStream()
        {
            using (var reader = new StreamReader(path, encoding))
            {
                return await TestReaderTests.MeasureAndTime(reader);
            }
        }

        [Benchmark(Description = "PipeTextReader/MemoryMappedPipeReader")]
        public async ValueTask<string> BasicNetworkStream()
        {
            var mmap = MemoryMappedPipeReader.Create(path);
            using (mmap as IDisposable)
            using (var reader = new PipeTextReader(mmap, encoding))
            {
                return await TestReaderTests.MeasureAndTime(reader);
            }
        }
    }
}
