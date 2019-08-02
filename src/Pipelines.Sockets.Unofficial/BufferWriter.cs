using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// Represents a ReadOnlySequence<typeparamref name="T"/> with lifetime management over the data
    /// </summary>
    public readonly struct OwnedReadOnlySequence<T> : IDisposable
    {
        private readonly Action<ReadOnlySequence<T>> _onDispose;
        private readonly ReadOnlySequence<T> _value;
        /// <summary>
        /// The sequence of data represented by this value
        /// </summary>
        public ReadOnlySequence<T> Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _value;
        }

        /// <summary>
        /// Release any resources associated with this sequence
        /// </summary>
        public void Dispose() => _onDispose?.Invoke(Value);

        /// <summary>
        /// Create a new instance with a call-defined lifetime management callback
        /// </summary>
        public OwnedReadOnlySequence(ReadOnlySequence<T> value, Action<ReadOnlySequence<T>> onDispose)
        {
            _value = value;
            _onDispose = onDispose;
        }

        /// <summary>
        /// Access this data as a ReadOnlySequence<typeparamref name="T"/>
        /// </summary>
        /// <param name="value"></param>
        public static implicit operator ReadOnlySequence<T>(in OwnedReadOnlySequence<T> value) => value._value;
        /// <summary>
        /// Represent an existing ReadOnlySequence<typeparamref name="T"/> with dummy lifetime management
        /// </summary>
        public static implicit operator OwnedReadOnlySequence<T>(in ReadOnlySequence<T> value) => new OwnedReadOnlySequence<T>(value, null);
    }
    
    /// <summary>
    /// Implements a buffer-writer over arbitrary memory
    /// </summary>
    public abstract partial class BufferWriter<T> : IDisposable, IBufferWriter<T>
    {
        private protected int BlockSize { get; }

        /// <summary>
        /// Create a new buffer-writer instance that uses a memory pool as the backing store; if the provided pool is null, the shared instance is used
        /// </summary>
        public static BufferWriter<T> Create(MemoryPool<T> memoryPool, int? blockSize = null)
            => new MemoryPoolBufferWriter(memoryPool, blockSize ?? DefaultBlockSize);

        /// <summary>
        /// Create a new buffer-writer instance that uses an array-pool as the backing store; if the provided pool is null, the shared instance is used
        /// </summary>
        public static BufferWriter<T> Create(ArrayPool<T> arrayPool, int? blockSize = null)
            => new ArrayPoolBufferWriter(arrayPool, blockSize ?? DefaultBlockSize);

        /// <summary>
        /// Create a new buffer-writer instance that uses an array-pool as the backing store; if the provided pool is null, the shared instance is used
        /// </summary>
        public static BufferWriter<T> Create(int? blockSize = null) => Create(ArrayPool<T>.Shared, blockSize);

        const int DefaultBlockSize = 8 * 1024; // we can change the internal default without breaking the API

        /// <summary>
        /// Get the writer used to append data to this instance
        /// </summary>
        public IBufferWriter<T> Writer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this;
        }

        private RefCountedSegment _head, _tail;

        private int _headOffset, _offset, _remaining;
        private Memory<T> _block;

        private protected BufferWriter(int blockSize)
        {
            if (blockSize <= 0) Throw.ArgumentOutOfRange(nameof(blockSize));
            BlockSize = blockSize;
        }

        /// <summary>
        /// Release all resources associate with this instance
        /// </summary>
        public virtual void Dispose()
        {
            // release anything that is in the pending buffer
            if (_head != null)
            {
                var value = new ReadOnlySequence<T>(_head, _headOffset, _tail, _offset);
                _head = _tail = null;
                _remaining = _offset = _headOffset = 0;
                RefCountedSegment.Release(value);
            }
        }

        /// <summary>
        /// Gets the currently buffered data as a sequence of buffer-segments (with lifetime management); you
        /// can continue to append data after calling <c>Flush</c> - any additional data will form a new payload
        /// that can be fetched by the next call to <c>Flush</c>
        /// </summary>
        public OwnedReadOnlySequence<T> Flush()
        {
            // is it a trivial sequence? (this includes the null case)
            if (ReferenceEquals(_head, _tail) && _offset == _headOffset) return default;

            // create a new sequence from the current chain
            var value = new ReadOnlySequence<T>(_head, _headOffset, _tail, _offset);

            if (_remaining == 0)
            {
                // nothing left in the tail; start a whole new chain
                _head = _tail = null;
                _remaining = _offset = _headOffset = 0;
            }
            else
            {
                // we have some capacity left in the tail; we'll keep that one, so:
                // increment the tail and continue from there
                // this is a short-cut for:
                // - AddRef on all the elements in the result
                // - Release on everything in the old chain *except* the new head
                _tail.AddRef();
                _head = _tail;
                _headOffset = _offset;
            }
            return new OwnedReadOnlySequence<T>(value, RefCountedSegment.s_Release);
        }

        void IBufferWriter<T>.Advance(int count)
        {
            if (count < 0 | count > _remaining) Throw.ArgumentOutOfRange(nameof(count));
            _remaining -= count;
            _offset += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Memory<T> GetMemory(int sizeHint)
            => _remaining >= sizeHint ? _block.Slice(_offset) : GetMemorySlow(sizeHint);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Memory<T> GetMemorySlow(int sizeHint)
        {
            Debug.Assert(sizeHint > _remaining, "shouldn't have called slow impl");
            if (sizeHint > BlockSize) Throw.ArgumentOutOfRange(nameof(sizeHint));

            // limit the tail to the committed bytes
            _tail?.Trim(_offset);

            // rent new block
            _tail = CreateNewSegment(_tail, out _block);
            if (_head == null) _head = _tail;
            _offset = 0;
            _remaining = _block.Length;

            return _block;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Memory<T> IBufferWriter<T>.GetMemory(int sizeHint) => GetMemory(sizeHint);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Span<T> IBufferWriter<T>.GetSpan(int sizeHint) => GetMemory(sizeHint).Span;

        private protected abstract RefCountedSegment CreateNewSegment(RefCountedSegment tail, out Memory<T> memory);

        private protected abstract partial class RefCountedSegment : ReadOnlySequenceSegment<T>
        {
            private int _count;
            internal static readonly Action<ReadOnlySequence<T>> s_Release = value => Release(value);

            internal static void Release(ReadOnlySequence<T> value)
            {
                var end = value.End.GetObject() as RefCountedSegment;

                var node = value.Start.GetObject() as RefCountedSegment;

                while (node != null)
                {
                    var next = (RefCountedSegment)node.Next; // need to do this *first*, since Release nukes it
                    node.Release();
                    if (ReferenceEquals(node, end)) break;
                    node = next;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected RefCountedSegment(RefCountedSegment previous, Memory<T> memory)
            {
                _count = 1;
                IncrLiveCount();
                Memory = memory;
                if (previous != null)
                {
                    RunningIndex = previous.RunningIndex + previous.Memory.Length;
                    previous.Next = this;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Trim(int length) => Memory = Memory.Slice(0, length);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Release()
            {
                if (Volatile.Read(ref _count) != 0 && Interlocked.Decrement(ref _count) == 0)
                {
                    DecrLiveCount();
                    Memory = default;
                    Next = default; // break the chain, in case of dangling references
                    ReleaseImpl();
                }
            }

            static partial void IncrLiveCount();
            static partial void DecrLiveCount();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddRef()
            {
                if (Volatile.Read(ref _count) == 0) Throw.ObjectDisposed(nameof(RefCountedSegment));
                Interlocked.Increment(ref _count);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            protected abstract void ReleaseImpl();
        }


        internal sealed class MemoryPoolBufferWriter : BufferWriter<T>
        {
            private MemoryPool<T> _memoryPool;
            private protected override RefCountedSegment CreateNewSegment(RefCountedSegment previous, out Memory<T> memory)
            {
                var owner = _memoryPool.Rent(BlockSize);
                memory = owner.Memory;
                return new MemoryPoolRefCountedSegment(previous, owner, memory);
            }
                

            public MemoryPoolBufferWriter(MemoryPool<T> memoryPool, int blockSize)
                : base(Math.Min(memoryPool.MaxBufferSize, blockSize))
            {
                _memoryPool = memoryPool ?? MemoryPool<T>.Shared;
            }

            public override void Dispose()
            {
                _memoryPool = null;
                base.Dispose();
            }

            private sealed class MemoryPoolRefCountedSegment : RefCountedSegment
            {
                private readonly IMemoryOwner<T> _memoryOwner;
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public MemoryPoolRefCountedSegment(RefCountedSegment previous, IMemoryOwner<T> memoryOwner, Memory<T> memory) : base(previous, memory)
                    => _memoryOwner = memoryOwner;

                protected override void ReleaseImpl() => _memoryOwner.Dispose();
            }
        }

        internal sealed class ArrayPoolBufferWriter : BufferWriter<T>
        {
            private ArrayPool<T> _arrayPool;
            private protected override RefCountedSegment CreateNewSegment(RefCountedSegment previous, out Memory<T> memory)
            {
                var array = _arrayPool.Rent(BlockSize);
                memory = new Memory<T>(array, 0, BlockSize); // don't want to over-size here; contract is to respect BlockSize
                return new ArrayPoolRefCountedSegment(previous, _arrayPool, array);
            }

            public ArrayPoolBufferWriter(ArrayPool<T> arrayPool, int blockSize)
                : base(blockSize)
            {
                _arrayPool = arrayPool ?? ArrayPool<T>.Shared;
            }

            public override void Dispose()
            {
                _arrayPool = null;
                base.Dispose();
            }

            private sealed class ArrayPoolRefCountedSegment : RefCountedSegment
            {
                private readonly ArrayPool<T> _arrayPool;
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public ArrayPoolRefCountedSegment(RefCountedSegment previous, ArrayPool<T> arrayPool, Memory<T> memory) : base(previous, memory)
                    => _arrayPool = arrayPool;

                protected override void ReleaseImpl()
                {
                    T[] arr;
                    if (MemoryMarshal.TryGetArray(Memory, out var segment) && (arr = segment.Array) != null)
                        _arrayPool.Return(arr);
                }
            }
        }
    }
}
