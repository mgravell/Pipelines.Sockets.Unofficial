using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Linq;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class ReferenceTests
    {
        [Fact]
        public void ArrayReferenceWorks()
        {
            var arr = "abcde".ToArray();
            var r = new Reference<char>(arr, 2);

            Assert.Equal('c', r.Value);
            Assert.Equal('c', (char)r);
            r.Value = 'q';
            Assert.Equal('q', arr[2]);
            Assert.Equal("abqde", new string(arr));
        }

        [Fact]
        public void ArenaReferenceWorks()
        {
            using (var arena = new Arena<char>())
            {
                var arr = arena.Allocate(5);
                ReadOnlySpan<char> c = "abcde".AsSpan();
                c.CopyTo(arr);
                var r = arr.GetReference(2);

                Assert.Equal('c', r.Value);
                Assert.Equal('c', (char)r);
                r.Value = 'q';
                Assert.Equal('q', arr.FirstSpan[2]);
                Assert.Equal("abqde", new string(arr.ToArray()));
            }
        }
    }
}
