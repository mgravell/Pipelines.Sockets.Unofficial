using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Pipelines.Sockets.Unofficial.Internal;
using System.Diagnostics;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Allocates blocks of memory
    /// </summary>
    public abstract class Allocator<T>
    {
        /// <summary>
        /// Allocate a new block
        /// </summary>
        public abstract IMemoryOwner<T> Allocate(int length);

        /// <summary>
        /// Clear (zero) the supplied region
        /// </summary>
        public virtual void Clear(IMemoryOwner<T> allocation, int length)
            => allocation.Memory.Span.Slice(0, length).Clear();

        internal virtual bool IsPinned => false;
        internal virtual bool IsUnmanaged => false;
    }

    /// <summary>
    /// An allocator that rents memory from the array-pool provided, returning them to the pool when done
    /// </summary>
    public sealed class ArrayPoolAllocator<T> : Allocator<T>
    {
        private readonly ArrayPool<T> _pool;

        /// <summary>
        /// An array-pool allocator that uses the shared array-pool
        /// </summary>
        public static ArrayPoolAllocator<T> Shared { get; } = new ArrayPoolAllocator<T>();

        /// <summary>
        /// Create a new array-pool allocator that uses the provided array pool (or the shared array-pool otherwise)
        /// </summary>
        public ArrayPoolAllocator(ArrayPool<T> pool = null) => _pool = pool ?? ArrayPool<T>.Shared;

        /// <summary>
        /// Allocate a new block 
        /// </summary>
        public override IMemoryOwner<T> Allocate(int length)
            => new OwnedArray(_pool, _pool.Rent(length));

        private sealed class OwnedArray : MemoryManager<T>
        {
            private T[] _array;
            private readonly ArrayPool<T> _pool;
            public OwnedArray(ArrayPool<T> pool, T[] array)
            {
                _pool = pool;
                _array = array;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    var arr = _array;
                    _array = null;
                    if (arr != null) _pool.Return(arr);
                }
            }

            public override MemoryHandle Pin(int elementIndex = 0) { Throw.NotSupported(); return default; }

            public override void Unpin() => Throw.NotSupported();

            public override Span<T> GetSpan() => _array;

            protected override bool TryGetArray(out ArraySegment<T> segment)
            {
                if (_array != null)
                {
                    segment = new ArraySegment<T>(_array);
                    return true;
                }
                else
                {
                    segment = default;
                    return false;
                }
            }
        }
    }

    internal sealed class PinnedArrayPoolAllocator<T> : Allocator<T>
    {
        // where T : unmanaged
        // is intended - can't enforce due to a: convincing compiler, and
        // b: runtime (AOT) limitations

        internal override bool IsPinned => true;

        private readonly ArrayPool<T> _pool;

        /// <summary>
        /// An array-pool allocator that uses the shared array-pool
        /// </summary>
        public static PinnedArrayPoolAllocator<T> Shared { get; } = new PinnedArrayPoolAllocator<T>();

        public PinnedArrayPoolAllocator(ArrayPool<T> pool = null)
        {
            Debug.Assert(PerTypeHelpers<T>.IsBlittable);
            _pool = pool ?? ArrayPool<T>.Shared;
        }

        public override IMemoryOwner<T> Allocate(int length)
            => new PinnedArray(_pool, _pool.Rent(length));

        private unsafe sealed class PinnedArray : MemoryManager<T>, IPinnedMemoryOwner<T>
        {
            private T[] _array;
            private readonly ArrayPool<T> _pool;
            private GCHandle _pin;
            private void* _ptr;
            public PinnedArray(ArrayPool<T> pool, T[] array)
            {
                _pool = pool;
                _array = array;
                Length = array.Length;
                _pin = GCHandle.Alloc(array, GCHandleType.Pinned);
                _ptr = _pin.AddrOfPinnedObject().ToPointer();
            }

            protected override void Dispose(bool disposing)
            {
                if (_ptr != null)
                {
                    _ptr = null;
                    try { _pin.Free(); } catch { } // best efforts
                    _pin = default;
                }
                if (disposing)
                {
                    var arr = _array;
                    _array = null;
                    if (arr != null) _pool.Return(arr);
                    GC.SuppressFinalize(this);
                }
            }


            public int Length { get; }
            public override Span<T> GetSpan() => new(_ptr, Length);

            public override MemoryHandle Pin(int elementIndex = 0) => new(Unsafe.Add<T>(_ptr, elementIndex));

            protected override bool TryGetArray(out ArraySegment<T> segment)
            {
                if (_array != null)
                {
                    segment = new ArraySegment<T>(_array);
                    return true;
                }
                else
                {
                    segment = default;
                    return false;
                }
            }

            public override void Unpin() { }

            void* IPinnedMemoryOwner<T>.Origin => _ptr;

#pragma warning disable IDE0079
#pragma warning disable CA2015 // possible GC while span in play; self-inflicted!
            ~PinnedArray() => Dispose(false);
#pragma warning restore CA2015 // possible GC while span in play; self-inflicted!
#pragma warning restore IDE0079

            public void Dispose() => Dispose(true);
        }
    }

    /// <summary>
    /// An allocator that allocates unmanaged memory, releasing the memory back to the OS when done
    /// </summary>
    public unsafe sealed class UnmanagedAllocator<T> : Allocator<T>
    {
        // where T : unmanaged
        // is intended - can't enforce due to a: convincing compiler, and
        // b: runtime (AOT) limitations
        internal override bool IsUnmanaged => true;

        private UnmanagedAllocator()
        {
            Debug.Assert(PerTypeHelpers<T>.IsBlittable);
        }

        /// <summary>
        /// The global instance of the unmanaged allocator
        /// </summary>
        public static UnmanagedAllocator<T> Shared { get; } = new UnmanagedAllocator<T>();

        /// <summary>
        /// Allocate a new block
        /// </summary>
        public override IMemoryOwner<T> Allocate(int length) => new OwnedPointer(length);

        private sealed class OwnedPointer : MemoryManager<T>, IPinnedMemoryOwner<T>
        {
#pragma warning disable IDE0079
#pragma warning disable CA2015 // possible GC while span in play; self-inflicted!
            ~OwnedPointer() => Dispose(false);
#pragma warning restore CA2015 // possible GC while span in play; self-inflicted!
#pragma warning restore IDE0079

            private void* _ptr;

            public int Length { get; }
            void* IPinnedMemoryOwner<T>.Origin => _ptr;

            public OwnedPointer(int length)
                => _ptr = Marshal.AllocHGlobal((Length = length) * Unsafe.SizeOf<T>()).ToPointer();

            public override Span<T> GetSpan() => new(_ptr, Length);

            public override MemoryHandle Pin(int elementIndex = 0)
                => new(Unsafe.Add<T>(_ptr, elementIndex));

            public override void Unpin() { } // nothing to do

            protected override bool TryGetArray(out ArraySegment<T> segment)
            {
                segment = default;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                var ptr = _ptr;
                _ptr = null;
                if (ptr != null) Marshal.FreeHGlobal(new IntPtr(ptr));
                if (disposing) GC.SuppressFinalize(this);
            }
        }
    }
}
