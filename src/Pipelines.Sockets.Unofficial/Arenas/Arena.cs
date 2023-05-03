using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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

        private const ArenaFlags DefaultFlags = ArenaFlags.BlittableNonPaddedSharing; // good compromise betweeen perf and memory

        /// <summary>
        /// Create a new ArenaOptions instance
        /// </summary>
        public ArenaOptions(ArenaFlags flags = DefaultFlags, int blockSizeBytes = 0, Func<long, long, long> retentionPolicy = null)
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
            // if the user prefers unmanaged: we don't need to do anything - the arena will already select this
            if (options.HasFlag(ArenaFlags.PreferUnmanaged)) return null;

            // if we're talking about bytes, then we *might* want to switch to a pinned allocator if
            // the user hasn't disabled padded blittables
            if (typeof(T) == typeof(byte) && options.HasFlag(ArenaFlags.BlittablePaddedSharing))
            {
                return PinnedArrayPoolAllocator<T>.Shared;
            }

            // and if the user hasn't disabled same-size shared blittables, again we should prefer pinned
            if (options.HasFlag(ArenaFlags.BlittableNonPaddedSharing))
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
    public abstract class OwnedArena<T> : IArena<T>
    {
        Type IArena.ElementType => typeof(T);

        internal OwnedArena() { }

        /// <summary>
        /// Allocate a (possibly non-contiguous) region of memory from the arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract Sequence<T> Allocate(int length);

        /// <summary>
        /// Allocate a single instance as a reference
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Reference<T> Allocate() => Allocate(1).GetReference(0);

        /// <summary>
        /// Allocate a (possibly non-contiguous) region of memory from the arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T> Allocate(IEnumerable<T> source)
            => Arena.Allocate<T>(this, source);

        SequencePosition IArena.GetPosition() => GetPosition();
        internal abstract SequencePosition GetPosition();

        long IArena.AllocatedBytes() => AllocatedBytes();
        internal abstract long AllocatedBytes();

        Sequence<T> IArena<T>.AllocateRetainingSegmentData(int length)
            => AllocateRetainingSegmentData(length);
        internal abstract Sequence<T> AllocateRetainingSegmentData(int length);

        internal abstract object GetAllocator();

        internal abstract void Reset();
        internal abstract void Dispose();
        void IArena.Reset() => Reset();
        void IDisposable.Dispose() => Dispose();
    }

    internal sealed class SimpleOwnedArena<T> : OwnedArena<T>
    {
#pragma warning disable RCS1085
        internal Arena<T> Arena => _arena;
        private readonly Arena<T> _arena;
#pragma warning restore RCS1085

        public SimpleOwnedArena(Arena parent, Allocator<T> suggestedAllocator)
        {
            var factory = parent.Factory;
            var options = parent.Options;

            // create the arena
            _arena = new Arena<T>(
                options: options,
                allocator: suggestedAllocator ?? factory.SuggestAllocator<T>(options),
                blockSizeBytes: factory.SuggestBlockSizeBytes<T>(options));
        }
        public override Sequence<T> Allocate(int length) => _arena.Allocate(length);

        internal override object GetAllocator() => _arena.GetAllocator();

        internal override void Reset() => _arena.Reset();
        internal override void Dispose() => _arena.Dispose();

        internal override SequencePosition GetPosition() => _arena.GetPosition();

        internal override long AllocatedBytes() => _arena.AllocatedBytes();

        internal override Sequence<T> AllocateRetainingSegmentData(int length)
            => _arena.AllocateRetainingSegmentData(length);
    }

    internal abstract class MappedBlittableOwnedArena<TFrom, TTo> : OwnedArena<TTo>
    {
        // where TFrom : unmanaged and where TTo : unmanaged
        // is intended - can't enforce due to a: convincing compiler, and
        // b: runtime (AOT) limitations

        protected readonly Arena<TFrom> _arena;
        protected MappedBlittableOwnedArena(Arena parent)
        {
            Debug.Assert(PerTypeHelpers<TFrom>.IsBlittable);
            Debug.Assert(PerTypeHelpers<TTo>.IsBlittable);

            // get the byte arena from the parent
            _arena = ((SimpleOwnedArena<TFrom>)parent.GetArena<TFrom>()).Arena;
        }
        internal override long AllocatedBytes() => 0; // not our data

        internal override void Reset() { } // the T allocator doesn't own the data
        internal override void Dispose() { } // the T allocator doesn't own the data
        internal override object GetAllocator() => _arena.GetAllocator();

        internal override SequencePosition GetPosition() => _arena.GetPosition();

        internal override Sequence<TTo> AllocateRetainingSegmentData(int length)
            => Allocate(length);

        private readonly List<Arena.MappedSegment<TFrom, TTo>> _mappedSegments = new List<Arena.MappedSegment<TFrom, TTo>>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected Arena.MappedSegment<TFrom, TTo> MapBlock(int index)
            => _mappedSegments.Count > index ? _mappedSegments[index] : MapTo(index);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Arena.MappedSegment<TFrom, TTo> MapTo(int index)
        {
            int neededCount = index + 1;
            Arena.MappedSegment<TFrom, TTo> current = _mappedSegments.Count == 0 ? null : _mappedSegments[_mappedSegments.Count - 1];
            while (_mappedSegments.Count < neededCount)
            {
                var nextUnderlying = current == null ? _arena.FirstBlock : current.Underlying.Next;
                var next = new Arena.MappedSegment<TFrom, TTo>(current, nextUnderlying);
                _mappedSegments.Add(next);
                current = next;
            }
            return current;
        }
    }

    internal sealed class NonPaddedBlittableOwnedArena<TFrom, TTo> : MappedBlittableOwnedArena<TFrom, TTo>
        where TFrom : unmanaged
        where TTo : unmanaged
    {
        public NonPaddedBlittableOwnedArena(Arena parent) : base(parent)
        {
            if (Unsafe.SizeOf<TFrom>() != Unsafe.SizeOf<TTo>()) Throw.InvalidOperation($"A non-padded arena requires the size of {typeof(TFrom).Name} ({Unsafe.SizeOf<TFrom>()}) and {typeof(TTo).Name} ({Unsafe.SizeOf<TTo>()}) to match");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Sequence<TTo> Allocate(int length)
        {
            var fromParent = _arena.AllocateRetainingSegmentData(length);

            if (!fromParent.TryGetSegments(out var startSeq, out var endSeq, out var startOffset, out var endOffset))
                Throw.SegmentDataUnavailable();

            var startMapped = MapBlock(((Block<TFrom>)startSeq).SegmentIndex);
            var endMapped = ReferenceEquals(startSeq, endSeq) ? startMapped : MapBlock(((Block<TFrom>)endSeq).SegmentIndex);

            return new Sequence<TTo>(
                startMapped, endMapped,
                startOffset, endOffset);
        }
    }

    internal sealed class PaddedBlittableOwnedArena<T> : MappedBlittableOwnedArena<byte, T>
    {
        // where T : unmanaged is intended - can't enforce due to a: convincing compiler, and
        // b: runtime (AOT) limitations
        public PaddedBlittableOwnedArena(Arena parent) : base(parent)
        {
            Debug.Assert(PerTypeHelpers<T>.IsBlittable);
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
            var mappedBlock = MapBlock(arena.CurrentBlock.SegmentIndex);

            var startBlock = mappedBlock;
            var startOffset = arena.AllocatedCurrentBlock / Unsafe.SizeOf<T>();

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

            // make sure that we've mapped as far as the end
            mappedBlock = MapBlock(arena.CurrentBlock.SegmentIndex);

            Debug.Assert(length == 0);

            return new Sequence<T>(startBlock, mappedBlock, startOffset, arena.AllocatedCurrentBlock / Unsafe.SizeOf<T>());
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

            var bySize = _blittableBySize;
            _blittableBySize = null;
            if (bySize != null)
            {
                bySize.Clear();
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
        /// Allocate a (possibly non-contiguous) region of memory from the arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // aggressive mostly in the JIT-optimized cases!
        public Sequence<T> Allocate<T>(int length) => GetArena<T>().Allocate(length);

        /// <summary>
        /// Allocate a (possibly non-contiguous) region of memory from the arena
        /// </summary>
        public Sequence<T> Allocate<T>(IEnumerable<T> source) => Allocate<T>(GetArena<T>(), source);

        /// <summary>
        /// Get a per-type arena inside a multi-type arena
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OwnedArena<T> GetArena<T>()
        {
            if (_lastArena?.ElementType == typeof(T)
                || _ownedArenas.TryGetValue(typeof(T), out _lastArena))
            {
                return (OwnedArena<T>)_lastArena;
            }
            return CreateAndAddArena<T>();
        }

#pragma warning disable IDE0069
        private IArena _lastArena;
#pragma warning restore IDE0069

        private Dictionary<int, IArena> _blittableBySize = new Dictionary<int, IArena>();
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
                Allocator<T> allocator = null;
                bool addBySize = false;
                try
                {
                    if (PerTypeHelpers<T>.IsBlittable && Unsafe.SizeOf<T>() > 0) // we can do fun things for blittable types
                    {
                        // can we use a padded approach? (more complex/slower allocations due to padding, but better memory usage)
                        if (Options.HasFlag(ArenaFlags.BlittablePaddedSharing) // if the caller wants
                            && Unsafe.SizeOf<T>() <= 256) // don't use too-large T because of excessive padding
                        {
                            if (typeof(T) == typeof(byte)) // for blittable scenarios, byte is the underlying type, so don't thunk it
                            {
                                // however, we still need to let the factory suggest a (hopefully pinned) blittable allocator
                                allocator = (Allocator<T>)(object)Factory.SuggestBlittableAllocator<byte>(Options);
                            }
                            else
                            {
                                return new PaddedBlittableOwnedArena<T>(this);
                            }
                        }

                        // if we aren't using padded blittables; can we use a non-padded approach instead?
                        if (!Options.HasFlag(ArenaFlags.BlittablePaddedSharing) && Options.HasFlag(ArenaFlags.BlittableNonPaddedSharing))
                        {
                            if(_blittableBySize.TryGetValue(Unsafe.SizeOf<T>(), out var existing))
                            {
                                // one already exists for that size, yay!
                                return (OwnedArena<T>)Activator.CreateInstance(
                                    typeof(NonPaddedBlittableOwnedArena<int,uint>).GetGenericTypeDefinition()
                                        .MakeGenericType(existing.ElementType, typeof(T)),
                                    args: new object[] { this });
                            }

                            // and even if one doesn't already exist; we will want to create one, which
                            // means we need a blittable allocator
                            allocator = (Allocator<T>)typeof(AllocatorFactory).GetMethod(nameof(AllocatorFactory.SuggestBlittableAllocator),
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .MakeGenericMethod(typeof(T)).Invoke(Factory, parameters: new object[] { Options });
                            addBySize = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // if bad things happen; give up
                    Debug.WriteLine(ex.Message);
                }
                var newArena = new SimpleOwnedArena<T>(this, allocator);
                if (addBySize) _blittableBySize.Add(Unsafe.SizeOf<T>(), newArena);
                return newArena;
            }
        }

        internal object GetAllocator<T>() => GetArena<T>().GetAllocator();

        internal sealed class MappedSegment<TFrom, TTo> : SequenceSegment<TTo>, IPinnedMemoryOwner<TTo>
        {
            // where TFrom : unmanaged and where TTo : unmanaged
            // is intended - can't enforce due to a: convincing compiler, and
            // b: runtime (AOT) limitations

            public unsafe void* Origin { get; }
            public Block<TFrom> Underlying { get; }

            protected override Type GetUnderlyingType() => typeof(TFrom);

            protected override int GetSegmentIndex() => Underlying.SegmentIndex; // block index is always shared

#if DEBUG
            private readonly int _byteCount;
            protected override long ByteOffset { get; }
#endif

            private static unsafe Memory<TTo> CreateMapped(Block<TFrom> underlying, out void* origin)
            {
                MemoryManager<TTo> mapped;
                origin = null;
                if (underlying.Allocation is IPinnedMemoryOwner<TFrom> rooted && rooted.Origin != null)
                {   // in this case, we can just cheat like crazy
                    var x = new PinnedConvertingMemoryManager(rooted);
                    origin = x.Origin;
                    mapped = x;
                }
                else
                {   // need to do everything properly; slower, but it'll work
                    mapped = new ConvertingMemoryManager(underlying.Allocation);
                }
                return mapped.Memory;
            }
            public unsafe MappedSegment(MappedSegment<TFrom, TTo> previous, Block<TFrom> underlying)
                : base(CreateMapped(underlying, out void* origin), previous)
            {
                Origin = origin;
                Underlying = underlying;
                Debug.Assert(PerTypeHelpers<TFrom>.IsBlittable);
                Debug.Assert(PerTypeHelpers<TTo>.IsBlittable);
#if DEBUG
                _byteCount = underlying.Length * Unsafe.SizeOf<TFrom>();
                if (previous != null)
                {   // we can't use "underlying" for this, because of padding etc
                    ByteOffset = previous.ByteOffset + previous._byteCount;
                }
#endif
            }

            private sealed unsafe class PinnedConvertingMemoryManager : MemoryManager<TTo>, IPinnedMemoryOwner<TTo>
            {
                private readonly void* _origin;

                public PinnedConvertingMemoryManager(IPinnedMemoryOwner<TFrom> rooted)
                {
                    _origin = rooted.Origin;
                    Length = PerTypeHelpers.Cast<TFrom, TTo>(rooted.Memory.Span).Length;
                }

                public void* Origin => _origin;

                public override Span<TTo> GetSpan() => new Span<TTo>(_origin, Length);

                public int Length { get; }

                public override MemoryHandle Pin(int elementIndex = 0) => new MemoryHandle(Unsafe.Add<TTo>(_origin, elementIndex));

                public override void Unpin() { }

                protected override void Dispose(bool disposing) { } // not our memory
            }
            private sealed class ConvertingMemoryManager : MemoryManager<TTo>
            {
                private readonly IMemoryOwner<TFrom> _unrooted;
                public ConvertingMemoryManager(IMemoryOwner<TFrom> unrooted) => _unrooted = unrooted;

                public override Span<TTo> GetSpan() => PerTypeHelpers.Cast<TFrom, TTo>(_unrooted.Memory.Span);

                public override MemoryHandle Pin(int elementIndex = 0) { Throw.NotSupported(); return default; }

                public override void Unpin() => Throw.NotSupported();

                protected override void Dispose(bool disposing) { } // not our memory
            }
        }

        internal static Sequence<T> Allocate<T>(IArena<T> arena, IEnumerable<T> source)
        {
            Debug.Assert(arena != null, "null arena");
            if (SequenceExtensions.SequenceBuilder<T>.IsTrivial(source, out var seq))
            {   // we need it allocated in *this* arena, which we
                // can't know unless we copy it
                if (seq.IsEmpty) return default;
                var len = seq.Length;
                if (len <= int.MaxValue)
                {
                    var result = arena.Allocate((int)len);
                    seq.CopyTo(result);
                    return result;
                }
            }
            return SlowAllocate<T>(arena, source);
        }
        private static Sequence<T> SlowAllocate<T>(IArena<T> arena, IEnumerable<T> source)
        {
            using var iter = source.GetEnumerator();
            if (!iter.MoveNext()) return default; // empty
            Sequence<T> alloc = arena.AllocateRetainingSegmentData(1);
            alloc[0] = iter.Current;

            var pos = arena.GetPosition();
            if (!iter.MoveNext()) return alloc; // exactly 1
            if (!alloc.TryGetSegments(out var x, out _, out var i, out _))
                Throw.SegmentDataUnavailable();

            do
            {
                // check we didn't allocate during the MoveNext
                if (!pos.Equals(arena.GetPosition())) Throw.AllocationDuringEnumeration();
                alloc = arena.AllocateRetainingSegmentData(1);
                alloc[0] = iter.Current;
                pos = arena.GetPosition();
            } while (iter.MoveNext());
            // now get the segment data from the start and end, and combine them
            if (!alloc.TryGetSegments(out _, out var y, out _, out var j))
                Throw.SegmentDataUnavailable();
            return new Sequence<T>(x, y, i, j);
        }

        internal long AllocatedBytes()
        {
            long total = 0;
            var owned = _ownedArenas;
            if (owned != null)
            {
                foreach (var arena in owned) total += arena.Value.AllocatedBytes();
            }
            return total;
        }
    }
}