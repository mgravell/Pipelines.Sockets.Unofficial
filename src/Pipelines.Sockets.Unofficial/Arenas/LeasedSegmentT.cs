using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    internal sealed class LeasedSegment<T> : SequenceSegment<T>
    {
#if DEBUG
        internal static long s_leaseCount;
#endif
        internal static LeasedSegment<T> Create(int minimumSize, LeasedSegment<T> previous)
        {
            var array = ArrayPool<T>.Shared.Rent(minimumSize);
#if DEBUG
            Interlocked.Increment(ref s_leaseCount);
#endif

            // try and get from the pool, noting that we might be squabbling
            for (int i = 0; i < 5; i++)
            {
                LeasedSegment<T> oldHead;
                if (Volatile.Read(ref s_poolSize) == 0
                    || (oldHead = Volatile.Read(ref s_poolHead)) == null) break;
                var newHead = (LeasedSegment<T>)oldHead.Next;

                if (Interlocked.CompareExchange(ref s_poolHead, newHead, oldHead) == oldHead)
                {
                    Interlocked.Decrement(ref s_poolSize); // doesn't need to be hard in lock-step with the actual length
                    oldHead.Init(array, previous);
                    return oldHead;
                }
            }
            return new LeasedSegment<T>(array, previous);
        }

        static void PushToPool(LeasedSegment<T> segment)
        {
            if (segment == null) return;
            for (int i = 0; i < 5; i++)
            {
                if (Volatile.Read(ref s_poolSize) >= MAX_RECYCLED) return;

                var oldHead = Volatile.Read(ref s_poolHead);
                segment.SetNext(oldHead);
                if (Interlocked.CompareExchange(ref s_poolHead, segment, oldHead) == oldHead)
                {
                    Interlocked.Increment(ref s_poolSize); // doesn't need to be hard in lock-step with the actual length
                    return;
                }
            }
        }

        const int MAX_RECYCLED = 50; // hold a small number of LeasedSegment tokens for re-use
        static LeasedSegment<T> s_poolHead;
        static int s_poolSize;

        private LeasedSegment(T[] array, LeasedSegment<T> previous) : base(array, previous) { }

        internal void CascadeRelease(bool inclusive)
        {
            var segment = inclusive ? this : (LeasedSegment<T>)ResetNext();
            while (segment != null)
            {
                if (MemoryMarshal.TryGetArray<T>(segment.ResetMemory(), out var array))
                {
                    ArrayPool<T>.Shared.Return(array.Array);
#if DEBUG
                    Interlocked.Decrement(ref s_leaseCount);
#endif
                }
                var next = (LeasedSegment<T>)segment.ResetNext();
                PushToPool(segment);
                segment = next;
            }
        }
    }
}
