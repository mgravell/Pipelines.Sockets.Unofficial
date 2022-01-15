using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Pipelines.Sockets.Unofficial.Threading;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Benchmark
{
    [MemoryDiagnoser, MinColumn, MaxColumn]
    public class LockBenchmarks : BenchmarkBase
    {
        private const int TIMEOUTMS = 2000;
        private readonly MutexSlim _mutexSlim = new MutexSlim(TIMEOUTMS);
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
#if !NO_NITO
        private readonly Nito.AsyncEx.AsyncSemaphore _asyncSemaphore = new Nito.AsyncEx.AsyncSemaphore(1);
#endif
        private readonly object _syncLock = new object();

        private const int PER_TEST = 5 * 1024;

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
                else
                {
                    Log?.Invoke($"failed at i={i}");
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
                if (await _semaphoreSlim.WaitAsync(TIMEOUTMS).ConfigureAwait(false))
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
                else
                {
                    Log?.Invoke($"failed at i={i}");
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
                    else
                    {
                        Log?.Invoke($"failed at i={i}");
                    }
                }
                else
                {
                    if (await awaitable)
                    {
                        count++;
                        _semaphoreSlim.Release();
                    }
                    else
                    {
                        Log?.Invoke($"failed at i={i}");
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
                using var token = _mutexSlim.TryWait();
                if (token) count++;
                else Log?.Invoke($"failed at i={i},{_mutexSlim}");
            }
            return count.AssertIs(PER_TEST);
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        public async ValueTask<int> MutexSlim_Async()
        {
            int count = 0;
            for (int i = 0; i < PER_TEST; i++)
            {
                using var token = await _mutexSlim.TryWaitAsync().ConfigureAwait(false);
                if (token) count++;
                else Log?.Invoke($"failed at i={i},{_mutexSlim}");
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
                    using var token = awaitable.Result;
                    if (token) count++;
                    else Log?.Invoke($"failed at i={i},{_mutexSlim}");
                }
                else
                {
                    using var token = await awaitable;
                    if (token) count++;
                    else Log?.Invoke($"failed at i={i},{_mutexSlim}");
                }
            }
            return count.AssertIs(PER_TEST);
        }

#if DEBUG
        private const int CC_LOAD = 5;
#else
        private const int CC_LOAD = 50;
#endif

        [Benchmark(OperationsPerInvoke = CC_LOAD * CC_LOAD)]
        public async Task<int> MutexSlim_ConcurrentLoadAsync()
        {
            var tasks = Enumerable.Range(0, CC_LOAD).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < CC_LOAD; t++)
                {
                    using (var taken = await _mutexSlim.TryWaitAsync().ConfigureAwait(false))
                    {
                        if (taken) success++;
                        else Log?.Invoke($"failed at i={i},{_mutexSlim}");
                        await Task.Yield();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(CC_LOAD * CC_LOAD);
        }

        [Benchmark(OperationsPerInvoke = CC_LOAD * CC_LOAD)]
        public async Task<int> MutexSlim_ConcurrentLoadAsync_DisableContext()
        {
            var tasks = Enumerable.Range(0, CC_LOAD).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < CC_LOAD; t++)
                {
                    using (var taken = await _mutexSlim.TryWaitAsync(options: MutexSlim.WaitOptions.DisableAsyncContext).ConfigureAwait(false))
                    {
                        if (taken) success++;
                        else Log?.Invoke($"failed at i={i},t={t},{_mutexSlim}");
                        await Task.Yield();
                    }
                    await Task.Yield();
                }
                return success;
            }).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(CC_LOAD * CC_LOAD);
        }

        // this is a sync-over-async; don't do it
        //[Benchmark(OperationsPerInvoke = CC_LOAD * CC_LOAD)]
        public async Task<int> MutexSlim_ConcurrentLoad()
        {
            var tasks = Enumerable.Range(0, CC_LOAD).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < CC_LOAD; t++)
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

            await Task.WhenAll(tasks).ConfigureAwait(false);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(CC_LOAD * CC_LOAD);
        }

        [Benchmark(OperationsPerInvoke = CC_LOAD * CC_LOAD)]
        public async Task<int> SemaphoreSlim_ConcurrentLoadAsync()
        {
            var tasks = Enumerable.Range(0, CC_LOAD).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < CC_LOAD; t++)
                {
                    var taken = await _semaphoreSlim.WaitAsync(TIMEOUTMS).ConfigureAwait(false);
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

            await Task.WhenAll(tasks).ConfigureAwait(false);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(CC_LOAD * CC_LOAD);
        }

        // this is a sync-over-async; don't do it
        // [Benchmark(OperationsPerInvoke = CC_LOAD * CC_LOAD)]
        public async Task<int> SemaphoreSlim_ConcurrentLoad()
        {
            var tasks = Enumerable.Range(0, CC_LOAD).Select(async i =>
            {
                int success = 0;
                for (int t = 0; t < CC_LOAD; t++)
                {
                    var taken = _semaphoreSlim.Wait(TIMEOUTMS);
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

            await Task.WhenAll(tasks).ConfigureAwait(false);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(CC_LOAD * CC_LOAD);
        }

#if !NO_NITO

        // this is a sync-over-async; don't do it
        // [Benchmark(OperationsPerInvoke = CC_LOAD * CC_LOAD)]
        public async Task<int> AsyncSemaphore_ConcurrentLoad()
        {
            var tasks = Enumerable.Range(0, CC_LOAD).Select(async _ =>
            {
                int success = 0;
                for (int t = 0; t < CC_LOAD; t++)
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

            await Task.WhenAll(tasks).ConfigureAwait(false);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(CC_LOAD * CC_LOAD);
        }

        [Benchmark(OperationsPerInvoke = CC_LOAD * CC_LOAD)]
        public async Task<int> AsyncSemaphore_ConcurrentLoadAsync()
        {
            var tasks = Enumerable.Range(0, CC_LOAD).Select(async _ =>
            {
                int success = 0;
                for (int t = 0; t < CC_LOAD; t++)
                {
                    await _asyncSemaphore.WaitAsync().ConfigureAwait(false);
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

            await Task.WhenAll(tasks).ConfigureAwait(false);
            int total = tasks.Sum(x => x.Result);
            return total.AssertIs(CC_LOAD * CC_LOAD);
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
                await _asyncSemaphore.WaitAsync().ConfigureAwait(false);
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
