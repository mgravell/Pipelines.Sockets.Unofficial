#if DEBUG
using System.Threading;

namespace Pipelines.Sockets.Unofficial.Buffers
{
    partial class BufferWriter<T>
    {
        internal static int LiveSegmentCount => RefCountedSegment.LiveCount;
        partial class RefCountedSegment
        {
            static partial void IncrLiveCount() => Interlocked.Increment(ref s_LiveCount);
            static partial void DecrLiveCount() => Interlocked.Decrement(ref s_LiveCount);
            private static int s_LiveCount;
            internal static int LiveCount => Volatile.Read(ref s_LiveCount);
        }
    }
}
#endif