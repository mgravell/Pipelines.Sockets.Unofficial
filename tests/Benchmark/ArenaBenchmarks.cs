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
            // allocate a uint on all 3 allocators, as
            // we want to force thunking (we test with int later)
            // (this actually only impacts "no padding", but ...
            // let's be fair)
            _multiArenaPadding.Allocate<uint>();
            _multiArenaNoPadding.Allocate<uint>();
            _multiArenaNoSharing.Allocate<uint>();
            _multiArenaPadding.Reset();
            _multiArenaNoPadding.Reset();
            _multiArenaNoSharing.Reset();

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

            _arenaAllocs = new List<Sequence<int>>(_maxCount);
            _arrayAllocs = new List<int[]>(_maxCount);
            _poolAllocs = new List<ArraySegment<int>>(_maxCount);

            var firstArray = _sizes[0];
            _arrayRW = new int[firstArray.Length][];
            _arenaRW = new Sequence<int>[firstArray.Length];
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

        readonly Arena<int> _rwArena;
        readonly List<Sequence<int>> _arenaAllocs;
        readonly List<int[]> _arrayAllocs;
        readonly List<ArraySegment<int>> _poolAllocs;
        readonly int[][] _arrayRW;
        readonly Sequence<int>[] _arenaRW;
        readonly ArraySegment<int>[] _poolRW;

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

        static long WriteSpans(Sequence<int>[] segments)
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

        static long WriteSegments(Sequence<int>[] segments)
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

        static long ReadSpansFor(Sequence<int>[] segments)
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

        static long ReadSegmentsFor(Sequence<int>[] segments)
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

        static long ReadForeach(Sequence<int>[] segments)
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
        public void Alloc_NewArray()
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
        public void Alloc_ArrayPool()
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
        readonly Arena _multiArenaPadding = new Arena(new ArenaOptions(ArenaFlags.BlittablePaddedSharing | ArenaFlags.BlittableNonPaddedSharing));
        readonly Arena _multiArenaNoPadding = new Arena(new ArenaOptions(ArenaFlags.BlittableNonPaddedSharing));
        readonly Arena _multiArenaNoSharing = new Arena(new ArenaOptions(ArenaFlags.None));

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "Arena<int>.Allocate")]
        public void Alloc_ArenaT()
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

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "Arena.Allocate<int> (padding)")]
        public void Alloc_Arena_Default()
        {

            for (int i = 0; i < _sizes.Length; i++)
            {
                _arenaAllocs.Clear();
                _multiArenaPadding.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    _arenaAllocs.Add(_multiArenaPadding.Allocate<int>(arr[j]));
                }
            }
        }

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "Arena.Allocate<int> (no padding)")]
        public void Alloc_Arena_NoPadding()
        {
            for (int i = 0; i < _sizes.Length; i++)
            {
                _arenaAllocs.Clear();
                _multiArenaNoPadding.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    _arenaAllocs.Add(_multiArenaNoPadding.Allocate<int>(arr[j]));
                }
            }
        }

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "Arena.Allocate<int> (no sharing)")]
        public void Alloc_Arena_NoSharing()
        {
            for (int i = 0; i < _sizes.Length; i++)
            {
                _arenaAllocs.Clear();
                _multiArenaNoSharing.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    _arenaAllocs.Add(_multiArenaNoSharing.Allocate<int>(arr[j]));
                }
            }
        }

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "OwnedArena<int>.Allocate (padding)")]
        public void Alloc_Arena_Owned_Default()
        {
            var owned = _multiArenaPadding.GetArena<int>();
            for (int i = 0; i < _sizes.Length; i++)
            {
                _arenaAllocs.Clear();
                _multiArenaPadding.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    _arenaAllocs.Add(owned.Allocate(arr[j]));
                }
            }
        }

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "OwnedArena<int>.Allocate (no padding)")]
        public void Alloc_Arena_Owned_NoPadding()
        {
            var owned = _multiArenaNoPadding.GetArena<int>();
            for (int i = 0; i < _sizes.Length; i++)
            {
                _arenaAllocs.Clear();
                _multiArenaNoPadding.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    _arenaAllocs.Add(owned.Allocate(arr[j]));
                }
            }
        }

        [BenchmarkCategory("allocate")]
        [Benchmark(Description = "OwnedArena<int>.Allocate (no sharing)")]
        public void Alloc_Arena_Owned_NoSharingg()
        {
            var owned = _multiArenaNoSharing.GetArena<int>();
            for (int i = 0; i < _sizes.Length; i++)
            {
                _arenaAllocs.Clear();
                _multiArenaNoSharing.Reset();
                var arr = _sizes[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    _arenaAllocs.Add(owned.Allocate(arr[j]));
                }
            }
        }
    }
}
