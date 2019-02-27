//using System;
//using System.Buffers;
//using System.Collections.Generic;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;

//namespace Pipelines.Sockets.Unofficial.Arenas
//{
//    public class MultiArena : Arena<byte>
//    {
//        public MultiArena(Allocator<byte> allocator = null, ArenaOptions options = default, int blockSize = 0, Func<long, long, long> retentionPolicy = null)
//            : base(allocator, options, blockSize, retentionPolicy) { }

//        readonly struct TypedBlockKey : IEquatable<TypedBlockKey>
//        {
//            public Type Type { get; }
//            public IBlock Block { get; }

//            public override bool Equals(object obj) => obj is TypedBlockKey other && Equals(other);
//            public bool Equals(TypedBlockKey other) => ReferenceEquals(Type, other.Type) & ReferenceEquals(Block, other.Block);
//            public override string ToString() => $"{Type} - {Block}";
//            public override int GetHashCode() => (Type == null ? 0 : Type.GetHashCode()) ^ (Block == null ? 0 : Block.GetHashCode());
//            public TypedBlockKey(Type type, IBlock block)
//            {
//                Type = type;
//                Block = block;
//            }
//        }

//        readonly Dictionary<TypedBlockKey, IBlock> _typedBlocks = new Dictionary<TypedBlockKey, IBlock>();

//        private Block<T> GetTypedBlock<T>(Block<byte> untyped) where T : unmanaged
//        {
//            if (untyped == null) return null;
//            var key = new TypedBlockKey(typeof(T), untyped);
//            if (_typedBlocks.TryGetValue(key, out var typed))
//            {
//                typed = ApplyAlignmentAndSizeLimit<T>(untyped);
//                _typedBlocks.Add(key, typed);
//            }
//            return (Block<T>)typed;
//        }

//        static Block<T> ApplyAlignmentAndSizeLimit<T>(Block<byte> block) where T : unmanaged
//        {
//            void ThrowAllocationFailure() => throw new InvalidOperationException($"After converting to {typeof(T).Name}, the allocated block is not large enough");

//            var bytesPerT = Unsafe.SizeOf<T>();

//            var memory = block.Memory;
//            // TODO: slice for alignment

//            // trim any memory we can't really use at the end
//            var len = (memory.Length / Unsafe.SizeOf<T>()) * Unsafe.SizeOf<T>();
//            if (len == 0) ThrowAllocationFailure();
//            memory = memory.Slice(0, len);

//            var dummy = new DummyMemoryOwner<T>(memory);
//            var block = new Block<T>(dummy, )
//        }

//        sealed class DummyMemoryOwner<T> : MemoryManager<T> where T : unmanaged
//        {
//            public DummyMemoryOwner(Memory<byte> memory) => _memory = memory;
//            private readonly Memory<byte> _memory;

//            public override Span<T> GetSpan() => MemoryMarshal.Cast<byte, T>(_memory.Span);

//            public override MemoryHandle Pin(int elementIndex = 0)
//                => elementIndex == 0 ? _memory.Pin() : _memory.Slice(elementIndex * Unsafe.SizeOf<T>()).Pin();

//            public override void Unpin() => _memory.Pin();

//            protected override void Dispose(bool disposing) { } // not our memory to release
//        }


//        public Allocation<T> Allocate<T>(int count) where T : unmanaged
//        {
            
            

//            Block<byte> current = default;

//            Block<T> typed = current.Typed<T>();

//        }
//    }
//}
