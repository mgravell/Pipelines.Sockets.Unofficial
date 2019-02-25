using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace Benchmark
{
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
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

            var firstArray = _sizes[0];
            _arrayRW = new int[firstArray.Length][];
            _arenaRW = new Allocation<int>[firstArray.Length];
            _poolRW = new ArraySegment<int>[firstArray.Length];
            _rwArena = new Arena<int>();

            for (int i = 0; i < firstArray.Length; i++)
            {
                int len = firstArray[i];
                _arrayRW[i] = new int[len];
                _arenaRW[i] = _rwArena.Allocate(len);
                var rented = ArrayPool<int>.Shared.Rent(len);
                _poolRW[i] = new ArraySegment<int>(rented, 0, len);
            }

            var expected = Write(_arrayRW);
            Write(_arenaRW).AssertIs(expected);
            Write(_poolRW).AssertIs(expected);

            int n = firstArray.Sum();
            Read(_arrayRW).AssertIs(expected);
            Read(_arenaRW).AssertIs(expected);
            Read(_poolRW).AssertIs(expected);
        }
        Arena<int> _rwArena;
        List<Allocation<int>> _arenaAllocs;
        List<int[]> _arrayAllocs;
        List<ArraySegment<int>> _poolAllocs;

        int[][] _arrayRW;
        Allocation<int>[] _arenaRW;
        ArraySegment<int>[] _poolRW;

        static long Write(int[][] segments)
        {
            long total = 0;
            int val = 0;
            for (int i = 0; i < segments.Length;i++)
            {
                var arr = segments[i];
                for(int j = 0; j < arr.Length; j++)
                {
                    total += arr[j] = ++val;
                }
            }
            return total;
        }

        static long Read(int[][] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var arr = segments[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    total += arr[j];
                }
            }
            return total;
        }

        static long Write(Allocation<int>[] segments)
        {
            long total = 0;
            int val = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.IsSingleSegment)
                {
                    var span = segment.FirstSpan;
                    for(int j = 0; j < span.Length;j++)
                    {
                        total += span[j] = ++val;
                    }
                }
                else
                {
                    foreach(var span in segment.Spans)
                    {
                        for (int j = 0; j < span.Length; j++)
                        {
                            total+= span[j] = ++val;
                        }
                    }
                }
            }
            return total;
        }

        static long Read(Allocation<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.IsSingleSegment)
                {
                    var span = segment.FirstSpan;
                    for (int j = 0; j < span.Length; j++)
                    {
                        total += span[j];
                    }
                }
                else
                {
                    foreach (var span in segment.Spans)
                    {
                        for (int j = 0; j < span.Length; j++)
                        {
                            total += span[j];
                        }
                    }
                }
            }
            return total;
        }

        static long Write(ArraySegment<int>[] segments)
        {
            long total = 0;
            int val = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var arr = segment.Array;
                var end = segment.Offset + segment.Count;
                for (int j = segment.Offset; j < end; j++)
                {
                    total += arr[j] = ++val;
                }
            }
            return total;
        }

        static long Read(ArraySegment<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var arr = segment.Array;
                var end = segment.Offset + segment.Count;
                for (int j = segment.Offset; j < end; j++)
                {
                    total += arr[j];
                }
            }
            return total;
        }


        [BenchmarkCategory("read/for")]
        [Benchmark(Description = "int[]")]
        public long ReadArrayFor() => Read(_arrayRW);

        [BenchmarkCategory("read/for")]
        [Benchmark(Description = "ArrayPool<int>", Baseline = true)]
        public long ReadArrayPoolFor() => Read(_poolRW);

        [BenchmarkCategory("read/for")]
        [Benchmark(Description = "Arena<int>")]
        public long ReadArenaFor() => Read(_arenaRW);

        [BenchmarkCategory("allocate")]
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

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "ArrayPool<int>.Rent", Baseline = true)]
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

        [BenchmarkCategory("allocate")]
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
