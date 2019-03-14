using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.IO;

namespace Benchmark
{

    public class StreamBenchmarks : BenchmarkBase
    {
        const int SEED = 123456, MAX_BYTES = 2048, ITERS = 512, TOTAL_SIZE = 514672;
        private readonly byte[] buffer = new byte[MAX_BYTES];
        [Benchmark(Baseline = true)]
        public long MemoryStreamDefault()
        {
            using (var ms = new MemoryStream())
            {
                var rand = new Random(SEED);
                for (int i = 0; i < ITERS; i++)
                {
                    rand.NextBytes(buffer);
                    ms.Write(buffer, 0, rand.Next(MAX_BYTES));
                }
                return ms.Length.AssertIs(TOTAL_SIZE);
            }
        }

        [Benchmark]
        public long MemoryStreamPreSize()
        {
            using (var ms = new MemoryStream(TOTAL_SIZE))
            {
                var rand = new Random(SEED);
                for (int i = 0; i < ITERS; i++)
                {
                    rand.NextBytes(buffer);
                    ms.Write(buffer, 0, rand.Next(MAX_BYTES));
                }
                return ms.Length.AssertIs(TOTAL_SIZE);
            }
        }

        [Benchmark]
        public long SequenceStreamDefault()
        {
            using (var ms = SequenceStream.Create())
            {
                var rand = new Random(SEED);
                for (int i = 0; i < ITERS; i++)
                {
                    rand.NextBytes(buffer);
                    ms.Write(buffer, 0, rand.Next(MAX_BYTES));
                }
                return ms.Length.AssertIs(TOTAL_SIZE);
            }
        }

        [Benchmark]
        public long SequenceStreamPreSize()
        {
            using (var ms = SequenceStream.Create(TOTAL_SIZE))
            {
                var rand = new Random(SEED);
                for (int i = 0; i < ITERS; i++)
                {
                    rand.NextBytes(buffer);
                    ms.Write(buffer, 0, rand.Next(MAX_BYTES));
                }
                return ms.Length.AssertIs(TOTAL_SIZE);
            }
        }
    }
}
