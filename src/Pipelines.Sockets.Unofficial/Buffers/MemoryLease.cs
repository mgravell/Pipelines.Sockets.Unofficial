using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial.Buffers
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct MemoryLease<T> : IMemoryOwner<T>, IEquatable<MemoryLease<T>>
    {
        private readonly object _pool;
        public Memory<T> Memory { get; }
        private readonly LeaseOptions _options;

        internal MemoryLease(object pool, Memory<T> memory, int requestedSize, LeaseOptions options)
        {
            _pool = pool;
            _options = options;
            Memory = (options & LeaseOptions.AllowOversized) == 0 ? memory.Slice(0, requestedSize) : memory;
            if ((options & LeaseOptions.ClearBeforeUse) != 0) Clear();
        }

        public void Clear() => Span.Clear();

        public Span<T> Span => Memory.Span;

        public bool TryGetArray(out ArraySegment<T> segment)
            => MemoryMarshal.TryGetArray(Memory, out segment);

        public void Dispose()
        {
            if ((_options & LeaseOptions.ClearAfterUse) != 0) Clear();
            switch (_pool)
            {
                case ArrayPool<T> arrayPool:
                    if (MemoryMarshal.TryGetArray<T>(Memory, out var segment))
                        arrayPool.Return(segment.Array);
                    break;
                case IMemoryOwner<T> memoryOwner:
                    memoryOwner.Dispose();
                    break;
            }
        }

        public override bool Equals(object obj) => obj is MemoryLease<T> other && Equals(other);

        public bool Equals(MemoryLease<T> other) => Memory.Equals(other.Memory);

        public override int GetHashCode() => Memory.GetHashCode();

        public override string ToString() => Memory.ToString();
    }

    [Flags]
    public enum LeaseOptions
    {
        None = 0,
        AllowOversized = 1 << 0,
        ClearBeforeUse = 1 << 1,
        ClearAfterUse = 1 << 2,
    }
    public static class MemoryLease
    {
        public static MemoryLease<T> Lease<T>(this MemoryPool<T> pool, int size, LeaseOptions options = LeaseOptions.None)
        {
            var owner = pool.Rent(size);
            return new MemoryLease<T>(owner, owner.Memory, size, options);
        }

        public static MemoryLease<T> Lease<T>(this ArrayPool<T> pool, int size, LeaseOptions options = LeaseOptions.None)
        {
            var arr = pool.Rent(size);
            return new MemoryLease<T>(pool, arr, size, options);
        }
    }
}
