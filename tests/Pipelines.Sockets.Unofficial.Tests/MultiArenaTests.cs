using Pipelines.Sockets.Unofficial.Arenas;
using System.Runtime.CompilerServices;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class MultiArenaTests
    {
        [Fact]
        public void MultiArenaCanUseSharedMemory()
        {
            using (var arena = new Arena())
            {
                // simple values
                Sequence<byte> bytes = arena.Allocate<byte>(41);
#if DEBUG // byte offset only tracked in debug
                Assert.Equal(0, bytes.Start().TryGetByteOffset<byte>()); // internal utility method
                Assert.Equal(41, bytes.End().TryGetByteOffset<byte>());
#endif

                Sequence<int> integers = arena.Allocate<int>(20);
#if DEBUG // byte offset only tracked in debug
                Assert.Equal(44, integers.Start().TryGetByteOffset<int>());
                Assert.Equal(124, integers.End().TryGetByteOffset<int>());
#endif

                // complex *unmanaged* structs
                ShowUnmanaged<Foo>(); // fine, unmanaged
                Assert.Equal(16, Unsafe.SizeOf<Foo>()); // prove we know how big Foo is
                Sequence<Foo> foos = arena.Allocate<Foo>(5);
#if DEBUG // byte offset only tracked in debug
                // 7*16=112, 8*16=128
                Assert.Equal(128, foos.Start().TryGetByteOffset<Foo>());
                // 128 + 5*16 = 208
                Assert.Equal(208, foos.End().TryGetByteOffset<Foo>());
#endif

                // ShowUnmanaged<Bar>(); // won't compile, not unmanaged
                Sequence<Bar> bars = arena.Allocate<Bar>(20);
#if DEBUG // byte offset only tracked in debug
                Assert.Equal(0, bars.Start().TryGetByteOffset<Bar>()); // go into different block
                Assert.Equal(20 * Unsafe.SizeOf<Bar>(), bars.End().TryGetByteOffset<Bar>());
#endif
            }
        }

        [Fact]
        public void MultiArenaCanUseNonSharedMemory()
        {
            using (var arena = new Arena(new ArenaOptions(ArenaFlags.DisableBlittableSharedMemory)))
            {
                // simple values
                Sequence<byte> bytes = arena.Allocate<byte>(41);
#if DEBUG // byte offset only tracked in debug
                Assert.Equal(0, bytes.Start().TryGetByteOffset<byte>()); // internal utility method
                Assert.Equal(41, bytes.End().TryGetByteOffset<byte>());
#endif

                Sequence<int> integers = arena.Allocate<int>(20);
#if DEBUG // byte offset only tracked in debug
                Assert.Equal(0, integers.Start().TryGetByteOffset<int>());
                Assert.Equal(80, integers.End().TryGetByteOffset<int>());
#endif

                // complex *unmanaged* structs
                ShowUnmanaged<Foo>(); // fine, unmanaged
                Assert.Equal(16, Unsafe.SizeOf<Foo>()); // prove we know how big Foo is
                Sequence<Foo> foos = arena.Allocate<Foo>(5);
#if DEBUG // byte offset only tracked in debug
                Assert.Equal(0, foos.Start().TryGetByteOffset<Foo>());
                // 5*16=80
                Assert.Equal(80, foos.End().TryGetByteOffset<Foo>());
#endif

                // ShowUnmanaged<Bar>(); // won't compile, not unmanaged
                Sequence<Bar> bars = arena.Allocate<Bar>(20);
#if DEBUG // byte offset only tracked in debug
                Assert.Equal(0, bars.Start().TryGetByteOffset<Bar>()); // go into different block
                Assert.Equal(20 * Unsafe.SizeOf<Bar>(), bars.End().TryGetByteOffset<Bar>());
#endif
            }
        }

        // calls to ShowUnmanaged won't compile
        static void ShowUnmanaged<T>() where T : unmanaged { }

        struct Foo
        {
            int a, b, c, d;
        }
        struct Bar
        {
            object a, b;
        }
    }
}
