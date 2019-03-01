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
            using (var arena = new Arena())
            {
                // simple values
                Sequence<byte> bytes = arena.Allocate<byte>(41);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Byte", bytes.Start()); // internal utility method
                AssertPosition("segment: 0, offset: 41; byte-offset: 41; type: Byte", bytes.End());

                Sequence<int> integers = arena.Allocate<int>(20);
                AssertPosition("segment: 0, offset: 11; byte-offset: 44; type: Byte", integers.Start());
                AssertPosition("segment: 0, offset: 31; byte-offset: 124; type: Byte", integers.End());

                // complex *unmanaged* structs
                ShowUnmanaged<Foo>(); // fine, unmanaged
                Assert.Equal(16, Unsafe.SizeOf<Foo>()); // prove we know how big Foo is
                Sequence<Foo> foos = arena.Allocate<Foo>(5);
                // 7*16=112, 8*16=128
                AssertPosition("segment: 0, offset: 8; byte-offset: 128; type: Byte", foos.Start());
                // 128 + 5*16 = 208
                AssertPosition("segment: 0, offset: 13; byte-offset: 208; type: Byte", foos.End());

                // ShowUnmanaged<Bar>(); // won't compile, not unmanaged
                Sequence <Bar> bars = arena.Allocate<Bar>(20);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Bar", bars.Start()); // go into different block
                AssertPosition("segment: 0, offset: 20; byte-offset: " + (20 * Unsafe.SizeOf<Bar>()) + "; type: Bar", bars.End());
            }
        }

        [Fact]
        public void MultiArenaCanUseNonSharedMemory()
        {
            using (var arena = new Arena(new ArenaOptions(ArenaFlags.DisableBlittableSharedMemory)))
            {
                // simple values
                Sequence<byte> bytes = arena.Allocate<byte>(41);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Byte", bytes.Start());
                AssertPosition("segment: 0, offset: 41; byte-offset: 41; type: Byte", bytes.End());

                Sequence<int> integers = arena.Allocate<int>(20);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Int32", integers.Start());
                AssertPosition("segment: 0, offset: 20; byte-offset: 80; type: Int32", integers.End());

                // complex *unmanaged* structs
                ShowUnmanaged<Foo>(); // fine, unmanaged
                Assert.Equal(16, Unsafe.SizeOf<Foo>()); // prove we know how big Foo is
                Sequence<Foo> foos = arena.Allocate<Foo>(5);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Foo", foos.Start());
                // 5*16=80
                AssertPosition("segment: 0, offset: 5; byte-offset: 80; type: Foo", foos.End());

                // ShowUnmanaged<Bar>(); // won't compile, not unmanaged
                Sequence<Bar> bars = arena.Allocate<Bar>(20);
                AssertPosition("segment: 0, offset: 0; byte-offset: 0; type: Bar", bars.Start());
                AssertPosition("segment: 0, offset: 20; byte-offset: " + (20 * Unsafe.SizeOf<Bar>()) + "; type: Bar", bars.End());
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
