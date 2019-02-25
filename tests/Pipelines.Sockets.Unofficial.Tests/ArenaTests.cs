using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Linq;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class ArenaTests
    {
        [Fact]
        public void WriteAndRead()
        {
            using (var arena = new Arena<int>(blockSize: 1024))
            {
                var rand = new Random(43134114);
                var arr = new Allocation<int>[100];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i] = arena.Allocate(rand.Next(0, 512));
                }
                long total = Write(arr);
                Assert.Equal(total, ReadSpans(arr));
                Assert.Equal(total, ReadSegments(arr));
                Assert.Equal(total, ReadElementsIndexer(arr));
                Assert.Equal(total, ReadElementsRefAdd(arr));

                var singleSegmentCount = arr.Count(x => x.IsSingleSegment);

                // we expect "some" (not zero, not all) single-segments
                Assert.NotEqual(0, singleSegmentCount);
                Assert.NotEqual(arr.Length, singleSegmentCount);

            }
        }
        internal static long Write(Allocation<int>[] segments)
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

        internal static long ReadSpans(Allocation<int>[] segments)
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

        internal static long ReadSegments(Allocation<int>[] segments)
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

        internal static long ReadElementsIndexer(Allocation<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                foreach(var val in segments[i].Indexer)
                {
                    total += val;
                }
            }
            return total;
        }
        internal static long ReadElementsRefAdd(Allocation<int>[] segments)
        {
            long total = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                foreach (var val in segments[i].RefAdd)
                {
                    total += val;
                }
            }
            return total;
        }
    }
}
