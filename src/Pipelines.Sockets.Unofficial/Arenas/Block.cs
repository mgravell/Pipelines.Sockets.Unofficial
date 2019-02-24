using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    internal interface IBlock
    {
        Type ElementType { get; }
    }

    internal sealed class NilBlock<T> : IBlock
    {   // this exists just so empty allocations (no block) can be untyped/cast correctly
        public static IBlock Default { get; } = new NilBlock<T>(); 
        Type IBlock.ElementType => typeof(T);
        private NilBlock() { }
    }

    internal sealed class Block<T> : ReadOnlySequenceSegment<T>, IDisposable, IBlock
    {
        public int Length { get; }
        public IMemoryOwner<T> Allocation { get; private set; }
        private Block<T> _next;

        Type IBlock.ElementType => typeof(T);

        public Block(IMemoryOwner<T> allocation, long offset)
        {
            Allocation = allocation;
            base.Memory = Memory = Allocation.Memory;
            Length = Memory.Length;
            RunningIndex = offset;
        }

        public new Block<T> Next // note: choosing to duplicate the field here rather
        { // than constantly pay the cast cost; that's fine, we should have few blocks
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _next;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _next = value;
                base.Next = value;
            }
        }

        public new Memory<T> Memory { get; } // see comment on Next re duplicate

        public void Dispose()
        {
            try { Allocation?.Dispose(); } catch { } // best efforts
            Allocation = null;
        }
    }
}
