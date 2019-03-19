using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Arenas;
using System.Collections.Generic;
using System.Linq;

namespace Benchmark
{
    [MemoryDiagnoser, CoreJob, ClrJob, MinColumn, MaxColumn]
    public class ListBenchmarks : BenchmarkBase
    {
        const int SIZE = 1024;

        [Benchmark]
        public int ListDefault_Add()
        {
            var list = new List<int>();
            for (int i = 0; i < SIZE; i++)
                list.Add(i);
            return list.Count.AssertIs(SIZE);
        }
        [Benchmark]
        public int ListPresized_Add()
        {
            var list = new List<int>(SIZE);
            for (int i = 0; i < SIZE; i++)
                list.Add(i);
            return list.Count.AssertIs(SIZE);
        }

        [Benchmark]
        public int ListDefault_AddRange()
        {
            var list = new List<int>();
            list.AddRange(Enumerable.Range(0, SIZE));
            return list.Count.AssertIs(SIZE);
        }
        [Benchmark]
        public int ListPresized_AddRange()
        {
            var list = new List<int>(SIZE);
            list.AddRange(Enumerable.Range(0, SIZE));
            return list.Count.AssertIs(SIZE);
        }
        [Benchmark]
        public int SequenceListDefault_Add()
        {
            var list = SequenceList<int>.Create();
            for (int i = 0; i < SIZE; i++)
                list.Add(i);
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }
        [Benchmark]
        public int SequenceListPresized_Add()
        {
            var list = SequenceList<int>.Create(SIZE);
            for (int i = 0; i < SIZE; i++)
                list.Add(i);
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }

        [Benchmark]
        public int SequenceListDefault_AddRange()
        {
            var list = SequenceList<int>.Create();
            list.AddRange(Enumerable.Range(0, SIZE));
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }
        [Benchmark]
        public int SequenceListPresized_AddRange()
        {
            var list = SequenceList<int>.Create(SIZE);
            list.AddRange(Enumerable.Range(0, SIZE));
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }
    }
}
