using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Represents an abstract chained segment of mutable memory
    /// </summary>
    public interface ISegment
    {
        /// <summary>
        /// The type of data represented by this segment
        /// </summary>
        Type ElementType { get; }
        /// <summary>
        /// The offset of this segment in the chain
        /// </summary>
        long RunningIndex { get; }
    }

    /// <summary>
    /// Represents an abstract chained segment of mutable memory
    /// </summary>
    public abstract class SequenceSegment<T> : ReadOnlySequenceSegment<T>, ISegment, IMemoryOwner<T>
    {
        void IDisposable.Dispose() { } // just to satisfy IMemoryOwner<T>

        /// <summary>
        /// The length of the memory
        /// </summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => base.Memory.Length; // don't need the extra cast here
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
            protected set => base.Memory = value;
        }

        Type ISegment.ElementType => typeof(T);
    }

    internal sealed class Block<T> : SequenceSegment<T>, IDisposable // , IBlock
    {
        public IMemoryOwner<T> Allocation { get; private set; }

        public Block(IMemoryOwner<T> allocation, long offset)
        {
            Allocation = allocation;
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
