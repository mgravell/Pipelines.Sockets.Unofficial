using Pipelines.Sockets.Unofficial.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using static Pipelines.Sockets.Unofficial.Threading.MutexSlim;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class MutexSlimTests
    {
        public MutexSlimTests(ITestOutputHelper log)
        {
            _log = log;
#if DEBUG
            _timeoutMux.Logged += Log;
#endif
        }
        private readonly ITestOutputHelper _log;
        private void Log(string message)
        {
            if (_log != null)
            {
                lock (_log) _log.WriteLine(message);
            }
        }

        private readonly MutexSlim _zeroTimeoutMux = new MutexSlim(0),
            _timeoutMuxCustomScheduler = new MutexSlim(1000, DedicatedThreadPoolPipeScheduler.Default),
            _timeoutMux = new MutexSlim(1000);

        private class DummySyncContext : SynchronizationContext
        {
            public Guid Id { get; }
            public DummySyncContext(Guid guid) => Id = guid;

            public static bool Is(Guid id) => Current is DummySyncContext dsc && dsc.Id == id;

            public override void Post(SendOrPostCallback d, object state)
                => ThreadPool.QueueUserWorkItem(_ => Send(d, state), null);

            public override void Send(SendOrPostCallback d, object state)
            {
                var original = Current;
                try
                {
                    SetSynchronizationContext(this);
                    d.Invoke(state);
                }
                finally
                {
                    SetSynchronizationContext(original);
                }
            }
        }

        [Fact]
        public async Task SyncContextNotPreservedByTryWaitAsync()
        {
            var taken = _timeoutMuxCustomScheduler.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                var pending = _timeoutMuxCustomScheduler.TryWaitAsync(options: WaitOptions.DisableAsyncContext);
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMuxCustomScheduler uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using var token2 = await pending;
                int awaitedThread = Environment.CurrentManagedThreadId;
                Assert.True(token2.Success, "obtained lock after dispose");

                Assert.NotEqual(originalThread, awaitedThread);
                Assert.False(DummySyncContext.Is(id));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public async Task SyncContextNotPreservedByTryWaitAsync_AsValueTask()
        {
            var taken = _timeoutMuxCustomScheduler.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                ValueTask<LockToken> pending = _timeoutMuxCustomScheduler.TryWaitAsync(options: WaitOptions.DisableAsyncContext);
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMuxCustomScheduler uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using var token2 = await pending;
                int awaitedThread = Environment.CurrentManagedThreadId;
                Assert.True(token2.Success, "obtained lock after dispose");

                Assert.NotEqual(originalThread, awaitedThread);
                Assert.False(DummySyncContext.Is(id));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public async Task SyncContextPreservedByTryWaitAsync_WithCapture()
        {
            var taken = _timeoutMuxCustomScheduler.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                ValueTask<LockToken> pending = _timeoutMuxCustomScheduler.TryWaitAsync();
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMuxCustomScheduler uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using var token2 = await pending;
                int awaitedThread = Environment.CurrentManagedThreadId;
                Assert.True(token2.Success, "obtained lock after dispose");

                Assert.NotEqual(originalThread, awaitedThread);
                Assert.True(DummySyncContext.Is(id));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public async Task SyncContextNotPreservedByTryWaitAsync_WithCaptureAndConfigureAwait()
        {
            var taken = _timeoutMuxCustomScheduler.TryWait();
            Assert.True(taken.Success, "obtained original lock");

            var id = Guid.NewGuid();
            var orig = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DummySyncContext(id));
                Assert.True(DummySyncContext.Is(id));

                ValueTask<LockToken> pending = _timeoutMuxCustomScheduler.TryWaitAsync(options: WaitOptions.None);
                Assert.False(pending.IsCompleted);

                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(100); taken.Dispose(); }, null);
                // note that _timeoutMuxCustomScheduler uses DedicatedThreadPoolPipeScheduler to
                // force us to be on a different thread here (since [Fact] won't be using that thread)
                int originalThread = Environment.CurrentManagedThreadId;
                using var token2 = await pending.ConfigureAwait(false);
                int awaitedThread = Environment.CurrentManagedThreadId;
                Assert.True(token2.Success, "obtained lock after dispose");

                Assert.NotEqual(originalThread, awaitedThread);
                Assert.False(DummySyncContext.Is(id));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(orig);
            }
        }

        [Fact]
        public void CanObtain()
        {
            // can obtain when not contested (outer)
            // can not obtain when contested, even if re-entrant (inner)
            // can obtain after released (loop)
            for (int i = 0; i < 2; i++)
            {
                Assert.True(_zeroTimeoutMux.IsAvailable);
                using var outer = _zeroTimeoutMux.TryWait();
                Assert.True(outer.Success);
                Log(outer.ToString());
                Assert.False(_zeroTimeoutMux.IsAvailable);
                using var inner = _zeroTimeoutMux.TryWait();
                Log(inner.ToString());
                Assert.False(inner.Success);
            }
            Assert.True(_zeroTimeoutMux.IsAvailable);

            for (int i = 0; i < 2; i++)
            {
                using var outer = _timeoutMux.TryWait();
                Assert.True(outer.Success);
            }
        }

        [Fact]
        public void ChangeStatePreservesCounter()
        {
            Assert.Equal(0xAAA8, LockState.ChangeState(0xAAAA, LockState.Timeout));
            Assert.Equal(0xAAA9, LockState.ChangeState(0xAAAA, LockState.Pending));
            Assert.Equal(0xAAAA, LockState.ChangeState(0xAAAA, LockState.Success));
            Assert.Equal(0xAAAB, LockState.ChangeState(0xAAAA, LockState.Canceled));
        }
        [Fact]
        public void NextTokenIncrementsCorrectly()
        {
            // GetNextToken should always reset the 2 LSB (we'll test it with all 4 inputs), and increment the others
            int token = 0;
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Timeout));
            Assert.Equal(6, token); // 000110
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Pending));
            Assert.Equal(10, token); // 001010
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Success));
            Assert.Equal(14, token); // 001110
            token = LockState.GetNextToken(LockState.ChangeState(token, LockState.Canceled));
            Assert.Equal(18, token); // 010010

            // and at wraparound, we expect zero again
            token = -1; // anecdotally: a cancelation, but that doesn't matter
            token = LockState.GetNextToken(token);
            Assert.Equal(2, token); // 000010
            token = LockState.GetNextToken(token);
            Assert.Equal(6, token); // 000110
        }

        [Fact]
        public async Task CanObtainAsyncWithoutTimeout()
        {
            // can obtain when not contested (outer)
            // can not obtain when contested, even if re-entrant (inner)
            // can obtain after released (loop)
            // with no timeout: is always completed
            // with timeout: is completed on the success option

            for (int i = 0; i < 2; i++)
            {
                var awaitable = _zeroTimeoutMux.TryWaitAsync();
                Assert.True(awaitable.IsCompleted, nameof(awaitable.IsCompleted));
                //Assert.True(awaitable.CompletedSynchronously, nameof(awaitable.CompletedSynchronously));
                using var outer = await awaitable;
                Assert.True(outer.Success, nameof(outer.Success));

                awaitable = _zeroTimeoutMux.TryWaitAsync();
                Assert.True(awaitable.IsCompleted, nameof(awaitable.IsCompleted) + " inner");
                //Assert.True(awaitable.CompletedSynchronously, nameof(awaitable.CompletedSynchronously) + " inner");
                using var inner = await awaitable;
                Assert.False(inner.Success, nameof(inner.Success) + " inner");
            }
        }

        [Fact]
        public async Task CanObtainAsyncWithTimeout()
        {
            for (int i = 0; i < 2; i++)
            {
                var awaitable = _timeoutMux.TryWaitAsync();
                Assert.True(awaitable.IsCompleted, nameof(awaitable.IsCompleted));
                //Assert.True(awaitable.CompletedSynchronously, nameof(awaitable.CompletedSynchronously));
                using var outer = await awaitable;
                Assert.True(outer.Success);
            }
        }

        [Fact]
        public void Timeout()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, complete = 0, success = 0;

            Assert.NotEqual(0, _timeoutMux.TimeoutMilliseconds);
            lock (allDone)
            {
                using (var token = _timeoutMux.TryWait())
                {
                    lock (allReady)
                    {
                        for (int i = 0; i < COMPETITORS; i++)
                        {
                            Task.Run(() =>
                            {
                                lock (allReady)
                                {
                                    if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                    else Monitor.Wait(allReady);
                                }
                                using var inner = _timeoutMux.TryWait();
                                lock (allDone)
                                {
                                    if (inner) success++;
                                    if (++complete == COMPETITORS) Monitor.Pulse(allDone);
                                }
                                Thread.Sleep(10);
                            });
                        }
                        Monitor.Wait(allReady);
                    }
                    Thread.Sleep(_timeoutMux.TimeoutMilliseconds * 2);
                }
                Monitor.Wait(allDone);
                Assert.Equal(COMPETITORS, complete);
                Assert.Equal(0, success);
            }
        }

        [Theory]
        [InlineData(WaitOptions.None)]
        [InlineData(WaitOptions.DisableAsyncContext)]
        public void CompetingCallerAllExecute(WaitOptions waitOptions)
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, complete = 0, success = 0;
            lock (allDone)
            {
                using (var token = _timeoutMux.TryWait())
                {
                    lock (allReady)
                    {
                        for (int i = 0; i < COMPETITORS; i++)
                        {
                            Task.Run(() =>
                            {
                                lock (allReady)
                                {
                                    if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                    else Monitor.Wait(allReady);
                                }
                                using var inner = _timeoutMux.TryWait(waitOptions);
                                lock (allDone)
                                {
                                    if (inner) success++;
                                    if (++complete == COMPETITORS) Monitor.Pulse(allDone);
                                }
                                Thread.Sleep(10);
                            });
                        }
                        Monitor.Wait(allReady);
                    }
                    Thread.Sleep(100);
                }
                Monitor.Wait(allDone);
                Assert.Equal(COMPETITORS, complete);
                Assert.Equal(COMPETITORS, success);
            }
        }

        [Theory]
        [InlineData(WaitOptions.None)]
        [InlineData(WaitOptions.DisableAsyncContext)]
        public async Task CompetingCallerAllExecuteAsync(WaitOptions waitOptions)
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, success = 0, asyncOps = 0;

            var tasks = new Task[COMPETITORS];
            using (var token = await _timeoutMux.TryWaitAsync().ConfigureAwait(false))
            {
                lock (allReady)
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        int j = i;
                        tasks[i] = Task.Run(async () =>
                        {
                            lock (allReady)
                            {
                                if (++active == COMPETITORS)
                                {
                                    Log($"all tasks ready; releasing everyone");
                                    Monitor.PulseAll(allReady);
                                }
                                else
                                {
                                    Monitor.Wait(allReady);
                                }
                            }
                            var awaitable = _timeoutMux.TryWaitAsync(options: waitOptions);
                            if (!awaitable.IsCompleted) asyncOps++;
                            Log($"task {j} about to await...");
                            using var inner = await awaitable;
                            Log($"task {j} resumed; got lock: {inner.Success}");
                            lock (allDone)
                            {
                                if (inner) success++;
                                //if (!awaitable.CompletedSynchronously) asyncOps++;
                            }
                            await Task.Delay(10).ConfigureAwait(false);
                        });
                    }
                    Log($"outer lock waiting for everyone to be ready");
                    Monitor.Wait(allReady);
                }
                Log("delaying release...");
                await Task.Delay(100).ConfigureAwait(false);
                Log("about to release outer lock");
            }
            Log("outer lock released");
            for (int i = 0; i < tasks.Length; i++)
            {   // deliberately not an await - we want a simple timeout here
                Assert.True(tasks[i].Wait(_timeoutMux.TimeoutMilliseconds), $"task {i} completes after {_timeoutMux.TimeoutMilliseconds}ms");
            }

            lock (allDone)
            {
                Assert.Equal(COMPETITORS, success);
                Assert.Equal(COMPETITORS, asyncOps);
            }
        }

        [Fact]
        public async Task TimeoutAsync()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, success = 0, asyncOps = 0;

            var tasks = new Task[COMPETITORS];
            using (var token = await _timeoutMux.TryWaitAsync().ConfigureAwait(false))
            {
                lock (allReady)
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        tasks[i] = Task.Run(async () =>
                        {
                            lock (allReady)
                            {
                                if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                else Monitor.Wait(allReady);
                            }
                            var awaitable = _timeoutMux.TryWaitAsync();
                            if (!awaitable.IsCompleted)
                            {
                                Interlocked.Increment(ref asyncOps);
                            }
                            using var inner = await awaitable;
                            lock (allDone)
                            {
                                if (inner) success++;
                                //if (!awaitable.CompletedSynchronously) asyncOps++;
                            }
                            await Task.Delay(10).ConfigureAwait(false);
                        });
                    }
                    Monitor.Wait(allReady);
                }
                await Task.Delay(_timeoutMux.TimeoutMilliseconds * 2).ConfigureAwait(false);
            }
            for (int i = 0; i < tasks.Length; i++)
            {   // deliberately not an await - we want a simple timeout here
                Assert.True(tasks[i].Wait(_timeoutMux.TimeoutMilliseconds));
            }

            lock (allDone)
            {
                Assert.Equal(0, success);
                Assert.Equal(COMPETITORS, asyncOps);
            }
        }

        [Fact]
        public async Task PreCanceledReportsCorrectly()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            var ct = _timeoutMux.TryWaitAsync(cancel.Token);
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            Assert.Throws<TaskCanceledException>(() => ct.Result);

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }

        [Fact]
        public async Task DuringCanceledReportsCorrectly()
        {
            using var cancel = new CancellationTokenSource();

            // cancel it *after* issuing incomplete token

            ValueTask<LockToken> ct;
            using (var token = _timeoutMux.TryWait())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync(cancel.Token);
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted));
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

                cancel.Cancel(); // cancel it *before* release; should be respected
            }
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            Assert.Throws<TaskCanceledException>(() => ct.Result);

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }

        [Fact]
        public async Task PostCanceledReportsCorrectly()
        {
            using var cancel = new CancellationTokenSource();
            // cancel it *after* issuing incomplete token

            ValueTask<LockToken> ct;
            using (var token = _timeoutMux.TryWait())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync(cancel.Token);
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted) + ":1");
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled) + ":1");
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully) + ":1");
            }
            // cancel it *after* release - should be ignored
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted) + ":2");
            Assert.False(ct.IsCanceled, nameof(ct.IsCanceled) + ":2");
            Assert.True(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully) + ":2");

            var result = ct.Result;
            Assert.True(result.Success);

            result = await ct;
            Assert.True(result.Success);
        }

        [Fact]
        public async Task ManualCanceledReportsCorrectly()
        {
            ValueTask<LockToken> ct;
            using (var token = _timeoutMux.TryWait())
            using (var cancel = new CancellationTokenSource())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync(cancel.Token);
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted));
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

                cancel.Cancel();
                //Assert.True(ct.TryCancel());
                //Assert.True(ct.TryCancel());
            }
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            Assert.Throws<TaskCanceledException>(() => ct.Result);

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }

        [Fact]
        public async Task ManualCancelAfterAcquisitionDoesNothing()
        {
            using var cancel = new CancellationTokenSource();
            ValueTask<LockToken> ct;
            using (var token = _timeoutMux.TryWait())
            {
                Assert.True(token.Success);

                ct = _timeoutMux.TryWaitAsync(cancel.Token);
                Assert.False(ct.IsCompleted, nameof(ct.IsCompleted));
                Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
                Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));
            }
            cancel.Cancel();
            //Assert.False(ct.TryCancel());
            //Assert.False(ct.TryCancel());

            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.False(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.True(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            var result = ct.Result;
            Assert.True(result.Success);

            result = await ct;
            Assert.True(result.Success);
        }

        [Fact]
        public async Task ManualCancelOnPreCanceledDoesNothing()
        {
            // cancel it *before* issuing token
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            var ct = _timeoutMux.TryWaitAsync(cancel.Token);
            Assert.True(ct.IsCompleted, nameof(ct.IsCompleted));
            Assert.True(ct.IsCanceled, nameof(ct.IsCanceled));
            Assert.False(ct.IsCompletedSuccessfully, nameof(ct.IsCompletedSuccessfully));

            //Assert.True(ct.TryCancel());
            //Assert.True(ct.TryCancel());

            Assert.Throws<TaskCanceledException>(() => ct.Result);

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await ct).ConfigureAwait(false);
        }

        [Theory]
        [InlineData(1, 50000000)] // uncontested
        [InlineData(2, 25000000)] // duel
        [InlineData(10, 100000)] // battle royale
        public void DuelingThreadsShouldNotStall(int workerCount, int perWorker)
        {
#if DEBUG
            perWorker /= 100;
#endif
            Volatile.Write(ref _failCount, 0);
            Volatile.Write(ref _successCount, 0);
            Array.Clear(_buckets, 0, _buckets.Length);
            Thread[] workers = new Thread[workerCount - 1];
#pragma warning disable IDE0039
            ThreadStart work = () => RunAcquireReleaseLoop(perWorker);
#pragma warning restore IDE0039
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(work)
                {
                    Priority = ThreadPriority.AboveNormal,
                    IsBackground = true,
                    Name = nameof(DuelingThreadsShouldNotStall)
                };
            }
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i].Start();
            }
            work(); // we are the final worker
            for (int i = 0; i < workers.Length; i++)
            {
                Assert.True(workers[i].Join(10000), "failure to join worker " + i);
            }

            int failCount = Volatile.Read(ref _failCount);
            int successCount = Volatile.Read(ref _successCount);
            int maxTaken = Volatile.Read(ref _maxGetLock);
            Log($"success: {successCount}, failure: {failCount}, max get lock: {_maxGetLock}");
            Assert.Equal(0, failCount);
            Assert.Equal(workerCount * perWorker, successCount);
            int endBucket;
            for(endBucket = _buckets.Length - 1; endBucket >= 0; endBucket--)
            {
                if (_buckets[endBucket] != 0) break;
            }
            for(int i = 0; i <= endBucket; i++)
            {
                Log($"{i}ms: {Volatile.Read(ref _buckets[i])}");
            }
        }

        [Theory]
        [InlineData(1, 30000000)] // uncontested
        [InlineData(2, 1500000)] // duel
        [InlineData(10, 150000)] // battle royale
        public async Task DuelingThreadsShouldNotStallAsync(int workerCount, int perWorker)
        {
#if DEBUG
            perWorker /= 100;
#endif
            Volatile.Write(ref _failCount, 0);
            Volatile.Write(ref _successCount, 0);
            Array.Clear(_buckets, 0, _buckets.Length);
            Task[] workers = new Task[workerCount];
#pragma warning disable IDE0039
            Func<Task> work = () => RunAcquireReleaseLoopAsync(perWorker);
#pragma warning restore IDE0039
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.Run(work);
            }
            var allDone = Task.WhenAll(workers);
            Assert.True(allDone.Wait(20000), "failure to join");
            await allDone;

            int failCount = Volatile.Read(ref _failCount);
            int successCount = Volatile.Read(ref _successCount);
            int maxTaken = Volatile.Read(ref _maxGetLock);
            Log($"success: {successCount}, failure: {failCount}, max get lock: {_maxGetLock}");
            Assert.Equal(0, failCount);
            Assert.Equal(workerCount * perWorker, successCount);
            int endBucket;
            for (endBucket = _buckets.Length - 1; endBucket >= 0; endBucket--)
            {
                if (_buckets[endBucket] != 0) break;
            }
            for (int i = 0; i <= endBucket; i++)
            {
                Log($"{i}ms: {Volatile.Read(ref _buckets[i])}");
            }
        }

        const int BUCKET_COUNT = 50;
        readonly int[] _buckets = new int[BUCKET_COUNT];
        int _failCount, _successCount, _maxGetLock, _attempts;
        void RunAcquireReleaseLoop(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int startedTakingLock = Environment.TickCount;
                var attempt = Interlocked.Increment(ref _attempts);
                using var token = _timeoutMux.TryWait();
                var gotLock = Environment.TickCount;
                int taken = unchecked(gotLock - startedTakingLock), oldMax;
                int aggregate = taken < 0 ? 0 : taken >= BUCKET_COUNT ? (BUCKET_COUNT - 1) : taken;
                Interlocked.Increment(ref _buckets[aggregate]);
                do
                {
                    oldMax = Volatile.Read(ref _maxGetLock);
                } while (taken > oldMax && Interlocked.CompareExchange(ref _maxGetLock, taken, oldMax) != oldMax);

                if (token.Success) Interlocked.Increment(ref _successCount);
                else
                {
                    var nowAttempt = Volatile.Read(ref _attempts);
                    _log.WriteLine($"failure: {token}, available: {_timeoutMux.IsAvailable}; attempts before: {attempt}, now: {nowAttempt}");
                    Interlocked.Increment(ref _failCount);
                    return; // give up promptly if we start failing
                }
                GC.KeepAlive(null);
            }
        }
        async Task RunAcquireReleaseLoopAsync(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int startedTakingLock = Environment.TickCount;
                var attempt = Interlocked.Increment(ref _attempts);
                using var token = await _timeoutMux.TryWaitAsync();
                var gotLock = Environment.TickCount;
                int taken = unchecked(gotLock - startedTakingLock), oldMax;
                int aggregate = taken < 0 ? 0 : taken >= BUCKET_COUNT ? (BUCKET_COUNT - 1) : taken;
                Interlocked.Increment(ref _buckets[aggregate]);
                do
                {
                    oldMax = Volatile.Read(ref _maxGetLock);
                } while (taken > oldMax && Interlocked.CompareExchange(ref _maxGetLock, taken, oldMax) != oldMax);

                if (token.Success) Interlocked.Increment(ref _successCount);
                else
                {
                    var nowAttempt = Volatile.Read(ref _attempts);
                    _log.WriteLine($"failure: {token}, available: {_timeoutMux.IsAvailable}; attempts before: {attempt}, now: {nowAttempt}");
                    Interlocked.Increment(ref _failCount);
                    return; // give up promptly if we start failing
                }
                GC.KeepAlive(null);
            }
        }
    }
}
