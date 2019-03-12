using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Similar to Func, but with "in" parameters
    /// </summary>
    public delegate TResult Projection<T, out TResult>(in T x);
    /// <summary>
    /// Similar to Func, but with "in" parameters
    /// </summary>
    public delegate TResult Projection<T1, T2, out TResult>(in T1 x1, in T2 x2);

    /// <summary>
    /// Provides utility methods for working with sequences
    /// </summary>
    public static class SequenceExtensions
    {
        /// <summary>
        /// Create an array with the contents of the sequence; if possible, an existing
        /// wrapped array may be reused
        /// </summary>
        public static T[] ToArray<T>(this in Sequence<T> source)
        {
            if (source.IsEmpty) return Array.Empty<T>();
            if (source.IsSingleSegment)
            {
                if (source.TryGetArray(out var segment)
                    && segment.Offset == 0 && segment.Array != null && segment.Count == segment.Array.Length)
                {
                    return segment.Array; // the source was wrapping an array *exactly*
                }
            }
            var arr = new T[source.Length];
            source.CopyTo(arr);
            return arr;
        }

        /// <summary>
        /// Obtain a Sequence from an enumerable; this may reuse existing sequence-compatible data if possible
        /// </summary>
        public static Sequence<T> ToSequence<T>(this IEnumerable<T> source)
            => SequenceBuilder<T>.IsTrivial(source, out var seq)
            ? seq : SlowToSequence<T>(source);

        private static Sequence<T> SlowToSequence<T>(IEnumerable<T> source)
        {
            var builder = new SequenceBuilder<T>();
            using (var iter = source.GetEnumerator())
            {
                if (!iter.MoveNext()) return default;

                builder.PrepareSpace(source);
                do
                {
                    builder.Add(iter.Current);
                } while (iter.MoveNext());
                builder.Complete();
            }
            return builder.Result;
        }

        // TODO: add IAsyncEnumerable<T> support

        internal ref struct SequenceBuilder<T>
        {
            internal SequenceBuilder<T> Create() => default;
            private int _offset;
            private void Resize(int minimumCount)
            {
                var newArr = ArrayPool<T>.Shared.Rent(minimumCount);
                if(!Result.IsEmpty)
                {
                    Result.CopyTo(newArr);
                    Recycle();
                }
                Result = new Sequence<T>(newArr);
            }
            private void Recycle()
            {
                if (!Result.IsEmpty && Result.TryGetArray(out var segment))
                {
                    ArrayPool<T>.Shared.Return(segment.Array, clearArray: !PerTypeHelpers<T>.IsBlittable);
                }
            }
            internal void PrepareSpace(IEnumerable<T> source)
            {
                int assumedCount = 16;
                if (source is ICollection<T> collection)
                {
                    assumedCount = collection.Count;
                }
                Resize(assumedCount);
            }
            internal static bool IsTrivial(IEnumerable<T> source, out Sequence<T> sequence)
            {
                if (source == null) Throw.ArgumentNull(nameof(source));
                if (source is SequenceList<T> list)
                {
                    sequence = list.ToSequence();
                    return true;
                }
                if (source is T[] arr)
                {
                    sequence = new Sequence<T>(arr);
                    return true;
                }
                if (source is ArraySegment<T> segment)
                {
                    sequence = new Sequence<T>(segment.Array, segment.Count, segment.Count);
                    return true;
                }
                if (source is Sequence<T> seq)
                {
                    sequence = seq;
                    return true;
                }
                sequence = default;
                return false;
            }

            internal void Add(in T current)
            {
                if(_offset == Result.Length)
                {
                    Resize(_offset * 2);
                }
                Result[_offset++] = current;
            }

            internal void Complete()
            {
                if (_offset < Result.Length)
                    Result = Result.Slice(0, _offset);
            }

            internal Sequence<T> Result { get; private set; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Span<T> GetSpan<T>(this IPinnedMemoryOwner<T> pinned)
            => new Span<T>(pinned.Origin, pinned.Length);

        /// <summary>
        /// Create a list-like object that provides access to the sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SequenceList<T> ToList<T>(this in Sequence<T> source)
            => SequenceList<T>.Create(source);

        /// <summary>
        /// Create an array with the contents of the sequence, applying a projection
        /// </summary>
        public static TTo[] ToArray<TFrom, TTo>(this in Sequence<TFrom> source, Projection<TFrom, TTo> projection)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection);
            return arr;
        }

        /// <summary>
        /// Create an array with the contents of the sequence, applying a projection
        /// </summary>
        public static TTo[] ToArray<TFrom, TState, TTo>(this in Sequence<TFrom> source, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection, in state);
            return arr;
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this in Sequence<TFrom> source,
#pragma warning disable RCS1231
            Span<TTo> destination,
#pragma warning restore RCS1231
            Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(in source, destination, projection))
                Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this in Sequence<TFrom> source,
#pragma warning disable RCS1231
            Span<TTo> destination,
