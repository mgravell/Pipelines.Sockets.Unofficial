using Pipelines.Sockets.Unofficial.Internal;
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
    [CLSCompliant(false)]
    public interface IPinnedMemoryOwner<T> : IMemoryOwner<T>
    {
        /// <summary>
        /// The root reference of the block, or a null-pointer if the data should not be considered pinned
        /// </summary>
        unsafe void* Origin { get; } // expressed as an untyped pointer so that IPinnedMemoryOwner<T> can be used without needing the T : unmanaged constraint

        /// <summary>
        /// Gets the size of the data
        /// </summary>
        int Length { get; }
    }

    /// <summary>
    /// Represents an abstract chained segment of mutable memory
    /// </summary>
    public abstract class SequenceSegment<T> : ReadOnlySequenceSegment<T>, ISegment, IMemoryOwner<T>
    {
        /// <summary>
        /// Creates a new SequenceSegment, optionally attaching the segment to an existing chain
        /// </summary>
        protected SequenceSegment(Memory<T> memory, SequenceSegment<T> previous = null)
        {
            if (previous != null)
            {
                var oldNext = previous.Next;
                if (oldNext != null) Throw.InvalidOperation("The previous segment already has an onwards chain");
                previous.Next = this;
                RunningIndex = previous.RunningIndex + previous.Length;
            }
            Memory = memory; // also sets Length
        }

        int ISegment.ElementSize => Unsafe.SizeOf<T>();
        void IDisposable.Dispose() { } // just to satisfy IMemoryOwner<T>

        /// <summary>
        /// The length of the memory
        /// </summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get; private set;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetSegmentPosition(ref SequenceSegment<T> segment, long index)
            => (index >= 0 & segment != null) && (index < segment.Length)
                ? (int)index
                : SlowGetSegmentPosition(ref segment, index);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int SlowGetSegmentPosition(ref SequenceSegment<T> segment, long index)
        {
            if (index < 0) Throw.IndexOutOfRange();
            if (segment == null) Throw.ArgumentNull(nameof(segment));

            do
            {
                if (index < segment.Length | // inside this segment
                    (segment.Next == null & index == segment.Length)) // EOF in final segment
                {
                    return (int)index;
                }

                index -= segment.Length;
                segment = segment.Next;
            } while (segment != null);

            Throw.IndexOutOfRange(); // not in the sequence at all
            return default;
        }

        /// <summary>
        /// The logical position of the start of this segment
        /// </summary>
        public new long RunningIndex
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => base.RunningIndex;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set => base.RunningIndex = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Trim(int length)
        {
            long delta = Length - length;
            if (delta < 0) Throw.ArgumentOutOfRange(nameof(length));
            else if (delta != 0)
            {
                Memory = Memory.Slice(0, length);
                // fixup the running index of any later segments
                var node = Next;
                while (node != null)
                {
                    node.RunningIndex -= delta;
                    node = node.Next;
                }
            }
        }

        /// <summary>
        /// The next segment in the chain
        /// </summary>
        public new SequenceSegment<T> Next
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (SequenceSegment<T>)base.Next;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set => base.Next = value;
        }

        /// <summary>
        /// The memory represented by this segment
        /// </summary>
        public new Memory<T> Memory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MemoryMarshal.AsMemory(base.Memory);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private protected set
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

        /// <summary>
        /// Remove the Next node in this chain, terminating the chain - returning the detached segment
        /// </summary>
        protected SequenceSegment<T> DetachNext()
        {
            var next = Next;
            Next = null;
            return next;
        }

#if DEBUG
        long ISegment.ByteOffset => ByteOffset;

        protected virtual long ByteOffset => Unsafe.SizeOf<T>() * RunningIndex;
#endif
    }
}
