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
    [CategoriesColumn]
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
            WriteSpans(_arenaRW).AssertIs(expected);
            WriteSegments(_arenaRW).AssertIs(expected);
            Write(_poolRW).AssertIs(expected);

            int n = firstArray.Sum();
            ReadFor(_arrayRW).AssertIs(expected);
            ReadSpansFor(_arenaRW).AssertIs(expected);
            ReadSegmentsFor(_arenaRW).AssertIs(expected);
            ReadFor(_poolRW).AssertIs(expected);

            ReadForeach(_arrayRW).AssertIs(expected);
            ReadForeach(_arenaRW).AssertIs(expected);
            ReadForeach(_poolRW).AssertIs(expected);
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

        static long ReadFor(int[][] segments)
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

        static long ReadForeach(int[][] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var arr = segments[i];
                foreach(int val in arr)
                {
                    total += val;
                }
            }
            return total;
        }

        static long WriteSpans(Allocation<int>[] segments)
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

        static long WriteSegments(Allocation<int>[] segments)
        {
            long total = 0;
            int val = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.IsSingleSegment)
                {
                    var span = segment.FirstSegment.Span;
                    for (int j = 0; j < span.Length; j++)
                    {
                        total += span[j] = ++val;
                    }
                }
                else
                {
                    foreach (var seg in segment.Segments)
                    {
                        var span = seg.Span;
                        for (int j = 0; j < span.Length; j++)
                        {
                            total += span[j] = ++val;
                        }
                    }
                }
            }
            return total;
        }

        static long ReadSpansFor(Allocation<int>[] segments)
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

        static long ReadSegmentsFor(Allocation<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.IsSingleSegment)
                {
                    var span = segment.FirstSegment.Span;
                    for (int j = 0; j < span.Length; j++)
                    {
                        total += span[j];
                    }
                }
                else
                {
                    foreach (var mem in segment.Segments)
                    {
                        var span = mem.Span;
                        for (int j = 0; j < span.Length; j++)
                        {
                            total += span[j];
                        }
                    }
                }
            }
            return total;
        }

        static long ReadForeach(Allocation<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                foreach (int val in segments[i])
                {
                    total += val;
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

        static long ReadFor(ArraySegment<int>[] segments)
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

        static long ReadForeach(ArraySegment<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                foreach (int val in segments[i])
                {
                    total += val;
                }
            }
            return total;
        }


        [BenchmarkCategory("read/for")]
        [Benchmark(Description = "int[]")]
        public long ReadArrayFor() => ReadFor(_arrayRW);

        [BenchmarkCategory("read/for")]
        [Benchmark(Description = "ArrayPool<int>", Baseline = true)]
        public long ReadArrayPoolFor() => ReadFor(_poolRW);

        [BenchmarkCategory("read/for")]
        [Benchmark(Description = "Arena<int>.Spans")]
        public long ReadArenaSpansFor() => ReadSpansFor(_arenaRW);

        [BenchmarkCategory("read/for")]
        [Benchmark(Description = "Arena<int>.Segments")]
        public long ReadArenaSegmentsFor() => ReadSegmentsFor(_arenaRW);

        [BenchmarkCategory("read/foreach")]
        [Benchmark(Description = "int[]")]
        public long ReadArrayForeach() => ReadForeach(_arrayRW);

        [BenchmarkCategory("read/foreach")]
        [Benchmark(Description = "ArrayPool<int>", Baseline = true)]
        public long ReadArrayPoolForeach() => ReadForeach(_poolRW);

        [BenchmarkCategory("read/foreach")]
        [Benchmark(Description = "Arena<int>")]
        public long ReadArenaForeachRefAdd() => ReadForeach(_arenaRW);



        [BenchmarkCategory("write/for")]
        [Benchmark(Description = "int[]")]
        public long WriteArrayFor() => Write(_arrayRW);

        [BenchmarkCategory("write/for")]
        [Benchmark(Description = "ArrayPool<int>", Baseline = true)]
        public long WriteArrayPoolFor() => Write(_poolRW);

        [BenchmarkCategory("write/for")]
        [Benchmark(Description = "Arena<int>.Spans")]
        public long WriteArenaSpansFor() => WriteSpans(_arenaRW);

        [BenchmarkCategory("write/for")]
        [Benchmark(Description = "Arena<int>.Segments")]
        public long WriteArenaSegmentsFor() => WriteSegments(_arenaRW);


        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "int[]")]
        public void NewArray()
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
