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

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(42)]
        [InlineData(1024)]
        public void CheckArray(int length)
        {
            Sequence<int> seq = new Sequence<int>(new int[length]);
            Assert.True(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, length);
        }


        [Fact]
        public void CheckDefaultMemory()
        {
            Memory<int> memory = default;
            Sequence<int> seq = new Sequence<int>(memory);
            Assert.True(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, 0);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(42)]
        [InlineData(1024)]
        public void CheckArrayBackedMemory(int length)
        {
            Memory<int> memory = new int[length];
            Sequence<int> seq = new Sequence<int>(memory);
            Assert.True(seq.IsArray);
            Assert.True(seq.IsSingleSegment);
            TestEveryWhichWay(seq, length);
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


        [Fact]
        public void CheckDefaultCustomManager()
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

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(42)]
        [InlineData(1024)]
        public void CheckArrayBackedCustomManager(int length)
        {
            Memory<int> memory = new int[length];
            using (var owner = new MyManager(memory))
            {
                Sequence<int> seq = new Sequence<int>(owner.Memory);
                Assert.False(seq.IsArray);
                Assert.True(seq.IsSingleSegment);
                TestEveryWhichWay(seq, length);
            }
        }


        unsafe class MyUnsafeManager : MemoryManager<int>
        {
            protected readonly int* _ptr;
            protected readonly int _length;
            public MyUnsafeManager(int* ptr, int length)
            {
                _ptr = ptr;
                _length = length;
            }
            public override Span<int> GetSpan() => new Span<int>(_ptr, _length);

            public override MemoryHandle Pin(int elementIndex = 0) => default;

            public override void Unpin() { }

            protected override void Dispose(bool disposing) { }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(42)]
        [InlineData(1024)]
        public unsafe void CheckUnsafeCustomManager(int length)
        {
            int* ptr = stackalloc int[length];
            using (var owner = new MyUnsafeManager(ptr, length))
            {
                Sequence<int> seq = new Sequence<int>(owner.Memory);
                Assert.False(seq.IsArray);
                Assert.False(seq.IsPinned);
                Assert.True(seq.IsSingleSegment);
                TestEveryWhichWay(seq, length);
            }
        }

        unsafe class MyUnsafePinnedManager : MyUnsafeManager, IPinnedMemoryOwner<int>
        {
            public MyUnsafePinnedManager(int* ptr, int length) : base(ptr, length) { }

            public void* Origin => _ptr;

            public int Length => _length;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(42)]
        [InlineData(1024)]
        public unsafe void CheckUnsafePinnedCustomManager(int length)
        {
            int* ptr = stackalloc int[length + 1]; // extra to ensure never nil
            using (var owner = new MyUnsafePinnedManager(ptr, length))
            {
                Sequence<int> seq = new Sequence<int>(owner.Memory);
                Assert.False(seq.IsArray);
                Assert.True(seq.IsPinned);
                Assert.True(seq.IsSingleSegment);
                TestEveryWhichWay(seq, length);
            }
        }

        unsafe class MySegment : SequenceSegment<int>, IPinnedMemoryOwner<int>
        {
            public void* Origin { get; }
            public MySegment(Memory<int> memory, MySegment previous = null) : base(memory, previous){}
            public MySegment(IMemoryOwner<int> owner, MySegment previous = null) : base(owner.Memory, previous)
            {
                if (owner is IPinnedMemoryOwner<int> pinned) Origin = pinned.Origin;
            }
        }

        [Fact]
        public void CheckDefaultSegments()
        {
            var first = new MySegment(memory: default);
            var seq = new Sequence<int>(first, first, 0, 0);
            Assert.False(seq.IsArray);
            Assert.False(seq.IsPinned);
            Assert.True(seq.IsSingleSegment);

            TestEveryWhichWay(seq, 0);
        }

        [Theory]
        [InlineData(new int[] { 0 }, true)]
        [InlineData(new int[] { 1 }, true)]
        [InlineData(new int[] { 2 }, true)]
        [InlineData(new int[] { 42 }, true)]
        [InlineData(new int[] { 1024 }, true)]
        // test roll forward
        [InlineData(new int[] { 0, 0 }, true)]
        [InlineData(new int[] { 0, 1 }, true)]
        [InlineData(new int[] { 0, 2 }, true)]
        [InlineData(new int[] { 0, 42 }, true)]
        [InlineData(new int[] { 0, 1024 }, true)]
        // test roll backward
        [InlineData(new int[] { 1, 0 }, true)]
        [InlineData(new int[] { 2, 0 }, true)]
        [InlineData(new int[] { 42, 0 }, true)]
        [InlineData(new int[] { 1024, 0 }, true)]
        // test non-trivial
        [InlineData(new int[] { 128, 128, 64 }, false)]
        [InlineData(new int[] { 128, 0, 64, 0, 12 }, false)] // zero length blocks in the middle
        [InlineData(new int[] { 0, 128, 0, 64, 0 }, false)] // zero length blocks at the ends
        [InlineData(new int[] { 0, 128, 0 }, true)]

        public void CheckArrayBackedSegments(int[] sizes, bool isSingleSegment)
        {
            Memory<int> Create(int size)
            {
                return new int[size];
            }
            int length = sizes.Sum();
            var first = new MySegment(Create(sizes[0]));
            var last = first;
            for(int i = 1; i < sizes.Length; i++)
            {
                last = new MySegment(Create(sizes[i]), last);
            }
            Sequence<int> seq = new Sequence<int>(first, last, 0, last.Length);
            Assert.False(seq.IsArray);
            Assert.False(seq.IsPinned);

            Assert.Equal(isSingleSegment, seq.IsSingleSegment);
            TestEveryWhichWay(seq, length);
        }

        [Theory(Skip = "odd things afoot")]
        [InlineData(new int[] { 0 }, true)]
        [InlineData(new int[] { 1 }, true)]
        [InlineData(new int[] { 2 }, true)]
        [InlineData(new int[] { 42 }, true)]
        [InlineData(new int[] { 1024 }, true)]
        // test roll forward
        [InlineData(new int[] { 0, 0 }, true)]
        [InlineData(new int[] { 0, 1 }, true)]
        [InlineData(new int[] { 0, 2 }, true)]
        [InlineData(new int[] { 0, 42 }, true)]
        [InlineData(new int[] { 0, 1024 }, true)]
        // test roll backward
        [InlineData(new int[] { 1, 0 }, true)]
        [InlineData(new int[] { 2, 0 }, true)]
        [InlineData(new int[] { 42, 0 }, true)]
        [InlineData(new int[] { 1024, 0 }, true)]
        // test non-trivial
        [InlineData(new int[] { 128, 128, 64 }, false)]
        [InlineData(new int[] { 128, 0, 64, 0, 12 }, false)] // zero length blocks in the middle
        [InlineData(new int[] { 0, 128, 0, 64, 0 }, false)] // zero length blocks at the ends
        [InlineData(new int[] { 0, 128, 0 }, true)]
        public unsafe void CheckUnsafeBackedSegments(int[] sizes, bool isSingleSegment)
        {
            int length = sizes.Sum();
            int* ptr = stackalloc int[length + 1]; // extra to ensure never nil

            IMemoryOwner<int> Create(int size)
            {
                var mem = new MyUnsafeManager(ptr, size);
                ptr += length;
                return mem;
            }

            var first = new MySegment(Create(sizes[0]));
            var last = first;
            for (int i = 1; i < sizes.Length; i++)
            {
                last = new MySegment(Create(sizes[i]), last);
            }
            Sequence<int> seq = new Sequence<int>(first, last, 0, last.Length);
            Assert.False(seq.IsArray);
            Assert.False(seq.IsPinned);

            Assert.Equal(isSingleSegment, seq.IsSingleSegment);
            TestEveryWhichWay(seq, length);

            Assert.False(true, "da fuk");
        }

        [Theory(Skip = "odd things afoot")]
        [InlineData(new int[] { 0 }, true)]
        [InlineData(new int[] { 1 }, true)]
        [InlineData(new int[] { 2 }, true)]
        [InlineData(new int[] { 42 }, true)]
        [InlineData(new int[] { 1024 }, true)]
        // test roll forward
        [InlineData(new int[] { 0, 0 }, true)]
        [InlineData(new int[] { 0, 1 }, true)]
        [InlineData(new int[] { 0, 2 }, true)]
        [InlineData(new int[] { 0, 42 }, true)]
        [InlineData(new int[] { 0, 1024 }, true)]
        // test roll backward
        [InlineData(new int[] { 1, 0 }, true)]
        [InlineData(new int[] { 2, 0 }, true)]
        [InlineData(new int[] { 42, 0 }, true)]
        [InlineData(new int[] { 1024, 0 }, true)]
        // test non-trivial
        [InlineData(new int[] { 128, 128, 64 }, false)]
        [InlineData(new int[] { 128, 0, 64, 0, 12 }, false)] // zero length blocks in the middle
        [InlineData(new int[] { 0, 128, 0, 64, 0 }, false)] // zero length blocks at the ends
        [InlineData(new int[] { 0, 128, 0 }, true)]
        public unsafe void CheckUnsafePinnedBackedSegments(int[] sizes, bool isSingleSegment)
        {
            int length = sizes.Sum();
            int* ptr = stackalloc int[length + 1]; // extra to ensure never nil

            IMemoryOwner<int> Create(int size)
            {
                var mem = new MyUnsafePinnedManager(ptr, size);
                ptr += length;
                return mem;
            }

            var first = new MySegment(Create(sizes[0]));
            var last = first;
            for (int i = 1; i < sizes.Length; i++)
            {
                last = new MySegment(Create(sizes[i]), last);
            }
            Sequence<int> seq = new Sequence<int>(first, last, 0, last.Length);
            Assert.False(seq.IsArray);
            Assert.True(seq.IsPinned);

            Assert.Equal(isSingleSegment, seq.IsSingleSegment);
            TestEveryWhichWay(seq, length);
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

            // get ROS; note: we won't attempt to compare ROS and S positions,
            // as positions are only meaningful inside the context in which they
            // are obtained - we can check the slice contents one at a time, though
            var ros = sequence.AsReadOnly();
            Assert.Equal(c, ros.Length);
            for (int i = 0; i <= count; i++)
            {
                var roSlice = ros.Slice(i, 0);
                var slice = sequence.Slice(i, 0);
                Assert.Equal(roSlice.Length, slice.Length);
            }
            for (int i = 0; i < count; i++)
            {
                var roSlice = ros.Slice(i, 1);
                var slice = sequence.Slice(i, 1);
                Assert.Equal(roSlice.Length, slice.Length);
                Assert.Equal(roSlice.First.Span[0], slice[0]);
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

            void AssertEqualExceptMSB(SequencePosition expected, SequencePosition actual)
            {
                object eo = expected.GetObject(), ao = actual.GetObject();
                int ei = expected.GetInteger() & ~Sequence.IsArrayFlag,
                    ai = actual.GetInteger() & ~Sequence.IsArrayFlag;

                Assert.Equal(ei , ai );
                Assert.Equal(eo, ao);
                
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
