using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    public readonly struct Allocation
    {
        private readonly int _offset, _length;
        private readonly object _block;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation<T> Cast<T>()
            => _block == null ? default : new Allocation<T>((Block<T>)_block, _offset, _length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Allocation(object block, int offset, int length)
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
            => new Allocation(allocation._block, allocation.Offset, allocation._length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Allocation<T>(Allocation allocation)
            => allocation.Cast<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySequence<T>(Allocation<T> allocation)
            => allocation.AsReadOnly();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation Untyped() => this;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySequence<T> AsReadOnly()
            => IsSingleSegment ? new ReadOnlySequence<T>(_block, Offset, _block, Offset + _length) : MultiSegmentAsReadOnly();

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

        public long Length => _length;
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
            get => _block == null ? default : _block.Memory.Slice(Offset, _length);
        }
        public Span<T> FirstSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _block == null ? default : _block.Memory.Span.Slice(Offset, _length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            if (IsSingleSegment) FirstSpan.CopyTo(destination);
            else SlowCopy(destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Memory<T> destination)
        {
            if (IsSingleSegment) FirstSegment.CopyTo(destination);
            else SlowCopy(destination.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<T> destination)
            => IsSingleSegment ? FirstSpan.TryCopyTo(destination) : TrySlowCopyTo(destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Memory<T> destination)
            => IsSingleSegment ? FirstSegment.TryCopyTo(destination) : TrySlowCopyTo(destination.Span);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SlowCopy(Span<T> destination)
        {
            if (!TrySlowCopyTo(destination)) ThrowLengthError();
            void ThrowLengthError()
            {
                Span<int> one = stackalloc int[1];
                one.CopyTo(default); // this should give use the CLR's error text (let's hope it doesn't mention sizes!)
            }
        }

        private bool TrySlowCopyTo(Span<T> destination)
        {
            if (destination.Length < _length) return false;

            throw new NotImplementedException();
        }

        internal Allocation(Block<T> block, int offset, int length)
        {
            _offsetAndMultiSegmentFlag = offset + block.Length < length ? (offset | MSB) : offset;
            _length = length;
            _block = block;
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

                var span = _nextBlock.Memory.Span;

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
