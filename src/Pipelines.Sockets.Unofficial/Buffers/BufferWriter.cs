using Pipelines.Sockets.Unofficial.Arenas;
using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Pipelines.Sockets.Unofficial.Buffers
{
    /// <summary>
    /// Represents a <typeparamref name="T"/> with lifetime management over the data
    /// </summary>
    public readonly struct Owned<T> : IDisposable
    {
        private readonly Action<T> _onDispose;
        private readonly T _value;
        /// <summary>
        /// The data represented by this value
        /// </summary>
        public T Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _value;
        }

        /// <summary>
        /// Release any resources associated with this value
        /// </summary>
        public void Dispose() => _onDispose?.Invoke(Value);

        /// <summary>
        /// Create a new instance with a call-defined lifetime management callback
        /// </summary>
        public Owned(T value, Action<T> onDispose)
        {
            _value = value;
            _onDispose = onDispose;
        }

        /// <summary>
        /// Access the underlying data directly
        /// </summary>
        /// <param name="value"></param>
        public static implicit operator T(in Owned<T> value) => value._value;
        /// <summary>
        /// Represent an existing value with dummy lifetime management
        /// </summary>
        public static implicit operator Owned<T>(in T value) => new Owned<T>(value, null);
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

        /// <summary>
        /// Gets the amount of data buffered by the writer
        /// </summary>
        public long Length => _head == null ? 0 :
            ((_tail.RunningIndex + _tailOffset) - (_head.RunningIndex + _headOffset));

#pragma warning disable IDE0069
        // field never disposed - not needed, this is just 
        // from IMemoryOwner<T> on segment; see also DiscardChain
        private RefCountedSegment _head, _tail, _final;
#pragma warning restore IDE0069

        private int _headOffset, _tailOffset, _tailRemaining;

        private protected BufferWriter(int blockSize)
        {
            if (blockSize <= 0) Throw.ArgumentOutOfRange(nameof(blockSize));
            BlockSize = blockSize;
        }

        /// <summary>
        /// Release all resources associate with this instance
        /// </summary>
        public virtual void Dispose() => DiscardChain();

        private void DiscardChain()
        {
            // release anything that is in the pending buffer
            var node = _head;
            _head = _tail = _final = null;
            _tailRemaining = _tailOffset = _headOffset = 0;
            while (node != null)
            {
                var next = (RefCountedSegment)node.Next; // need to do this *first*, since Release nukes it
                node.Release();
                node = next;
            }
        }

        /// <summary>
        /// Gets the currently buffered data as a sequence of read-write buffer-segments
        /// </summary>
        public Sequence<T> GetBuffer()
        {
            // is it a trivial sequence? (this includes the null case)
            if (ReferenceEquals(_head, _tail) && _tailOffset == _headOffset) return default;

            // create a new sequence from the current chain
            var value = new Sequence<T>(_head, _tail, _headOffset, _tailOffset);
            return value.IsEmpty ? default : value;
        }

        /*
        /// <summary>
        /// Get the logical start of the committed data
        /// </summary>
        public SequencePosition Start => _head == null ? default : new SequencePosition(_head, _headOffset);

        /// <summary>
        /// Get the logical end of the committed data
        /// </summary>
        public SequencePosition End => _head == null ? default : new SequencePosition(_tail, _tailOffset);
        */

        /// <summary>
        /// Gets some subset of the currently buffered data as a sequence of read-only buffer-segments (with lifetime management); you
        /// can continue to append data after calling <c>Flush</c> - any additional data will form a new payload
        /// that can be fetched by the next call to <c>Flush</c>
        /// </summary>
        public Owned<ReadOnlySequence<T>> Flush(long count)
        {
            if (count < 0) Throw.ArgumentOutOfRange(nameof(count));
            return FlushImpl(count);
        }

        /// <summary>
        /// Gets the currently buffered data as a sequence of read-only buffer-segments (with lifetime management); you
        /// can continue to append data after calling <c>Flush</c> - any additional data will form a new payload
        /// that can be fetched by the next call to <c>Flush</c>
        /// </summary>
        public Owned<ReadOnlySequence<T>> Flush() => FlushImpl(-1);

        private Owned<ReadOnlySequence<T>> FlushImpl(long count)
        {
            if (count == 0) return default;

            var value = GetBuffer();
            if (count > 0) value = value.Slice(0, count);
            else if (value.IsEmpty) return default; // "all of nothing"
            var end = value.End;

            _head = (RefCountedSegment) end.GetObject();
            _headOffset = end.GetInteger();
            // we have some capacity left in the head; we'll keep that one, so:
            // increment the tail and continue from there
            // this is a short-cut for:
            // - AddRef on all the elements in the result
            // - Release on everything in the old chain *except* the new head (if it will be shared)
            if (_headOffset != 0) _head.AddRef();

            return new Owned<ReadOnlySequence<T>>(value, RefCountedSegment.s_Release);
        }

        /// <summary>
        /// Commit a number of bytes to the underyling buffer
        /// </summary>
        public void Advance(int count)
        {
            if (count < 0)
            {
                Throw.ArgumentOutOfRange(nameof(count));
            }
            else if (count == 0)
            {
                // nothing to do
            }
            else if (count <= _tailRemaining)
            {
                // single-block take
                _tailRemaining -= count;
                _tailOffset += count;
            }
            else
            {
                // multi-block take
                SlowAdvance(count);
            }
        }

        internal string GetState() // used for testing only; doesn't need to be efficient
        {
            if (_tail == null) return "(nil)";
            var sb = new StringBuilder();
            sb.Append($"[{_head.RunningIndex + _headOffset},{_tail.RunningIndex + _tailOffset}) - {_tailRemaining}/{(_final.RunningIndex + _final.Length) - (_tail.RunningIndex + _tailOffset)} available; counts: ");
            var node = _head;
            while(node != null)
            {
                sb.Append(node.RefCount).Append('/');
                node = (RefCountedSegment)node.Next;
            }
            return sb.ToString();
        }

        private void SlowAdvance(int count)
        {
            // consume the current node
            count -= _tailRemaining;
            var node = _tail.Next;

            // consume nodes until the required data fits
            while (node != null & count > node.Length)
            {
                count -= node.Length;
                node = node.Next;
            }
            if (node == null) Throw.ArgumentOutOfRange(nameof(count));

            // consume part of the final node
            _tail = (RefCountedSegment)node;
            _tailOffset = count;
            _tailRemaining = node.Length - count;
        }

        /// <summary>
        /// Access a contiguous write buffer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetSpan(int sizeHint)
            => _tailRemaining >= sizeHint ? _tail.Memory.Span.Slice(_tailOffset) : GetMemorySlow(sizeHint).Span;

        /// <summary>
        /// Access a contiguous write buffer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<T> GetMemory(int sizeHint)
            => _tailRemaining >= sizeHint ? _tail.Memory.Slice(_tailOffset) : GetMemorySlow(sizeHint);

        /// <summary>
        /// Access a non-contiguous write buffer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T> GetSequence(int sizeHint)
        {
            long availableLength;
            if (_final == null)
            {
                availableLength = 0;
            }
            else
            {
                var seq = new Sequence<T>(_tail, _final, _tailOffset, _final.Length);
                availableLength = seq.Length;
                if (availableLength >= sizeHint) return seq;
            }
            return GetSequenceSlow((int)(sizeHint - availableLength));
        }

        private Sequence<T> GetSequenceSlow(int extraSpaceNeeded)
        {
            do
            {
                _final = CreateNewSegment(_final);
                if (_head == null)
                {
                    _head = _tail = _final;
                    _tailOffset = 0;
                    _tailRemaining = _tail.Length;
                }
                extraSpaceNeeded -= _final.Length;
            } while (extraSpaceNeeded > 0);
            return new Sequence<T>(_tail, _final, _tailOffset, _final.Length);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Memory<T> GetMemorySlow(int sizeHint)
        {
            Debug.Assert(sizeHint > _tailRemaining, "shouldn't have called slow impl");
            if (sizeHint > BlockSize) Throw.ArgumentOutOfRange(nameof(sizeHint));

            // limit the tail to the committed bytes
            _tail?.Trim(_tailOffset);
            bool wasHead = ReferenceEquals(_tail, _head);

            var next = (RefCountedSegment)_tail?.Next;
            if (next != null)
            {   // we already have an onwards chain
                _tail = next;
            }
            else
            {   // rent new block in the chain
                _final = _tail = CreateNewSegment(_final);
            }

            if (_head == null) { _head = _tail; }
            else if (wasHead && _head.Length == _headOffset)
            {
                // the old head had capacity that we couldn't use and was trimmed; we can
                // release the old head and start from the next - this avoids starting with
                // a zero-length segment, which could cause leaks because Sequence<T>
                // *rolls forwards over them* and they would therefore never get released
                var oldHead = _head;
                _head = (RefCountedSegment)oldHead.Next;
                _headOffset = 0;
                oldHead.Release();
            }
            _tailOffset = 0;
            _tailRemaining = _tail.Length;

            return _tail.Memory;
        }

        private protected abstract RefCountedSegment CreateNewSegment(RefCountedSegment previous);

        internal abstract partial class RefCountedSegment : SequenceSegment<T>
        {
            private int _count;
            internal static readonly Action<ReadOnlySequence<T>> s_Release = value => Release(value);

            internal int RefCount => Volatile.Read(ref _count);

            internal static void Release(ReadOnlySequence<T> value)
            {
                if (value.IsEmpty) return;

                var start = value.Start;
                var len = value.Length + start.GetInteger(); // we'll be counting the full length of every segment, including the first
                var node = value.Start.GetObject() as RefCountedSegment;

                while (len > 0 & node != null)
                {
                    len -= node.Length;
                    var next = (RefCountedSegment)node.Next; // need to do this *first*, since Release nukes it
                    node.Release();
                    node = next;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected RefCountedSegment(Memory<T> memory, RefCountedSegment previous)
                : base(memory, previous)
            {
                _count = 1;
                IncrLiveCount();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Release()
            {
                if (Volatile.Read(ref _count) > 0 && Interlocked.Decrement(ref _count) == 0)
                {
                    DecrLiveCount();
                    Memory = default;
                    DetachNext(); // break the chain, in case of dangling references
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
            private protected override RefCountedSegment CreateNewSegment(RefCountedSegment previous)
            {
                var owner = _memoryPool.Rent(BlockSize);
                return new MemoryPoolRefCountedSegment(owner, previous);
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
                public MemoryPoolRefCountedSegment(IMemoryOwner<T> memoryOwner, RefCountedSegment previous)
                    : base(memoryOwner.Memory, previous)
                    => _memoryOwner = memoryOwner;

                protected override void ReleaseImpl() => _memoryOwner.Dispose();
            }
        }

        internal sealed class ArrayPoolBufferWriter : BufferWriter<T>
        {
            private ArrayPool<T> _arrayPool;
            private protected override RefCountedSegment CreateNewSegment(RefCountedSegment previous)
            {
                var array = _arrayPool.Rent(BlockSize);
                return new ArrayPoolRefCountedSegment(_arrayPool, array, previous);
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
                public ArrayPoolRefCountedSegment(ArrayPool<T> arrayPool, Memory<T> memory, RefCountedSegment previous)
                    : base(memory, previous)
                    => _arrayPool = arrayPool;

                protected override void ReleaseImpl()
                {
                    T[] arr;
                    if (MemoryMarshal.TryGetArray<T>(Memory, out var segment) && (arr = segment.Array) != null)
                        _arrayPool.Return(arr);
                }
            }
        }
    }
}
