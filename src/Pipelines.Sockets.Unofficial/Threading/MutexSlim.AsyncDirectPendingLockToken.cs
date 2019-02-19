using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private sealed class AsyncDirectPendingLockToken : AsyncPendingLockToken, IValueTaskSource<LockToken>
        {
            private static readonly object s_Completed = new object();
            private object _continuation, _continuationState;

            public AsyncDirectPendingLockToken(MutexSlim mutex, uint start) : base(mutex, start) { }

            protected override void OnAssigned()
            {
                var callback = Interlocked.Exchange(ref _continuation, s_Completed);
                if (callback != null && callback != s_Completed)
                {
                    if (callback is Action action)
                    {
                        Schedule(s => ((Action)s).Invoke(), callback);
                    }
                    else
                    {
                        Schedule((Action<object>)callback, Volatile.Read(ref _continuationState));
                    }
                }
            }

            public override ValueTask<LockToken> AsTask() => new ValueTask<LockToken>(this, default);

            public override void OnCompleted(Action continuation)
            {
                if (continuation != null)
                {
                    var oldValue = Interlocked.CompareExchange(ref _continuation, continuation, null);
                    if (oldValue == s_Completed)
                    {
                        // already complete; invoke instead
                        continuation.Invoke();
                    }
                    else if (oldValue != null) ThrowMultipleContinuations();
                }
            }
            private static void ThrowMultipleContinuations() => throw new NotSupportedException($"Only one pending continuation is permitted");

            public void OnCompleted(Action<object> continuation, object state)
            {
                if (continuation != null)
                {
                    // since we can't guarantee an atomic swap (see below), let's at least *try* and prevent
                    // problems by detecting obvious errors
                    var oldValue = Volatile.Read(ref _continuation);
                    if (oldValue == null)
                    {
                        // this is the danger case: adding a new continuation while incomplete;
                        // it *could* complete while we're doing this, which makes it fun... or another
                        // thread could be calling OnCompleted at the same time (both getting past this check)

                        // we're going to try and set the state *before* changing the field; now, this doesn't
                        // technically guarantee an atomic swap of both continuation and state, but if the caller is calling
                        // OnCompleted twice, they're already in unsupported land
                        Volatile.Write(ref _continuationState, state);

                        // re-fetch and check
                        oldValue = Interlocked.CompareExchange(ref _continuation, continuation, null);
                        if (oldValue == s_Completed)
                        {
                            // already complete; invoke instead
                            continuation.Invoke(state);
                        }
                        else if (oldValue != null) ThrowMultipleContinuations();
                    }
                    else if (oldValue == s_Completed)
                    {
                        // already complete; invoke instead
                        continuation.Invoke(state);
                    }
                    else ThrowMultipleContinuations();
                }
            }

            public ValueTaskSourceStatus GetStatus(short token)
            {
                switch(VolatileStatus())
                {
                    case LockState.Canceled:
                        return ValueTaskSourceStatus.Canceled;
                    case LockState.Pending:
                        return ValueTaskSourceStatus.Pending;
                    default: // LockState.Success, LockState.Timeout (we only have 4 bits for status)
                        return ValueTaskSourceStatus.Succeeded;
                }
            }

            public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
                => OnCompleted(continuation, state);

            public LockToken GetResult(short token) => GetResultAsToken();
        }
    }
}
