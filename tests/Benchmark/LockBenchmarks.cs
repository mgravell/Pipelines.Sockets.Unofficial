using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Threading;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Benchmark
{
    [MemoryDiagnoser, CoreJob, ClrJob, MinColumn, MaxColumn]
    public class LockBenchmarks
    {
        const int TIMEOUTMS = 2000;
        private readonly MutexSlim _mutexSlim = new MutexSlim(TIMEOUTMS);
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
#if !NO_NITO
        private readonly Nito.AsyncEx.AsyncSemaphore _asyncSemaphore = new Nito.AsyncEx.AsyncSemaphore(1);
#endif
        private readonly object _syncLock = new object();

        const int PER_TEST = 5 * 1024;

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public int Monitor_Sync()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                bool haveLock = false;
                Monitor.TryEnter(_syncLock, TIMEOUTMS, ref haveLock);
                try
                {
                    if (haveLock) count++;
                }
                finally
                {
                    if (haveLock) Monitor.Exit(_syncLock);
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public int SemaphoreSlim_Sync()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                if (_semaphoreSlim.Wait(TIMEOUTMS))
                {
                    try // make sure we measure the expected try/finally usage
                    {
                        count++;
                    }
                    finally
                    {
                        _semaphoreSlim.Release();
                    }
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public async ValueTask<int> SemaphoreSlim_Async()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                if (await _semaphoreSlim.WaitAsync(TIMEOUTMS))
                {
                    try // make sure we measure the expected try/finally usage
                    {
                        count++;
                    }
                    finally
                    {
                        _semaphoreSlim.Release();
                    }
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public async ValueTask<int> SemaphoreSlim_Async_HotPath()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                var awaitable = _semaphoreSlim.WaitAsync(TIMEOUTMS);
                if (awaitable.IsCompleted)
                {
                    if (awaitable.Result)
                    {
                        count++;
                        _semaphoreSlim.Release();
                    }
                }
                else
                {
                    if (await awaitable)
                    {
                        count++;
                        _semaphoreSlim.Release();
                    }
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public int MutexSlim_Sync()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                using (var token = _mutexSlim.TryWait())
                {
                    if (token) count++;
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public async ValueTask<int> MutexSlim_Async()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                using (var token = await _mutexSlim.TryWaitAsync())
                {
                    if (token) count++;
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public async ValueTask<int> MutexSlim_Async_HotPath()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                var awaitable = _mutexSlim.TryWaitAsync();
                if (awaitable.IsCompletedSuccessfully)
                {
                    using (var token = awaitable.Result)
                    {
                        if (token) count++;
                    }
                }
                else
                {
                    using (var token = await awaitable)
                    {
                        if (token) count++;
                    }
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = 100 * 100)]
        public async Task<int> MutexSlim_ConcurrentLoadAsync()
        {
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    using (var taken = await _mutexSlim.TryWaitAsync())
                    {
                        if (taken) success++;
                        await Task.Yield();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

        [Benchmark(OperationsPerInvoke = 100 * 100)]
        public async Task<int> MutexSlim_ConcurrentLoadAsync_DisableContext()
        {
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    using (var taken = await _mutexSlim.TryWaitAsync(options: MutexSlim.WaitOptions.DisableAsyncContext))
                    {
                        if (taken) success++;
                        await Task.Yield();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

        // this is a sync-over-async; don't do it
        //[Benchmark(OperationsPerInvoke = 100 * 100)]
        public async Task<int> MutexSlim_ConcurrentLoad()
        {
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    using (var taken = _mutexSlim.TryWait())
                    {
                        if (taken) success++;
                        await Task.Yield();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

        [Benchmark(OperationsPerInvoke = 100 * 100)]
        public async Task<int> SemaphoreSlim_ConcurrentLoadAsync()
        {
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    var taken = await _semaphoreSlim.WaitAsync(1000);
                    try
                    {
                        if (taken) success++;
                        await Task.Yield();
                    }
                    finally
                    {
                        if (taken) _semaphoreSlim.Release();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

        // this is a sync-over-async; don't do it
        // [Benchmark(OperationsPerInvoke = 100 * 100)]
        public async Task<int> SemaphoreSlim_ConcurrentLoad()
        {
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    var taken = _semaphoreSlim.Wait(1000);
                    try
                    {
                        if (taken) success++;
                        await Task.Yield();
                    }
                    finally
                    {
                        if (taken) _semaphoreSlim.Release();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

#if !NO_NITO

        // this is a sync-over-async; don't do it
        // [Benchmark(OperationsPerInvoke = 100 * 100)]
        public async Task<int> AsyncSemaphore_ConcurrentLoad()
        {
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    _asyncSemaphore.Wait();
                    try
                    {
                        success++;
                        await Task.Yield();
                    }
                    finally
                    {
                        _asyncSemaphore.Release();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

        [Benchmark(OperationsPerInvoke = 100 * 100)]
        public async Task<int> AsyncSemaphore_ConcurrentLoadAsync()
        {
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    await _asyncSemaphore.WaitAsync();
                    try
                    {
                        success++;
                        await Task.Yield();
                    }
                    finally
                    {
                        _asyncSemaphore.Release();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public int AsyncSemaphore_Sync()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                _asyncSemaphore.Wait();
                try
                {
                    count++;
                }
                finally
                { // to make useful comparison
                    _asyncSemaphore.Release();
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public async ValueTask<int> AsyncSemaphore_Async()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                await _asyncSemaphore.WaitAsync();
                try
                {
                    count++;
                }
                finally
                { // to make useful comparison
                    _asyncSemaphore.Release();
                }
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public async ValueTask<int> AsyncSemaphore_Async_HotPath()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                var pending = _asyncSemaphore.WaitAsync();
                if (pending.Status != TaskStatus.RanToCompletion)
                {
                    await pending;
                }
                try
                {
                    count++;
                }
                finally
                { // to make useful comparison
                    _asyncSemaphore.Release();
                }
            }
            return count.AssertIs(PER_TEST);
        }
#endif
    }
}
