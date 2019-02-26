using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Flags that impact behaviour of the arena
    /// </summary>
    [Flags]
    public enum ArenaOptions
    {
        /// <summary>
        /// None
        /// </summary>
        None,
        /// <summary>
        /// Allocations are cleared at each reset (and when initially allocated), so that they are always wiped before use
        /// </summary>
        ClearAtReset,
        /// <summary>
        /// Allocations are cleared when the arean is disposed, so that the contents are not released back to the underlying allocator
        /// </summary>
        ClearAtDispose,
    }

    /// <summary>
    /// Represents a lifetime-bound allocator of multiple non-contiguous memory regions
    /// </summary>
    public sealed class Arena<T> : IDisposable
    {
        /// <summary>
        /// The number of elements allocated since the last reset
        /// </summary>
        public long Allocated => _allocatedTotal;

        /// <summary>
        /// The current capacity of all regions tracked by the arena
        /// </summary>
        public long Capacity => _capacityTotal;

        private long _allocatedTotal, _capacityTotal;
        private readonly ArenaOptions _options;
        private readonly int _blockSize;
        private int _allocatedCurrentBlock;
        private Block<T> _first, _current;
        private readonly Allocator<T> _allocator;
        private readonly Func<long, long, long> _retentionPolicy;
        private long _lastRetention;

        /// <summary>
        /// Create a new Arena
        /// </summary>
        public Arena(Allocator<T> allocator = null, ArenaOptions options = default, int blockSize = 0, Func<long,long, long> retentionPolicy = null)
        {
            _allocator = allocator ?? ArrayPoolAllocator<T>.Shared;
            _blockSize = blockSize <= 0 ? _allocator.DefaultBlockSize : blockSize;
            _options = options;
            _first = _current = AllocateDetachedBlock();
            _retentionPolicy = retentionPolicy ?? RetentionPolicy.Default;
        }

        private Block<T> AllocateDetachedBlock()
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
            var block = new Block<T>(allocation, _capacityTotal);
            _capacityTotal += block.Length;
            return block;
        }

        /// <summary>
        /// Allocate a (possibly non-contiguous) region of memory from the arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation<T> Allocate(int length)
        {
            // note: even for zero-length blocks, we'd rather have them start
            // at the start of the next block, for consistency
            if(length > 0 & _allocatedCurrentBlock + length <= _current.Length)
            {
                var offset = _allocatedCurrentBlock;
                _allocatedCurrentBlock += length;
                _allocatedTotal += length;
                return new Allocation<T>(_current, offset, length);
            }
            return SlowAllocate(length);
        }

        // this is when there wasn't enough space in the current block
        private Allocation<T> SlowAllocate(int length)
        {
            void ThrowInvalidLength() => throw new ArgumentOutOfRangeException(nameof(length));
            if (length < 0) ThrowInvalidLength();
            void MoveNextBlock()
            {
                _current = _current.Next ?? (_current.Next = AllocateDetachedBlock());
                _allocatedCurrentBlock = 0;
            }

            // check to see if the first block is full (so we don't have an
            // allocation that starts at the EOF of a block; it would work, but
            // would be less efficient)
            if (_current.Length <= _allocatedCurrentBlock) MoveNextBlock();

            var result = new Allocation<T>(_current, _allocatedCurrentBlock, length);
            _allocatedTotal += length; // we're going to allocate everything

            // now make sure we actually have blocks to cover that promise
            while (true)
            {
                var remainingThisBlock = _current.Length - _allocatedCurrentBlock;
                if (remainingThisBlock >= length)
                {
                    _allocatedCurrentBlock += length;
                    break; // that's all we need, thanks
                }
                else
                {
                    length -= remainingThisBlock; // consume all of this block
                    MoveNextBlock(); // and we're going to need another
                }
            }

            return result;
        }

        bool ClearAtReset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_options & ArenaOptions.ClearAtReset) != 0;
        }

        /// <summary>
        /// Resets the arena; all current allocations should be considered invalid - new allocations may overwrite them
        /// </summary>
        public void Reset()
        {
            long allocated = _allocatedTotal;
            Reset(ClearAtReset);

            var retain = _retentionPolicy(_lastRetention, allocated);
            Trim(retain);
            _lastRetention = retain;
        }

        private void Trim(long retain) { } // not yet implemented

        private void Reset(bool clear)
        {
            if (clear)
            {
                var block = _first;
                while (block != null)
                {
                    if (block == _current)
                    {
                        _allocator.Clear(block.Allocation, _allocatedCurrentBlock);
                        block = null;
                    }
                    else
                    {
                        _allocator.Clear(block.Allocation, block.Length);
                        block = block.Next;
                    }
                }
            }
            _current = _first;
            _allocatedCurrentBlock = 0;
            _allocatedTotal = 0;
        }

        /// <summary>
        /// Releases all resources associated with the arena
        /// </summary>
        public void Dispose()
        {
            if ((_options & ArenaOptions.ClearAtDispose) != 0)
                try { Reset(true); } catch { } // best effort only

            var current = _first;
            _first = _current = null;
            while (current != null)
            {
                current.Dispose();
                current = current.Next;
            }
        }
    }
}
