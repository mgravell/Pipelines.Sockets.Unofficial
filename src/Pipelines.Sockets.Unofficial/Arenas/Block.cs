using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{

    internal sealed class Block<T> : SequenceSegment<T>, IDisposable, IPinnedMemoryOwner<T>
    {
        private readonly unsafe void* _origin;
        unsafe void* IPinnedMemoryOwner<T>.Origin => _origin;

        public override string ToString() => $"Block {SegmentIndex}, {Length}×{typeof(T).Name}";
        internal int SegmentIndex { get; }

        protected override int GetSegmentIndex() => SegmentIndex;

        public IMemoryOwner<T> Allocation { get; private set; }

        public Block(IMemoryOwner<T> allocation, int segmentIndex, Block<T> previous)
            : base(allocation.Memory, previous)
        {
            Allocation = allocation;
            SegmentIndex = segmentIndex;
            if (allocation is IPinnedMemoryOwner<T> pinned)
            {
                unsafe { _origin = pinned.Origin; }
            }
        }

        public new Block<T> Next // just to expose the type a bit more clearly
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Block<T>)((ReadOnlySequenceSegment<T>)this).Next;
            // no point in casting twice! (note: this is a no-op, since we inherit it)
        }

        public void Dispose()
        {
            try { Allocation?.Dispose(); } catch { } // best efforts
            Allocation = null;
        }

        internal new Block<T> DetachNext() => (Block<T>)base.DetachNext();
    }
}
