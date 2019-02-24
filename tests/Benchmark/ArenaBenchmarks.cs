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

            _arenaAllocs = new List<Allocation<int>>(_maxCount);
            _arrayAllocs = new List<int[]>(_maxCount);
            _poolAllocs = new List<ArraySegment<int>>(_maxCount);
        }
        List<Allocation<int>> _arenaAllocs;
        List<int[]> _arrayAllocs;
        List<ArraySegment<int>> _poolAllocs;

        [Benchmark(Description = "new int[]")]
        public void New()
        {
            for (int i = 0; i < _sizes.Length; i++)
            {
                _arrayAllocs.Clear();
                var arr = _sizes[i];
                for(int j = 0; j < arr.Length; j++)
                {
                    _arrayAllocs.Add(new int[arr[j]]);
                }
            }
        }

        [Benchmark(Description = "ArrayPool<int>.Rent")]
        public void ArrayPool()
        {
            for (int i = 0; i < _sizes.Length; i++)
            {
                _poolAllocs.Clear();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    var size = arr[j];
                    _poolAllocs.Add(new ArraySegment<int>(_pool.Rent(size), 0, size));
                }

                // and put back
                foreach (var item in _poolAllocs)
                {
                    _pool.Return(item.Array, clearArray: false);
                }
            }
        }

        readonly ArrayPool<int> _pool = ArrayPool<int>.Shared;
        readonly Arena<int> _arena = new Arena<int>();

        [Benchmark(Description = "Arena<int>.Allocate")]
        public void Arena()
        {
            
            for (int i = 0; i < _sizes.Length; i++)
            {
                _arenaAllocs.Clear();
                _arena.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    _arenaAllocs.Add(_arena.Allocate(arr[j]));
                }
            }
        }
    }
}
