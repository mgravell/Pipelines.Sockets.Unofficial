using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    public partial struct Sequence<T>
    {
        /// <summary>
        /// Allows a sequence to be enumerated as spans
        /// </summary>
        public SpanEnumerable Spans => new(this);

        /// <summary>
        /// Allows a sequence to be enumerated as memory instances
        /// </summary>
        public MemoryEnumerable Segments => new(this);

        /// <summary>
        /// Allows a sequence to be enumerated as spans
        /// </summary>
        public readonly ref struct SpanEnumerable
        {
            private readonly Sequence<T> _sequence;
            internal SpanEnumerable(scoped in Sequence<T> sequence) => _sequence = sequence; // flat copy

            /// <summary>
            /// Allows a sequence to be enumerated as spans
            /// </summary>
            public SpanEnumerator GetEnumerator() => new(in _sequence);
        }

        /// <summary>
        /// Allows a sequence to be enumerated as memory instances
        /// </summary>
        public readonly ref struct MemoryEnumerable
        {
            private readonly Sequence<T> _sequence;
            internal MemoryEnumerable(scoped in Sequence<T> sequence) => _sequence = sequence; // flat copy

            /// <summary>
            /// Allows a sequence to be enumerated as memory instances
            /// </summary>
            public MemoryEnumerator GetEnumerator() => new(in _sequence);
        }

        /// <summary>
        /// Allows a sequence to be enumerated as values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(in this);

        internal IEnumerator<T> GetObjectEnumerator()
        {
            if (IsSingleSegment)
            {
                int startOffset = StartOffset, len = SingleSegmentLength;
                if (len == 0) return EmptyEnumerator.Default;

                if (IsArray) return GetArrayEnumerator((T[])_startObj, startOffset, startOffset + len);
                unsafe
                {
                    void* origin;
                    if (_startObj is IPinnedMemoryOwner<T> pinned
                        && (origin = pinned.Origin) is not null)
                    {
                        return new PointerBasedEnumerator(origin, startOffset, len);
                    }
                }
                return GetSingleSegmentEnumerator(FirstSegment);
            }
            return GetMultiSegmentEnumerator();
        }

        private static IEnumerator<T> GetArrayEnumerator(T[] array, int start, int end)
        {
            for (int i = start; i < end; i++)
                yield return array[i];
        }

        private static IEnumerator<T> GetSingleSegmentEnumerator(Memory<T> memory)
        {
            var len = memory.Length;
            for (int i = 0; i < len; i++)
                yield return memory.Span[i];
        }

        private IEnumerator<T> GetMultiSegmentEnumerator()
        {   // we *could* optimize this a bit, but it really doesn't seem worthwhile
            foreach (var segment in Segments)
            {
                int len = segment.Length;
                for (int i = 0; i < len; i++)
                    yield return segment.Span[i];
            }
        }

        private sealed class EmptyEnumerator : IEnumerator<T>
        {
            public static readonly IEnumerator<T> Default = new EmptyEnumerator();
            private EmptyEnumerator() { }
            bool IEnumerator.MoveNext() => false;
            T IEnumerator<T>.Current => default;
            object IEnumerator.Current => default;
            void IDisposable.Dispose() { }
            void IEnumerator.Reset() { }
        }

        private sealed unsafe class PointerBasedEnumerator : IEnumerator<T>
        {
            private void* _ptr;
            private int _remaining;

            public PointerBasedEnumerator(void* origin, int offset, int length)
            {
                _ptr = Unsafe.Add<T>(origin, offset);
                _remaining = length;
            }

            public T Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose() { }

            public bool MoveNext()
            {
                // if exhausted, give up
                if (_remaining <= 0) return false;

                // otherwise, we'll de-reference a value, increment the pointer, and indicate success
                Current = Unsafe.AsRef<T>(_ptr);
                _ptr = Unsafe.Add<T>(_ptr, 1);
                _remaining--;
                return true;
            }

            public void Reset() => Throw.NotSupported();
        }

        /// <summary>
        /// Allows a sequence to be enumerated as values
        /// </summary>
        public ref struct Enumerator
        {
            private int _remainingThisSpan, _offsetThisSpan;
            private long _remainingOtherSegments;
            private SequenceSegment<T> _nextSegment;
            private Span<T> _span;

            internal Enumerator(scoped in Sequence<T> sequence)
            {
                _span = sequence.FirstSpan;
                _remainingThisSpan = _span.Length;
                _offsetThisSpan = -1;
                if (sequence.IsSingleSegment)
                {
                    _remainingOtherSegments = 0;
                    _nextSegment = null;
                }
                else
                {
                    _nextSegment = ((SequenceSegment<T>)sequence._startObj).Next;
                    _remainingOtherSegments = sequence.Length - _span.Length;
                }
                _offsetThisSpan = -1;
            }

            /// <summary>
            /// Attempt to move the next value
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_remainingThisSpan == 0) return MoveNextNonEmptySegment();
                _offsetThisSpan++;
                _remainingThisSpan--;
                return true;
            }

            /// <summary>
            /// Progresses the iterator, asserting that space is available, returning a reference to the next value
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref T GetNext()
            {
                if (!MoveNext()) Throw.EnumeratorOutOfRange();
                return ref Current;
            }

            private bool MoveNextNonEmptySegment()
            {
                Span<T> span;
                do
                {
                    if (_remainingOtherSegments == 0) return false;

                    span = _nextSegment.Memory.Span;
                    _nextSegment = _nextSegment.Next;

                    if (_remainingOtherSegments <= span.Length)
                    {   // we're at the end
                        span = span.Slice(0, (int)_remainingOtherSegments);
                        _remainingOtherSegments = 0;
                    }
                    else
                    {
                        _remainingOtherSegments -= span.Length;
                    }

                } while (span.IsEmpty); // check for empty segment

                _span = span;
                _remainingThisSpan = span.Length - 1; // because we're consuming one
                _offsetThisSpan = 0;
                return true;
            }

            /// <summary>
            /// Obtain a reference to the current value
            /// </summary>
            public ref T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _span[_offsetThisSpan];
                /*

                Note: the choice of using the indexer here was compared against:

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    get => Unsafe.Add(ref MemoryMarshal.GetReference(_span), _offsetThisSpan);

                with the results as below; ref-add is *marginally* faster on netcoreapp2.1,
                but the indexer is *significantly* faster everywhere else, so; let's assume
                that the indexer is a more reasonable default. Note that in all cases it is
                significantly faster (double) compared to ArraySegment<int> via ArrayPool<int>

                |              Method | Runtime |     Toolchain |   Categories |        Mean |
                |-------------------- |-------- |-------------- |------------- |------------:|
                |  Arena<int>.Indexer |     Clr |        net472 | read/foreach |   105.06 us |
                |   Arena<int>.RefAdd |     Clr |        net472 | read/foreach |   131.81 us |
                |  Arena<int>.Indexer |    Core | netcoreapp2.0 | read/foreach |   105.13 us |
                |   Arena<int>.RefAdd |    Core | netcoreapp2.0 | read/foreach |   142.11 us |
                |  Arena<int>.Indexer |    Core | netcoreapp2.1 | read/foreach |    95.80 us |
                |   Arena<int>.RefAdd |    Core | netcoreapp2.1 | read/foreach |    92.80 us |
                                                (for context only)
                |      ArrayPool<int> |     Clr |        net472 | read/foreach |   258.75 us |
                |             'int[]' |     Clr |        net472 | read/foreach |    22.92 us |
                |      ArrayPool<int> |    Core | netcoreapp2.0 | read/foreach |   154.89 us |
                |             'int[]' |    Core | netcoreapp2.0 | read/foreach |    23.58 us |
                |      ArrayPool<int> |    Core | netcoreapp2.1 | read/foreach |   172.11 us |
                |             'int[]' |    Core | netcoreapp2.1 | read/foreach |    23.42 us |
                 */
            }
        }

        /// <summary>
        /// Allows a sequence to be enumerated as spans
        /// </summary>
        public ref struct SpanEnumerator
        {
            private int _offset;
            private long _remaining;
            private object _next;

            internal SpanEnumerator(scoped in Sequence<T> sequence)
            {
                _next = sequence._startObj;
                _offset = sequence.StartOffset;
                _remaining = sequence.Length;
                Current = default;
            }

            /// <summary>
            /// Attempt to move the next segment
            /// </summary>
            public bool MoveNext()
            {
                if (_remaining == 0) return false;

                Span<T> span;
                if (_next is T[] arr)
                {
                    span = new Span<T>(arr, _offset, (int)_remaining);
                    _next = null;
                }
                else if (_next is SequenceSegment<T> segment)
                {
                    span = segment.Memory.Span;
                    _next = segment.Next;

                    // need to figure out whether this is the first, the last, or one in the middle
                    if (_offset == 0)
                    {
                        if (_remaining < span.Length)
                        {   // need to trim the end
                            span = span.Slice(0, (int)_remaining);
                        }
                    }
                    else
                    {
                        // first slice
                        var usableThisSpan = span.Length - _offset;
                        if (_remaining < usableThisSpan)
                        {   // need to trim both ends
                            span = span.Slice(_offset, (int)_remaining);
                        }
                        else
                        {   // need to trim the start
                            span = span.Slice(_offset);
                        }
                        _offset = 0; // subsequent spans always start at 0
                    }
                }
                else if (_next is IPinnedMemoryOwner<T> pinned)
                {
                    span = pinned.GetSpan().Slice(_offset, (int)_remaining);
                    _next = null;
                }
                else
                {
                    span = ((IMemoryOwner<T>)_next).Memory.Span.Slice(_offset, (int)_remaining);
                    _next = null;
                }

                _remaining -= span.Length;
                Current = span;
                return true;
            }

            /// <summary>
            /// Asserts that another span is available, and returns then next span
            /// </summary>
            public Span<T> GetNext()
            {
                if (!MoveNext()) Throw.EnumeratorOutOfRange();
                return Current;
            }

            /// <summary>
            /// Obtain the current segment
            /// </summary>
            public Span<T> Current { get; private set; }
        }

        /// <summary>
        /// Allows a sequence to be enumerated as memory instances
        /// </summary>
        public struct MemoryEnumerator
        {
            private int _offset;
            private long _remaining;
            private object _next;

            internal MemoryEnumerator(in Sequence<T> sequence)
            {
                _next = sequence._startObj;
                _offset = sequence.StartOffset;
                _remaining = sequence.Length;
                Current = default;
            }

            /// <summary>
            /// Attempt to move the next segment
            /// </summary>
            public bool MoveNext()
            {
                if (_remaining == 0) return false;

                Memory<T> memory;
                if (_next is T[] arr)
                {
                    memory = new Memory<T>(arr, _offset, (int)_remaining);
                    _next = null;
                }
                else if (_next is SequenceSegment<T> segment)
                {
                    memory = segment.Memory;
                    _next = segment.Next;

                    // need to figure out whether this is the first, the last, or one in the middle
                    if (_offset == 0)
                    {
                        if (_remaining < memory.Length)
                        {   // need to trim the end
                            memory = memory.Slice(0, (int)_remaining);
                        }
                    }
                    else
                    {
                        // first slice
                        var usableThisSpan = memory.Length - _offset;
                        if (_remaining < usableThisSpan)
                        {   // need to trim both ends
                            memory = memory.Slice(_offset, (int)_remaining);
                        }
                        else
                        {   // need to trim the start
                            memory = memory.Slice(_offset);
                        }
                        _offset = 0; // subsequent spans always start at 0
                    }
                }
                else
                {
                    memory = ((IMemoryOwner<T>)_next).Memory.Slice(_offset, (int)_remaining);
                    _next = null;
                }

                _remaining -= memory.Length;
                Current = memory;
                return true;
            }

            /// <summary>
            /// Obtain the current segment
            /// </summary>
            public Memory<T> Current { get; private set; }

            /// <summary>
            /// Asserts that another span is available, and returns then next span
            /// </summary>
            public Memory<T> GetNext()
            {
                if (!MoveNext()) Throw.EnumeratorOutOfRange();
                return Current;
            }
        }
    }
}
