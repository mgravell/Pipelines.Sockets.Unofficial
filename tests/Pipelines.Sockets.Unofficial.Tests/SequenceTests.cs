using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Linq;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class SequenceTests
    {
        [Fact]
        public void CheckDefaultSequence()
        {
            Sequence<int> seq = default;
            Assert.False(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 0);
        }

        [Fact]
        public void CheckEmptyArray()
        {
            Sequence<int> seq = new Sequence<int>(Array.Empty<int>());
            Assert.True(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 0);
        }

        [Fact]
        public void CheckNonEmptyArray()
        {
            Sequence<int> seq = new Sequence<int>(new int[42]);
            Assert.True(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 42);
        }

        [Fact]
        public void CheckEmptyMemory()
        {
            Memory<int> memory = default;
            Sequence<int> seq = new Sequence<int>(memory);
            Assert.True(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 0);
        }

        [Fact]
        public void CheckNonEmptyMemory()
        {
            Memory<int> memory = new int[42];
            Sequence<int> seq = new Sequence<int>(memory);
            Assert.True(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 42);
        }

        class MyManager : MemoryManager<int>
        {
            private readonly Memory<int> _memory;
            public MyManager(Memory<int> memory) => _memory = memory;
            public override Span<int> GetSpan() => _memory.Span;

            public override MemoryHandle Pin(int elementIndex = 0) => default;

            public override void Unpin() { }

            protected override void Dispose(bool disposing) { }
        }

        class MyOwner : IMemoryOwner<int>
        {
            public MyOwner(Memory<int> memory) => Memory = memory;
            public Memory<int> Memory { get; }
            public void Dispose() { }
        }

        [Fact]
        public void CheckEmptyCustomManager()
        {
            Memory<int> memory = default;
            using (var owner = new MyManager(memory))
            {
                Sequence<int> seq = new Sequence<int>(owner.Memory);
                Assert.False(seq.IsArray);
                Assert.True(seq.IsSingleSegment);
                TestEveryWhichWay(seq, 0);
            }
        }

        [Fact]
        public void CheckNonEmptyCustomManager()
        {
            Memory<int> memory = new int[42];
            using (var owner = new MyManager(memory))
            {
                Sequence<int> seq = new Sequence<int>(owner.Memory);
                Assert.False(seq.IsArray);
                Assert.True(seq.IsSingleSegment);
                TestEveryWhichWay(seq, 42);
            }
        }

        [Fact]
        public void CheckEmptyCustomOwner()
        {
            Memory<int> memory = default;
            using (var owner = new MyOwner(memory))
            {
                Sequence<int> seq = new Sequence<int>(owner.Memory);
                Assert.True(seq.IsArray);
                Assert.True(seq.IsSingleSegment);
                TestEveryWhichWay(seq, 0);
            }
        }

        [Fact]
        public void CheckNonEmptyCustomOwner()
        {
            Memory<int> memory = new int[42];
            using (var owner = new MyOwner(memory))
            {
                Sequence<int> seq = new Sequence<int>(owner.Memory);
                Assert.True(seq.IsArray);
                Assert.True(seq.IsSingleSegment);
                TestEveryWhichWay(seq, 42);
            }
        }

        class MySegment : SequenceSegment<int>
        {
            public MySegment(Memory<int> memory, MySegment previous = null) : base(memory, previous) { }
        }

        [Fact]
        public void CheckEmptySingleSegment()
        {
            var segment = new MySegment(default);
            Sequence<int> seq = new Sequence<int>(segment, segment, 0, segment.Length);
            Assert.False(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 0);
        }

        [Fact]
        public void CheckNonEmptySingleSegment()
        {
            var segment = new MySegment(new int[42]);
            Sequence<int> seq = new Sequence<int>(segment, segment, 0, segment.Length);
            Assert.False(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 42);
        }

        [Fact]
        public void CheckChainOfEmpty()
        {
            var first = new MySegment(default);
            var segment = new MySegment(default, first);
            segment = new MySegment(default, segment);

            Sequence<int> seq = new Sequence<int>(first, segment, 0, 0);
            Assert.False(seq.IsArray);
            Assert.True(seq.IsSingleSegment); // due to roll-forward
            TestEveryWhichWay(seq, 0);
        }

        [Fact]
        public void CheckEmptySandwich()
        {
            var first = new MySegment(new int[20]);
            var second = new MySegment(default, first);
            var third = new MySegment(new int[22], second);

            Sequence<int> seq = new Sequence<int>(first, third, 0, third.Length);
            Assert.False(seq.IsArray);
            Assert.False(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 42);
        }

        private void TestEveryWhichWay(Sequence<int> sequence, int count)
        {
            Random rand = null; //int _nextRandom = 0;
            int GetNextRandom() => rand.Next(0, 100); //_nextRandom++; 
            void ResetRandom() => rand = new Random(12345); // _nextRandom = 0;


            Assert.Equal(count, sequence.Length);
            if (!sequence.IsEmpty)
            {
                ResetRandom();
                var filler = sequence.GetEnumerator();
                while (filler.MoveNext())
                    filler.Current = GetNextRandom();
            }
            // count/sum via the item iterator
            long total = 0, t;
            int c = 0;
            ResetRandom();
            foreach (var item in sequence)
            {
                c++;
                total += item;
                Assert.Equal(GetNextRandom(), item);
            }
            Assert.Equal(count, c);

            if (count == 0) Assert.True(sequence.IsEmpty);
            else Assert.False(sequence.IsEmpty);

            // count/sum via the span iterator
            t = 0;
            c = 0;
            int spanCount = 0;
            ResetRandom();
            foreach (var span in sequence.Spans)
            {
                if (!span.IsEmpty) spanCount++; // ROS always returns at least one, so...
                foreach (var item in span)
                {
                    c++;
                    t += item;
                    Assert.Equal(GetNextRandom(), item);
                }
            }
            Assert.Equal(total, t);
            Assert.Equal(count, c);

            if (spanCount <= 1) Assert.True(sequence.IsSingleSegment);
            else Assert.False(sequence.IsSingleSegment);

            // count/sum via the segment iterator
            t = 0;
            c = 0;
            int memoryCount = 0;
            ResetRandom();
            foreach (var memory in sequence.Segments)
            {
                if (!memory.IsEmpty) memoryCount++;
                foreach (var item in memory.Span)
                {
                    c++;
                    t += item;
                    Assert.Equal(GetNextRandom(), item);
                }
            }
            Assert.Equal(total, t);
            Assert.Equal(count, c);
            Assert.Equal(spanCount, memoryCount);

            // count/sum via reference iterator
            ResetRandom();
            var iter = sequence.GetEnumerator();
            t = 0;
            c = 0;
            while (iter.MoveNext())
            {
                c++;
                t += iter.CurrentReference;
                Assert.Equal(GetNextRandom(), iter.Current);
            }
            Assert.Equal(total, t);
            Assert.Equal(count, c);

            // count/sum via GetNext();
            ResetRandom();
            iter = sequence.GetEnumerator();
            t = 0;
            c = 0;
            for (long index = 0; index < count; index++)
            {
                c++;
                var n = iter.GetNext();
                t += n;
                Assert.Equal(GetNextRandom(), n);
            }
            try
            {
                iter.GetNext(); // this should throw (can't use Assert.Throws here because ref-struct)
                Assert.Throws<IndexOutOfRangeException>(() => { });
            }
            catch (IndexOutOfRangeException) { }
            Assert.Equal(total, t);
            Assert.Equal(count, c);

            // count/sum via indexer
            t = 0;
            c = 0;
            ResetRandom();
            for (long index = 0; index < count; index++)
            {
                c++;
                t += sequence[index];
                Assert.Equal(GetNextRandom(), sequence[index]);
            }
            Assert.Throws<IndexOutOfRangeException>(() => sequence[-1]);
            Assert.Throws<IndexOutOfRangeException>(() => sequence[c]);
            Assert.Equal(total, t);
            Assert.Equal(count, c);

            // count/sum via Reference<T>
            t = 0;
            c = 0;
            ResetRandom();
            for (long index = 0; index < count; index++)
            {
                c++;
                var r = sequence.GetReference(index);
                t += r.Value;
                Assert.Equal(GetNextRandom(), (int)r);
            }
            Assert.Throws<IndexOutOfRangeException>(() => sequence[-1]);
            Assert.Throws<IndexOutOfRangeException>(() => sequence[c]);
            Assert.Equal(total, t);
            Assert.Equal(count, c);

            // count/sum via list using struct iterator
            t = 0;
            c = 0;
            ResetRandom();
            var list = sequence.ToList();
            foreach (var item in list)
            {
                c++;
                t += item;
                Assert.Equal(GetNextRandom(), item);
            }
            Assert.Equal(total, t);
            Assert.Equal(count, c);

            // count/sum via list using object iterator
            t = 0;
            c = 0;
            ResetRandom();
            foreach (var item in list.AsEnumerable())
            {
                c++;
                t += item;
                Assert.Equal(GetNextRandom(), item);
            }
            Assert.Equal(total, t);
            Assert.Equal(count, c);

            // check by list index
            Assert.Equal(c, list.Count);
            ResetRandom();
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(sequence[i], list[i]);
                Assert.Equal(GetNextRandom(), list[i]);
            }
            Assert.Throws<IndexOutOfRangeException>(() => list[-1]);
            Assert.Throws<IndexOutOfRangeException>(() => list[c]);

            // count/sum via list using GetReference
            t = 0;
            c = 0;
            for (long index = 0; index < count; index++)
            {
                c++;
                t += sequence.GetReference(index);
            }
            Assert.Equal(total, t);
            Assert.Equal(count, c);
            Assert.Throws<IndexOutOfRangeException>(() => sequence.GetReference(-1));
            Assert.Throws<IndexOutOfRangeException>(() => sequence.GetReference(c));

            // check positions are obtainable
            for (long index = 0; index <= count; index++)
            {
                sequence.GetPosition(index);
            }
            Assert.Throws<IndexOutOfRangeException>(() => sequence.GetPosition(-1));
            Assert.Throws<IndexOutOfRangeException>(() => sequence.GetPosition(c + 1));

            // get ROS
            var ros = sequence.AsReadOnly();
            Assert.Equal(c, ros.Length);
            AssertEqualExceptMSB(ros.Start, sequence.Start);
            AssertEqualExceptMSB(ros.End, sequence.End);
            for (int i = 0; i <= count; i++)
            {
                if (i == 0 || i == count)
                {
                    AssertEqualExceptMSB(ros.GetPosition(i), sequence.GetPosition(i));
                }
                else
                {
                    Assert.Equal(ros.GetPosition(i), sequence.GetPosition(i));
                }

                var roSlice = ros.Slice(i, 0);
                var slice = sequence.Slice(i, 0);
                AssertEqualExceptMSB(roSlice.Start, slice.Start);
                AssertEqualExceptMSB(roSlice.End, slice.End);
            }

            // and get back again
            Assert.True(Sequence<int>.TryGetSequence(ros, out var andBackAgain));
            Assert.Equal(sequence, andBackAgain);

            // count/sum via list using ROS
            t = 0;
            c = 0;
            int roSpanCount = 0;
            foreach (var memory in ros)
            {
                if (!memory.IsEmpty) roSpanCount++;
                foreach (int item in memory.Span)
                {
                    c++;
                    t += item;
                }
            }
            Assert.Equal(total, t);
            Assert.Equal(count, c);
            Assert.Equal(spanCount, roSpanCount);

            void AssertEqualExceptMSB(SequencePosition x, SequencePosition y)
            {
                object xo = x.GetObject(), yo = y.GetObject();
                int xi = x.GetInteger(), yi = y.GetInteger();

                Assert.Equal(new SequencePosition(xo, xi & ~Sequence.MSB),
                    new SequencePosition(yo, yi & ~Sequence.MSB));
            }

            // slice everything
            t = 0;
            c = 0;
            ResetRandom();
            for (int i = 0; i < count; i++)
            {
                var pos = sequence.GetPosition(i);
                var slice = sequence.Slice(i, 0);

                Assert.True(slice.IsEmpty);
                AssertEqualExceptMSB(pos, slice.Start);
                AssertEqualExceptMSB(slice.Start, slice.End);

                slice = sequence.Slice(i);
                Assert.Equal(count - i, slice.Length);
                AssertEqualExceptMSB(pos, slice.Start);
                Assert.Equal(sequence.End, slice.End);

                slice = sequence.Slice(0, i);
                Assert.Equal(i, slice.Length);
                Assert.Equal(sequence.Start, slice.Start);
                AssertEqualExceptMSB(pos, slice.End);

                slice = sequence.Slice(i, 1);
                Assert.Equal(1, slice.Length);
                AssertEqualExceptMSB(pos, slice.Start);
                AssertEqualExceptMSB(sequence.GetPosition(i+1), slice.End);

                t += slice[0];
                c += (int)slice.Length; // 1
                Assert.Equal(GetNextRandom(), slice[0]);
            }
            Assert.Equal(count, c);
            Assert.Equal(total, t);
            

            Assert.Throws<ArgumentOutOfRangeException>(() => sequence.Slice(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => sequence.Slice(c, 1));

            var end = sequence.Slice(0, 0);
            Assert.True(end.IsEmpty);
            Assert.Equal(sequence.Start, end.Start);
            AssertEqualExceptMSB(sequence.Start, end.End);

            end = sequence.Slice(c, 0);
            Assert.True(end.IsEmpty);
            AssertEqualExceptMSB(sequence.End, end.Start);
            Assert.Equal(sequence.End, end.End);

        }
    }
}
