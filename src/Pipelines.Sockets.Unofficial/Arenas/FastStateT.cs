using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    internal struct FastState<T> // not actually a buffer as such - just prevents
    {                                   // us constantly having to slice etc
        public void Init(in SequencePosition position, long remaining)
        {
            if (position.GetObject() is ReadOnlySequenceSegment<T> segment
                && MemoryMarshal.TryGetArray(segment.Memory, out var array))
            {
                int offset = position.GetInteger();
                _array = array.Array;
                _offset = offset; // the net offset into the array
                _count = (int)Math.Min( // the smaller of (noting it will always be an int)
                        array.Count - offset, // the amount left in this buffer
                        remaining); // the logical amount left in the stream
            }
            else
            {
                this = default;
            }
        }

        public override string ToString() => $"{_count} remaining";

        T[] _array;
        int _count, _offset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(in T item)
        {
            if (_count != 0)
            {
                _array[_offset++] = item;
                _count--;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TryRead(Span<T> span)
        {
            var items = Math.Min(span.Length, _count);
            if (items != 0)
            {
                new Span<T>(_array, _offset, items).CopyTo(span);
                _count -= items;
                _offset += items;
            }
            return items;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWrite(ReadOnlySpan<T> span)
        {
            int items = span.Length;
            if (_count < items) return false;
            span.CopyTo(new Span<T>(_array, _offset, items));
            _count -= items;
            _offset += items;
            return true;
        }
    }
}
