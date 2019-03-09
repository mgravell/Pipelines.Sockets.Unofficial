using BenchmarkDotNet.Attributes;
using Pipelines.Sockets.Unofficial.Threading;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Pipelines.Sockets.Unofficial.Threading.MutexSlim;

namespace Benchmark
{
    [MemoryDiagnoser, CoreJob, ClrJob, MinColumn, MaxColumn]
    public class LockBenchmarks : BenchmarkBase
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
                    else Log?.Invoke($"failed at i={i}");
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
                else Log?.Invoke($"failed at i={i}");
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
                else Log?.Invoke($"failed at i={i}");
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
                    else Log?.Invoke($"failed at i={i}");
                }
                else
                {
                    if (await awaitable)
                    {
                        count++;
                        _semaphoreSlim.Release();
                    }
                    else Log?.Invoke($"failed at i={i}");
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
                    else Log?.Invoke($"failed at i={i},{_mutexSlim}");
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
                    else Log?.Invoke($"failed at i={i},{_mutexSlim}");
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
                        else Log?.Invoke($"failed at i={i},{_mutexSlim}");
                    }
                }
                else
                {
                    using (var token = await awaitable)
                    {
                        if (token) count++;
                        else Log?.Invoke($"failed at i={i},{_mutexSlim}");
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
                        else Log?.Invoke($"failed at i={i},{_mutexSlim}");
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
                    using (var taken = await _mutexSlim.TryWaitAsync(options: WaitOptions.DisableAsyncContext))
                    {
                        if (taken) success++;
                        else Log?.Invoke($"failed at i={i},t={t},{_mutexSlim}");
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

        [Benchmark(OperationsPerInvoke = 100 * 100)] // regular async (TaskCompletionSource<T> if async)
        public Task<int> MutexSlim_ConcurrentLoadAsync_AD_Off()
            => ExecuteMutexSlimConcurrentLoad_SD(WaitOptions.None);

        [Benchmark(OperationsPerInvoke = 100 * 100)] // aggressively remove sync/exec/etc context overhead
        public Task<int> MutexSlim_ConcurrentLoadAsync_AD_Off_DisableContext()
            => ExecuteMutexSlimConcurrentLoad_SD(WaitOptions.DisableAsyncContext);

        [Benchmark(OperationsPerInvoke = 100 * 100)] // regular async (TaskCompletionSource<T> if async)
        public Task<int> MutexSlim_ConcurrentLoadAsync_AD()
            => ExecuteMutexSlimConcurrentLoad_AD(WaitOptions.None);

        [Benchmark(OperationsPerInvoke = 100 * 100)] // aggressively remove sync/exec/etc context overhead
        public Task<int> MutexSlim_ConcurrentLoadAsync_AD_DisableContext()
            => ExecuteMutexSlimConcurrentLoad_AD(WaitOptions.DisableAsyncContext);

        [Benchmark(OperationsPerInvoke = 100 * 100)] // above plus something worse
        public Task<int> MutexSlim_ConcurrentLoadAsync_AD_Evil()
            => ExecuteMutexSlimConcurrentLoad_AD(WaitOptions.DisableAsyncContext | WaitOptions.EvilMode);


        private async Task<int> ExecuteMutexSlimConcurrentLoad_AD(WaitOptions options)
        {
            // spin up 100 async workers all fighting for the same mutex resource
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    // this should be "await using (var taken = ...)", when
                    // IAsyncDisposable and async-streams/disposables land
                    LockToken taken = await _mutexSlim.TryWaitAsync(options: options);
                    try {
                        if (taken) {
                            success++;
                            // scenario modelled: sometimes yield inside the lock
                            if (t % 10 == 0) await Task.Yield();
                        }
                        else Log?.Invoke($"failed at i={i},t={t},{_mutexSlim}");
                    }
                    finally {
                        await taken.DisposeAsync();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(100 * 100);
        }

        private async Task<int> ExecuteMutexSlimConcurrentLoad_SD(WaitOptions options)
        {
            // spin up 100 async workers all fighting for the same mutex resource
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < 100; t++)
                {
                    // this should be "await using (var taken = ...)", when
                    // IAsyncDisposable and async-streams/disposables land
                    LockToken taken = await _mutexSlim.TryWaitAsync(options: options);
                    try
                    {
                        if (taken)
                        {
                            success++;
                            // scenario modelled: sometimes yield inside the lock
                            if (t % 10 == 0) await Task.Yield();
                        }
                        else Log?.Invoke($"failed at i={i},t={t},{_mutexSlim}");
                    }
                    finally
                    {
                        taken.Dispose();
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
                        else Log?.Invoke($"failed at i={i},t={t},{_mutexSlim}");
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
                        else Log?.Invoke($"failed at i={i},t={t}");
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
                        else Log?.Invoke($"failed at i={i},t={t}");
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
