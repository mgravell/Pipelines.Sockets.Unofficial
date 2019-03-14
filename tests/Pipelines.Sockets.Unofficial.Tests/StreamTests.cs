using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class StreamTests
    {
        [Fact]
        public void CanCreateDynamicStream()
        {
            const int seed = 123134;

#if DEBUG
            Assert.Equal(0, SequenceStream.LeaseCount);
#endif
            using (var s = SequenceStream.Create())
            {
                byte[] buffer = new byte[512];
                var rand = new Random(seed);
                long length = 0;
                for (int i = 0; i < 1000; i++)
                {
                    for (int j = 0; j < buffer.Length; j++)
                        buffer[j] = (byte)rand.Next(0, 256);
                    s.Write(buffer, 0, buffer.Length);

                    length += buffer.Length;
                    Assert.Equal(length, s.Length);
                    Assert.Equal(length, s.Position);
                }
                
                rand = new Random(seed);
                foreach (byte b in s.GetBuffer())
                {
                    Assert.Equal((byte)rand.Next(0, 256), b);
                }

                s.Position = length = 0;
                rand = new Random(seed);                
                int x;
                while((x = s.ReadByte()) >= 0)
                {
                    Assert.Equal((byte)rand.Next(0, 256), (byte)x);
                    Assert.Equal(++length, s.Position);
                }

#if DEBUG
                Assert.NotEqual(0, SequenceStream.LeaseCount);
#endif
            }

#if DEBUG
            Assert.Equal(0, SequenceStream.LeaseCount);
#endif
        }

        [Fact]
        public void CanCreateReadWriteStreamFromExisting()
        {
            using (var arena = new Arena<byte>())
            {
                var bytes = arena.Allocate(1024);
                const int seed = 123134;
                var rand = new Random(seed);
                foreach (ref byte b in bytes)
                    b = (byte)rand.Next(0, 256);

#if DEBUG
                Assert.Equal(0, SequenceStream.LeaseCount);
#endif
                using (var s = SequenceStream.Create(bytes))
                {
#if DEBUG
                    Assert.Equal(0, SequenceStream.LeaseCount);
#endif
                    Assert.Equal(bytes.Length, s.Length);
                    Assert.Equal(0, s.Position);

                    rand = new Random(seed);
                    int x, length = 0;
                    while ((x = s.ReadByte()) >= 0)
                    {
                        Assert.Equal((byte)rand.Next(0, 256), (byte)x);
                        Assert.Equal(++length, s.Position);
                    }

                    Assert.Throws<InvalidOperationException>(() => s.SetLength(1025));

                    s.SetLength(1024);
                    Assert.Equal(1024, s.Length);

                    bytes[1023] = 42;
                    s.Position = 1023;
                    Assert.Equal(42, s.ReadByte());
                    Assert.Equal(1024, s.Position);

                    s.SetLength(1023);
                    Assert.Equal(1023, s.Length);
                    Assert.Equal(1023, s.Position);

                    Assert.Equal(42, bytes[1023]);
                    s.SetLength(1024); // this is fine (doesn't exceed original range), but: should wipe
                    Assert.Equal(0, bytes[1023]);
                }
#if DEBUG
                Assert.Equal(0, SequenceStream.LeaseCount);
#endif
            }
        }

        [Fact]
        public void CanCreateReadOnlyStreamFromExisting()
        {
            using (var arena = new Arena<byte>())
            {
                var bytes = arena.Allocate(1024);
                const int seed = 123134;
                var rand = new Random(seed);
                foreach (ref byte b in bytes)
                    b = (byte)rand.Next(0, 256);

#if DEBUG
                Assert.Equal(0, SequenceStream.LeaseCount);
#endif
                ReadOnlySequence<byte> ros = bytes;
                using (var s = ReadOnlySequenceStream.Create(ros))
                {
#if DEBUG
                    Assert.Equal(0, SequenceStream.LeaseCount);
#endif
                    Assert.Equal(bytes.Length, s.Length);
                    Assert.Equal(0, s.Position);

                    rand = new Random(seed);
                    int x, length = 0;
                    while ((x = s.ReadByte()) >= 0)
                    {
                        Assert.Equal((byte)rand.Next(0, 256), (byte)x);
                        Assert.Equal(++length, s.Position);
                    }

                    Assert.Throws<NotSupportedException>(() => s.SetLength(1023));
                    s.SetLength(1024);
                    Assert.Equal(1024, s.Length);
                }
#if DEBUG
                Assert.Equal(0, SequenceStream.LeaseCount);
#endif
            }
        }
    }
}
