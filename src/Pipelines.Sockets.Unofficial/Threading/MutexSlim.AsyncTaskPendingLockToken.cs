using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private sealed class AsyncTaskPendingLockToken : TaskCompletionSource<LockToken>, IAsyncPendingLockToken
        {
            private readonly MutexSlim _mutex;
            private int _token;
            public AsyncTaskPendingLockToken(MutexSlim mutex) : base(
                mutex.IsThreadPool ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None)
               => _mutex = mutex;

            void IPendingLockToken.Reset(short key) => _token = LockState.Pending;

            ValueTask<LockToken> IAsyncPendingLockToken.GetTask(short key) => new ValueTask<LockToken>(Task);

            bool IPendingLockToken.TryCancel(short key)
            {
                bool success = LockState.TryCancel(ref _token);
                if (success) OnAssigned();
                return success;
            }

            bool IPendingLockToken.TrySetResult(short key, int token)
            {
                bool success = LockState.TrySetResult(ref _token, token);
                if (success) OnAssigned();
                return success;
            }

            bool IPendingLockToken.HasResult(short key) => LockState.GetState(Volatile.Read(ref _token)) != LockState.Pending;

            int IPendingLockToken.GetResult(short key) => LockState.GetResult(ref _token);

            private void OnAssigned()
            {
                if (_mutex.IsThreadPool)
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
                    _mutex._scheduler.Schedule(s => ((AsyncTaskPendingLockToken)s).OnAssignedImpl(), this);
                }
            }

            private void OnAssignedImpl() // make sure this happens on the
            { // scheduler's thread to avoid the release thread being stolen
                var token = LockState.GetResult(ref _token);
                if (LockState.GetState(token) == LockState.Canceled) TrySetCanceled();
                else TrySetResult(new LockToken(_mutex, token));
            }
        }
    }
}
