using System;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private sealed class AsyncTaskPendingLockToken : AsyncPendingLockToken
        {
            private readonly TaskCompletionSource<LockToken> _source;
            public AsyncTaskPendingLockToken(MutexSlim mutex, uint start) : base(mutex, start)
            {
                _source = new TaskCompletionSource<LockToken>(
                    mutex.IsThreadPool ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);
            }

            public override void OnCompleted(Action continuation)
                => _source.Task.GetAwaiter().OnCompleted(continuation);

            protected override void OnAssigned()
            {
                if (Mutex.IsThreadPool)
                {
                    // in this case, we already specified RunContinuationsAsynchronously, so we aren't
                    // thread-stealing; by setting directly, we avoid an extra QUWI in both the
                    // "no awaiters" and "awaiter is a .Wait()" cases, as the TPL detects this
                    // scenario directly and avoids it; worst case (something more complex), we just
                    // get the exact same QUWI behaviour that we would have had *anyway*.
                    OnAssignedImpl();
                }
                else
                {
                    Schedule(s => ((AsyncTaskPendingLockToken)s).OnAssignedImpl(), this);
                }
            }

            private void OnAssignedImpl() // make sure this happens on the
            { // scheduler's thread to avoid the release thread being stolen
                if (IsCanceled) _source.TrySetCanceled();
                else _source.TrySetResult(new LockToken(Mutex, GetResult()));
            }

            public override ValueTask<LockToken> AsTask() => new ValueTask<LockToken>(_source.Task);
        }
    }
}
