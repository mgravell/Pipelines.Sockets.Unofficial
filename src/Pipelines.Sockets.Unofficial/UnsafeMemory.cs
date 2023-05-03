using Pipelines.Sockets.Unofficial.Arenas;
using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// A MemoryManager over a raw pointer
    /// </summary>
    /// <remarks>The pointer is assumed to be fully unmanaged, or externally pinned - no attempt will be made to pin this data</remarks>
    public sealed unsafe class UnmanagedMemoryManager<T> : MemoryManager<T>
    {
        // where T : unmanaged
        // is intended - can't enforce due to a: convincing compiler, and
        // b: runtime (AOT) limitations

        private readonly void* _pointer;
        private readonly int _length;

        /// <summary>
        /// Create a new UnmanagedMemoryManager instance at the given pointer and size
        /// </summary>
        /// <remarks>It is assumed that the span provided is already unmanaged or externally pinned</remarks>
        public UnmanagedMemoryManager(Span<T> span)
        {
            Debug.Assert(PerTypeHelpers<T>.IsBlittable);
            _pointer = Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));
            _length = span.Length;
        }

        /// <summary>
        /// Create a new UnmanagedMemoryManager instance at the given pointer and size
        /// </summary>
        [CLSCompliant(false)]
#pragma warning disable CS8500 // T* - would prefer void*, but can't change API
        public UnmanagedMemoryManager(T* pointer, int length) : this((void*)pointer, length) {}
#pragma warning restore CS8500

        /// <summary>
        /// Create a new UnmanagedMemoryManager instance at the given pointer and size
        /// </summary>
        public UnmanagedMemoryManager(IntPtr pointer, int length) : this(pointer.ToPointer(), length) { }

        private UnmanagedMemoryManager(void* pointer, int length)
        {
            Debug.Assert(PerTypeHelpers<T>.IsBlittable);
            if (length < 0) Throw.ArgumentOutOfRange(nameof(length));
            _pointer = pointer;
            _length = length;
        }

        /// <summary>
        /// Obtains a span that represents the region
        /// </summary>
        public override Span<T> GetSpan() => new(_pointer, _length);

        /// <summary>
        /// Provides access to a pointer that represents the data (note: no actual pin occurs)
        /// </summary>
        public override MemoryHandle Pin(int elementIndex = 0)
        {
            if (elementIndex < 0 || elementIndex >= _length)
                Throw.ArgumentOutOfRange(nameof(elementIndex));
            return new MemoryHandle(Unsafe.Add<T>(_pointer, elementIndex));
        }
        /// <summary>
        /// Has no effect
        /// </summary>
        public override void Unpin() { }

        /// <summary>
        /// Releases all resources associated with this object
        /// </summary>
        protected override void Dispose(bool disposing) { }
    }
}
