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
                _source = new TaskCompletionSource<LockToken>();
            }

            public override void OnCompleted(Action continuation)
                => _source.Task.GetAwaiter().OnCompleted(continuation);

            protected override void OnAssigned()
                => Schedule(s => ((AsyncTaskPendingLockToken)s).OnAssignedImpl(), this);

            private void OnAssignedImpl() // make sure this happens on the
            { // scheduler's thread to avoid the release thread being stolen
                if (IsCanceled) _source.TrySetCanceled();
                else _source.TrySetResult(new LockToken(Mutex, GetResult()));
            }

            public override ValueTask<LockToken> AsTask() => new ValueTask<LockToken>(_source.Task);
        }
    }
}
