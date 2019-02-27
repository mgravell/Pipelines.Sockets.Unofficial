using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Flags that impact behaviour of the arena
    /// </summary>
    [Flags]
    public enum ArenaFlags
    {
        /// <summary>
        /// None
        /// </summary>
        None,
        /// <summary>
        /// Allocations are cleared at each reset (and when initially allocated), so that they are always wiped before use
        /// </summary>
        ClearAtReset = 1,
        /// <summary>
        /// Allocations are cleared when the arean is disposed, so that the contents are not released back to the underlying allocator
        /// </summary>
        ClearAtDispose = 2,
        /// <summary>
        /// When possible, and when no allocator is explicitly provided; prefer using unmanaged memory
        /// </summary>
        PreferUnmanaged = 4,
        /// <summary>
        /// When possible, use pinned memory
        /// </summary>
        PreferPinned = 8,
        /// <summary>
        /// Disallow blittable types from using a single pool of memory
        /// </summary>
        DisableBlittableSharedMemory = 16
    }

    internal interface IArena : IDisposable
    {
        long Allocated();
        long Capacity { get; }
        void Reset();
    }

    /// <summary>
    /// Represents a lifetime-bound allocator of multiple non-contiguous memory regions
    /// </summary>
    public sealed class Arena<T> : IDisposable, IArena
    {
        /// <summary>
        /// The number of elements allocated since the last reset
        /// </summary>
        public long Allocated()
        {
            var current = _first;
            long total = 0;
            while(current != null)
            {
                if(ReferenceEquals(current, _current))
                {
                    total += _allocatedCurrentBlock;
                    break;
                }
                else
                {
                    total += current.Length;
                    current = current.Next;
                }
            }
            return total;
        }
        

        /// <summary>
        /// The current capacity of all regions tracked by the arena
        /// </summary>
        public long Capacity => _capacityTotal;

        private long _capacityTotal;
        private readonly ArenaFlags _flags;
        private readonly int _blockSize;
        private int _allocatedCurrentBlock;
        private Block<T> _first, _current;
        private readonly Allocator<T> _allocator;
        private readonly Func<long, long, long> _retentionPolicy;
        private long _lastRetention;

        /// <summary>
        /// Create a new Arena
        /// </summary>
        public Arena(ArenaOptions options = null, Allocator<T> allocator = null)
        {
            
            if (options == null) options = ArenaOptions.Default;
            _flags = options.Flags;
            if (!PerTypeHelpers<T>.IsBlittable)
            {
                _flags &= ~(ArenaFlags.DisableBlittableSharedMemory | ArenaFlags.PreferUnmanaged | ArenaFlags.PreferPinned); // remove options that can't apply
                _flags |= ArenaFlags.ClearAtDispose | ArenaFlags.ClearAtReset; // add options that *must* apply
                // (in particular, we don't want references keeping objects alive; we won't be held to blame!)
            }

            if (allocator == null & (_flags & ArenaFlags.PreferUnmanaged) != 0)
                allocator = PerTypeHelpers<T>.PreferUnmanaged();
            if (allocator == null & (_flags & ArenaFlags.PreferPinned) != 0)
                allocator = PerTypeHelpers<T>.PreferPinned();

            _allocator = allocator ?? ArrayPoolAllocator<T>.Shared; // safest default for everything

            _blockSize = options.BlockSize <= 0 ? _allocator.DefaultBlockSize : options.BlockSize;
            _first = _current = AllocateDetachedBlock();
            _retentionPolicy = options.RetentionPolicy ?? RetentionPolicy.Default;
        }

        private Block<T> AllocateDetachedBlock()
        {
            var allocation = _allocator.Allocate(_blockSize);
            if (allocation == null) Throw.InvalidOperation("The allocator provided an empty range");
            if (allocation.Memory.IsEmpty)
            {
                try { allocation.Dispose(); } catch { } // best efforts
                Throw.InvalidOperation("The allocator provided an empty range");
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
        public Sequence<T> Allocate(int length)
        {
            // note: even for zero-length blocks, we'd rather have them start
            // at the start of the next block, for consistency; for consistent
            // *End()*, we also want end-terminated blocks to create a new block,
            // so that we always have first.End() == second.Start()
            if(length > 0 & length < RemainingCurrentBlock)
            {
                var offset = _allocatedCurrentBlock;
                _allocatedCurrentBlock += length;
                return new Sequence<T>(offset, length, _current);
            }
            return SlowAllocate(length);
        }

        internal int AllocatedCurrentBlock => _allocatedCurrentBlock;

        internal Block<T> CurrentBlock => _current;
        internal Block<T> FirstBlock => _first;

        internal int RemainingCurrentBlock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current.Length - _allocatedCurrentBlock;
        }

        internal void SkipToNextPage()
        {
            if (AllocatedCurrentBlock == 0) return; // we're already there

            // burn whatever is left, if any
            if (RemainingCurrentBlock != 0) Allocate(RemainingCurrentBlock); // discard

            // now do a dummy zero-length allocation, which has the side-effect
            // of moving us to a new page
            SlowAllocate(0);
        }

        /// <summary>
        /// Allocate a reference to a new instance from the arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Reference<T> Allocate() => Allocate(1).GetReference(0);

        // this is when there wasn't enough space in the current block
        private Sequence<T> SlowAllocate(int length)
        {
            if (length < 0) Throw.ArgumentOutOfRange(nameof(length));
            void MoveNextBlock()
            {
                _current = _current.Next ?? (_current.Next = AllocateDetachedBlock());
                _allocatedCurrentBlock = 0;
            }

            // check to see if the first block is full (so we don't have an
            // allocation that starts at the EOF of a block; it would work, but
            // would be less efficient)
            if (_current.Length <= _allocatedCurrentBlock) MoveNextBlock();

            var result = new Sequence<T>(_allocatedCurrentBlock, length, _current);

            // now make sure we actually have blocks to cover that promise
            while (true)
            {
                var remainingThisBlock = _current.Length - _allocatedCurrentBlock;
                if (length == remainingThisBlock)
                {
                    MoveNextBlock(); // burn the page, to ensure we have everything covererd
                    break; // done
                }
                else if (length < remainingThisBlock)
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
            get => (_flags & ArenaFlags.ClearAtReset) != 0;
        }

        /// <summary>
        /// Resets the arena; all current allocations should be considered invalid - new allocations may overwrite them
        /// </summary>
        public void Reset()
        {
            long allocated = Allocated();
            Reset(ClearAtReset);

            var retain = _retentionPolicy(_lastRetention, allocated);
            Trim(retain);
            _lastRetention = retain;
        }

#pragma warning disable IDE0060 // unused arg
        private void Trim(long retain) { } // not yet implemented
#pragma warning restore IDE0060

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
        }

        /// <summary>
        /// Releases all resources associated with the arena
        /// </summary>
        public void Dispose()
        {
            if ((_flags & ArenaFlags.ClearAtDispose) != 0)
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
