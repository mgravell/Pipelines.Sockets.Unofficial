using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Represents a Sequence without needing to know the type at compile-time
    /// </summary>
    public readonly struct Sequence : IEquatable<Sequence>
    {
        private readonly int _offset, _length;
        private readonly object _obj;

        /// <summary>
        /// Returns an empty sequence of the supplied type
        /// </summary>
        public static Sequence Empty<T>() => new Sequence(Sequence<T>.EmptySentinel, 0, 0);

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is Sequence other && Equals(in other);

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IEquatable<Sequence>.Equals(Sequence other) => Equals(in other);

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in Sequence other)
            => _length == 0 ? other.Length == 0 // all empty sequences are equal - in part because default is type-less
                : (_length == other.Length & _offset == other._offset & _obj == other._obj);

        /// <summary>
        /// Used for equality operations
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
            => _length == 0 ? 0 : (_length * -_offset) ^ RuntimeHelpers.GetHashCode(_obj);

        /// <summary>
        /// Summarizes a sequence as a string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => $"{_length}×{ElementType.Name}";

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Sequence x, Sequence y) => x.Equals(in y);

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Sequence x, Sequence y) => !x.Equals(in y);

        /// <summary>
        /// Indicates the number of elements in the sequence
        /// </summary>
        public long Length => _length; // we currently only allow int, but technically we could support huge regions

        /// <summary>
        /// Indicates whether the sequence is empty (zero elements)
        /// </summary>
        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length == 0;
        }

        /// <summary>
        /// Indicates the type of element defined by the sequence
        /// </summary>
        public Type ElementType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_obj is ISegment segment) return segment.ElementType;
                return _obj == null ? typeof(void) : _obj.GetType().GetElementType();
                
            }
        }

        /// <summary>
        /// Converts an untyped sequence back to a typed sequence; the type must be correct
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T> Cast<T>()
            => _length == 0 && _obj == Sequence<T>.EmptySentinel
            ? default
            : new Sequence<T>(_offset, _length, (SequenceSegment<T>)_obj);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Sequence(object obj, int offset, int length)
        {
            _obj = obj;
            _offset = offset;
            _length = length;
        }
    }

    /// <summary>
    /// Represents a (possibly non-contiguous) region of memory; the read/write cousin or ReadOnlySequence-T
    /// </summary>
    public readonly struct Sequence<T> : IEquatable<Sequence<T>>
    {
        private readonly int _offsetAndMultiSegmentFlag, _length;
        private readonly SequenceSegment<T> _head;

        /// <summary>
        /// Represents a typed sequence as an untyped sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Sequence(Sequence<T> sequence)
            => sequence.Untyped();

        /// <summary>
        /// Converts an untyped sequence back to a typed sequence; the type must be correct
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Sequence<T>(Sequence sequence)
            => sequence.Cast<T>();

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is Sequence<T> other && Equals(in other);

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IEquatable<Sequence<T>>.Equals(Sequence<T> other) => Equals(in other);

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in Sequence<T> other)
            => _length == 0 ? other.Length == 0 // all empty sequences are equal - in part because default is type-less
                : (_length == other.Length & _offsetAndMultiSegmentFlag == other._offsetAndMultiSegmentFlag & _head == other._head);

        /// <summary>
        /// Used for equality operations
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
            => _length == 0 ? 0 : (_length * -_offsetAndMultiSegmentFlag) ^ RuntimeHelpers.GetHashCode(_head);

        /// <summary>
        /// Summaries a sequence as a string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => $"{_length}×{typeof(T).Name}";

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Sequence<T> x, Sequence<T> y) => x.Equals(in y);

        /// <summary>
        /// Tests two sequences for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Sequence<T> x, Sequence<T> y) => !x.Equals(in y);

        /// <summary>
        /// Converts a typed sequence to a typed read-only-sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySequence<T>(Sequence<T> sequence)
            => sequence.AsReadOnly();

        /// <summary>
        /// Get a reference to an element by index; note that this *can* have
        /// poor performance for multi-segment sequences, but it is usually satisfactory
        /// </summary>
        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var first = FirstSpan;
                if (index < first.Length) return ref first[index];
                return ref GetByIndex(index);
            }
        }

        private ref T GetByIndex(int index)
        {
            if (index < 0 | index >= _length) Throw.IndexOutOfRange();

            foreach(var span in Spans)
            {
                if (index < span.Length) return ref span[index];
                index -= span.Length;
            }
            Throw.IndexOutOfRange();
            return ref FirstSpan[0]; // shouldn't get here, just to make compiler happy
        }

        internal SequenceSegment<T> GetSegmentAndOffset(out int offset)
        {
            offset = Offset;
            return _head;
        }

        /// <summary>
        /// Converts a typed sequence to a typed read-only-sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Sequence<T> (ReadOnlySequence<T> readOnlySequence)
        {
            if (TryGetSequence(readOnlySequence, out var sequence)) return sequence;
            Throw.InvalidCast();
            return default; // to make compiler happy
        }

        /// <summary>
        /// Represents a typed sequence as an untyped sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence Untyped() => new Sequence((object)_head ?? EmptySentinel, Offset, _length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Sequence<TTo> DirectCast<TTo>() => new Sequence<TTo>(Offset, _length, (SequenceSegment<TTo>)(object)_head);

        // used to accurately identify a default instance (null _head) when
        // using untyped sequences
        internal static readonly object EmptySentinel = new object();

        /// <summary>
        /// Converts a typed sequence to a typed read-only-sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySequence<T> AsReadOnly()
            => _head == null ? default
            : IsSingleSegment ? new ReadOnlySequence<T>(_head, Offset, _head, Offset + _length) : MultiSegmentAsReadOnly();

        /// <summary>
        /// Calculate the start position of the current sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SequencePosition Start() => new SequencePosition(_head, Offset);

        /// <summary>
        /// Calculate the end position of the current sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SequencePosition End() => GetPosition(_length);

        /// <summary>
        /// Calculate a position inside the current sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SequencePosition GetPosition(long offset)
        {
            int segmentOffset;
            // if the position is well-defined inside the current page, we can do this cheaply
            if ((offset >= 0 & offset <= _length) && (segmentOffset = (int)offset + Offset) < _head.Length)
                return new SequencePosition(_head, segmentOffset);
            return SliceIntoLaterPage(checked((int)offset), 0).Start();
        }

        /// <summary>
        /// Obtains a sub-region of a sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T> Slice(int start)
        {
            // does the start fit into the first segment?
            int newStart;
            if ((_length != 0 & start >= 0 & start <= _length) && (newStart = Offset + start) < _head.Length)
                return new Sequence<T>(newStart, _length - start, _head);
            return SliceIntoLaterPage(start, _length - start);
        }

        /// <summary>
        /// Obtains a sub-region of a sequence
        /// </summary>
        public Sequence<T> Slice(int start, int length)
        {
            // does the start fit into the first segment and still well-defined?
            int newStart;
            if ((_length != 0 & start >= 0 & length >= 0 & (start + length <= _length)) && (newStart = Offset + start) < _head.Length)
                return new Sequence<T>(newStart, length, _head);
            return SliceIntoLaterPage(start, length);
        }

        /// <summary>
        /// Obtains a reference into the segment
        /// </summary>
        public Reference<T> GetReference(int offset)
        {
            int finalOffset;
            if ((_length != 0 & offset >= 0 & offset <= _length) && (finalOffset = Offset + offset) < _head.Length)
                return new Reference<T>(_head, finalOffset);
            return SliceIntoLaterPage(offset, 1).GetReference(0);
        }

        private Sequence<T> SliceIntoLaterPage(long start, int length)
        {
            if (start < 0 | start > _length) ThrowArgumentOutOfRange(nameof(start));
            if (length < 0 | start + length > _length) ThrowArgumentOutOfRange(nameof(length));

            if (_head == null)
            {
                if (length == 0) return this;
                ThrowArgumentOutOfRange(nameof(length));
            }

            // remove whatever is left from the current page
            // and move to the next
            int fromThisPage = _head.Length - Offset;
            start -= fromThisPage;
            var segment = _head.Next;
            Debug.Assert(segment != null, "expected a later page");

            // remove however-many entire pages we need
            // (note: we already asserted that it should fit!)
            while (start > segment.Length)
            {
                start -= segment.Length;
                segment = segment.Next;
                Debug.Assert(start <= 0 || segment != null, "expected a later page");
            }

            return new Sequence<T>((int)start, length, segment);

            void ThrowArgumentOutOfRange(string paramName) => Throw.ArgumentOutOfRange(paramName);
        }

        /// <summary>
        /// Attempts to convert a typed read-only-sequence back to a typed sequence; the sequence must have originated from a valid typed sequence
        /// </summary>
        public static bool TryGetSequence(in ReadOnlySequence<T> readOnlySequence, out Sequence<T> sequence)
        {
            SequencePosition start = readOnlySequence.Start;
            if (start.GetObject() is SequenceSegment<T> segment && readOnlySequence.End.GetObject() is SequenceSegment<T>)
            {
                sequence = new Sequence<T>(start.GetInteger(), checked((int)readOnlySequence.Length), segment);
                return true;
            }
            sequence = default;
            return readOnlySequence.IsEmpty; // empty sequences can be considered acceptable
        }

        private ReadOnlySequence<T> MultiSegmentAsReadOnly()
        {
            var start = _head;
            var startIndex = Offset;

            var current = start.Next;
            var remaining = _length - startIndex;
            while (current.Length < remaining)
            {
                remaining -= current.Length;
                current = current.Next;
            }
            return new ReadOnlySequence<T>(start, startIndex, current, remaining);
        }

        /// <summary>
        /// Indicates the number of elements in the sequence
        /// </summary>
        public long Length => _length; // we currently only allow int, but technically we could support huge regions

        /// <summary>
        /// Indicates whether the sequence involves multiple segments, vs whether all the data fits into the first segment
        /// </summary>
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

        /// <summary>
        /// Indicates whether the sequence is empty (zero elements)
        /// </summary>
        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length == 0;
        }

        /// <summary>
        /// Obtains the first segment, in terms of a memory
        /// </summary>
        public Memory<T> FirstSegment
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _head == null ? default :
                IsSingleSegment ? _head.Memory.Slice(_offsetAndMultiSegmentFlag, _length) : _head.Memory.Slice(Offset);
        }
        /// <summary>
        /// Obtains the first segment, in terms of a span
        /// </summary>
        public Span<T> FirstSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _head == null ? default :
                IsSingleSegment ? _head.Memory.Span.Slice(_offsetAndMultiSegmentFlag, _length) : _head.Memory.Span.Slice(Offset);
        }

        /// <summary>
        /// Copy the contents of the sequence into a contiguous region
        /// </summary>
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

        /// <summary>
        /// If possible, copy the contents of the sequence into a contiguous region
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<T> destination)
            => IsSingleSegment ? FirstSpan.TryCopyTo(destination) : TrySlowCopy(destination);

        private bool TrySlowCopy(Span<T> destination)
        {
            if (destination.Length < _length) return false;

            foreach (var span in Spans)
            {
                span.CopyTo(destination);
                destination = destination.Slice(span.Length);
            }
            return true;
        }

        /// <summary>
        /// Create a new sequence from a segment chain
        /// </summary>
        public Sequence(SequenceSegment<T> segment, int offset, int length)
        {
            // basica parameter check
            if (segment == null) Throw.ArgumentNull(nameof(segment));
            if (offset < 0) Throw.ArgumentOutOfRange(nameof(offset));
            if (length < 0) Throw.ArgumentOutOfRange(nameof(length));

            // check that length is valid for the complete chain
            long unaffountedFor = length + offset;
            var current = segment;
            while (unaffountedFor > 0 & current != null)
            {
                unaffountedFor -= current.Length;
                current = current.Next;
            }
            if (unaffountedFor > 0) Throw.ArgumentOutOfRange(nameof(length));

            // assign
            _head = segment;
            _offsetAndMultiSegmentFlag = ((offset + length) > segment.Length) ? (offset | MSB) : offset;
            _length = length;
        }

        // this is the TRUSTED ctor; full checks are not conducted
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Sequence(int offset, int length, SequenceSegment<T> segment)
        {
            Debug.Assert(segment != null, "segment should never be null");
            Debug.Assert(length >= 0, "length should not be negative");
            Debug.Assert(offset >= 0, "offset should not be negative");

            _head = segment;
            _offsetAndMultiSegmentFlag = ((offset + length) > segment.Length) ? (offset | MSB) : offset;
            _length = length;

        }

        /// <summary>
        /// Allows a sequence to be enumerated as spans
        /// </summary>
        public SpanEnumerable Spans => new SpanEnumerable(this);

        /// <summary>
        /// Allows a sequence to be enumerated as memory instances
        /// </summary>
        public MemoryEnumerable Segments => new MemoryEnumerable(this);

        /// <summary>
        /// Allows a sequence to be enumerated as spans
        /// </summary>
        public readonly ref struct SpanEnumerable
        {
            private readonly int _offset, _length;
            private readonly SequenceSegment<T> _segment;
            internal SpanEnumerable(in Sequence<T> sequence)
            {
                _offset = sequence.Offset;
                _length = sequence._length;
                _segment = sequence._head;
            }

            /// <summary>
            /// Allows a sequence to be enumerated as spans
            /// </summary>
            public SpanEnumerator GetEnumerator() => new SpanEnumerator(_segment, _offset, _length);
        }

        /// <summary>
        /// Allows a sequence to be enumerated as memory instances
        /// </summary>
        public readonly ref struct MemoryEnumerable
        {
            private readonly int _offset, _length;
            private readonly SequenceSegment<T> _segment;
            internal MemoryEnumerable(in Sequence<T> sequence)
            {
                _offset = sequence.Offset;
                _length = sequence._length;
                _segment = sequence._head;
            }

            /// <summary>
            /// Allows a sequence to be enumerated as memory instances
            /// </summary>
            public MemoryEnumerator GetEnumerator() => new MemoryEnumerator(_segment, _offset, _length);
        }

        /// <summary>
        /// Allows a sequence to be enumerated as values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new Enumerator(_head, Offset, _length);

        /// <summary>
        /// Allows a sequence to be enumerated as values
        /// </summary>
        public ref struct Enumerator
        {
            private int _remainingThisSpan, _offsetThisSpan, _remainingOtherSegments;
            private SequenceSegment<T> _nextSegment;
            private Span<T> _span;

            internal Enumerator(SequenceSegment<T> segment, int offset, int length)
            {
                var firstSpan = segment.Memory.Span;
                if (offset + length > firstSpan.Length)
                {
                    // multi-segment
                    _nextSegment = segment.Next;
                    _remainingThisSpan = firstSpan.Length - offset;
                    _span = firstSpan.Slice(offset, _remainingThisSpan);
                    _remainingOtherSegments = length - _remainingThisSpan;
                }
                else
                {
                    // single-segment
                    _nextSegment = null;
                    _remainingThisSpan = length;
                    _span = firstSpan.Slice(offset);
                    _remainingOtherSegments = 0;
                }
                _offsetThisSpan = -1;
            }

            /// <summary>
            /// Attempt to move the next value
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_remainingThisSpan == 0) return MoveNextSegment();
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
                return ref CurrentReference;
            }

            private bool MoveNextSegment()
            {
                if (_remainingOtherSegments == 0) return false;

                var span = _nextSegment.Memory.Span;
                _nextSegment = _nextSegment.Next;

                if (_remainingOtherSegments <= span.Length)
                {   // we're at the end
                    span = span.Slice(0, _remainingOtherSegments);
                    _remainingOtherSegments = 0;
                }
                else
                {
                    _remainingOtherSegments -= span.Length;
                }
                _span = span;
                _remainingThisSpan = span.Length - 1; // because we're consuming one
                _offsetThisSpan = 0;
                return true;
            }

            /// <summary>
            /// Obtain the current value
            /// </summary>
            public T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _span[_offsetThisSpan];
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set => _span[_offsetThisSpan] = value;
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

            /// <summary>
            /// Obtain a reference to the current value
            /// </summary>
            public ref T CurrentReference
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _span[_offsetThisSpan];
            }
        }

        /// <summary>
        /// Allows a sequence to be enumerated as spans
        /// </summary>
        public ref struct SpanEnumerator
        {
            private int _offset, _remaining;
            private SequenceSegment<T> _nextSegment;
            private Span<T> _current;

            internal SpanEnumerator(SequenceSegment<T> segment, int offset, int length)
            {
                _nextSegment = segment;
                _offset = offset;
                _remaining = length;
                _current = default;
            }

            /// <summary>
            /// Attempt to move the next segment
            /// </summary>
            public bool MoveNext()
            {
                if (_remaining == 0) return false;
                var span = _nextSegment.Memory.Span;
                _nextSegment = _nextSegment.Next;

                if (_remaining <= span.Length - _offset)
                {
                    // last segment; need to trim end
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
            public Span<T> Current => _current;
        }

        /// <summary>
        /// Allows a sequence to be enumerated as memory instances
        /// </summary>
        public struct MemoryEnumerator
        {
            private int _offset, _remaining;
            private SequenceSegment<T> _nextSegment;
            private Memory<T> _current;

            internal MemoryEnumerator(SequenceSegment<T> segment, int offset, int length)
            {
                _nextSegment = segment;
                _offset = offset;
                _remaining = length;
                _current = default;
            }

            /// <summary>
            /// Attempt to move the next segment
            /// </summary>
            public bool MoveNext()
            {
                if (_remaining == 0) return false;
                var memory = _nextSegment.Memory;
                _nextSegment = _nextSegment.Next;

                if (_remaining <= memory.Length - _offset)
                {
                    // last segment; need to trim end
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

            /// <summary>
            /// Obtain the current segment
            /// </summary>
            public Memory<T> Current => _current;

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
