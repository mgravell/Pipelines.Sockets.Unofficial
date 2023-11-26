using Pipelines.Sockets.Unofficial.Arenas;
using Pipelines.Sockets.Unofficial.Buffers;
using System;
using System.Buffers;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class BufferWriterTests
    {
        public BufferWriterTests(ITestOutputHelper log) => Log = log;

        public ITestOutputHelper Log { get; }

        static string Raw(ReadOnlySequence<byte> values)
        {
            // this doesn't need to be efficient, just correct
            var chars = ArrayPool<char>.Shared.Rent((int)values.Length);
            int offset = 0;
            foreach(var segment in values)
            {
                var span = segment.Span;
                for (int i = 0; i < span.Length; i++)
                    chars[offset++] = (char)('0' + span[i]);
            }
            return new string(chars, 0, (int)values.Length);
        }

        [Fact]
        public void CanPartialFlush()
        {
            using var bw = BufferWriter<byte>.Create(blockSize: 16);
            bw.GetSequence(128);
            bw.Advance(50);
            bw.Advance(30);

            Assert.Equal(80, bw.Length);

            using var x1 = bw.Flush(20);
            Assert.Equal(20, x1.Value.Length);
            Assert.Equal(60, bw.Length);
            using var x2 = bw.Flush();
            Assert.Equal(60, x2.Value.Length);
            Assert.Equal(0, bw.Length);
        }

        [Fact]
        public void BufferWriterDoesNotLeak()
        {
#pragma warning disable IDE0063 // this would break the "all dead now" test
            using (var bw = BufferWriter<byte>.Create(blockSize: 16))
#pragma warning restore IDE0063
            {
                var writer = bw.Writer;

                byte nextVal = 0;
                Owned<ReadOnlySequence<byte>> Write(int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var span = writer.GetSpan(5);
                        span.Fill(nextVal++);
                        writer.Advance(5);
                    }
                    Log?.WriteLine($"before flush, wrote {count * 5}... {bw.GetState()}");
                    var result = bw.Flush();
                    Log?.WriteLine($"after flush: {bw.GetState()}");
                    return result;
                }

                var chunks = new Owned<ReadOnlySequence<byte>>[5];
                var rand = new Random(1234);
                for (int i = 0; i < chunks.Length; i++)
                {
                    // note that the lifetime of the chunks are completely independent
                    // and can be disposed arbitrarily
                    var chunk = Write(i);
                    ReadOnlySequence<byte> ros = chunk;
                    Assert.Equal(i * 5, ros.Length);

                    chunks[i] = chunk;
                }

                Assert.Equal("", Raw(chunks[0]));
                Assert.Equal("00000", Raw(chunks[1]));
                Assert.Equal("1111122222", Raw(chunks[2]));
                Assert.Equal("333334444455555", Raw(chunks[3]));
                Assert.Equal("66666777778888899999", Raw(chunks[4]));

#if DEBUG
                // can fit 15 in each, dropping one byte on the floor
                Assert.Equal(4, BufferWriter<byte>.LiveSegmentCount);
#endif

                for (int i = 0; i < chunks.Length; i++) Log?.WriteLine($"chunk {i}: {GetState(chunks[i])}");
                for (int i = 0; i < chunks.Length; i++) chunks[i].Dispose();
                for (int i = 0; i < chunks.Length; i++) Log?.WriteLine($"chunk {i}: {GetState(chunks[i])}");
            }
#if DEBUG
            // all dead now
            Assert.Equal(0, BufferWriter<byte>.LiveSegmentCount);
#endif


        }

        [Fact]
        public void BufferWriterReturnsMemoryToPool()
        {
#pragma warning disable IDE0063 // this would break the "all dead now" test
            using (var bw = BufferWriter<byte>.Create(blockSize: 1 << 8))
#pragma warning restore IDE0063
            {
                var span = bw.GetSpan(1);
                span[0] = 123;
                span[128] = 210;
                bw.Advance((1 << 8));
                bw.Flush().Dispose();

                // Release the head off the buffer.
                bw.GetSpan(1);

                var rent = ArrayPool<byte>.Shared.Rent(1 << 8);
                Assert.Equal(123, rent[0]);
                Assert.Equal(210, rent[128]);
            }
        }

        static string GetState(ReadOnlySequence<byte> ros)
        {
            var start = ros.Start;
            var node = start.GetObject() as BufferWriter<byte>.RefCountedSegment;
            long len = ros.Length + start.GetInteger();


            var sb = new StringBuilder();
            sb.Append($"{start.TryGetOffset()}-{ros.End.TryGetOffset()}; counts: ");
            while (node is not null & len > 0)
            {
                sb.Append("[").Append(node.RunningIndex).Append(',').Append(node.RunningIndex + node.Length).Append("):").Append(node.RefCount).Append(' ');
                len -= node.Length;
                node = (BufferWriter<byte>.RefCountedSegment)node.Next;
            }
            return sb.ToString();
        }

        [Fact]
        public void CanAllocateSequences()
        {
            using var bw = BufferWriter<byte>.Create(blockSize: 16);
            Log?.WriteLine(bw.GetState());
            Assert.Equal(0, bw.Length);

            var seq = bw.GetSequence(70);
            Assert.Equal(80, seq.Length);
            Log?.WriteLine(bw.GetState());
            Assert.Equal(0, bw.Length);

            bw.Advance(40);
            Log?.WriteLine(bw.GetState());
            Assert.Equal(40, bw.Length);

            for (int i = 1; i <= 5; i++)
            {
                Log?.WriteLine($"Leasing span {i}... {bw.GetState()}");
                bw.GetSpan(8);
                bw.Advance(5);
                Assert.Equal(40 + (5 * i), bw.Length);
            }
            Log?.WriteLine(bw.GetState());

            Assert.Equal(65, bw.Length);
            using (var ros = bw.Flush())
            {
                Assert.Equal(65, ros.Value.Length);
            }
            Assert.Equal(0, bw.Length);
        }
    }
}
