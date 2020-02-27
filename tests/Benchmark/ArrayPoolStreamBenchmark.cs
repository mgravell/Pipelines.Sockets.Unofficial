using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Pipelines.Sockets.Unofficial;
using Sylvan.IO;
using System;
using System.IO;

namespace Benchmark
{
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    [SimpleJob(RuntimeMoniker.Net472)]
    [WarmupCount(2)]
    public class ArrayPoolStreamBenchmark
    {
        private readonly byte[] Chunk = new byte[2048];
        public ArrayPoolStreamBenchmark() => new Random(12345).NextBytes(Chunk);

        [Params(0, 100, 1_000, 10_000, 100_000, 1_000_000, 5_000_000, 10_000_000, 50_000_000)]
        public int Bytes { get; set; }

        [Benchmark(Baseline = true)]
        public long MemoryStream() => Write<MemoryStream>();

        [Benchmark]
        public long ArrayPoolStream() => Write<ArrayPoolStream>();

        [Benchmark]
        public long BlockMemoryStream() => Write<BlockMemoryStream>();

        private long Write<T>() where T : Stream, new()
        {
            using var stream = new T();
            int remaining = Bytes;
            while (remaining > 0)
            {
                int take = Math.Min(remaining, Chunk.Length);
                stream.Write(Chunk, 0, take);
                remaining -= take;
            }
            if (Bytes != stream.Length) throw new InvalidOperationException("Length mismatch!");
            return stream.Length;
        }
    }
}
