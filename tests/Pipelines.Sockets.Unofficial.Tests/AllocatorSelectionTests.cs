using Pipelines.Sockets.Unofficial.Arenas;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class AllocatorSelectionTests
    {
        [Fact]
        public void TestInt32()
        {
            var allocator = PerTypeHelpers<int>.PreferUnmanaged();
            Assert.True(allocator.IsUnmanaged);
            Assert.False(allocator.IsPinned);

            allocator = PerTypeHelpers<int>.PreferPinned();
            Assert.False(allocator.IsUnmanaged);
            Assert.True(allocator.IsPinned);
        }

        [Fact]
        public void TestString()
        {
            var allocator = PerTypeHelpers<string>.PreferUnmanaged();
            Assert.False(allocator.IsUnmanaged);
            Assert.False(allocator.IsPinned);

            allocator = PerTypeHelpers<string>.PreferPinned();
            Assert.False(allocator.IsUnmanaged);
            Assert.False(allocator.IsPinned);
        }
    }
}

