using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace Benchmark
{
    [MemoryDiagnoser, CoreJob, ClrJob]
    public class ArenaBenchmarks
    {
        private readonly int[][] _sizes;
        private readonly int _maxCount;
        public ArenaBenchmarks()
        {
            var rand = new Random(43134114);
            _sizes = new int[100][];
            for(int i = 0; i < _sizes.Length;i++)
            {
                int[] arr = _sizes[i] = new int[rand.Next(10, 100)];
                for(int j = 0; j < arr.Length; j++)
                {
                    arr[j] = rand.Next(1024);
                }
            }
            _maxCount = _sizes.Max(x => x.Length);
        }

        [Benchmark]
        public void New()
        {
            var allocs = new List<int[]>(_maxCount);
            for (int i = 0; i < _sizes.Length; i++)
            {
                allocs.Clear();
                var arr = _sizes[i];
                for(int j = 0; j < arr.Length; j++)
                {
                    allocs.Add(new int[arr[j]]);
                }
            }
        }

        [Benchmark]
        public void ArrayPool()
        {
            var allocs = new List<ArraySegment<int>>(_maxCount);
            for (int i = 0; i < _sizes.Length; i++)
            {
                allocs.Clear();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    var size = arr[j];
                    allocs.Add(new ArraySegment<int>(_pool.Rent(size), 0, size));
                }

                // and put back
                foreach (var item in allocs)
                {
                    _pool.Return(item.Array, clearArray: false);
                }
            }
        }

        readonly ArrayPool<int> _pool = ArrayPool<int>.Shared;
        readonly Arena<int> _arena = new Arena<int>();

        [Benchmark]
        public void Arena()
        {
            var allocs = new List<Allocation<int>>(_maxCount);
            for (int i = 0; i < _sizes.Length; i++)
            {
                allocs.Clear();
                _arena.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    allocs.Add(_arena.Allocate(arr[j]));
                }
            }
        }
    }
}
