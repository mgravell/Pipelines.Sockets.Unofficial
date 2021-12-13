using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static Pipelines.Sockets.Unofficial.Threading.MutexSlim.LockToken;

namespace Pipelines.Sockets.Unofficial.Threading
{
    /// <summary>
    /// A mutex primitive that can be waited or awaited, with support for schedulers
    /// </summary>
    public sealed partial class MutexSlim
    {
        /*
         * - must have single lock-token-holder (mutex)
         * - must be waitable (sync)
         * - must be awaitable (async)
         * - async context does not flow by default
         *   (can use WaitOptions.CaptureContext to get full Task handling)
         * - must allow fully async consumer
         *   ("wait" and "release" can be from different threads)
         * - must not suck when using both sync+async callers
         *   (I'm looking at you, SemaphoreSlim... you know what you did)
         * - must be low allocation
         * - should allow control of the threading model for async callback
         * - fairness is desirable; see note "FAIRNESS" below
         * - timeout support is required
         *   value can be per mutex - doesn't need to be per-Wait[Async]
         * - a "using"-style API is a nice-to-have, to avoid try/finally
         * - we won't even *attempt* to detect re-entrancy
         *   (if you try and take a lock that you have, that's your fault)
         *
         * - sync path uses per-thread ([ThreadStatic])/Monitor pulse for comms
         * - async path uses custom awaitable with zero-alloc on immediate win
         */

        /* Note on FAIRNESS

        in particular, consider the following scenario with two
        *incomplete* async calls from the same execution path:

        // ** note not awaited yet
        var a = obj.DoSomethingAsync("a"); // that uses MutexSlim
        var b = obj.DoSomethingAsync("b"); // that uses MutexSlim

        await a;
        await b;

        Now: which runs first? "a"? or "b"? Without fairness, it can
        be either; consider that the lock is owned when "a" starts
        so a continuation is queued and the execution flow of
        DoSomethingAsync is suspended at the TryWaitAsync; now
        imagine a rare race when the thing releasing the lock
        happens at *exactly* the same time as our call into "b";
        MutexSlim allows callers to interlocked-take the token,
        so "b" can sneak in and win; so "b" runs, and activates
        "a" *on the way out*. The fairness of MutexSlim has
        been modified to prevent this problem.

        */

        /* usage:

                using (var token = mutex.TryWait())
                {
                    if (token) {...}
                }

        or

                using (var token = await mutex.TryWaitAsync())
                {
                    if (token) {...}
                }
        */

        [Conditional("DEBUG")]
#pragma warning disable CA1822 // Mark members as static
        private void Log(string message)
#pragma warning restore CA1822 // Mark members as static
        {
#if DEBUG
            Logged?.Invoke(message);
#endif
        }
#if DEBUG
        public event Action<string> Logged;
#endif

        private readonly PipeScheduler _scheduler;
        private readonly Queue<PendingLockItem> _queue = new Queue<PendingLockItem>();
        private int _token; // the current status of the mutex - first 2 bits indicate if currently owned; rest is counter for conflict detection
        private int _pendingAsyncOperations; // the number of outstanding async ops; used to know whether we need to have an async timeout

        // for async timeout tracking (a single timeout is maintained for the head of the queue)
        private uint _timeoutStart;
        private CancellationTokenSource _timeoutCancel;

        /// <summary>
        /// Time to wait, in milliseconds - or zero for immediate-only
        /// </summary>
        public int TimeoutMilliseconds { get; }

        internal bool IsThreadPool { get; } // are we confident that the chosen scheduler is the thread-pool?

        private int BacklogCount()
        {
            lock (_queue) return _queue.Count;
        }

        /// <summary>
        /// See Object.ToString
        /// </summary>
        public override string ToString() => $"{GetType().Name}, {LockState.GetStatus(ref _token)}, {BacklogCount()} pending";

