using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

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
         * - fairness would be nice, but is not a hard demand
         * - timeout support is required
         *   value can be per mutex - doesn't need to be per-Wait[Async]
         * - a "using"-style API is a nice-to-have, to avoid try/finally
         * - we won't even *attempt* to detect re-entrancy
         *   (if you try and take a lock that you have, that's your fault)
         *
         * - sync path uses per-thread ([ThreadStatic])/Monitor pulse for comms
         * - async path uses custom awaitable with zero-alloc on immediate win
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
        private void Log(string message)
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
        private volatile bool _mayHavePendingItems; // note: can false-positive; shouldn't false-negative
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
            void ThrowInvalidTimeout() => Throw.ArgumentOutOfRange(nameof(timeoutMilliseconds));
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
            // release the token (we can check for wrongness without needing the lock, note)
            Log($"attempting to release token {LockState.ToString(token)}");
            if (Interlocked.CompareExchange(ref _token, LockState.ChangeState(token, LockState.Pending), token) != token)
            {
                Log($"token {LockState.ToString(token)} is invalid");
                if (demandMatch) Throw.InvalidLockHolder();
                return;
            }


            if (_mayHavePendingItems) ActivateNextQueueItem();
            else Log($"no pending items to activate");
        }

        private void ActivateNextQueueItem()
        {
            // see if we can nudge the next waiter
            lock (_queue)
            {
                try
                {
                    int token; // if work to do, try and get a new token
                    Log($"pending items: {_queue.Count}");
                    if (_queue.Count == 0 || (token = TryTakeLoopIfChanges()) == 0) return;

                    while (_queue.Count != 0)
                    {
                        var next = DequeueInsideLock();
                        if (next.TrySetResult(token))
                        {
                            Log($"handed lock to {next}");
                            return; // so we don't release the token
                        }
                        else
                        {
                            Log($"lock rejected by {next}");
                        }
                    }

                    // nobody actually wanted it; return it
                    Log("returning unwanted lock");
                    Volatile.Write(ref _token, LockState.ChangeState(token, LockState.Pending));
                }
                finally
                {
                    FixMayHavePendingItemsInsideLock();
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
                    FixMayHavePendingItemsInsideLock();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TryTakeLoopIfChanges()
        {
            int current, next;
            do // try and take, interlocked; if the value changes, we need to redo in case
            { // it turns out to be different but still available
                next = 0; // this is used if it turns out that the current state isn't pending
                current = Volatile.Read(ref _token);
            } while (LockState.GetState(current) == LockState.Pending // needs to look available
                && Interlocked.CompareExchange(ref _token, next = LockState.GetNextToken(current), current) != current); // loop if changed
            return next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TryTakeOnceOnly()
        {
            int current, next;
            return LockState.GetState(current = Volatile.Read(ref _token)) == LockState.Pending // needs to look available
                && Interlocked.CompareExchange(ref _token, next = LockState.GetNextToken(current), current) == current
                ? next : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TryTakeBySpinning()
        {
            // try a SpinWait to see if we can avoid the expensive bits
            SpinWait spin = new SpinWait();
            do
            {
                spin.SpinOnce();
                var token = TryTakeOnceOnly();
                if (token != 0) return token;
            } while (!spin.NextSpinWillYield);
            return default;
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

        private void FixMayHavePendingItemsInsideLock() => _mayHavePendingItems = _queue.Count != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool HasFlag(WaitOptions options, WaitOptions flag) => (options & flag) != 0;

        private int TakeWithTimeout(WaitOptions options)
        {
            // try and spin
            int token;
#if DEBUG
            token = HasFlag(options, DisableFastPath) ? 0 : TryTakeBySpinning();
#else
            token = TryTakeBySpinning();
#endif
            if (token != 0) return token;

            // if "now or never", bail
            if (TimeoutMilliseconds == 0) return default;

            bool itemLockTaken = false, queueLockTaken = false;
            var start = GetTime();

            var item = SyncPendingLockToken.GetPerThreadLockObject();
            const short KEY = 0;
            var queueItem = new PendingLockItem(start, KEY, item);
            try
            {
                // we want to have the item-lock *before* we put anything in the queue
                // (and we only want to do that once we've checked we can reset it)
                Monitor.TryEnter(item, 0, ref itemLockTaken);
                if (!itemLockTaken)
                {
                    // this should have been available immediately; if it isn't, something
                    // is very wrong; we can try again, though
                    item = SyncPendingLockToken.GetNewPerThreadLockObject();
                    Monitor.TryEnter(item, 0, ref itemLockTaken);
                    Debug.Assert(itemLockTaken);
                    if (!itemLockTaken) return default; // just give up!
                }

                // now lock the global queue, and then have a final stab at getting it cheaply
                _mayHavePendingItems = true; // set this *before* getting the lock
                Monitor.TryEnter(_queue, TimeoutMilliseconds, ref queueLockTaken);
                if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex

#if DEBUG
                token = HasFlag(options, DisableFastPath) ? 0 : TryTakeOnceOnly();
#else
                token = TryTakeOnceOnly();
#endif
                if (token != 0)
                {
                    FixMayHavePendingItemsInsideLock();
                    return token;
                }

                // otherwise enqueue the pending item, and release
                // the global queue *before* we wait
                _queue.Enqueue(queueItem);
                Monitor.Exit(_queue);
                queueLockTaken = false;

                // k, the item is now in the queue; we're going to
                // wait to see if it gets pulsed; note: we're not
                // going to depend on the result here - the value
                // inside the object is the single source of truth here
                // because otherwise we could get a race condition where it
                // gets a token *just after* the Wait times out, which
                // could lead to a dropped token, and a blocked mux
                Monitor.Wait(item, UpdateTimeOut(start, TimeoutMilliseconds));
                Monitor.Exit(item);
                itemLockTaken = false;

                var result = item.GetResult();
                if (LockState.GetState(result) != LockState.Success)
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
                        FixMayHavePendingItemsInsideLock();
                    }
                    else // we didn't get the queue lock; no idea whether we're still in the queue
                    {
                        SyncPendingLockToken.ResetPerThreadLockObject();
                    }
                }
                return result;
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
                if (itemLockTaken) Monitor.Exit(item);
            }
        }

#pragma warning disable RCS1231 // Make parameter ref read-only.
        private ValueTask<LockToken> TakeWithTimeoutAsync(CancellationToken cancellationToken, WaitOptions options)
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            int token;
            // try and spin
#if DEBUG
            token = HasFlag(options, DisableFastPath) ? 0 : TryTakeBySpinning();
#else
            token = TryTakeBySpinning();
#endif

            if (token != 0) return new ValueTask<LockToken>(new LockToken(this, token));

            // if "now or never", bail
            if (TimeoutMilliseconds == 0) return default;

            var start = GetTime();

            // lock the global queue; then have a final stab at getting it cheaply
            bool queueLockTaken = false;
            try
            {
                _mayHavePendingItems = true; // set this *before* getting the lock
                Monitor.TryEnter(_queue, TimeoutMilliseconds, ref queueLockTaken);
                if (!queueLockTaken) return default; // couldn't even get the lock, let alone the mutex
                if (cancellationToken.IsCancellationRequested) return GetCanceled();

#if DEBUG
                token = HasFlag(options, DisableFastPath) ? 0 : TryTakeOnceOnly();
#else
                token = TryTakeOnceOnly();
#endif
                if (token != 0)
                {
                    FixMayHavePendingItemsInsideLock();
                    return new ValueTask<LockToken>(new LockToken(this, token));
                }

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

                // enqueue the pending item, and release the global queue
                var queueItem = new PendingLockItem(start, key, asyncItem);
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(GetCancelationCallback(key), asyncItem);
                }
                if (!asyncItem.IsCanceled(key)) // Register can invoke directly if it became canceled already
                {
                    _queue.Enqueue(queueItem);
                    if (_pendingAsyncOperations++ == 0) SetNextAsyncTimeoutInsideLock(); // first async op
                }
                return asyncItem.GetTask(key);
            }
            finally
            {
                if (queueLockTaken) Monitor.Exit(_queue);
            }
        }

        private AsyncDirectPendingLockSlab _directSlab;

        static readonly Action<object>[] _slabCallbacks = new Action<object>[AsyncDirectPendingLockSlab.SlabSize - 1];
        static Action<object> GetCancelationCallback(short key)
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
        public ValueTask<LockToken> TryWaitAsync(CancellationToken cancellationToken = default, WaitOptions options = WaitOptions.None)
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            if (cancellationToken.IsCancellationRequested) return GetCanceled();
#if DEBUG
            var token = HasFlag(options, DisableFastPath) ? 0 : TryTakeOnceOnly();
#else
            var token = TryTakeOnceOnly();
#endif
            if (token != 0 | HasFlag(options, WaitOptions.NoDelay))
                return new ValueTask<LockToken>(new LockToken(this, token));

            // otherwise, do things the hard way
            return TakeWithTimeoutAsync(cancellationToken, options);
        }

        static Task<LockToken> s_canceledTask;
        internal static ValueTask<LockToken> GetCanceled() => new ValueTask<LockToken>(s_canceledTask ?? (s_canceledTask = CreateCanceledLockTokenTask()));
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
        {
#if DEBUG
            var token = HasFlag(options, DisableFastPath) ? 0 : TryTakeOnceOnly();
#else
            var token = TryTakeOnceOnly();
#endif
            return new LockToken(this, (token != 0 | HasFlag(options, WaitOptions.NoDelay)) ? token : TakeWithTimeout(options));
        }

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

            // note: MSB is reserved for debugging
        }

        internal const WaitOptions DisableFastPath = unchecked((WaitOptions)0x80000000); // MSB
    }
}
