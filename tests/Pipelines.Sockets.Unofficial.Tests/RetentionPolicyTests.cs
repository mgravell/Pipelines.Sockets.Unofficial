using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class RetentionPolicyTests
    {
        [Fact]
        public void TestNothingPolicy()
        {
            using var arena = new Arena<int>(new ArenaOptions(
                blockSizeBytes: 1024, retentionPolicy: RetentionPolicy.Nothing));
            for (int loop = 0; loop < 5; loop++)
            {
                Assert.Equal(0, arena.AllocatedBytes());
                Assert.Equal(1024, arena.CapacityBytes()); // one page

                for (int i = 0; i < 10; i++)
                    arena.Allocate(512);

                Assert.Equal(20480, arena.AllocatedBytes());
                Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block

                arena.Reset();
            }
        }

        [Fact]
        public void TestEverythingPolicy()
        {
            using var arena = new Arena<int>(new ArenaOptions(
                blockSizeBytes: 1024, retentionPolicy: RetentionPolicy.Everything));
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(1024, arena.CapacityBytes()); // one page

            // allocate a big chunk and reset; should keep everything
            for (int i = 0; i < 10; i++)
                arena.Allocate(512);
            Assert.Equal(20480, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // keeps everything

            // allocate another big chunk and reset; should keep everything
            for (int i = 0; i < 10; i++)
                arena.Allocate(512);
            Assert.Equal(20480, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // keeps everything

            // allocate a small chunk and reset; should keep everything
            for (int i = 0; i < 5; i++)
                arena.Allocate(512);
            Assert.Equal(10240, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // keeps everything

            // allocate another big chunk and reset; should keep everything
            for (int i = 0; i < 10; i++)
                arena.Allocate(512);
            Assert.Equal(20480, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // keeps everything
        }

        [Fact]
        public void TestRecentPolicy()
        {
            using var arena = new Arena<int>(new ArenaOptions(
                blockSizeBytes: 1024, retentionPolicy: RetentionPolicy.Recent));
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(1024, arena.CapacityBytes()); // one page

            // allocate a big chunk and reset; should keep everything
            for (int i = 0; i < 10; i++)
                arena.Allocate(512);
            Assert.Equal(20480, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // keeps everything

            // allocate another big chunk and reset; should keep everything
            for (int i = 0; i < 10; i++)
                arena.Allocate(512);
            Assert.Equal(20480, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // keeps everything

            // allocate a small chunk and reset; should keep the small size
            for (int i = 0; i < 5; i++)
                arena.Allocate(512);
            Assert.Equal(10240, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(11264, arena.CapacityBytes()); // keeps enough for the recent data

            // allocate another big chunk and reset; should keep everything
            for (int i = 0; i < 10; i++)
                arena.Allocate(512);
            Assert.Equal(20480, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // EOF allocates an extra block
            arena.Reset();
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(21504, arena.CapacityBytes()); // keeps everything
        }

        [Fact]
        public void TestDefaultPolicy()
        {
            using var arena = new Arena<int>(new ArenaOptions(
                blockSizeBytes: 1024, retentionPolicy: RetentionPolicy.Default));
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(1024, arena.CapacityBytes()); // one page

            // note that the decay cycle is jagged because of page sizes, but the important
            // thing is that we don't retain everything, nor do we release everything;
            // over time, it gets gradually smaller
            for (int loop = 0; loop < 5; loop++)
            {

                // allocate a big chunk and reset; should keep everything
                for (int i = 0; i < 10; i++)
                    arena.Allocate(512);
                Assert.Equal(20480, arena.AllocatedBytes());
                Assert.Equal(21504, arena.CapacityBytes());
                arena.Reset();
                Assert.Equal(0, arena.AllocatedBytes());
                Assert.Equal(21504, arena.CapacityBytes()); // 100%

                Span<int> expectedSizes = stackalloc int[] {
                    21504,
                    18432,
                    17408,
                    15360,
                    14336,
                    12288,
                    11264,
                    10240,
                    9216,
                    8192,
                    7168,
                    7168,
                    6144,
                    6144,
                    5120,
                    5120,
                    4096,
                    4096,
                    3072,
                    3072,
                    3072,
                    3072,
                    3072,
                    3072,
                    3072
                }; // can't release the last page, as keep touching it
                for (int i = 1; i < expectedSizes.Length; i++)
                {
                    // allocate a small chunk; decay applies
                    arena.Allocate(512);
                    Assert.Equal(2048, arena.AllocatedBytes());
                    Assert.Equal(expectedSizes[i - 1], arena.CapacityBytes());
                    arena.Reset();
                    Assert.Equal(0, arena.AllocatedBytes());
                    Assert.Equal(expectedSizes[i], arena.CapacityBytes());
                }
            }
        }

        [Fact]
        public void TestFastDecayPolicy()
        {
            using var arena = new Arena<int>(new ArenaOptions(
                blockSizeBytes: 1024, retentionPolicy: RetentionPolicy.Decay(0.5F)));
            Assert.Equal(0, arena.AllocatedBytes());
            Assert.Equal(1024, arena.CapacityBytes()); // one page

            // note that the decay cycle is jagged because of page sizes, but the important
            // thing is that we don't retain everything, nor do we release everything;
            // over time, it gets gradually smaller
            for (int loop = 0; loop < 5; loop++)
            {

                // allocate a big chunk and reset; should keep everything
                for (int i = 0; i < 10; i++)
                    arena.Allocate(512);
                Assert.Equal(20480, arena.AllocatedBytes());
                Assert.Equal(21504, arena.CapacityBytes());
                arena.Reset();
                Assert.Equal(0, arena.AllocatedBytes());
                Assert.Equal(21504, arena.CapacityBytes()); // 100%

                Span<int> expectedSizes = stackalloc int[] {
                    21504,
                    11264,
                    6144,
                    3072,
                    3072,
                    3072,
                    3072,
                    3072,
                    3072,
                    3072
                }; // can't release the last page, as keep touching it
                for (int i = 1; i < expectedSizes.Length; i++)
                {
                    // allocate a small chunk; decay applies
                    arena.Allocate(512);
                    Assert.Equal(2048, arena.AllocatedBytes());
                    Assert.Equal(expectedSizes[i - 1], arena.CapacityBytes());
                    arena.Reset();
                    Assert.Equal(0, arena.AllocatedBytes());
                    Assert.Equal(expectedSizes[i], arena.CapacityBytes());
                }
            }
        }
    }
}