        /// <summary>
        /// Create a new MutexSlim instance
        /// </summary>
        /// <param name = "timeoutMilliseconds">Time to wait, in milliseconds - or zero for immediate-only</param>
        /// <param name="scheduler">The scheduler to use for async continuations, or the thread-pool if omitted</param>
        public MutexSlim(int timeoutMilliseconds, PipeScheduler scheduler = null)
        {
            if (timeoutMilliseconds < 0) ThrowInvalidTimeout();
            TimeoutMilliseconds = timeoutMilliseconds;
            _scheduler = scheduler ?? PipeScheduler.ThreadPool;
            _token = LockState.ChangeState(0, LockState.Pending); // initialize as unowned
            static void ThrowInvalidTimeout() => Throw.ArgumentOutOfRange(nameof(timeoutMilliseconds));
            IsThreadPool = (object)_scheduler == (object)PipeScheduler.ThreadPool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PendingLockItem DequeueInsideLock()
        {
            var item = _queue.Dequeue();
            if (item.IsAsync) _pendingAsyncOperations--;
            return item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Release(int token, bool demandMatch = true)
        {
            // validate the token
            if (Volatile.Read(ref _token) != token)
            {
                Log($"token {LockState.ToString(token)} is invalid");
                if (demandMatch) Throw.InvalidLockHolder();
                return;
            }

            ActivateNextQueueItemWithValidatedToken(token);
        }

        private void ActivateNextQueueItemWithValidatedToken(int token)
        {
            // see if we can nudge the next waiter
            lock (_queue)
            {
                if (Volatile.Read(ref _token) != token)
                {
                    Throw.InvalidLockHolder();
                }

                try
                {
                    if (_queue.Count == 0)
                    {
                        Log($"no pending items to activate");
                        return;
                    }

                    // there's work to do; get a new token
                    Log($"pending items: {_queue.Count}");

                    // we're expecting to activate; get a new token speculatively
                    // (needs to be new so the old caller can't double-dispose and
                    // release someone else's lock)
                    Volatile.Write(ref _token, token = LockState.GetNextToken(token));

                    // try to give the new token to someone
                    while (_queue.Count != 0)
                    {
                        var next = DequeueInsideLock();
                        if (next.TrySetResult(token))
                        {
                            Log($"handed lock to {next}");
                            token = 0; // so we don't release the token
                            return;
                        }
                        else
                        {
                            Log($"lock rejected by {next}");
                        }
                    }
                }
                finally
                {
                    if (token != 0) // nobody actually wanted it; return it
                    { // (this could be the original token, or a new speculative token)
                        Log("returning unwanted lock");
                        Volatile.Write(ref _token, LockState.ChangeState(token, LockState.Pending));
                    }
                    SetNextAsyncTimeoutInsideLock();
                }
            }
        }

        private void DequeueExpired()
        {
            lock (_queue)
            {
                try
                {
                    while (_queue.Count != 0)
                    {
                        var next = _queue.Peek();
                        var remaining = UpdateTimeOut(next.Start, TimeoutMilliseconds);
                        if (remaining == 0)
                        {
                            // tell them that they failed
                            next = DequeueInsideLock();
                            Log($"timing out: {next}");
                            next.TrySetResult(default);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    SetNextAsyncTimeoutInsideLock();
                }
            }
        }

        private void SetNextAsyncTimeoutInsideLock()
        {
            void CancelExistingTimeout()
            {
                if (_timeoutCancel != null)
                {
                    try { _timeoutCancel.Cancel(); } catch { }
                    try { _timeoutCancel.Dispose(); } catch { }
                    _timeoutCancel = null;
                }
            }
            uint nextItemStart;
            if (_pendingAsyncOperations == 0)
            {
                CancelExistingTimeout();
                return;
            }
            if ((nextItemStart = _queue.Peek().Start) == _timeoutStart && _timeoutCancel != null)
            {   // timeout hasn't changed (so: don't change anything)
                return;
            }

            // something has changed
            CancelExistingTimeout();

            _timeoutStart = nextItemStart;
            var localTimeout = UpdateTimeOut(nextItemStart, TimeoutMilliseconds);
            if (localTimeout == 0)
            {   // take a peek back right away (just... not on this thread)
                _scheduler.Schedule(s => ((MutexSlim)s).DequeueExpired(), this);
            }
            else
            {
                // take a peek back in a little while, kthx
                var cts = new CancellationTokenSource();
                var timeout = Task.Delay(localTimeout, cts.Token);
                timeout.ContinueWith((_, state) =>
                {
                    try { ((MutexSlim)state).DequeueExpired(); } catch { }
                }, this);
                _timeoutCancel = cts;
            }
        }

        /// <summary>
        /// Indicates whether the lock is currently available
        /// </summary>
        public bool IsAvailable
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => LockState.GetState(Volatile.Read(ref _token)) == LockState.Pending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TryTakeOnceOnly()
        {
            int current, next;
            return LockState.GetState(current = Volatile.Read(ref _token)) == LockState.Pending // needs to look available
                && Interlocked.CompareExchange(ref _token, next = LockState.GetNextToken(current), current) == current
                ? next : 0;
        }

        private static uint GetTime() => (uint)Environment.TickCount;
        // borrowed from SpinWait
        private static int UpdateTimeOut(uint startTime, int originalWaitMillisecondsTimeout)
        {
            uint elapsedMilliseconds = (GetTime() - startTime);

            // Check the elapsed milliseconds is greater than max int because this property is uint
            if (elapsedMilliseconds > int.MaxValue)
            {
                return 0;
            }

            // Subtract the elapsed time from the current wait time
            int currentWaitTimeout = originalWaitMillisecondsTimeout - (int)elapsedMilliseconds;
            if (currentWaitTimeout <= 0)
            {
                return 0;
            }

            return currentWaitTimeout;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasFlag(WaitOptions options, WaitOptions flag) => (options & flag) != 0;

        private LockToken TakeWithTimeout(WaitOptions options)
        {
            bool itemLockTaken = false;
            bool queueLockTaken = false;
            SyncPendingLockToken item = null;
            try
            {
                var start = GetTime(); // read this promptly to include any time spent getting locks as part of our timeout interval
                if (HasFlag(options, WaitOptions.NoDelay))
                {
                    Monitor.TryEnter(_queue, 0, ref queueLockTaken);
                    if (!queueLockTaken) return LockToken.Fail(TimeoutReason.NoDelayImmediateTimeout);
                }
                else
                {
                    Monitor.TryEnter(_queue, UpdateTimeOut(start, TimeoutMilliseconds), ref queueLockTaken);
                    if (!queueLockTaken) return LockToken.Fail(TimeoutReason.UnableToGetQueueLock);
                }

                int token;
                if (_queue.Count == 0) // we now have the queue lock; are we fighting anyone? if not, we don't need to enqueue etc
                {
                    token = TryTakeOnceOnly();
                    if (token != 0) return new LockToken(this, token);
                }
                if (HasFlag(options, WaitOptions.NoDelay)) return LockToken.Fail(TimeoutReason.NoDelayImmediateTimeout);
                if (TimeoutMilliseconds == 0) return LockToken.Fail(TimeoutReason.ZeroTimeout);

                item = SyncPendingLockToken.GetPerThreadLockObject();

                // enqueue the pending item, and release
                // the global queue *before* we wait
                const short KEY = 0;
                var queueItem = new PendingLockItem(start, KEY, item); // note this resets the item
                _queue.Enqueue(queueItem);
                bool useSpinWait = _queue.Count == 1; // only spin if we're the next contestant
                Monitor.Exit(_queue);
                queueLockTaken = false;

                // k, the item is now in the queue; we can use a spin-lock to see if it
                // gets assigned a value *without* needing to take a lock (because we
                // don't want to tie the releasing thread into the spin-lock) by checking
                // IsPending
                if (!(useSpinWait && queueItem.TrySpinWait()))
                {
                    // if we didn't get it via spin wait (perhaps because we didn't try) - 
                    // then see if we can wait on a pulse instead
                    int remaining = UpdateTimeOut(start, TimeoutMilliseconds);
                    if (remaining > 0)
                    {
                        Monitor.TryEnter(item, remaining, ref itemLockTaken);
                        if (itemLockTaken)
                        {
                            if (item.IsPending) // double-checked (when assigning, state update comes first)
                            {
                                remaining = UpdateTimeOut(start, TimeoutMilliseconds);
                                if (remaining > 0)
                                {
                                    // note that the *outcome* isn't important - just
                                    // that we waited; it is GetResult() that seals
                                    // the fate here
                                    Monitor.Wait(item, remaining);
                                }
                            }
                            Monitor.Exit(item);

#pragma warning disable IDE0059 // removing this would be *bad*; https://github.com/dotnet/roslyn/issues/39344
                            itemLockTaken = false;
#pragma warning restore IDE0059
                        }
                    }
                }

                var result = item.GetResult();
                if (LockState.GetState(result) == LockState.Success)
                {
                    // to have a successful result, it must have been dequeued
                    return new LockToken(this, result);
                }
                else
                {
                    // if we *didn't* get the lock, we *could* still be in the queue;
                    // since we're in the failure path, let's take a moment to see if we can
                    // remove ourselves from the queue; otherwise we need to consider the
                    // lock object tainted
                    // (note the outer finally will release the queue lock either way)
                    Monitor.TryEnter(_queue, 0, ref queueLockTaken);
                    if (queueLockTaken)
                    {
                        if (_queue.Count == 0) { } // nothing to do; queue is empty
                        else if (_queue.Peek() == queueItem)
                        {
                            _queue.Dequeue(); // we were next and we cleaned up; nice!
                            // (note: don't need to use DequeueInsideLock here; if it is "us", it isn't async)
                        }
                        else if (_queue.Count != 1) // if there's only one item and it isn't us: nothing to do
                        {
                            // we *might* be later in the queue, but we can't check or be sure,
                            // and we don't want the sync object getting treated oddly; nuke it
                            SyncPendingLockToken.ResetPerThreadLockObject();
                        }
                    }
                    else // we didn't get the queue lock; no idea whether we're still in the queue
                    {
                        SyncPendingLockToken.ResetPerThreadLockObject();
                    }
                    return LockToken.Fail(TimeoutReason.NotHandedLockInsideTimeout);
                }
            }
            finally
            {
                if (itemLockTaken) Monitor.Exit(item);
                if (queueLockTaken)
                {
                    Monitor.Exit(_queue);
                }
            }
        }

#pragma warning disable RCS1231 // Make parameter ref read-only.
#pragma warning disable CA1068 // CancellationToken parameters must come last
        private ValueTask<LockToken> TakeWithTimeoutAsync(CancellationToken cancellationToken, WaitOptions options)
#pragma warning restore CA1068 // CancellationToken parameters must come last
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            if (cancellationToken.IsCancellationRequested) return GetCanceled();

            bool queueLockTaken = false;
            try
            {
                var start = GetTime(); // read this promptly to include any time spent getting locks as part of our timeout interval
                if (HasFlag(options, WaitOptions.NoDelay))
                {
                    Monitor.TryEnter(_queue, 0, ref queueLockTaken);
                    if (!queueLockTaken) return LockToken.FailAsync(TimeoutReason.NoDelayImmediateTimeout);
                }
                else
                {
                    Monitor.TryEnter(_queue, TimeoutMilliseconds, ref queueLockTaken);
                    if (!queueLockTaken) return LockToken.FailAsync(TimeoutReason.UnableToGetQueueLock);
                }

                // check if it became cancelled while we were busy
                if (cancellationToken.IsCancellationRequested) return GetCanceled();

                int token;
                if (_queue.Count == 0) // we now have the queue lock; are we fighting anyone? if not, we don't need to enqueue etc
                {
                    token = TryTakeOnceOnly();
                    if (token != 0) return new ValueTask<LockToken>(new LockToken(this, token));
                }
                if (HasFlag(options, WaitOptions.NoDelay)) return LockToken.FailAsync(TimeoutReason.NoDelayImmediateTimeout);
                if (TimeoutMilliseconds == 0) return LockToken.FailAsync(TimeoutReason.ZeroTimeout);

                // otherwise we'll need an async pending lock token
                IAsyncPendingLockToken asyncItem;
                short key;

                if (HasFlag(options, WaitOptions.DisableAsyncContext))
                {
                    asyncItem = GetSlabTokenInsideLock(out key); // bypass TPL - no context flow
                }
                else
                {
                    asyncItem = new AsyncTaskPendingLockToken(this); // let the TPL deal with capturing async context
                    key = 0;
                }

                // enqueue the pending item and release the queue
                var queueItem = new PendingLockItem(start, key, asyncItem);
                _queue.Enqueue(queueItem);
                bool useSpinWait = _queue.Count == 1; // only spin if we're the next contestant
                if (_pendingAsyncOperations++ == 0) SetNextAsyncTimeoutInsideLock(); // first async op
                Monitor.Exit(_queue);
                queueLockTaken = false;

                // if just one contendor, we might be able to wait it out
                if (useSpinWait)
                {
                    if (queueItem.TrySpinWait())
                    {
                        // we win; that's the lot
                        return FromTokenAsync(asyncItem.GetResult(key));
                    }
                    else if (cancellationToken.IsCancellationRequested)
                    {
                        // while we were spinning, cancelation happened
                        return GetCanceled();
                    }
                }

                // if we get here, either we didn't wait (multiple competitors), or waiting didn't help
                if (cancellationToken.CanBeCanceled)
                {
                    // register for cancelation
                    cancellationToken.Register(GetCancelationCallback(key), asyncItem);   
                }

                // technically, Register can cause completion; this is an extreme outlier, but
                // it simplifies the logic if we deal with it now rather than later
                if (asyncItem.HasResult(key))
                {
                    return FromTokenAsync(asyncItem.GetResult(key));
                }

                // give the caller an awaitable item
                return asyncItem.GetTask(key);
            }
            finally
            {
                if (queueLockTaken)
                {
                    Monitor.Exit(_queue);
                }
            }
        }

        private ValueTask<LockToken> FromTokenAsync(int token)
        {
            if (LockState.GetState(token) == LockState.Canceled) return GetCanceled();
            return new ValueTask<LockToken>(new LockToken(this, token));
        }

        private AsyncDirectPendingLockSlab _directSlab;

        private static readonly Action<object>[] _slabCallbacks = new Action<object>[AsyncDirectPendingLockSlab.SlabSize - 1];
        private static Action<object> GetCancelationCallback(short key)
        {
            if (key == 0) return state => ((IPendingLockToken)state).TryCancel(0);
            var index = key - 1; // don't need to worry about 0, so: offset by one
            return _slabCallbacks[index] ?? (_slabCallbacks[index] = CreateCancelationCallback(key));

            Action<object> CreateCancelationCallback(short lkey)
            {   // this will involve a capture per SlabSize element; I'm OK with this!
                // better than unbounded numbers of cancelation callbacks
                return state => ((IPendingLockToken)state).TryCancel(lkey);
            }
        }
        private IAsyncPendingLockToken GetSlabTokenInsideLock(out short key)
        {
            key = _directSlab?.TryGetKey() ?? -1;
            if (key < 0) key = (_directSlab = new AsyncDirectPendingLockSlab(this)).TryGetKey();
            return _directSlab;
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable RCS1231 // Make parameter ref read-only.
#pragma warning disable CA1068 // CancellationToken parameters must come last
        public ValueTask<LockToken> TryWaitAsync(CancellationToken cancellationToken = default, WaitOptions options = WaitOptions.None)
#pragma warning restore CA1068 // CancellationToken parameters must come last
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            return TakeWithTimeoutAsync(cancellationToken, options);
        }

        private static Task<LockToken> s_canceledTask;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ValueTask<LockToken> GetCanceled() => new ValueTask<LockToken>(s_canceledTask ??= CreateCanceledLockTokenTask());
        private static Task<LockToken> CreateCanceledLockTokenTask()
        {
            var tcs = new TaskCompletionSource<LockToken>();
            tcs.SetCanceled();
            return tcs.Task;
        }

        /// <summary>
        /// Attempt to take the lock (Success should be checked by the caller)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LockToken TryWait(WaitOptions options = WaitOptions.None)
            => TakeWithTimeout(options);

        /// <summary>
        /// Additional options that influence how TryWait/TryWaitAsync operate
        /// </summary>
        [Flags]
        public enum WaitOptions
        {
            /// <summary>
            /// Default options
            /// </summary>
            None = 0,
            /// <summary>
            /// If the mutex cannot be acquired immediately, it is failed
            /// </summary>
            NoDelay = 1,
            /// <summary>
            /// Disable full TPL flow; more efficient, but no sync-context or execution-context guarantees
            /// </summary>
            DisableAsyncContext = 2,
        }
    }
}
