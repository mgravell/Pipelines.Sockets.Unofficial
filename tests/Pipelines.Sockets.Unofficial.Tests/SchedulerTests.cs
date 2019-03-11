using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class SchedulerTests
    {
        private readonly ITestOutputHelper _output;

        public SchedulerTests(ITestOutputHelper output)
            => _output = output;

        private void Log(string line)
        {
            if(_output != null)
            {
                lock (_output) _output.WriteLine(line);
            }
        }

        [Fact]
        public async Task TestDedicatedScheduler()
        {
            var pool = DedicatedThreadPoolPipeScheduler.Default;
            await Task.Delay(100).ConfigureAwait(false);

            Log("after:");
            Log($"serviced by pool: {pool.TotalServicedByPool}");
            Log($"serviced by queue: {pool.TotalServicedByQueue}");
            Log($"available: {pool.AvailableCount}");

            await TestScheduler(pool).ConfigureAwait(false);

            Log("after:");
            Log($"serviced by pool: {pool.TotalServicedByPool}");
            Log($"serviced by queue: {pool.TotalServicedByQueue}");
            Log($"available: {pool.AvailableCount}");
        }

        [Fact]
        public Task TestThreadPoolScheduler() => TestScheduler(PipeScheduler.ThreadPool);

        private class SchedulerState : TaskCompletionSource<int>
        {
            private int _count;
            private readonly int _start;
            private readonly PipeScheduler _scheduler;
            private static readonly Action<object> RunNext = s => ((SchedulerState)s).Next();
            public void Start() => _scheduler.Schedule(RunNext, this);
            private void Next()
            {
                if (Task.IsCanceled) { }
                else if (--_count == 0)
                {
                    var end = Environment.TickCount;
                    TrySetResult(unchecked(end - _start));
                }
                else
                {
                    _scheduler.Schedule(RunNext, this);
                }
            }
            public SchedulerState(PipeScheduler scheduler, int count)
            {
                _count = count;
                _start = Environment.TickCount;
                _scheduler = scheduler;
            }
        }
        private async Task<int> TestScheduler(PipeScheduler scheduler, int count = 1000000, int timeoutMilliseconds = 5000)
        {
            var time = await TestSchedulerImpl(scheduler, count, timeoutMilliseconds).ConfigureAwait(false);
            Log($"time taken: {time}ms for {count} schedules");
            return time;
        }
        private Task<int> TestSchedulerImpl(PipeScheduler scheduler, int count, int timeoutMilliseconds)
        {
            var timeout = Task.Delay(timeoutMilliseconds);
            var obj = new SchedulerState(scheduler, count);
            obj.Start();
            var winner = Task.WhenAny(timeout, obj.Task);
            return obj.Task;
        }
    }
}
