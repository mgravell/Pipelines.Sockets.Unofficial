using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    public readonly struct Allocation<T>
    {
        private readonly long _totalLength;
        private readonly Memory<T>[] _rawArray;
        private readonly int _rawOffset, _rawCount;

        public long Length => _totalLength;
        public bool IsSingleSegment => _rawCount == 1;

        public bool IsEmpty => _totalLength == 0;

        public ReadOnlyMemory<T> First => _rawArray?[_rawOffset] ?? default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            switch (_rawCount)
            {
                case 0: return;
                case 1:
                    _rawArray[_rawOffset].Span.CopyTo(destination);
                    return;
                default:
                    SlowCopy(destination);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Memory<T> destination)
        {
            switch (_rawCount)
            {
                case 0: return;
                case 1:
                    _rawArray[_rawOffset].CopyTo(destination);
                    return;
                default:
                    SlowCopy(destination.Span);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<T> destination)
        {
            switch (_rawCount)
            {
                case 0: return true;
                case 1: return _rawArray[_rawOffset].Span.TryCopyTo(destination);
                default: return TrySlowCopy(destination);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Memory<T> destination)
        {
            switch (_rawCount)
            {
                case 0: return true;
                case 1: return  _rawArray[_rawOffset].TryCopyTo(destination);
                default: return TrySlowCopy(destination.Span);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SlowCopy(Span<T> destination)
        {
            if (!TrySlowCopy(destination)) ThrowLengthError();
            void ThrowLengthError()
            {
                Span<int> one = stackalloc int[1];
                one.CopyTo(default); // this should give use the CLR's error text (let's hope it doesn't mention sizes!)
            }
        }

        private bool TrySlowCopy(Span<T> destination)
        {
            if (destination.Length < _totalLength) return false;
            int end = _rawOffset + _rawCount;
            for(int i = _rawOffset; i < end; i++)
            {
                var from = _rawArray[i].Span;
                from.CopyTo(destination);
                destination = destination.Slice(0, from.Length);
            }
            return true;
        }

        internal Allocation(long totalLength, ArraySegment<Memory<T>> segment)
        {
            _totalLength = totalLength;
            _rawArray = segment.Array;
            _rawOffset = segment.Offset;
            _rawCount = segment.Count;
            Debug.Assert(_totalLength == segment.Sum(x => (long)x.Length), "length mismatch in allocation");
        }

        public Enumerator GetEnumerator() => new Enumerator(_rawArray, _rawOffset, _rawOffset + _rawCount);

        public ref struct Enumerator
        {
            private readonly Memory<T>[] _array;
            private readonly int _start, _end;
            private int _current;

            public Enumerator(Memory<T>[] array, int start, int end)
            {
                _array = array;
                _start = start;
                _end = end;
                _current = start - 1;
            }

            public bool MoveNext() => ++_current < _end;
            public Memory<T> Current => _array[_current];
            public void Reset() => _current = _start - 1;
        }
    }
}
