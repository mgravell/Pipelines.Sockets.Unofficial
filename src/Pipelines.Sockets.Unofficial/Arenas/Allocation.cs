using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    public readonly struct Allocation
    {
        private readonly int _offset, _length;
        private readonly IBlock _block;

        public Type ElementType => _block?.ElementType ?? typeof(void);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation<T> Cast<T>()
            => _length == 0 ? TypeCheckedDefault<T>() : new Allocation<T>((Block<T>)_block, _offset, _length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Allocation<T> TypeCheckedDefault<T>()
        {
            GC.KeepAlive((NilBlock<T>)_block); // null (default) or correct
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Allocation(IBlock block, int offset, int length)
        {
            _block = block;
            _offset = offset;
            _length = length;
        }
    }

    public readonly struct Allocation<T>
    {
        private readonly int _offsetAndMultiSegmentFlag, _length;
        private readonly Block<T> _block;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Allocation(Allocation<T> allocation)
            => allocation.Untyped();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Allocation<T>(Allocation allocation)
            => allocation.Cast<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySequence<T>(Allocation<T> allocation)
            => allocation.AsReadOnly();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation Untyped() => new Allocation(_block ?? NilBlock<T>.Default, Offset, _length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySequence<T> AsReadOnly()
            => IsEmpty ? default
            : IsSingleSegment ? new ReadOnlySequence<T>(_block, Offset, _block, Offset + _length) : MultiSegmentAsReadOnly();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation<T> Slice(int start)
        {
            // does the start fit into the first block?
            int newStart;
            if ((_length != 0 & start >= 0) && (newStart = Offset + start) < _block.Length)
                return new Allocation<T>(_block, newStart, _length - start);
            return SlowSlice(start, _length - start);
        }


        public Allocation<T> Slice(int start, int length)
        {
            // does the start fit into the first block and still well-defined?
            int newStart;
            if ((_length != 0 & start >= 0 & length >= 0 & (start + length <= _length)) && (newStart = Offset + start) < _block.Length)
                return new Allocation<T>(_block, newStart, length);
            return SlowSlice(start, _length - start);
        }

        private Allocation<T> SlowSlice(int start, int length)
        {
            if (start < 0 | start > _length) ThrowArgumentOutOfRange(nameof(start));
            if (length < 0 | start + length > _length) ThrowArgumentOutOfRange(nameof(length));

            // note that this can be a zero length range that preserves the block, for SequencePosition purposes
            return new Allocation<T>(_block, Offset + start, length);

            void ThrowArgumentOutOfRange(string paramName) => throw new ArgumentOutOfRangeException(paramName);
        }

        public static bool TryGetAllocation(ReadOnlySequence<T> sequence, out Allocation<T> allocation)
        {
            if (sequence.IsEmpty)
            {
                allocation = default;
                return true;
            }
            SequencePosition start = sequence.Start;
            if(start.GetObject() is Block<T> startBlock && sequence.End.GetObject() is Block<T>)
            {
                allocation = new Allocation<T>(startBlock, start.GetInteger(), checked((int)sequence.Length));
                return true;
            }
            allocation = default;
            return false;

        }

        private ReadOnlySequence<T> MultiSegmentAsReadOnly()
        {
            var start = _block;
            var startIndex = Offset;

            var current = start.Next;
            var remaining = _length - startIndex;
            while(current.Length < remaining)
            {
                remaining -= current.Length;
                current = current.Next;
            }
            return new ReadOnlySequence<T>(start, startIndex, current, remaining);
        }

        public long Length => _length; // we currently only allow int, but technically we could support huge blocks
        public bool IsSingleSegment
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_offsetAndMultiSegmentFlag & MSB) == 0;
        }

        private const int MSB = unchecked((int)(uint)0x80000000);

        private int Offset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _offsetAndMultiSegmentFlag & ~MSB;
        }

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length == 0;
        }

        public Memory<T> FirstSegment
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _block == null ? default :
                IsSingleSegment ? _block.Memory.Slice(_offsetAndMultiSegmentFlag, _length) : _block.Memory.Slice(Offset);
        }
        public Span<T> FirstSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _block == null ? default :
                IsSingleSegment ? _block.Memory.Span.Slice(_offsetAndMultiSegmentFlag, _length) : _block.Memory.Span.Slice(Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            if (IsSingleSegment) FirstSpan.CopyTo(destination);
            else if (!TrySlowCopy(destination)) ThrowLengthError();

            void ThrowLengthError()
            {
                Span<int> one = stackalloc int[1];
                one.CopyTo(default); // this should give use the CLR's error text (let's hope it doesn't mention sizes!)
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<T> destination)
            => IsSingleSegment ? FirstSpan.TryCopyTo(destination) : TrySlowCopy(destination);

        private bool TrySlowCopy(Span<T> destination)
        {
            if (destination.Length < _length) return false;

            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Allocation(Block<T> block, int offset, int length)
        {
            Debug.Assert(block != null, "block should never be null");
            Debug.Assert(length >= 0, "block should not be negative");
            Debug.Assert(offset >= 0, "offset should not be negative");

            _block = block;
            _offsetAndMultiSegmentFlag = ((offset + length) > block.Length) ? (offset | MSB) : offset;
            _length = length;
            
        }

        public SpanEnumerable Spans => new SpanEnumerable(this);
        public MemoryEnumerable Segments => new MemoryEnumerable(this);

        public SpanEnumerator GetEnumerator() => new SpanEnumerator(_block, Offset, _length);

        public readonly ref struct SpanEnumerable
        {
            private readonly int _offset, _length;
            private readonly Block<T> _block;
            public SpanEnumerable(in Allocation<T> allocation)
            {
                _offset = allocation.Offset;
                _length = allocation._length;
                _block = allocation._block;
            }
            public SpanEnumerator GetEnumerator() => new SpanEnumerator(_block, _offset, _length);
        }

        public readonly ref struct MemoryEnumerable
        {
            private readonly int _offset, _length;
            private readonly Block<T> _block;
            public MemoryEnumerable(in Allocation<T> allocation)
            {
                _offset = allocation.Offset;
                _length = allocation._length;
                _block = allocation._block;
            }
            public MemoryEnumerator GetEnumerator() => new MemoryEnumerator(_block, _offset, _length);
        }

        public ref struct SpanEnumerator
        {
            private int _offset, _remaining;
            private Block<T> _nextBlock;
            private Span<T> _current;

            internal SpanEnumerator(Block<T> block, int offset, int length)
            {
                _nextBlock = block;
                _offset = offset;
                _remaining = length;
                _current = default;
            }

            public bool MoveNext()
            {
                if (_remaining == 0) return false;
                var block = _nextBlock;
                _nextBlock = block.Next;

                var span = block.Memory.Span;

                if (_remaining <= span.Length - _offset)
                {
                    // last block; need to trim end
                    span = span.Slice(_offset, _remaining);
                }
                else if (_offset != 0)
                {
                    // has offset (first only)
                    span = span.Slice(_offset);
                    _offset = 0;
                }
                // otherwise we can take the entire thing
                _remaining -= span.Length;
                _current = span;
                return true;
            }

            public Span<T> Current => _current;
        }

        public struct MemoryEnumerator
        {
            private int _offset, _remaining;
            private Block<T> _nextBlock;
            private Memory<T> _current;

            internal MemoryEnumerator(Block<T> block, int offset, int length)
            {
                _nextBlock = block;
                _offset = offset;
                _remaining = length;
                _current = default;
            }

            public bool MoveNext()
            {
                if (_remaining == 0) return false;
                var block = _nextBlock;
                _nextBlock = block.Next;

                var memory = _nextBlock.Memory;

                if (_remaining <= memory.Length - _offset)
                {
                    // last block; need to trim end
                    memory = memory.Slice(_offset, _remaining);
                }
                else if (_offset != 0)
                {
                    // has offset (first only)
                    memory = memory.Slice(_offset);
                    _offset = 0;
                }
                // otherwise we can take the entire thing
                _remaining -= memory.Length;
                _current = memory;
                return true;
            }

            public Memory<T> Current => _current;
        }
    }
}
