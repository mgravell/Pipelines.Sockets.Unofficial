using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Arenas;
using System.Collections.Generic;

namespace Benchmark
{
    [MemoryDiagnoser, CoreJob, ClrJob, MinColumn, MaxColumn]
    public class ListBenchmarks : BenchmarkBase
    {
        public ListBenchmarks()
        {
            _value32 = 42;
            _value64 = 42;
        }

        const int SIZE = 1024;

        [Benchmark]
        public int ListDefault_Int32()
        {
            var list = new List<int>();
            for (int i = 0; i < SIZE; i++)
                list.Add(_value32);
            return list.Count.AssertIs(SIZE);
        }
        [Benchmark]
        public int ListPresized_Int32()
        {
            var list = new List<int>(SIZE);
            for (int i = 0; i < SIZE; i++)
                list.Add(_value32);
            return list.Count.AssertIs(SIZE);
        }
        [Benchmark]
        public int SeqeunceListDefault_Int32()
        {
            var list = SequenceList<int>.Create();
            for (int i = 0; i < SIZE; i++)
                list.Add(_value32);
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }
        [Benchmark]
        public int SeqeunceListPresized_Int32()
        {
            var list = SequenceList<int>.Create(SIZE);
            for (int i = 0; i < SIZE; i++)
                list.Add(_value32);
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }

        private int _value32;
        private long _value64;

        [Benchmark]
        public int ListDefault_Int64()
        {
            var list = new List<long>();
            for (int i = 0; i < SIZE; i++)
                list.Add(_value64);
            return list.Count.AssertIs(SIZE);
        }
        [Benchmark]
        public int ListPresized_Int64()
        {
            var list = new List<long>(SIZE);
            for (int i = 0; i < SIZE; i++)
                list.Add(_value64);
            return list.Count.AssertIs(SIZE);
        }
        [Benchmark]
        public int SeqeunceListDefault_Int64()
        {
            var list = SequenceList<long>.Create();
            for (int i = 0; i < SIZE; i++)
                list.Add(_value64);
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }
        [Benchmark]
        public int SeqeunceListPresized_Int64()
        {
            var list = SequenceList<long>.Create(SIZE);
            for (int i = 0; i < SIZE; i++)
                list.Add(_value64);
            int count = list.Count;
            list.Clear(trim: true);
            return count.AssertIs(SIZE);
        }
    }
}