#pragma warning restore RCS1231
            Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(in source, destination, projection, in state))
                Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from a span to a sequence
        /// </summary>

        public static void CopyTo<T>(
#pragma warning disable RCS1231
            this ReadOnlySpan<T> source,
#pragma warning restore RCS1231
             in Sequence<T> destination)
        {
            if (!TryCopyTo<T>(source, in destination))
                Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(
#pragma warning disable RCS1231
            this Span<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, in destination, projection))
                Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(
#pragma warning disable RCS1231
            this ReadOnlySpan<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, in destination, projection, in state))
                Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(
#pragma warning disable RCS1231
            this ReadOnlySpan<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, in destination, projection))
                Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(
#pragma warning disable RCS1231
            this Span<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, in destination, projection, in state))
                Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(this in Sequence<TFrom> source,
#pragma warning disable RCS1231
            Span<TTo> destination,
#pragma warning restore RCS1231
            Projection<TFrom, TTo> projection)
        {
            void ThrowNoProjection() => Throw.ArgumentNull(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (source.IsSingleSegment)
            {
                var span = source.FirstSegment.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    destination[i] = projection(in span[i]);
                }
            }
            else
            {
                int offset = 0;
                foreach(var span in source.Spans)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        destination[offset++] = projection(in span[i]);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TState, TTo>(this in Sequence<TFrom> source,
#pragma warning disable RCS1231
                Span<TTo> destination,
#pragma warning restore RCS1231
                Projection<TFrom, TState, TTo> projection, in TState state)
        {
            void ThrowNoProjection() => Throw.ArgumentNull(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (source.IsSingleSegment)
            {
                var span = source.FirstSegment.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    destination[i] = projection(in span[i], in state);
                }
            }
            else
            {
                int offset = 0;
                foreach (var span in source.Spans)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        destination[offset++] = projection(in span[i], in state);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a span to a sequence
        /// </summary>
        public static bool TryCopyTo<T>(this ReadOnlySpan<T> source,
            in Sequence<T> destination)
        {
            if (source.Length > destination.Length) return false;

            if (destination.IsSingleSegment)
            {
                source.CopyTo(destination.FirstSpan);
            }
            else
            {
                var iter = destination.Spans.GetEnumerator();
                while(!source.IsEmpty)
                {
                    var span = iter.GetNext();
                    source.Slice(0, span.Length).CopyTo(span);
                    source = source.Slice(span.Length);
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(
#pragma warning disable RCS1231
            this Span<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            void ThrowNoProjection() => Throw.ArgumentNull(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (destination.IsSingleSegment)
            {
                var span = destination.FirstSegment.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] = projection(in source[i]);
                }
            }
            else
            {
                int offset = 0;
                foreach (var span in destination.Spans)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        span[i] = projection(in source[offset++]);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TState, TTo>(
#pragma warning disable RCS1231
            this Span<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            void ThrowNoProjection() => Throw.ArgumentNull(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (destination.IsSingleSegment)
            {
                var span = destination.FirstSegment.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] = projection(in source[i], in state);
                }
            }
            else
            {
                int offset = 0;
                foreach (var span in destination.Spans)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        span[i] = projection(in source[offset++], in state);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(
#pragma warning disable RCS1231
            this ReadOnlySpan<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            void ThrowNoProjection() => Throw.ArgumentNull(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (destination.IsSingleSegment)
            {
                var span = destination.FirstSegment.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] = projection(in source[i]);
                }
            }
            else
            {
                int offset = 0;
                foreach (var span in destination.Spans)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        span[i] = projection(in source[offset++]);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a span to a sequence, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TState, TTo>(
#pragma warning disable RCS1231
            this ReadOnlySpan<TFrom> source,
#pragma warning restore RCS1231
            in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            void ThrowNoProjection() => Throw.ArgumentNull(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (destination.IsSingleSegment)
            {
                var span = destination.FirstSegment.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] = projection(in source[i], in state);
                }
            }
            else
            {
                int offset = 0;
                foreach (var span in destination.Spans)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        span[i] = projection(in source[offset++], in state);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a sequence to a newly allocated sequence, applying a projection
        /// </summary>
        public static Sequence<TTo> Allocate<TFrom, TTo>(this Arena<TTo> arena, in Sequence<TFrom> source, Projection<TFrom, TTo> projection)
        {
            if (source.IsEmpty) return arena.Allocate(0); // retains position etc

            var block = arena.Allocate(checked((int)source.Length));
            source.CopyTo(block, projection);
            return block;
        }

        /// <summary>
        /// Copy the data from a sequence to a newly allocated sequence, applying a projection
        /// </summary>
        public static Sequence<TTo> Allocate<TFrom, TState, TTo>(this Arena<TTo> arena, in Sequence<TFrom> source,
            Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (source.IsEmpty) return arena.Allocate(0); // retains position etc

            var block = arena.Allocate(checked((int)source.Length));
            source.CopyTo(block, projection, state);
            return block;
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static void CopyTo<T>(this in Sequence<T> source, in Sequence<T> destination)
        {
            if (!TryCopyTo<T>(source, destination)) Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static bool TryCopyTo<T>(this in Sequence<T> source, in Sequence<T> destination)
        {
            if (source.Length > destination.Length) return false;
#pragma warning disable RCS1233 // Use short-circuiting operator.
            if (source.IsSingleSegment & destination.IsSingleSegment) return source.FirstSpan.TryCopyTo(destination.FirstSpan);
#pragma warning restore RCS1233 // Use short-circuiting operator.
            SlowCopyTo<T>(source, destination);
            return true; // we checked the lengths first
        }

        private static void SlowCopyTo<T>(in Sequence<T> source, in Sequence<T> destination)
        {
            var from = source.GetEnumerator();
            var to = destination.GetEnumerator();
            while (from.MoveNext())
            {
                to.Current = from.Current;
            }
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static void CopyTo<T>(this in ReadOnlySequence<T> source, in Sequence<T> destination)
        {
            if (!TryCopyTo<T>(source, destination)) Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static bool TryCopyTo<T>(this in ReadOnlySequence<T> source, in Sequence<T> destination)
        {
            if (source.Length > destination.Length) return false;
#pragma warning disable RCS1233 // Use short-circuiting operator.
            if (source.IsSingleSegment & destination.IsSingleSegment) return source.First.Span.TryCopyTo(destination.FirstSpan);
#pragma warning restore RCS1233 // Use short-circuiting operator.
            SlowCopyTo<T>(source, destination);
            return true; // we checked the lengths first
        }

        private static void SlowCopyTo<T>(in ReadOnlySequence<T> source, in Sequence<T> destination)
        {
            var from = source.GetEnumerator();
            var to = destination.GetEnumerator();
            while (from.MoveNext())
            {
                var span = from.Current.Span;
                for(int i = 0; i < span.Length;i++)
                {
                    to.GetNext() = span[i];
                }
            }
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this in Sequence<TFrom> source, in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, destination, projection)) Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(this in Sequence<TFrom> source, in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (source.Length > destination.Length) return false;
            if (source.IsSingleSegment) return source.FirstSpan.TryCopyTo(in destination, projection);
            if (destination.IsSingleSegment) return source.TryCopyTo(destination.FirstSpan, projection);
            SlowCopyTo<TFrom, TTo>(source, destination, projection);
            return true; // we checked the lengths first
        }

        private static void SlowCopyTo<TFrom, TTo>(in Sequence<TFrom> source, in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            var from = source.GetEnumerator();
            var to = destination.GetEnumerator();
            while(from.MoveNext())
            {
                to.GetNext() = projection(from.Current);
            }
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this in Sequence<TFrom> source, in Sequence<TTo> destination,
            Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, destination, projection, in state)) Throw.InvalidOperation();
        }

        /// <summary>
        /// Copy the data from between two sequences, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TState, TTo>(this in Sequence<TFrom> source, in Sequence<TTo> destination,
            Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (source.Length > destination.Length) return false;
            if (source.IsSingleSegment) return source.FirstSpan.TryCopyTo(in destination, projection, in state);
            if (destination.IsSingleSegment) return source.TryCopyTo(destination.FirstSpan, projection, in state);
            SlowCopyTo<TFrom, TState, TTo>(source, destination, projection, in state);
            return true; // we checked the lengths first
        }

        private static void SlowCopyTo<TFrom, TState, TTo>(in Sequence<TFrom> source, in Sequence<TTo> destination,
            Projection<TFrom, TState, TTo> projection, in TState state)
        {
            var from = source.GetEnumerator();
            var to = destination.GetEnumerator();
            while (from.MoveNext())
            {
                to.GetNext() = projection(from.Current, in state);
            }
        }

        /// <summary>
        /// Attempt to calculate the net offset of a position
        /// </summary>
        internal static long? TryGetOffset(this in SequencePosition position)
        {
            var obj = position.GetObject();
            var offset = position.GetInteger();
            if (obj == null) return offset;
            if (obj is ISegment segment) return segment.RunningIndex + offset;
            return null; // nope!
        }

        /// <summary>
        /// Attempt to calculate the net offset of a position
        /// </summary>
        internal static string TryGetSummary(this in SequencePosition position)
        {
            var obj = position.GetObject();
            var offset = position.GetInteger();
            if (obj == null && offset != 0) return $"offset: {offset}";
            if (obj is Array arr) return $"{arr.GetType().GetElementType().Name}[]; offset: {offset}";
            if (obj is ISegment segment)
            {
#if DEBUG // byte offset only tracked in debug
                return $"segment: {segment.Index}, offset: {offset}; byte-offset: {segment.ByteOffset + (offset * segment.ElementSize)}; type: {segment.UnderlyingType.Name}";
#else
                return $"segment: {segment.Index}, offset: {offset}; type: {segment.UnderlyingType.Name}";
#endif
            }

            if (obj == null && offset == 0) return "(nil)";
            return $"obj: {obj}; offset: {offset}";
        }
    }
}
