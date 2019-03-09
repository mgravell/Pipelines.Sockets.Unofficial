using System.Threading.Tasks;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class ThreadPoolTests
    {
        [Fact]
        public void TestRunnerIsNotWorker()
        {
            Assert.False(DedicatedThreadPoolPipeScheduler.IsWorker());
            Assert.False(DedicatedThreadPoolPipeScheduler.IsWorker(DedicatedThreadPoolPipeScheduler.Default));
        }

        [Fact]
        public async Task PoolThreadIsWorker()
        {
            var defautPool = DedicatedThreadPoolPipeScheduler.Default;
            using (var newPool = new DedicatedThreadPoolPipeScheduler())
            {
                var tcs = new TaskCompletionSource<bool>();
                defautPool.Schedule(
                    _ => tcs.SetResult(DedicatedThreadPoolPipeScheduler.IsWorker()), null);
                Assert.True(await tcs.Task);

                tcs = new TaskCompletionSource<bool>();
                newPool.Schedule(
                    _ => tcs.SetResult(DedicatedThreadPoolPipeScheduler.IsWorker()), null);
                Assert.True(await tcs.Task);

                tcs = new TaskCompletionSource<bool>();
                defautPool.Schedule(
                    _ => tcs.SetResult(DedicatedThreadPoolPipeScheduler.IsWorker(defautPool)), null);
                Assert.True(await tcs.Task);

                tcs = new TaskCompletionSource<bool>();
                newPool.Schedule(
                    _ => tcs.SetResult(DedicatedThreadPoolPipeScheduler.IsWorker(defautPool)), null);
                Assert.False(await tcs.Task);

                tcs = new TaskCompletionSource<bool>();
                defautPool.Schedule(
                    _ => tcs.SetResult(DedicatedThreadPoolPipeScheduler.IsWorker(newPool)), null);
                Assert.False(await tcs.Task);

                tcs = new TaskCompletionSource<bool>();
                newPool.Schedule(
                    _ => tcs.SetResult(DedicatedThreadPoolPipeScheduler.IsWorker(newPool)), null);
                Assert.True(await tcs.Task);
            }
        }
    }
}
