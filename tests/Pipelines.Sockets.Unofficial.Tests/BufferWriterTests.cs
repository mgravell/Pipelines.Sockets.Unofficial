using System;
using System.Buffers;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class BufferWriterTests
    {
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
        public void BufferWriterDoesNotLeak()
        {
            using(var bw = BufferWriter<byte>.Create(blockSize: 16))
            {
                var writer = bw.Writer;

                byte nextVal = 0;
                OwnedReadOnlySequence<byte> Write(int count)
                {
                    for(int i = 0; i < count; i++)
                    {
                        var span = writer.GetSpan(5);
                        span.Fill(nextVal++);
                        writer.Advance(5);
                    }
                    return bw.Flush();
                }


                var chunks = new OwnedReadOnlySequence<byte>[5];
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

                for (int i = 0; i < chunks.Length; i++)
                    chunks[i].Dispose();
#if DEBUG
                // should have the last buffer remaining
                Assert.Equal(1, BufferWriter<byte>.LiveSegmentCount);
#endif
            }

#if DEBUG
            // all dead now
            Assert.Equal(0, BufferWriter<byte>.LiveSegmentCount);
#endif


        }
    }
}
