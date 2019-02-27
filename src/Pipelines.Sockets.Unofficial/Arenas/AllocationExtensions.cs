using System;
using System.Buffers;

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
    /// Provides utility methods for working with allocations
    /// </summary>
    public static class SequenceExtensions
    {
        /// <summary>
        /// Create an array with the contents of the allocation
        /// </summary>
        public static T[] ToArray<T>(this in Sequence<T> source)
        {
            if (source.IsEmpty) return Array.Empty<T>();
            var arr = new T[source.Length];
            source.CopyTo(arr);
            return arr;
        }

        /// <summary>
        /// Create an array with the contents of the allocation, applying a projection
        /// </summary>
        public static TTo[] ToArray<TFrom, TTo>(this in Sequence<TFrom> source, Projection<TFrom, TTo> projection)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection);
            return arr;
        }

        /// <summary>
        /// Create an array with the contents of the allocation, applying a projection
        /// </summary>
        public static TTo[] ToArray<TFrom, TState, TTo>(this in Sequence<TFrom> source, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection, in state);
            return arr;
        }

        private static void ThrowInvalid() => throw new InvalidOperationException();

        /// <summary>
        /// Copy the data from an allocation to a span, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this in Sequence<TFrom> source,
            Span<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(in source, destination, projection))
                ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from an allocation to a span, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this in Sequence<TFrom> source,
            Span<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(in source, destination, projection, in state))
                ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this Span<TFrom> source,
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, in destination, projection))
                ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this ReadOnlySpan<TFrom> source,
            in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, in destination, projection, in state))
                ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this ReadOnlySpan<TFrom> source,
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, in destination, projection))
                ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this Span<TFrom> source,
            in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, in destination, projection, in state))
                ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from an allocation to a span, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(this in Sequence<TFrom> source,
            Span<TTo> destination, Projection<TFrom, TTo> projection)
        {
            void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

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
        /// Copy the data from an allocation to a span, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TState, TTo>(this in Sequence<TFrom> source,
                Span<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

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
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(this Span<TFrom> source,
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

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
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TState, TTo>(this Span<TFrom> source,
                in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

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
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(this ReadOnlySpan<TFrom> source,
            in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

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
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TState, TTo>(this ReadOnlySpan<TFrom> source,
                in Sequence<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

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
        /// Copy the data from an allocation to a new allocation, applying a projection
        /// </summary>
        public static Sequence<TTo> Allocate<TFrom, TTo>(this Arena<TTo> arena, in Sequence<TFrom> source, Projection<TFrom, TTo> projection)
        {
            if (source.IsEmpty) return arena.Allocate(0); // retains position etc

            var block = arena.Allocate(checked((int)source.Length));
            source.CopyTo(block, projection);
            return block;
        }

        /// <summary>
        /// Copy the data from an allocation to a new allocation, applying a projection
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
        /// Copy the data from between two allocations, applying a projection
        /// </summary>
        public static void CopyTo<T>(this in Sequence<T> source, in Sequence<T> destination)
        {
            if (!TryCopyTo<T>(source, destination)) ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from between two allocations, applying a projection
        /// </summary>
        public static bool TryCopyTo<T>(this in Sequence<T> source, in Sequence<T> destination)
        {
            if (source.Length > destination.Length) return false;
            if (source.IsSingleSegment & destination.IsSingleSegment) return source.FirstSpan.TryCopyTo(destination.FirstSpan);
            SlowCopyTo<T>(source, destination);
            return true; // we checked the lengths first
        }

        static void SlowCopyTo<T>(in Sequence<T> source, in Sequence<T> destination)
        {
            var from = source.GetEnumerator();
            var to = destination.GetEnumerator();
            while (from.MoveNext())
            {
                to.Current = from.Current;
            }
        }

        /// <summary>
        /// Copy the data from between two allocations, applying a projection
        /// </summary>
        public static void CopyTo<T>(this in ReadOnlySequence<T> source, in Sequence<T> destination)
        {
            if (!TryCopyTo<T>(source, destination)) ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from between two allocations, applying a projection
        /// </summary>
        public static bool TryCopyTo<T>(this in ReadOnlySequence<T> source, in Sequence<T> destination)
        {
            if (source.Length > destination.Length) return false;
            if (source.IsSingleSegment & destination.IsSingleSegment) return source.First.Span.TryCopyTo(destination.FirstSpan);
            SlowCopyTo<T>(source, destination);
            return true; // we checked the lengths first
        }

        static void SlowCopyTo<T>(in ReadOnlySequence<T> source, in Sequence<T> destination)
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
        /// Copy the data from between two allocations, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this in Sequence<TFrom> source, in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, destination, projection)) ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from between two allocations, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(this in Sequence<TFrom> source, in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (source.Length > destination.Length) return false;
            if (source.IsSingleSegment) return source.FirstSpan.TryCopyTo(in destination, projection);
            if (destination.IsSingleSegment) return source.TryCopyTo(destination.FirstSpan, projection);
            SlowCopyTo<TFrom, TTo>(source, destination, projection);
            return true; // we checked the lengths first
        }

        static void SlowCopyTo<TFrom, TTo>(in Sequence<TFrom> source, in Sequence<TTo> destination, Projection<TFrom, TTo> projection)
        {
            var from = source.GetEnumerator();
            var to = destination.GetEnumerator();
            while(from.MoveNext())
            {
                to.GetNext() = projection(from.Current);
            }
        }

        /// <summary>
        /// Copy the data from between two allocations, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this in Sequence<TFrom> source, in Sequence<TTo> destination,
            Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, destination, projection, in state)) ThrowInvalid();
        }

        /// <summary>
        /// Copy the data from between two allocations, applying a projection
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

        static void SlowCopyTo<TFrom, TState, TTo>(in Sequence<TFrom> source, in Sequence<TTo> destination,
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
        public static long? TryGetOffset(this SequencePosition position)
        {
            var obj = position.GetObject();
            var offset = position.GetInteger();
            if (obj == null && offset == 0) return 0;
            if (obj is ISegment segment) return segment.RunningIndex + offset;
            return null; // nope!
        }

    }
}
