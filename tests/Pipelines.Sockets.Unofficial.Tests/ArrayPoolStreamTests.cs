using System;
using System.IO;
using System.IO.Compression;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class ArrayPoolStreamTests
    {
        public ArrayPoolStreamTests(ITestOutputHelper output)
            => _output = output;
        private readonly ITestOutputHelper _output;
        private void Log(string message) => _output?.WriteLine(message);

        [Fact]
        public void TestArrayPoolViaGZipStream()
        {
            byte[] arr = new byte[1024];
            const int loop = 100;
            new Random(12345).NextBytes(arr);
            using (var buffer = new ArrayPoolStream())
            {
                using (var zip = new GZipStream(buffer, CompressionLevel.Optimal, true))
                {
                    for (int i = 0; i < loop; i++)
                    {
                        zip.Write(arr, 0, arr.Length);
                    }
                    zip.Flush();
                }
                Log($"written: {loop * arr.Length} bytes, gzip: {buffer.Length} bytes");
                buffer.Position = 0;
                using (var zip = new GZipStream(buffer, CompressionMode.Decompress))
                {
                    var ms = new MemoryStream();
                    zip.CopyTo(ms);
                    Assert.True(ms.TryGetBuffer(out var segment));
                    Assert.Equal(loop * arr.Length, segment.Count);
                    for (int i = 0; i < loop; i++)
                    {
                        Assert.True(new Span<byte>(segment.Array, segment.Offset + (i * arr.Length), arr.Length).SequenceEqual(arr));
                    }
                    Log($"validated: {loop}x{arr.Length} bytes");
                }
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, -1)]
        [InlineData(int.MinValue, int.MinValue)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 4)]
        [InlineData(4, 4)]
        [InlineData(5, 8)]
        [InlineData(6, 8)]
        [InlineData(7, 8)]
        [InlineData(8, 8)]
        [InlineData(9, 16)]
        [InlineData(913, 1024)]
        [InlineData(1023, 1024)]
        [InlineData(1024, 1024)]
        [InlineData(1025, 2048)]
        [InlineData(0b0010_0000_0000_0000_0000_0000_0000_0001, 0b0100_0000_0000_0000_0000_0000_0000_0000)]
        [InlineData(0b0100_0000_0000_0000_0000_0000_0000_0000, 0b0100_0000_0000_0000_0000_0000_0000_0000)]
        [InlineData(0b0100_0000_0000_0000_0000_0000_0000_0001, 0b0111_1111_1111_1111_1111_1111_1111_1111)]
        [InlineData(0b0111_1111_1111_1111_1111_1111_1111_1111, 0b0111_1111_1111_1111_1111_1111_1111_1111)]
        public void ValidateRoundUpBehavior(int capacity, int expected)
        {
            Assert.Equal(expected, ArrayPoolStream.RoundUp(capacity));
        }
    }
}
