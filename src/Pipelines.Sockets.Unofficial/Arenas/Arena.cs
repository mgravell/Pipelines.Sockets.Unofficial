using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    [Flags]
    public enum ArenaOptions
    {
        None,
        ClearAtReset,
        ClearAtDispose,
    }
    public sealed class Arena<T> : IDisposable
    {
        private readonly Allocator<T> _allocator;
        private readonly ArenaOptions _options;
        private readonly int _blockSize;
        private Stand<T> _head;

        public Arena(Allocator<T> allocator = null, ArenaOptions options = default, int blockSize = 0)
        {
            _blockSize = blockSize <= 0 ? _allocator.DefaultBlockSize : blockSize;
            _allocator = allocator ?? ArrayPoolAllocator<T>.Shared;
            _options = options;
        }

        private Stand<T> AllocateDetachedStand()
        {
            void ThrowAllocationFailure() => throw new InvalidOperationException("The allocator provided an empty range");
            var allocation = _allocator.Allocate(_blockSize);
            if (allocation == null) ThrowAllocationFailure();
            if (allocation.Memory.IsEmpty)
            {
                try { allocation.Dispose(); } catch { } // best efforts
                ThrowAllocationFailure();
            }
            if (ClearAtReset) // this really means "before use", so...
                _allocator.Clear(allocation, allocation.Memory.Length);
            return new Stand<T>(allocation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Stand<T> Head() => _head ?? (_head = AllocateDetachedStand());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation<T> Allocate(long length)
        {
            var head = Head();
            return head.Free <= length
                ? AsSingleBlock(head.Take(unchecked((int)length)))
                : SlowAllocate(head, length);
        }

        public Allocation<T> AsSingleBlock(Memory<T> block)
        {
            var segment = GetSegment(1);
            Debug.Assert(segment.Count == 1, "expected unit length");
            segment.Array[segment.Offset] = block;
            return new Allocation<T>(block.Length, segment);
        }

        ArraySegment<Memory<T>> GetSegment(int length)
            => new ArraySegment<Memory<T>>(new Memory<T>[length]); // TODO: implement pool

        private Allocation<T> SlowAllocate(Stand<T> head, long length)
        {
            // not enough in the "head"; this *count* be because the head is empty
            if (head.Free == 0)
            {
                // it was indeed; so allocate a new stand and attach it
                var newStand = AllocateDetachedStand();
                newStand.Next = head;
                _head = head = newStand;

                // which means the new stand *might* be big enough
                if (head.Free <= length)
                    return AsSingleBlock(head.Take(unchecked((int)length)));
            }

            int stands = 1; // we'll start with the newest stand, which we now know has space
            long remaining = length - head.Free;

            // allocate new stands as needed; note that we're going to
            // record them in *reverse* order for now; we will
            // reverse it again as we attach them
            Stand<T> newestStand = null;
            while (remaining > 0)
            {
                var newStand = AllocateDetachedStand();
                remaining -= Math.Min(remaining, newStand.Free);
                newStand.Next = newestStand;
                newestStand = newStand;
                stands++;
            }
            Debug.Assert(newestStand != null, "we should have allocated at least one new stand");

            // so now we've got some number of new stands, but
            // we want to consume them in reverse order
            var segment = GetSegment(stands);
            Debug.Assert(segment.Count == stands, $"expected length {stands}, got {segment.Count}");
            var arr = segment.Array;
            int offset = segment.Offset;

            var alloc = head.Take(remaining);
            arr[offset++] = alloc;
            remaining = length - alloc.Length;

            newestStand = Reverse(newestStand);
            while(remaining >= 0)
            {
                Debug.Assert(newestStand != null, "expected an allocated stand");
                var nextStand = newestStand.Next;
                newestStand.Next = head;
                alloc = newestStand.Take(remaining);
                arr[offset++] = alloc;
                remaining = length - alloc.Length;

                head = _head = newestStand; // attach
                newestStand = nextStand;
            }
            Debug.Assert(newestStand == null, "expected to exhaust the new stands");
            Debug.Assert(offset == segment.Offset + segment.Count, "expected to fill the segment");

            return new Allocation<T>(length, segment);

            Stand<T> Reverse(Stand<T> node)
            {
                throw new NotImplementedException();
            }
        }

        bool ClearAtReset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_options & ArenaOptions.ClearAtReset) != 0;
        }

        public void Reset() => Reset(ClearAtReset);

        private void Reset(bool clear)
        {
            var current = _head;
            while (current != null)
            {
                if (clear)
                {
                    var taken = current.Taken;
                    if (taken != 0) _allocator.Clear(current.Block, taken);
                }
                current.Reset();
                current = current.Next;
            }
        }
        public void Dispose()
        {
            if ((_options & ArenaOptions.ClearAtDispose) != 0)
                try { Reset(true); } catch { } // best effort only

            var current = _head;
            _head = null;
            while (current != null)
            {
                try { current.Dispose(); } catch { } // best effort only
                current = current.Next;
            }
        }
    }
}
