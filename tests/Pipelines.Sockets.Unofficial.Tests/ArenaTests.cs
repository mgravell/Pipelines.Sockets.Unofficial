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
                Assert.Equal(total, Read(arr));

                var singleSegment = arr.Count(x => x.IsSingleSegment);

                // we expect "some" (not zero, not all) single-segments
                Assert.NotEqual(0, singleSegment);
                Assert.NotEqual(arr.Length, singleSegment);

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

        internal static long Read(Allocation<int>[] segments)
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
    }
}
