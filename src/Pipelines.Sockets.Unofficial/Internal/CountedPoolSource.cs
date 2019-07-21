using System;
using System.Buffers;
using System.Threading;

namespace Pipelines.Sockets.Unofficial.Internal
{
    internal sealed class CountedPoolSource<T> : IDisposable
    {
        public void Dispose() => Release(_available, true);

        private ReadOnlySequence<T> _available;

        private readonly MemoryPool<T> _pool;
        private readonly int _chunkSize;
        public CountedPoolSource(MemoryPool<T> pool = null, int chunkSize = -1)
        {
            _pool = pool ?? MemoryPool<T>.Shared;
            if (chunkSize <= 0) chunkSize = FrameConnectionOptions.DefaultBlockSize;
            _chunkSize = Math.Min(chunkSize, _pool.MaxBufferSize);
            Console.WriteLine(_chunkSize);
            var segment = new RefCountedMemoryOwner<T>(null, _pool.Rent(_chunkSize));
            _available = new ReadOnlySequence<T>(segment, 0, segment, segment.Memory.Length);
        }

        public ReadOnlySequence<T> Peek(int count)
        {
            var available = _available.Length;
            if (available < count)
            {
                var last = (RefCountedMemoryOwner<T>)_available.End.GetObject();
                do
                {
                    Console.WriteLine($"requestion {_chunkSize}...");
                    var chunk = _pool.Rent(_chunkSize);
                    Console.WriteLine($"got {chunk.Memory.Length}, chaining...");
                    last = new RefCountedMemoryOwner<T>(last, chunk);
                    available += last.Memory.Length;
                } while (available < count);

                Console.WriteLine("creating new sequence");
                var start = _available.Start;
                _available = new ReadOnlySequence<T>((RefCountedMemoryOwner<T>)start.GetObject(), start.GetInteger(),
                    last, last.Memory.Length);
                Console.WriteLine($"sequence length now: {_available.Length}");
            }
            return _available.Slice(0, count);
        }

        public ReadOnlySequence<T> Take(int count, out Action<ReadOnlySequence<T>> onDisposed)
        {
            if (count == 0)
            {
                onDisposed = null;
                return default;
            }
            if (count < 0 | count > _available.Length) throw new ArgumentOutOfRangeException(nameof(count));

            onDisposed = s_ReleaseChain;
            var split = _available.GetPosition(count);
            var slice = _available.Slice(_available.Start, split);
            _available = _available.Slice(split);
            AddRef(slice);
            return slice;
        }

        private static readonly Action<ReadOnlySequence<T>> s_ReleaseChain = ros => Release(ros);
        private static void Release(in ReadOnlySequence<T> ros, bool ifZero = false)
        {
            var node = ros.Start.GetObject() as RefCountedMemoryOwner<T>;
            var lastNode = ros.End.GetObject();
            while (node != null)
            {
                node.Release(ifZero);
                node = ReferenceEquals(node, lastNode) ? null : (RefCountedMemoryOwner<T>)node.Next;
            }
        }
        private static void AddRef(in ReadOnlySequence<T> ros)
        {
            var node = ros.Start.GetObject() as RefCountedMemoryOwner<T>;
            var lastNode = ros.End.GetObject();
            while (node != null)
            {
                node.AddRef();
                node = ReferenceEquals(node, lastNode) ? null : (RefCountedMemoryOwner<T>)node.Next;
            }
        }
    }

    internal sealed class RefCountedMemoryOwner<T> : ReadOnlySequenceSegment<T>
    {
        private IMemoryOwner<T> _owner;
        private int _refCount;

        public RefCountedMemoryOwner(RefCountedMemoryOwner<T> previous, IMemoryOwner<T> owner)
        {
            base.Memory = owner.Memory;
            _owner = owner;
            if (previous != null)
            {
                RunningIndex = previous.RunningIndex + previous.Memory.Length;
                previous.Next = this;
            }

        }
        public void AddRef() => Interlocked.Increment(ref _refCount);
        public void Release(bool ifZero = false)
        {
            switch (Interlocked.Decrement(ref _refCount))
            {
                case 0:
                    var tmp = Interlocked.Exchange(ref _owner, null);
                    tmp?.Dispose();
                    Memory = default;
                    break;
                case -1:
                    if (ifZero) goto case 0;
                    break;
            }
        }
    }
}
