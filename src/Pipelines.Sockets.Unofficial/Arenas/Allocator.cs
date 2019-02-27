using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Pipelines.Sockets.Unofficial.Internal;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Allocates blocks of memory
    /// </summary>
    public abstract class Allocator<T>
    {
        static readonly int _defaultBlockSize = CalculateDefaultBlockSize();
        static int CalculateDefaultBlockSize()
        {
            try
            {
                // try for 128k *memory* (not 128k elements) - this is so we get on the LOH by default (~85k)
                int count = (128 * 1024) / Unsafe.SizeOf<T>();
                return count <= 64 ? 64 : count; // avoid too small (only impacts **huge** types)
            }
            catch
            {
                return 32 * 1024; // arbitrary 32k elements if that fails
            }
        }

        /// <summary>
        /// The default block-size used by this allocate
        /// </summary>
        public virtual int DefaultBlockSize => _defaultBlockSize;

        /// <summary>
        /// Allocate a new block
        /// </summary>
        public abstract IMemoryOwner<T> Allocate(int length);

        /// <summary>
        /// Clear (zero) the supplied region
        /// </summary>
        public virtual void Clear(IMemoryOwner<T> allocation, int length)
            => allocation.Memory.Span.Slice(0, length).Clear();
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

    internal sealed class PinnedArrayPoolAllocator<T> : Allocator<T> where T : unmanaged
    {
        private readonly ArrayPool<T> _pool;

        /// <summary>
        /// An array-pool allocator that uses the shared array-pool
        /// </summary>
        public static PinnedArrayPoolAllocator<T> Shared { get; } = new PinnedArrayPoolAllocator<T>();

        public PinnedArrayPoolAllocator(ArrayPool<T> pool = null) => _pool = pool ?? ArrayPool<T>.Shared;

        public override IMemoryOwner<T> Allocate(int length)
            => new PinnedArray(_pool, _pool.Rent(length));

        private unsafe sealed class PinnedArray : MemoryManager<T>, IPinnedMemoryOwner<T>
        {
            private T[] _array;
            private readonly int _length;
            private readonly ArrayPool<T> _pool;
            private GCHandle _pin;
            private T* _ptr;
            public PinnedArray(ArrayPool<T> pool, T[] array)
            {
                _pool = pool;
                _array = array;
                _length = array.Length;
                _pin = GCHandle.Alloc(array, GCHandleType.Pinned);
                _ptr = (T*)_pin.AddrOfPinnedObject().ToPointer();
            }

            protected override void Dispose(bool disposing)
            {
                if (_ptr != null)
                {
                    _ptr = null;
                    try { _pin.Free(); } catch { } // best efforst
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

            public override Span<T> GetSpan() => new Span<T>(_ptr, _length);

            public override MemoryHandle Pin(int elementIndex = 0) => new MemoryHandle(_ptr + elementIndex);

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

            T* IPinnedMemoryOwner<T>.Root => _ptr;

            ~PinnedArray() => Dispose(false);

            public void Dispose()
            {
                Dispose(true);
                
            }
        }
    }

    /// <summary>
    /// An allocator that allocates unmanaged memory, releasing the memory back to the OS when done
    /// </summary>
    public unsafe sealed class UnmanagedAllocator<T> : Allocator<T> where T : unmanaged
    {
        private UnmanagedAllocator() { }

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
            ~OwnedPointer() => Dispose(false);

            private T* _ptr;
            private readonly int _length;

            public T* Root => _ptr;

            public OwnedPointer(int length)
                => _ptr = (T*)Marshal.AllocHGlobal((_length = length) * sizeof(T)).ToPointer();

            public override Span<T> GetSpan() => new Span<T>(_ptr, _length);

            public override MemoryHandle Pin(int elementIndex = 0)
                => new MemoryHandle(_ptr + elementIndex);

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
