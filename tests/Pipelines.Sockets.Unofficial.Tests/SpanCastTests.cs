using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class SpanCastTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(1024)]
        [InlineData(1025)]
        public void CastInt32ToBytes(int count)
        {
            Span<int> source = count < 128 ? stackalloc int[count] : new int[count];
            for(int i = 0; i < count; i++)
            {
                source[i] = i;
            }
            var inbuilt = MemoryMarshal.Cast<int, byte>(source);
            var test = PerTypeHelpers.Cast<int, byte>(source);
            Assert.Equal(inbuilt.Length, test.Length);
            Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(inbuilt), ref MemoryMarshal.GetReference(test)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(1024)]
        [InlineData(1025)]
        public void CastBytesToInt32(int count)
        {
            Span<byte> source = count < 128 ? stackalloc byte[count] : new byte[count];
            for (int i = 0; i < count; i++)
            {
                source[i] = (byte)i;
            }
            var inbuilt = MemoryMarshal.Cast<byte, int>(source);
            var test = PerTypeHelpers.Cast<byte, int>(source);
            Assert.Equal(inbuilt.Length, test.Length);
            Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(inbuilt), ref MemoryMarshal.GetReference(test)));
        }
    }
}
