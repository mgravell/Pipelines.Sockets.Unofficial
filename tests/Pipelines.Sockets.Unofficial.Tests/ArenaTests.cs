using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class ArenaTests
    {
#pragma warning disable CS0169, IDE0051 // unused fields
        private struct TwoPositions<T>
        {
            private readonly SequencePosition start, end;
        }
        private struct TwoPair<T>
        {
            private readonly object a, b;
            private readonly int c, d;
        }
        private struct Len32<T>
        {
            private readonly object x;
            private readonly int offset, len;
        }
        private struct Len64<T>
        {
            private readonly long len;
            private readonly object x;
            private readonly int offse;
        }
#pragma warning restore CS0169

        [Fact]
        public void AssertPossibleLayoutSizes()
        {   // this test is re proving out the layout of Sequence<T>
            if (IntPtr.Size == 8)
            {
                Assert.Equal(32, Unsafe.SizeOf<ReadOnlySequence<int>>());
                Assert.Equal(24, Unsafe.SizeOf<Sequence<int>>());

                Assert.Equal(32, Unsafe.SizeOf<TwoPositions<int>>());
                Assert.Equal(24, Unsafe.SizeOf<TwoPair<int>>());
                Assert.Equal(16, Unsafe.SizeOf<Len32<int>>());
                Assert.Equal(24, Unsafe.SizeOf<Len64<int>>());
            }
            else if (IntPtr.Size == 4)
            {
                Assert.Equal(16, Unsafe.SizeOf<ReadOnlySequence<int>>());
                Assert.Equal(16, Unsafe.SizeOf<Sequence<int>>());

                Assert.Equal(16, Unsafe.SizeOf<TwoPositions<int>>());
                Assert.Equal(16, Unsafe.SizeOf<TwoPair<int>>());
                Assert.Equal(12, Unsafe.SizeOf<Len32<int>>());
                Assert.Equal(16, Unsafe.SizeOf<Len64<int>>());
            }
            else
            {
                Assert.True(false, "unknown CPU size: " + IntPtr.Size);
            }
        }

        [Fact]
        public void SliceAndDice()
        {
            using (var arena = new Arena<int>(new ArenaOptions(blockSizeBytes: 16 * sizeof(int))))
            {
                var alloc = arena.Allocate(2048);

                int i = 0, spanCount = 0;
                foreach (var span in alloc.Spans)
                {
                    spanCount++;
                    for (int j = 0; j < span.Length; j++)
                    {
                        span[j] = i++;
                    }
                }
                Assert.True(spanCount >= 10);

                var all = alloc.Slice(0, (int)alloc.Length);
                Assert.Equal(2048, all.Length);
                Check(all, 0);

                var small = alloc.Slice(8, 4);
                Assert.Equal(4, small.Length);
                Check(small, 8);

                var subSection = alloc.Slice(1250);
                Assert.Equal(2048 - 1250, subSection.Length);
                Check(subSection, 1250);

                Assert.Throws<ArgumentOutOfRangeException>(() => alloc.Slice(1, (int)alloc.Length));
            }

            void Check(Sequence<int> range, int start)
            {
                int count = 0;
                foreach (var span in range.Spans)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        Assert.Equal(start++, span[i]);
                        count++;
                    }
                }
                Assert.Equal(range.Length, count);
            }
        }

        [Fact]
        public void WriteAndRead()
        {
            using (var arena = new Arena<int>(new ArenaOptions(blockSizeBytes: 1024 * sizeof(int))))
            {
                var rand = new Random(43134114);
                var arr = new Sequence<int>[100];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i] = arena.Allocate(rand.Next(0, 512));
                }
                long total = Write(arr);
                Assert.Equal(total, ReadSpans(arr));
                Assert.Equal(total, ReadSegments(arr));
                Assert.Equal(total, ReadElements(arr));

                var singleSegmentCount = arr.Count(x => x.IsSingleSegment);

                // we expect "some" (not zero, not all) single-segments
                Assert.NotEqual(0, singleSegmentCount);
                Assert.NotEqual(arr.Length, singleSegmentCount);

            }
        }

        internal static long Write(Sequence<int>[] segments)
        {
            int val = 0;
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.IsSingleSegment)
                {
                    var span = segment.FirstSpan;
                    for (int j = 0; j < span.Length; j++)
                    {
                        total += span[j] = ++val;
                    }
                }
                else
                {
                    foreach (var span in segment.Spans)
                    {
                        for (int j = 0; j < span.Length; j++)
                        {
                            total += span[j] = ++val;
                        }
                    }
                }
            }
            return total;
        }

        internal static long ReadSpans(Sequence<int>[] segments)
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

        internal static long ReadSegments(Sequence<int>[] segments)
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
                    foreach (var seg in segment.Segments)
                    {
                        var span = seg.Span;
                        for (int j = 0; j < span.Length; j++)
                        {
                            total += span[j];
                        }
                    }
                }
            }
            return total;
        }

        internal static long ReadElements(Sequence<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                foreach (var val in segments[i])
                {
                    total += val;
                }
            }
            return total;
        }

        private readonly ArenaOptions _blockSizeFive = new ArenaOptions(blockSizeBytes: 5 * sizeof(int));
        [Fact]
        public void Copy()
        {
            using (Arena<int> from = new Arena<int>(_blockSizeFive), to = new Arena<int>(_blockSizeFive))
            {
                var source = from.Allocate(100);
                Assert.False(source.IsSingleSegment);
                Assert.Equal(100, source.Length);
                var iter = source.GetEnumerator();
                int i = 0;
                while (iter.MoveNext()) iter.Current = i++;

                var doubles = to.Allocate(source, (in int x) => 2 * x);
                Assert.False(doubles.IsSingleSegment);
                Assert.Equal(100, doubles.Length);
                i = 0;
                iter = doubles.GetEnumerator();
                while (iter.MoveNext())
                {
                    Assert.Equal(2 * i++, iter.Current);
                }
            }
        }

        [Fact]
        public void Positions()
        {
            using (Arena<int> from = new Arena<int>(_blockSizeFive))
            {
                from.Allocate(42); // just want an arbitrary offset here
                var source = from.Allocate(100);
                Assert.Throws<IndexOutOfRangeException>(() => source.GetPosition(-1));
                Assert.Throws<IndexOutOfRangeException>(() => source.GetPosition(101));

                Assert.Equal(source.GetPosition(0), source.Start);
                Assert.Equal(source.GetPosition(100), source.End);
                for (int i = 0; i <= 100; i++)
                {
                    var pos = source.GetPosition(i);
                    var offset = pos.TryGetOffset().Value;
                    if (offset != i + 42)
                    {
                        Debugger.Break();
                    }
                    Assert.Equal(i + 42, offset);
                }
            }
        }
    }
}
