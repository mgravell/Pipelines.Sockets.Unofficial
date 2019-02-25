using System;

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
    public static class AllocationExtensions
    {
        /// <summary>
        /// Create an array with the contents of the allocation
        /// </summary>
        public static T[] ToArray<T>(this in Allocation<T> source)
        {
            if (source.IsEmpty) return Array.Empty<T>();
            var arr = new T[source.Length];
            source.CopyTo(arr);
            return arr;
        }

        /// <summary>
        /// Create an array with the contents of the allocation, applying a projection
        /// </summary>
        public static TTo[] ToArray<TFrom, TTo>(this in Allocation<TFrom> source, Projection<TFrom, TTo> projection)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection);
            return arr;
        }

        /// <summary>
        /// Create an array with the contents of the allocation, applying a projection
        /// </summary>
        public static TTo[] ToArray<TFrom, TState, TTo>(this in Allocation<TFrom> source, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection, in state);
            return arr;
        }

        /// <summary>
        /// Copy the data from an allocation to a span, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this in Allocation<TFrom> source,
            Span<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(in source, destination, projection))
                ThrowInvalid();
            void ThrowInvalid() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from an allocation to a span, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this in Allocation<TFrom> source,
            Span<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(in source, destination, projection, in state))
                ThrowInvalid();
            void ThrowInvalid() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this Span<TFrom> source,
            in Allocation<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, in destination, projection))
                ThrowInvalid();
            void ThrowInvalid() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this ReadOnlySpan<TFrom> source,
            in Allocation<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, in destination, projection, in state))
                ThrowInvalid();
            void ThrowInvalid() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TTo>(this ReadOnlySpan<TFrom> source,
            in Allocation<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(source, in destination, projection))
                ThrowInvalid();
            void ThrowInvalid() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from a span to an allocation, applying a projection
        /// </summary>
        public static void CopyTo<TFrom, TState, TTo>(this Span<TFrom> source,
            in Allocation<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(source, in destination, projection, in state))
                ThrowInvalid();
            void ThrowInvalid() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from an allocation to a span, applying a projection
        /// </summary>
        public static bool TryCopyTo<TFrom, TTo>(this in Allocation<TFrom> source,
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
        public static bool TryCopyTo<TFrom, TState, TTo>(this in Allocation<TFrom> source,
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
            in Allocation<TTo> destination, Projection<TFrom, TTo> projection)
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
                in Allocation<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
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
            in Allocation<TTo> destination, Projection<TFrom, TTo> projection)
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
                in Allocation<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
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
    }
}
