using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{

    /// <summary>
    /// Options that configure the behahaviour of an arena
    /// </summary>
    public sealed class ArenaOptions
    {
        /// <summary>
        /// The default arena configuration
        /// </summary>
        public static ArenaOptions Default { get; } = new ArenaOptions();

        /// <summary>
        /// The flags that are enabled for the arena
        /// </summary>
        public ArenaFlags Flags { get; }

        /// <summary>
        /// Tests an individual flag
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasFlag(ArenaFlags flag) => (Flags & flag) != 0;

        /// <summary>
        /// The block-size to suggest for new allocations in the arena
        /// </summary>
        public int BlockSizeBytes { get; }

        /// <summary>
        /// The policy for retaining allocations when memory requirements decrease
        /// </summary>
        public Func<long, long, long> RetentionPolicy { get; }

        /// <summary>
        /// Create a new ArenaOptions instance
        /// </summary>
        public ArenaOptions(ArenaFlags flags = ArenaFlags.None, int blockSizeBytes = 0, Func<long, long, long> retentionPolicy = null)
        {
            Flags = flags;
            BlockSizeBytes = blockSizeBytes;
            RetentionPolicy = retentionPolicy;
        }
    }

    /// <summary>
    /// Provides facilities to create new type-specific allocators for use in an arena
    /// </summary>
    public class AllocatorFactory
    {
        /// <summary>
        /// The default allocator factory
        /// </summary>
        public static AllocatorFactory Default { get; } = new AllocatorFactory();

        /// <summary>
        /// Suggest an allocator for any type
        /// </summary>
        protected internal virtual Allocator<T> SuggestAllocator<T>(ArenaOptions options) => null; // use defaults

        /// <summary>
        /// Suggest an allocator for a blittable type
        /// </summary>
        protected internal virtual Allocator<T> SuggestBlittableAllocator<T>(ArenaOptions options) where T : unmanaged
        {
            // if we're talking about bytes, then we *might* want to switch to a pinned allocator;
            // - we don't need to do this if unmanaged is being selected, since that will be pinned
            // - we don't need to do this if memory sharing is disabled
            if (typeof(T) == typeof(byte)
                && (options.Flags & (ArenaFlags.PreferUnmanaged | ArenaFlags.DisableBlittableSharedMemory)) == 0)
            {
                return PinnedArrayPoolAllocator<T>.Shared;
            }
            return null;
        }

        /// <summary>
        /// Suggest a per-type block size (in bytes) to use for allocations
        /// </summary>
        protected internal virtual int SuggestBlockSizeBytes<T>(ArenaOptions options) => options?.BlockSizeBytes ?? 0;
    }

    /// <summary>
    /// Represents a typed subset of data within an arena
    /// </summary>
    public abstract class OwnedArena<T> : IArena
    {
        Type IArena.ElementType => typeof(T);

        internal OwnedArena() { }

        /// <summary>
        /// Allocate a block of data
        /// </summary>
        public abstract Sequence<T> Allocate(int length);

        /// <summary>
        /// Allocate a single instance as a reference
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Reference<T> Allocate() => Allocate(1).GetReference(0);

        internal abstract object GetAllocator();
        internal abstract void Reset();
        internal abstract void Dispose();
        void IArena.Reset() => Reset();
        void IDisposable.Dispose() => Dispose();
    }

    internal sealed class SimpleOwnedArena<T> : OwnedArena<T>
    {
        internal Arena<T> Arena => _arena;
        private readonly Arena<T> _arena;
        public SimpleOwnedArena(Arena parent)
        {
            var factory = parent.Factory;
            var options = parent.Options;
            _arena = new Arena<T>(
                options: options,
                allocator: factory.SuggestAllocator<T>(options),
                blockSizeBytes: factory.SuggestBlockSizeBytes<T>(options));
        }
        public override Sequence<T> Allocate(int length) => _arena.Allocate(length);

        internal override object GetAllocator() => _arena.GetAllocator();

        internal override void Reset() => _arena.Reset();
        internal override void Dispose() => _arena.Dispose();
    }

    internal sealed class BlittableOwnedArena<T> : OwnedArena<T> where T : unmanaged
    {
        private readonly Arena<byte> _arena;
        public BlittableOwnedArena(Arena parent)
        {   // get the byte arena from the parent
            _arena = ((SimpleOwnedArena<byte>)parent.GetArena<byte>()).Arena;
        }

        internal override void Reset() {} // the T allocator doesn't own the data
        internal override void Dispose() { } // the T allocator doesn't own the data

        internal override object GetAllocator() => _arena.GetAllocator();

        private readonly List<Arena.MappedSegment<T>> _mappedSegments = new List<Arena.MappedSegment<T>>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Arena.MappedSegment<T> MapBlock(Block<byte> block)
            => _mappedSegments.Count > block.SegmentIndex ? _mappedSegments[block.SegmentIndex] : MapTo(block.SegmentIndex);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Arena.MappedSegment<T> MapTo(int index)
        {
            int neededCount = index + 1;
            Arena.MappedSegment<T> current = _mappedSegments.Count == 0 ? null : _mappedSegments[_mappedSegments.Count - 1];
            while (_mappedSegments.Count < neededCount)
            {
                var nextUnderlying = current == null ? _arena.FirstBlock : current.Underlying.Next;
                var next = new Arena.MappedSegment<T>(current, nextUnderlying);
                _mappedSegments.Add(next);
                current = next;
            }
            return current;
        }

        public override Sequence<T> Allocate(int length)
        {
            // we need to think about padding/alignment; to allow proper
            // interleaving, we'll always need to talk in alignments of <T>,
            // so that a page of bytes can always be rooted from the same
            // origin (so we only need to map the page once per T)
            var arena = _arena;
            var overlap = arena.AllocatedCurrentBlock % Unsafe.SizeOf<T>();
            if (overlap != 0)
            {
                var padding = Unsafe.SizeOf<T>() - overlap;

                // burn the padding, or to the end of the page - whichever comes first
                if (padding >= arena.RemainingCurrentBlock) arena.SkipToNextPage();
                else arena.Allocate(padding); // and drop it on the floor
            }

            // do we at least have space for *one* item? this is because we
            // want our root record to be on the correct starting page
            if (arena.RemainingCurrentBlock < Unsafe.SizeOf<T>()) arena.SkipToNextPage();

            // create our result, since we know the details
            Debug.Assert(arena.AllocatedCurrentBlock % Unsafe.SizeOf<T>() == 0, "should be aligned to T");
            var mappedBlock = MapBlock(arena.CurrentBlock);
            var result = new Sequence<T>(arena.AllocatedCurrentBlock / Unsafe.SizeOf<T>(), length, mappedBlock);

            // now we need to allocate and map the rest of the pages, taking care to
            // allow for padding at the end of pages

            while (length != 0)
            {
                int elementsThisPage = arena.RemainingCurrentBlock / Unsafe.SizeOf<T>();
                if (elementsThisPage == 0) Throw.InvalidOperation($"Unable to fit {typeof(T).Name} on the block");

                if (length == elementsThisPage)
                {
                    // burn the rest of the page, to ensure End/Start handling; then we're done
                    arena.SkipToNextPage();
                    length = 0;
                    break;
                }
                else if (length < elementsThisPage)
                {
                    // take what we need, and we're done
                    arena.Allocate(length * Unsafe.SizeOf<T>());
                    length = 0;
                    break;
                }
                else
                {
                    // not enough room on the current page; burn *all* of the
                    // current page, and map in the next
                    length -= elementsThisPage;
                    arena.SkipToNextPage();                    
                }
            }

            // make sure that we've mapped as far as the end; we don't actually need the result - we
            // just need to know that it has happened, as this connects the chain
            MapBlock(arena.CurrentBlock);

            Debug.Assert(length == 0);

            return result;
        }
    }

    /// <summary>
    /// An arena allocator that can allocate sequences for multiple data types
    /// </summary>
    public sealed class Arena : IDisposable
    {
        internal ArenaOptions Options { get; }
        internal AllocatorFactory Factory { get; private set; }

        /// <summary>
        /// Create a new Arena instance
        /// </summary>
        public Arena(ArenaOptions options = null, AllocatorFactory factory = null)
        {
            Options = options ?? ArenaOptions.Default;
            Factory = factory ?? AllocatorFactory.Default;
        }

        /// <summary>
        /// Release all resources associated with this arena
        /// </summary>
        public void Dispose()
        {
            Factory = null; // prevent any resurrections
            var owned = _ownedArenas;
            _ownedArenas = null;
            if (owned != null)
            {
                foreach (var pair in owned)
                {
                    try { pair.Value.Dispose(); } catch { } // best efforts
                }
                owned.Clear();
            }
        }

        /// <summary>
        /// Reset the memory allocated by this arena
        /// </summary>
        public void Reset()
        {
            var typed = _ownedArenas;
            if (typed != null)
            {
                foreach (var pair in _ownedArenas)
                {
                    pair.Value.Reset();
                }
            }
        }

        /// <summary>
        /// Allocate a single instance as a reference
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Reference<T> Allocate<T>() => GetArena<T>().Allocate(1).GetReference(0);

        /// <summary>
        /// Allocate a block of data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // aggressive mostly in the JIT-optimized cases!
        public Sequence<T> Allocate<T>(int length) => GetArena<T>().Allocate(length);
        
        /// <summary>
        /// Get a per-type arena inside a multi-type arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OwnedArena<T> GetArena<T>()
        {
            if (_lastArena?.ElementType == typeof(T)
                || _ownedArenas.TryGetValue(typeof(T), out _lastArena))
                return (OwnedArena<T>)_lastArena;
            return CreateAndAddArena<T>();
        }
        IArena _lastArena;

        private Dictionary<Type, IArena> _ownedArenas = new Dictionary<Type, IArena>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private OwnedArena<T> CreateAndAddArena<T>()
        {
            var arena = Create();
            _ownedArenas.Add(typeof(T), arena);
            _lastArena = arena;
            return arena;

            OwnedArena<T> Create()
            {
                if (PerTypeHelpers<T>.IsBlittable // we can do fun things for blittable types
                    && typeof(T) != typeof(byte) // for blittable scenarios, byte is the underlying type, so don't thunk it
                    && !Options.HasFlag(ArenaFlags.DisableBlittableSharedMemory) // if the caller wants
                    && Unsafe.SizeOf<T>() > 0 && Unsafe.SizeOf<T>() <= 256) // don't use too-large T because of excessive padding
                {
                    try
                    {
                        return (OwnedArena<T>)Activator.CreateInstance(
                            typeof(BlittableOwnedArena<int>).GetGenericTypeDefinition().MakeGenericType(typeof(T)),
                            args: new object[] { this });
                    }
                    catch { } // if bad things happen; give up
                }
                return new SimpleOwnedArena<T>(this);
            }
        }

        internal sealed class MappedSegment<T> : SequenceSegment<T> where T : unmanaged
        {
            public Block<byte> Underlying { get; }

            protected override Type GetUnderlyingType() => typeof(byte);

            protected override int GetSegmentIndex() => Underlying.SegmentIndex; // block index is always shared

#if DEBUG
            private readonly long _byteOffset;
            private readonly int _byteCount;
            protected override long ByteOffset => _byteOffset;
#endif
            
            public unsafe MappedSegment(MappedSegment<T> previous, Block<byte> underlying)
            {
                Underlying = underlying;
                MemoryManager<T> mapped;
                if (underlying.Allocation is IPinnedMemoryOwner<byte> rooted && rooted.Origin != null)
                {   // in this case, we can just cheat like crazy
                    mapped = new PinnedConvertingMemoryManager(rooted);
                }
                else
                {   // need to do everything properly; slower, but it'll work
                    mapped = new ConvertingMemoryManager(underlying.Allocation);
                }
                Memory = mapped.Memory;

#if DEBUG
                _byteCount = underlying.Length;
#endif
                if (previous != null)
                {   // we can't use "underlying" for this, because of padding etc
                    RunningIndex = previous.RunningIndex + previous.Length;
#if DEBUG
                    _byteOffset = previous.ByteOffset + previous._byteCount;
#endif
                    previous.Next = this;
                }
            }

            sealed unsafe class PinnedConvertingMemoryManager : MemoryManager<T>, IPinnedMemoryOwner<T>
            {
                private readonly T* _origin;
                private readonly int _length;
                public PinnedConvertingMemoryManager(IPinnedMemoryOwner<byte> rooted)
                {
                    _origin = (T*)rooted.Origin;
                    _length = MemoryMarshal.Cast<byte, T>(rooted.Memory.Span).Length;
                }

                void* IPinnedMemoryOwner<T>.Origin => _origin;

                public override Span<T> GetSpan() => new Span<T>(_origin, _length);

                public override MemoryHandle Pin(int elementIndex = 0) => new MemoryHandle(_origin + elementIndex);

                public override void Unpin() { }

                protected override void Dispose(bool disposing) { } // not our memory
            }
            sealed class ConvertingMemoryManager : MemoryManager<T>
            {
                private readonly IMemoryOwner<byte> _unrooted;
                public ConvertingMemoryManager(IMemoryOwner<byte> unrooted) => _unrooted = unrooted;

                public override Span<T> GetSpan() => MemoryMarshal.Cast<byte, T>(_unrooted.Memory.Span);

                public override MemoryHandle Pin(int elementIndex = 0) { Throw.NotSupported(); return default; }

                public override void Unpin() => Throw.NotSupported();

                protected override void Dispose(bool disposing) { } // not our memory
            }
        }
    }
}