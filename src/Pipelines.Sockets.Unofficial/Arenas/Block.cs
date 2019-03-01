using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Represents an abstract chained segment of mutable memory
    /// </summary>
    internal interface ISegment // this interface mostly helps debugging
    {
        int ElementSize { get; }
        /// <summary>
        /// The segment index
        /// </summary>
        int Index { get; }
        /// <summary>
        /// The type of data represented by this segment
        /// </summary>
        Type ElementType { get; }
        /// <summary>
        /// The actual type of memory used for the storage
        /// </summary>
        Type UnderlyingType { get; }
        /// <summary>
        /// The offset of this segment in the chain
        /// </summary>
        long RunningIndex { get; }

#if DEBUG
        /// <summary>
        /// The true byte offset of the segment
        /// </summary>
        long ByteOffset { get; }
#endif
    }


    /// <summary>
    /// A memory-owner that provides direct access to the root reference
    /// </summary>
    public interface IPinnedMemoryOwner<T> : IMemoryOwner<T>
    {
        /// <summary>
        /// The root reference of the block, or a null-pointer if the data should not be considered pinned
        /// </summary>
        unsafe void* Origin { get; } // expressed as an untyped pointer so that IPinnedMemoryOwner<T> can be used without needing the T : unmanaged constraint
    }

    /// <summary>
    /// Represents an abstract chained segment of mutable memory
    /// </summary>
    public abstract class SequenceSegment<T> : ReadOnlySequenceSegment<T>, ISegment, IMemoryOwner<T>
    {
        int ISegment.ElementSize => Unsafe.SizeOf<T>();
        void IDisposable.Dispose() { } // just to satisfy IMemoryOwner<T>

        /// <summary>
        /// The length of the memory
        /// </summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get; private set;
        }

        /// <summary>
        /// The next segment in the chain
        /// </summary>
        public new SequenceSegment<T> Next
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (SequenceSegment<T>)base.Next;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set => base.Next = value;
        }

        /// <summary>
        /// The memory represented by this segment
        /// </summary>
        public new Memory<T> Memory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MemoryMarshal.AsMemory(base.Memory);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set
            {
                base.Memory = value;
                Length = value.Length;
            }
        }

        Type ISegment.ElementType => typeof(T);

        int ISegment.Index => GetSegmentIndex();

        /// <summary>
        /// Get the logical index of this segment
        /// </summary>
        protected virtual int GetSegmentIndex() => 0;

        Type ISegment.UnderlyingType => GetUnderlyingType();

        /// <summary>
        /// Get the real type of data being used to hold this data
        /// </summary>
        protected virtual Type GetUnderlyingType() => typeof(T);

#if DEBUG
        long ISegment.ByteOffset => ByteOffset;

        protected virtual long ByteOffset => Unsafe.SizeOf<T>() * RunningIndex;
#endif
    }

    internal sealed class Block<T> : SequenceSegment<T>, IDisposable
    {
        internal int SegmentIndex { get; }

        protected override int GetSegmentIndex() => SegmentIndex;

        public IMemoryOwner<T> Allocation { get; private set; }

        public Block(IMemoryOwner<T> allocation, int segmentIndex, long offset)
        {
            Allocation = allocation;
            SegmentIndex = segmentIndex;
            base.Memory = Memory = Allocation.Memory;
            RunningIndex = offset;
        }

        public new Block<T> Next
        {
            get => (Block<T>)base.Next;
            set => base.Next = value;
        }

        public void Dispose()
        {
            try { Allocation?.Dispose(); } catch { } // best efforts
            Allocation = null;
        }
    }
}
