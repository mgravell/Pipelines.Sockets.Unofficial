//using System;
//using System.IO;
//using System.IO.Compression;
//using Xunit;
//using Xunit.Abstractions;

//namespace Pipelines.Sockets.Unofficial.Tests
//{
//    public class ArrayPoolStreamTests
//    {
//        public ArrayPoolStreamTests(ITestOutputHelper output)
//            => _output = output;
//        private readonly ITestOutputHelper _output;
//        private void Log(string message) => _output?.WriteLine(message);

//        [Fact]
//        public void TestArrayPoolViaGZipStream()
//        {
//            byte[] arr = new byte[1024];
//            const int loop = 100;
//            new Random(12345).NextBytes(arr);
//            using (var buffer = new ArrayPoolStream())
//            {
//                using (var zip = new GZipStream(buffer, CompressionLevel.Optimal, true))
//                {
//                    for (int i = 0; i < loop; i++)
//                    {
//                        zip.Write(arr, 0, arr.Length);
//                    }
//                    zip.Flush();
//                }
//                Log($"written: {loop * arr.Length} bytes, gzip: {buffer.Length} bytes");
//                buffer.Position = 0;
//                using (var zip = new GZipStream(buffer, CompressionMode.Decompress))
//                {
//                    var ms = new MemoryStream();
//                    zip.CopyTo(ms);
//                    Assert.True(ms.TryGetBuffer(out var segment));
//                    Assert.Equal(loop * arr.Length, segment.Count);
//                    for (int i = 0; i < loop; i++)
//                    {
//                        Assert.True(new Span<byte>(segment.Array, segment.Offset + (i * arr.Length), arr.Length).SequenceEqual(arr));
//                    }
//                    Log($"validated: {loop}x{arr.Length} bytes");
//                }
//            }
//        }
//    }
//}
