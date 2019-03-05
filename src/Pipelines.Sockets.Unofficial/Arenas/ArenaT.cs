using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        /// Allow blittable types of the same size to share a pool of data (only applies to multi-type arenas)
        /// </summary>
        BlittableNonPaddedSharing = 16,
        /// <summary>
        /// Allow blittable types to all share a single pool of byte-data, using padding to align (only applies to multi-type arenas, and for reasonably sized types)
        /// </summary>
        BlittablePaddedSharing = 32
    }

    internal interface IArena : IDisposable
    {
        void Reset();
        Type ElementType { get; }
    }

    /// <summary>
    /// Represents a lifetime-bound allocator of multiple non-contiguous memory regions
    /// </summary>
    public sealed class Arena<T> : IDisposable, IArena
    {
        Type IArena.ElementType => typeof(T);

        /// <summary>
        /// The number of elements allocated since the last reset
        /// </summary>
        public long Allocated()
        {
            var current = _first;
            long total = 0;
            while (current != null)
            {
                if (ReferenceEquals(current, CurrentBlock))
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


        private readonly ArenaFlags _flags;
        private readonly int _blockSize;
        private int _allocatedCurrentBlock;
        private Block<T> _first, __current;

        private object _currentStartObj;
        private int _currentArrayFlag;
        internal Block<T> CurrentBlock
        {
            get => __current;
            private set
            {
                __current = value;
                _currentStartObj = null;
                _currentArrayFlag = 0;
                // we don't want to run TryGetArray every time we allocate memory, so we'll do it *once*
                // whenever the current segment changes, and we'll accept it as long as it is 0-based
                // and large enough
                if (value != null)
                {
                    _currentStartObj = __current.Allocation;
                    if (MemoryMarshal.TryGetArray<T>(__current.Memory, out var segment)
                    && segment.Offset == 0 && segment.Count >= __current.Length)
                    {
                        _currentStartObj = segment.Array;
                        _currentArrayFlag = Sequence.IsArrayFlag;
                    }
                }
            }
        }
        private readonly Allocator<T> _allocator;
        private readonly Func<long, long, long> _retentionPolicy;
        private long _lastRetention;

        /// <summary>
        /// Create a new Arena
        /// </summary>
        public Arena(ArenaOptions options = null, Allocator<T> allocator = null)
            : this(options, allocator, options?.BlockSizeBytes ?? 0) { }

        internal Arena(ArenaOptions options, Allocator<T> allocator, int blockSizeBytes)
        {
            if (Unsafe.SizeOf<T>() == 0) Throw.InvalidOperation("Cannot create an arena of a type with no size");

            if (options == null) options = ArenaOptions.Default;
            _flags = options.Flags;
            if (!PerTypeHelpers<T>.IsBlittable)
            {
                _flags &= ~(ArenaFlags.BlittableNonPaddedSharing | ArenaFlags.BlittablePaddedSharing | ArenaFlags.PreferUnmanaged | ArenaFlags.PreferPinned); // remove options that can't apply
                _flags |= ArenaFlags.ClearAtDispose | ArenaFlags.ClearAtReset; // add options that *must* apply
                // (in particular, we don't want references keeping objects alive; we won't be held to blame!)
            }

            if (allocator == null & (_flags & ArenaFlags.PreferUnmanaged) != 0)
                allocator = PerTypeHelpers<T>.PreferUnmanaged();
            if (allocator == null & (_flags & ArenaFlags.PreferPinned) != 0)
                allocator = PerTypeHelpers<T>.PreferPinned();

            _allocator = allocator ?? ArrayPoolAllocator<T>.Shared; // safest default for everything

            const int DefaultBlockSizeBytes = 128 * 1024, // 128KiB - gives good memory locality, avoids lots of split pages, and ensures LOH for managed allocators
                MinBlockSize = 1024;// aim for *at least* a 1KiB block; tiny arrays are a terrible idea in this context

            int blockSize = (blockSizeBytes > 0 ? blockSizeBytes : DefaultBlockSizeBytes) / Unsafe.SizeOf<T>(); // calculate the preferred block size
            _blockSize = Math.Max(blockSize, Math.Min(MinBlockSize / Unsafe.SizeOf<T>(), 1)); // calculate the *actual* size, after accounting for minimum sizes
  
            _first = CurrentBlock = AllocateAndAttachBlock(previous: null);
            _retentionPolicy = options.RetentionPolicy ?? RetentionPolicy.Default;
        }

        private Block<T> AllocateAndAttachBlock(Block<T> previous)
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
            var block = new Block<T>(allocation, previous == null ? 0 : previous.SegmentIndex + 1, previous);
            return block;
        }

        /// <summary>
        /// Allocate a (possibly non-contiguous) region of memory from the arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T> Allocate(int length) => Allocate(length, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Sequence<T> Allocate(int length, bool optimized)
        {
            // note: even for zero-length blocks, we'd rather have them start
            // at the start of the next block, for consistency; for consistent
            // *End()*, we also want end-terminated blocks to create a new block,
            // so that we always have first.End == second.Start
            // (this would self-correct later, if another segment got added, but if .End
            // is read *before* a new segment got added, it would never match
            // .Start; so... take a punt and extend it now)
            if(length > 0 & length <= RemainingCurrentBlock)
            {
                var offset = _allocatedCurrentBlock;
                _allocatedCurrentBlock += length;
                return optimized // should we remove the segment data? (makes access more efficient, but sometimes we need it)
                    ? Sequence<T>.TrustedSingleSegment(_currentStartObj, offset | _currentArrayFlag, length)
                    : Sequence<T>.TrustedSingleSegment(CurrentBlock, offset, length);
            }
            return SlowAllocate(length);
        }

        internal object GetAllocator() => _allocator;

        internal int AllocatedCurrentBlock => _allocatedCurrentBlock;

        internal Block<T> FirstBlock => _first;

        internal int RemainingCurrentBlock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CurrentBlock.Length - _allocatedCurrentBlock;
        }

        internal void SkipToNextPage()
        {
            if (AllocatedCurrentBlock == 0) return; // we're already there

            // burn whatever is left, if any
            if (RemainingCurrentBlock != 0) Allocate(RemainingCurrentBlock, optimized: false); // discard

            // now do a dummy zero-length allocation, which has the side-effect
            // of moving us to a new page
            SlowAllocate(0); // discard
        }

        /// <summary>
        /// Allocate a reference to a new instance from the arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Reference<T> Allocate() => Allocate(1, false).GetReference(0);

        // this is when there wasn't enough space in the current block
        private Sequence<T> SlowAllocate(int length)
        {
            if (length < 0) Throw.ArgumentOutOfRange(nameof(length));
            void MoveNextBlock()
            {
                CurrentBlock = CurrentBlock.Next ?? AllocateAndAttachBlock(previous: CurrentBlock);
                _allocatedCurrentBlock = 0;
            }

            // check to see if the first block is full (so we don't have an
            // allocation that starts at the EOF of a block; it would work, but
            // would be less efficient)
            if (CurrentBlock.Length <= _allocatedCurrentBlock) MoveNextBlock();

            var startBlock = CurrentBlock;
            int startOffset = _allocatedCurrentBlock;

            // now make sure we actually have blocks to cover that promise
            while (true)
            {
                var remainingThisBlock = CurrentBlock.Length - _allocatedCurrentBlock;
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

            return new Sequence<T>(startBlock, CurrentBlock, startOffset, _allocatedCurrentBlock);
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
                    if (block == CurrentBlock)
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
            CurrentBlock = _first;
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
            _first = CurrentBlock = null;
            _currentStartObj = null;
            while (current != null)
            {
                current.Dispose();
                current = current.Next;
            }
        }
    }
}
