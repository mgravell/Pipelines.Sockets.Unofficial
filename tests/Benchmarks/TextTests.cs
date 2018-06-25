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
    public class TextTests
    {
        const int LoopCount = 10;
        const string path = "t8.shakespeare.txt";
        static readonly Encoding encoding = Encoding.UTF8;
        [Benchmark(Baseline = true, Description = "StreamReader/FileStream", OperationsPerInvoke = LoopCount)]
        public async ValueTask<int> TestFileStream()
        {
            int x = 0;
            for (int i = 0; i < LoopCount; i++)
            {
                using (var reader = new StreamReader(path, encoding))
                {
                    x += (await TestReaderTests.MeasureAndTime(reader)).Length;
                }
            }
            return x;
        }

        [Benchmark(Description = "PipeTextReader/MemoryMappedPipeReader", OperationsPerInvoke = LoopCount)]
        public async ValueTask<int> TestMemoryMappedPipe()
        {
            int x = 0;
            for (int i = 0; i < LoopCount; i++)
            {
                var mmap = MemoryMappedPipeReader.Create(path);
                using (mmap as IDisposable)
                using (var reader = new PipeTextReader(mmap, encoding))
                {
                    x += (await TestReaderTests.MeasureAndTime(reader)).Length;
                }
            }
            return x;
        }
    }
}
