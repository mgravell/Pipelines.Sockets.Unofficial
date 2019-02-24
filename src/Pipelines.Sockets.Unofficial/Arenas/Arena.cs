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
        private long _allocatedTotal, _capacityTotal;
        private readonly ArenaOptions _options;
        private readonly int _blockSize;
        private int _allocatedCurrentBlock;
        private Block<T> _first, _current;
        private readonly Allocator<T> _allocator;

        public Arena(Allocator<T> allocator = null, ArenaOptions options = default, int blockSize = 0)
        {
            _blockSize = blockSize <= 0 ? _allocator.DefaultBlockSize : blockSize;
            _allocator = allocator ?? ArrayPoolAllocator<T>.Shared;
            _options = options;
            _first = _current = AllocateDetachedBlock();
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Allocation<T> Allocate(int length)
        {
            if(_allocatedCurrentBlock + length <= _current.Length)
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
                if (remainingThisBlock <= length)
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

        public void Reset() => Reset(ClearAtReset);

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
