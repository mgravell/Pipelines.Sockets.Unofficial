using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    //internal interface IBlock : ISegment
    //{
    //    bool TryCopyTo(in Sequence allocation, Array destination, int offset);
    //    void CopyTo(in Sequence allocation, Array destination, int offset);
    //}

    //internal abstract class NilSegment : ISegment
    //{
    //    Type ISegment.ElementType => ElementType;
    //    protected abstract Type ElementType { get; }
    //    //bool IBlock.TryCopyTo(in Sequence allocation, Array destination, int offset) => true;
    //    //void IBlock.CopyTo(in Sequence allocation, Array destination, int offset) { }
    //    long ISegment.RunningIndex => 0;
    //}
    //internal sealed class NilSegment<T> : NilSegment //, IBlock
    //{   // this exists just so empty allocations (no block) can be untyped/cast correctly
    //    public static ISegment Default { get; } = new NilSegment<T>();
    //    protected override Type ElementType => typeof(T);
    //    private NilSegment() { }
    //}

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
    public abstract class SequenceSegment<T> : ReadOnlySequenceSegment<T>, ISegment
    {
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

        public bool TryCopyTo(in Sequence allocation, Array destination, int offset)
        {
            var span = (Span<T>)(T[])destination;
            if (offset != 0) span = span.Slice(offset);
            return allocation.Cast<T>().TryCopyTo(span);
        }

        public void CopyTo(in Sequence allocation, Array destination, int offset)
        {
            var span = (Span<T>)(T[])destination;
            if (offset != 0) span = span.Slice(offset);
            allocation.Cast<T>().CopyTo(span);
        }
    }
}
