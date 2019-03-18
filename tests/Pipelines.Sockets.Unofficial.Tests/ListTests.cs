using Pipelines.Sockets.Unofficial.Arenas;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class ListTests
    {
        [Fact]
        public void CanUseLists()
        {
            var list = SequenceList<int>.Create(200);
            for (int i = 0; i < 200; i++)
            {
                Assert.Equal(i, list.Count);
                Assert.Equal(200, list.Capacity);
                list.Add(i);
            }
            Assert.Equal(200, list.Count);
            Assert.Equal(200, list.Capacity);

            int j = 0;
            foreach(var item in list)
            {
                Assert.Equal(j++, item);
            }
            for(int i = 0; i < 200;i++)
            {
                Assert.Equal(i, list[i]);
            }
        }

        //[Fact]
        //public void CanUseListsViaAppender()
        //{
        //    var list = SequenceList<int>.Create(200);
        //    var appender = list.GetAppender();
        //    for (int i = 0; i < 200; i++)
        //    {
        //        Assert.Equal(i, list.Count);
        //        Assert.Equal(200, list.Capacity);
        //        appender.Add(i);
        //    }
        //    Assert.Equal(200, list.Count);
        //    Assert.Equal(200, list.Capacity);

        //    int j = 0;
        //    foreach (var item in list)
        //    {
        //        Assert.Equal(j++, item);
        //    }
        //    for (int i = 0; i < 200; i++)
        //    {
        //        Assert.Equal(i, list[i]);
        //    }
        //}
    }
}
