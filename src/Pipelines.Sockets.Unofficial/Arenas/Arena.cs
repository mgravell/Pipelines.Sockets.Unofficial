using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
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
        public int BlockSize { get; }
        /// <summary>
        /// The policy for retaining allocations when memory requirements decrease
        /// </summary>
        public Func<long, long, long> RetentionPolicy { get; }

        /// <summary>
        /// Create a new ArenaOptions instance
        /// </summary>
        public ArenaOptions(ArenaFlags flags = ArenaFlags.None, int blockSize = 0, Func<long, long, long> retentionPolicy = null)
        {
            Flags = flags;
            BlockSize = blockSize;
            RetentionPolicy = retentionPolicy;
        }
    }

    /// <summary>
    /// Provides facilities to create new type-specific arenas inside a multi-type arena
    /// </summary>
    public class ArenaFactory
    {
        /// <summary>
        /// The default arena factory
        /// </summary>
        public static ArenaFactory Default { get; } = new ArenaFactory();

        /// <summary>
        /// Create a type-specific arena with the options suggested
        /// </summary>
        public virtual Arena<T> CreateArena<T>(ArenaOptions options)
        {
            Allocator<T> allocator = null; // use the implicit default
            if(typeof(T) == typeof(byte))
            {   // unless we're talking about bytes, in which case
                // using a pinned alloctor gives us more direct access
                // in the cast steps
                allocator = (Allocator<T>)(object)PinnedArrayPoolAllocator<byte>.Shared;
            }
            return new Arena<T>(options, allocator); // use the default allocator, and the options provided
        }
            
    }

    public sealed class Arena : IDisposable
    {
        private readonly ArenaOptions _options;
        private ArenaFactory _factory;

        public Arena(ArenaOptions options = null, ArenaFactory factory = null)
        {
            _options = options ?? ArenaOptions.Default;
            _factory = factory ?? ArenaFactory.Default;
        }

        /// <summary>
        /// Release all resources associated with this arena
        /// </summary>
        public void Dispose()
        {
            _factory = null; // prevent any resurrections
            try { _bytesArena?.Dispose(); } catch { } // best efforts
            _bytesArena = null;
            var typed = _typedArenas;
            _typedArenas = null;
            if (typed != null)
            {
                foreach (var pair in typed)
                {
                    try { pair.Value.Dispose(); } catch { } // best efforts
                }
                typed.Clear();
            }
            _mappedSegments.Clear();
        }

        /// <summary>
        /// Reset the memory allocated by this arena
        /// </summary>
        public void Reset()
        {
            _bytesArena?.Reset();
            var typed = _typedArenas;
            if (typed != null)
            {
                foreach (var pair in _typedArenas)
                {
                    pair.Value.Reset();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Reference<T> Allocate<T>() => Allocate<T>(1).GetReference(0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T> Allocate<T>(int length)
        {
            // recall that the JIT will remove the unwanted versions of these
            if (typeof(T) == typeof(byte)) return AllocateUnmanaged<byte>(length).DirectCast<T>();
            else if (typeof(T) == typeof(sbyte)) return AllocateUnmanaged<sbyte>(length).DirectCast<T>();
            else if (typeof(T) == typeof(int)) return AllocateUnmanaged<int>(length).DirectCast<T>();
            else if (typeof(T) == typeof(short)) return AllocateUnmanaged<short>(length).DirectCast<T>();
            else if (typeof(T) == typeof(ushort)) return AllocateUnmanaged<ushort>(length).DirectCast<T>();
            else if (typeof(T) == typeof(long)) return AllocateUnmanaged<long>(length).DirectCast<T>();
            else if (typeof(T) == typeof(uint)) return AllocateUnmanaged<uint>(length).DirectCast<T>();
            else if (typeof(T) == typeof(ulong)) return AllocateUnmanaged<ulong>(length).DirectCast<T>();
            else
            {
                Func<Arena, int, Sequence<T>> helper;
                if (
#if SOCKET_STREAM_BUFFERS // when this is available, we can check it here and the JIT will make magic happen
                !RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
#endif
                (helper = PerTypeHelpers<T>.Allocate) != null)
                {
                    return helper(this, length);
                }

                return GetArena<T>().Allocate(length);
            }
        }

        private Dictionary<Type, IArena> _typedArenas = new Dictionary<Type, IArena>();
        private Arena<byte> _bytesArena;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Arena<byte> GetBytesArena() => _bytesArena ?? (_bytesArena = CreateNewArena<byte>());

        private Arena<T> CreateNewArena<T>()
        {
            var factory = _factory;
            if (factory == null) throw new ObjectDisposedException(ToString());
            return factory.CreateArena<T>(_options);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Arena<T> GetArena<T>()
        {
            if (typeof(T) == typeof(byte))
            {
                return (Arena<T>)(object)GetBytesArena();
            }
            else
            {
                if (!_typedArenas.TryGetValue(typeof(T), out var arena))
                {
                    arena = CreateNewArena<T>();
                    _typedArenas.Add(typeof(T), arena);
                }
                return (Arena<T>)arena;
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never), Browsable(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Reference<T> AllocateUnmanaged<T>() where T : unmanaged => AllocateUnmanaged<T>(1).GetReference(0);

        [EditorBrowsable(EditorBrowsableState.Never), Browsable(false)]
        public Sequence<T> AllocateUnmanaged<T>(int length) where T : unmanaged
        {
            var arena = GetBytesArena();

            // we need to think about padding/alignment; to allow proper
            // interleaving, we'll always need to talk in alignments of <T>,
            // so that a page of bytes can always be rooted from the same
            // origin (so we only need to map the page once per T)
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
            var mappedBlock = MapBlockFromRoot<T>(arena, arena.CurrentBlock);
            var result = new Sequence<T>(arena.AllocatedCurrentBlock / Unsafe.SizeOf<T>(), length, mappedBlock);

            // now we need to allocate and map the rest of the pages, taking care to
            // allow for padding at the end of pages

            while (length != 0)
            {
                int elementsThisPage = arena.RemainingCurrentBlock / Unsafe.SizeOf<T>();
                if (elementsThisPage == 0) throw new InvalidOperationException($"Unable to fit {typeof(T).Name} on the block");

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
                    // get the new mapped block, using the current block for chaining if needed
                    mappedBlock = MapBlock<T>(mappedBlock, arena.CurrentBlock);
                }
            }

            Debug.Assert(length == 0);

            return result;
        }

        sealed class MappedSegment<T> : SequenceSegment<T> where T : unmanaged
        {
#if DEBUG
            private readonly long _byteOffset;
            private readonly int _byteCount;
            protected override long ByteOffset => _byteOffset;
#endif

            public unsafe MappedSegment(MappedSegment<T> previous, Block<byte> original)
            {
                MemoryManager<T> mapped;
                if (original.Allocation is IPinnedMemoryOwner<byte> rooted && rooted.Root != null)
                {   // in this case, we can just cheat like crazy
                    mapped = new PinnedConvertingMemoryManager(rooted);
                }
                else
                {   // need to do everything properly; slower, but it'll work
                    mapped = new ConvertingMemoryManager(original.Allocation);
                }
                Memory = mapped.Memory;

#if DEBUG
                _byteCount = original.Length;
#endif
                if (previous != null)
                {
                    RunningIndex = previous.RunningIndex + previous.Length;
#if DEBUG
                    _byteOffset = previous.ByteOffset + previous._byteCount;
#endif
                    previous.Next = this;
                }
            }

            sealed unsafe class PinnedConvertingMemoryManager : MemoryManager<T>, IPinnedMemoryOwner<T>
            {
                private readonly T* _root;
                private readonly int _length;
                public PinnedConvertingMemoryManager(IPinnedMemoryOwner<byte> rooted)
                {
                    _root = (T*)rooted.Root;
                    _length = MemoryMarshal.Cast<byte, T>(rooted.Memory.Span).Length;
                }

                public T* Root => _root;

                public override Span<T> GetSpan() => new Span<T>(_root, _length);

                public override MemoryHandle Pin(int elementIndex = 0) => new MemoryHandle(_root + elementIndex);

                public override void Unpin() { }

                protected override void Dispose(bool disposing) { } // not our memory
            }
            sealed class ConvertingMemoryManager : MemoryManager<T>
            {
                private readonly IMemoryOwner<byte> _unrooted;
                public ConvertingMemoryManager(IMemoryOwner<byte> unrooted) => _unrooted = unrooted;

                public override Span<T> GetSpan() => MemoryMarshal.Cast<byte, T>(_unrooted.Memory.Span);

                public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

                public override void Unpin() => throw new NotSupportedException();

                protected override void Dispose(bool disposing) { } // not our memory
            }
        }

        readonly Dictionary<TypedBlockKey, ISegment> _mappedSegments = new Dictionary<TypedBlockKey, ISegment>();

        private MappedSegment<T> MapBlockFromRoot<T>(Arena<byte> arena, Block<byte> original) where T : unmanaged
        {
            var key = new TypedBlockKey(typeof(T), original);
            return _mappedSegments.TryGetValue(key, out var segment)
                ? (MappedSegment<T>)segment : MapBlockFromRootImpl<T>(arena, original);
        }

        private MappedSegment<T> MapBlockFromRootImpl<T>(Arena<byte> arena, Block<byte> original) where T : unmanaged
        {
            var current = arena.FirstBlock;
            MappedSegment<T> previous = null;

            while (current != null)
            {
                var mapped = MapBlock<T>(previous, current);
                if (ReferenceEquals(current, original)) return mapped;

                previous = mapped;
                current = current.Next;
            }
            throw new InvalidOperationException("The requested block was not found in the map-chain");
        }

        private MappedSegment<T> MapBlock<T>(MappedSegment<T> previous, Block<byte> original) where T : unmanaged
        {
            var key = new TypedBlockKey(typeof(T), original);
            if (!_mappedSegments.TryGetValue(key, out var segment))
            {
                segment = new MappedSegment<T>(previous, original);
                _mappedSegments.Add(key, segment);
            }

            return (MappedSegment<T>)segment;
        }

        readonly struct TypedBlockKey : IEquatable<TypedBlockKey>
        {
            public Type Type { get; }
            public SequenceSegment<byte> Original { get; }

            public override bool Equals(object obj) => obj is TypedBlockKey other && Equals(other);
            public bool Equals(TypedBlockKey other) => ReferenceEquals(Type, other.Type) & ReferenceEquals(Original, other.Original);
            public override string ToString() => $"{Type} - {Original}";
            public override int GetHashCode() => RuntimeHelpers.GetHashCode(Type) ^ RuntimeHelpers.GetHashCode(Original);
            public TypedBlockKey(Type type, SequenceSegment<byte> original)
            {
                Type = type;
                Original = original;
            }
        }

        private static readonly MethodInfo s_AllocateUnmanaged =
            typeof(Arena).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(x => x.Name == nameof(Arena.AllocateUnmanaged)
            && x.IsGenericMethodDefinition
            && x.GetParameters().Length == 1);

        private static class PerTypeHelpers<T>
        {
            public static readonly Func<Arena, int, Sequence<T>> Allocate
                = IsReferenceOrContainsReferences() ? null : TryCreateHelper();

            private static Func<Arena, int, Sequence<T>> TryCreateHelper()
            {
                try
                {
                    var arena = Expression.Parameter(typeof(Arena), "arena");
                    var length = Expression.Parameter(typeof(int), "length");
                    Expression body = Expression.Call(
                        instance: arena,
                        method: s_AllocateUnmanaged.MakeGenericMethod(typeof(T)),
                        arguments: new[] { length });                    
                    return Expression.Lambda<Func<Arena, int, Sequence<T>>>(
                        body, arena, length).Compile();
                }
                catch (Exception ex)
                {   
                    Debug.Fail(ex.Message);
                    return null; // swallow in prod
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool IsReferenceOrContainsReferences()
            {
#if SOCKET_STREAM_BUFFERS
                return RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#else
                if (typeof(T).IsValueType)
                {
                    try
                    {
                        unsafe
                        {
                            byte* ptr = stackalloc byte[Unsafe.SizeOf<T>()];
                            var span = new Span<T>(ptr, 1); // this will throw if not legal
                            return span.Length != 1; // we expect 1; treat anything else as failure
                        }
                    }
                    catch { } // swallow, this is an expected failure
                }
                return true;
#endif
            }
        }
    }
}