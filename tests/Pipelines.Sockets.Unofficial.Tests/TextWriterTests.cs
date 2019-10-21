using Pipelines.Sockets.Unofficial.Buffers;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class TextWriterTests
    {
        [Fact]
        public void WriteIntegers()
        {
            using var bw = BufferWriter<byte>.Create();

            using var tw = BufferWriterTextWriter.Create(bw);
            for (int i = 0; i < 1000; i++)
            {
                tw.WriteLine(i);
            }
            tw.Flush();

            var expectedLength = (10 * 1)
                + (90 * 2)
                + (900 * 3)
                + (1000 * tw.NewLine.Length);
            Assert.Equal(expectedLength, bw.Length);


        }
    }
}
