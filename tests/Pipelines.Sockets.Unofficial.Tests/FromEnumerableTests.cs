using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class FromEnumerableTests
    {
        [Fact]
        public void FromEnumerableNull()
        {
            IEnumerable<int> source = null;
            Assert.Throws<ArgumentNullException>(() => source.ToSequence());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]
        public void FromEnumerableRange(int count)
        {
            var source = Enumerable.Range(42, count);
            var sequence = source.ToSequence();
            Assert.Equal(count, sequence.Length);
            for (int i = 0; i < count; i++)
                Assert.Equal(42 + i, sequence[i]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]
        public void FromArray(int count)
        {
            var source = Enumerable.Range(42, count).ToArray();
            var sequence = source.ToSequence();
            Assert.Equal(count, sequence.Length);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(42 + i, sequence[i]);
                // check reuses existing data
                Assert.Equal(new Reference<int>(source, i), sequence.GetReference(i));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]

        public void FromSequenceList(int count)
        {
            var original = new Sequence<int>(Enumerable.Range(42, count).ToArray());
            IEnumerable<int> source = original.ToList();
            var sequence = source.ToSequence();
            Assert.Equal(count, sequence.Length);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(42 + i, sequence[i]);
                // check reuses existing data
                Assert.Equal(original.GetReference(i), sequence.GetReference(i));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]
        public void AllocateFromSequenceTyped(int count)
        {
            using (var arena = new Arena<int>(new ArenaOptions(blockSizeBytes: 16 * sizeof(int))))
            {
                arena.Allocate(13); // just to make it interesting
                var sequence = arena.Allocate(Enumerable.Range(42, count));
                Assert.Equal(count, sequence.Length);
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal(42 + i, sequence[i]);
                }
                Assert.Equal((13 + count) * sizeof(int), arena.AllocatedBytes());
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]
        public void AllocateFromSequenceUntyped(int count)
        {
            using (var arena = new Arena(new ArenaOptions(blockSizeBytes: 16 * sizeof(int))))
            {
                arena.Allocate<int>(13); // just to make it interesting
                var sequence = arena.Allocate(Enumerable.Range(42, count));
                Assert.Equal(count, sequence.Length);
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal(42 + i, sequence[i]);
                }
                Assert.Equal((13 + count) * sizeof(int), arena.AllocatedBytes());
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]
        public void AllocateFromSequenceOwned(int count)
        {
            using (var arena = new Arena(new ArenaOptions(blockSizeBytes: 16 * sizeof(int))))
            {
                arena.Allocate<int>(13); // just to make it interesting
                var sequence = arena.GetArena<int>().Allocate(Enumerable.Range(42, count));
                Assert.Equal(count, sequence.Length);
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal(42 + i, sequence[i]);
                }
                Assert.Equal((13 + count) * sizeof(int), arena.AllocatedBytes());
            }
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(1, 0, false)]
        [InlineData(2, 0, true)]
        [InlineData(10, 9, false)]
        [InlineData(10, 8, true)]
        public void AllocateInterrupted(int count, int interruptAfter, bool faults)
        {
            using (var arena = new Arena<int>())
            {
                IEnumerable<int> Interrupted()
                {
                    for (int i = 0; i < count; i++)
                    {
                        yield return i;
                        if (interruptAfter == i) arena.Allocate(1);
                    }
                }
                IEnumerable<int> data = Interrupted();
                if (faults)
                {
                    var ex = Assert.Throws<InvalidOperationException>(() => arena.Allocate(data));
                    Assert.Equal("Data was allocated while the enumerator was iterating", ex.Message);
                }
                else
                {
                    var seq = arena.Allocate(data);
                    Assert.Equal(count, seq.Length);
                    for (int i = 0; i < count; i++)
                        Assert.Equal(i, seq[i]);
                }
            }
        }
    }
}
