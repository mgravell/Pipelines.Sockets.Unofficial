using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class MultiArenaTests
    {
        static void AssertPosition(string expected, SequencePosition position)
        {
#if !DEBUG
            // byte-offset only available in debug
            expected = System.Text.RegularExpressions.Regex.Replace(expected, "; byte-offset: [0-9]+", "");
#endif
            Assert.Equal(expected, position.TryGetSummary());  // internal utility method
        }

        [Fact]
        public void MultiArenaCanUseSharedMemory()
        {
            using (var arena = new Arena(new ArenaOptions(ArenaFlags.BlittableNonPaddedSharing | ArenaFlags.BlittablePaddedSharing)))
            {
                // simple values
                Sequence<byte> bytes = arena.Allocate<byte>(41);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Byte", bytes.Start()); // internal utility method
                AssertPosition("segment: 0, offset: 41; byte-offset: 41; type: Byte", bytes.End());
                Assert.IsType<PinnedArrayPoolAllocator<byte>>(arena.GetAllocator<byte>());
                Assert.IsType<SimpleOwnedArena<byte>>(arena.GetArena<byte>());

                Sequence<int> integers = arena.Allocate<int>(10);
                AssertPosition("segment: 0, offset: 11; byte-offset: 44; type: Byte", integers.Start());
                AssertPosition("segment: 0, offset: 21; byte-offset: 84; type: Byte", integers.End());
                Assert.IsType<PinnedArrayPoolAllocator<byte>>(arena.GetAllocator<int>());
                Assert.IsType<PaddedBlittableOwnedArena<int>>(arena.GetArena<int>());

                Sequence<uint> unsigned = arena.Allocate<uint>(10);
                AssertPosition("segment: 0, offset: 21; byte-offset: 84; type: Byte", unsigned.Start());
                AssertPosition("segment: 0, offset: 31; byte-offset: 124; type: Byte", unsigned.End());
                Assert.IsType<PinnedArrayPoolAllocator<byte>>(arena.GetAllocator<uint>());
                Assert.IsType<PaddedBlittableOwnedArena<uint>>(arena.GetArena<uint>());

                // complex *unmanaged* structs
                ShowUnmanaged<Foo>(); // fine, unmanaged
                Assert.Equal(16, Unsafe.SizeOf<Foo>()); // prove we know how big Foo is
                Sequence<Foo> foos = arena.Allocate<Foo>(5);
                // 7*16=112, 8*16=128
                AssertPosition("segment: 0, offset: 8; byte-offset: 128; type: Byte", foos.Start());
                // 128 + 5*16 = 208
                AssertPosition("segment: 0, offset: 13; byte-offset: 208; type: Byte", foos.End());
                Assert.IsType<PinnedArrayPoolAllocator<byte>>(arena.GetAllocator<Foo>());
                Assert.IsType<PaddedBlittableOwnedArena<Foo>>(arena.GetArena<Foo>());

                // ShowUnmanaged<Bar>(); // won't compile, not unmanaged
                Sequence<Bar> bars = arena.Allocate<Bar>(20);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Bar", bars.Start()); // go into different block
                AssertPosition("segment: 0, offset: 20; byte-offset: " + (20 * Unsafe.SizeOf<Bar>()) + "; type: Bar", bars.End());
                Assert.IsType<ArrayPoolAllocator<Bar>>(arena.GetAllocator<Bar>());
                Assert.IsType<SimpleOwnedArena<Bar>>(arena.GetArena<Bar>());
            }
        }

        [Fact]
        public void MultiArenaCanUseNonSharedMemory()
        {
            using (var arena = new Arena(new ArenaOptions(ArenaFlags.None)))
            {
                // simple values
                Sequence<byte> bytes = arena.Allocate<byte>(41);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Byte", bytes.Start());
                AssertPosition("segment: 0, offset: 41; byte-offset: 41; type: Byte", bytes.End());
                Assert.IsType<ArrayPoolAllocator<byte>>(arena.GetAllocator<byte>());
                Assert.IsType<SimpleOwnedArena<byte>>(arena.GetArena<byte>());

                Sequence<int> integers = arena.Allocate<int>(10);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Int32", integers.Start());
                AssertPosition("segment: 0, offset: 10; byte-offset: 40; type: Int32", integers.End());
                Assert.IsType<ArrayPoolAllocator<int>>(arena.GetAllocator<int>());
                Assert.IsType<SimpleOwnedArena<int>>(arena.GetArena<int>());

                Sequence<uint> unsigned = arena.Allocate<uint>(10);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: UInt32", unsigned.Start());
                AssertPosition("segment: 0, offset: 10; byte-offset: 40; type: UInt32", unsigned.End());
                Assert.IsType<ArrayPoolAllocator<uint>>(arena.GetAllocator<uint>());
                Assert.IsType<SimpleOwnedArena<uint>>(arena.GetArena<uint>());

                // complex *unmanaged* structs
                ShowUnmanaged<Foo>(); // fine, unmanaged
                Assert.Equal(16, Unsafe.SizeOf<Foo>()); // prove we know how big Foo is
                Sequence<Foo> foos = arena.Allocate<Foo>(5);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Foo", foos.Start());
                // 5*16=80
                AssertPosition("segment: 0, offset: 5; byte-offset: 80; type: Foo", foos.End());
                Assert.IsType<ArrayPoolAllocator<Foo>>(arena.GetAllocator<Foo>());
                Assert.IsType<SimpleOwnedArena<Foo>>(arena.GetArena<Foo>());

                // ShowUnmanaged<Bar>(); // won't compile, not unmanaged
                Sequence<Bar> bars = arena.Allocate<Bar>(20);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Bar", bars.Start());
                AssertPosition("segment: 0, offset: 20; byte-offset: " + (20 * Unsafe.SizeOf<Bar>()) + "; type: Bar", bars.End());
                Assert.IsType<ArrayPoolAllocator<Bar>>(arena.GetAllocator<Bar>());
                Assert.IsType<SimpleOwnedArena<Bar>>(arena.GetArena<Bar>());
            }
        }

        [Fact]
        public void MultiArenaCanUseNonPaddedMemory()
        {
            using (var arena = new Arena(new ArenaOptions(ArenaFlags.BlittableNonPaddedSharing)))
            {
                // simple values
                Sequence<byte> bytes = arena.Allocate<byte>(41);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Byte", bytes.Start());
                AssertPosition("segment: 0, offset: 41; byte-offset: 41; type: Byte", bytes.End());
                Assert.IsType<PinnedArrayPoolAllocator<byte>>(arena.GetAllocator<byte>());
                Assert.IsType<SimpleOwnedArena<byte>>(arena.GetArena<byte>());

                Sequence<int> integers = arena.Allocate<int>(10);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Int32", integers.Start());
                AssertPosition("segment: 0, offset: 10; byte-offset: 40; type: Int32", integers.End());
                Assert.IsType<PinnedArrayPoolAllocator<int>>(arena.GetAllocator<int>());
                Assert.IsType<SimpleOwnedArena<int>>(arena.GetArena<int>());

                Sequence<uint> unsigned = arena.Allocate<uint>(10); // can share a block with int
                AssertPosition("segment: 0, offset: 10; byte-offset: 40; type: Int32", unsigned.Start());
                AssertPosition("segment: 0, offset: 20; byte-offset: 80; type: Int32", unsigned.End());
                Assert.IsType<PinnedArrayPoolAllocator<int>>(arena.GetAllocator<uint>());
                Assert.IsType<NonPaddedBlittableOwnedArena<int, uint>>(arena.GetArena<uint>());

                // complex *unmanaged* structs
                ShowUnmanaged<Foo>(); // fine, unmanaged
                Assert.Equal(16, Unsafe.SizeOf<Foo>()); // prove we know how big Foo is
                Sequence<Foo> foos = arena.Allocate<Foo>(5);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Foo", foos.Start());
                // 5*16=80
                AssertPosition("segment: 0, offset: 5; byte-offset: 80; type: Foo", foos.End());
                Assert.IsType<PinnedArrayPoolAllocator<Foo>>(arena.GetAllocator<Foo>());
                Assert.IsType<SimpleOwnedArena<Foo>>(arena.GetArena<Foo>());

                // ShowUnmanaged<Bar>(); // won't compile, not unmanaged
                Sequence<Bar> bars = arena.Allocate<Bar>(20);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Bar", bars.Start());
                AssertPosition("segment: 0, offset: 20; byte-offset: " + (20 * Unsafe.SizeOf<Bar>()) + "; type: Bar", bars.End());
                Assert.IsType<ArrayPoolAllocator<Bar>>(arena.GetAllocator<Bar>());
                Assert.IsType<SimpleOwnedArena<Bar>>(arena.GetArena<Bar>());
            }
        }

        // calls to ShowUnmanaged won't compile
        static void ShowUnmanaged<T>() where T : unmanaged { }

#pragma warning disable IDE0044, IDE0051
        struct Foo
        {
            int a, b, c, d;
        }
        struct Bar
        {
            object a, b;
        }
#pragma warning restore IDE0044, IDE0051
    }
}
