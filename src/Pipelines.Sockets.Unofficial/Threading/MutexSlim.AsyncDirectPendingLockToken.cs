using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private sealed class AsyncDirectPendingLockToken : AsyncPendingLockToken
        {
            private Action _continuation;

            public AsyncDirectPendingLockToken(MutexSlim mutex, uint start) : base(mutex, start) { }

            protected override void OnAssigned()
            {
                var callback = Interlocked.Exchange(ref _continuation, null);
                if (callback != null) Schedule(s => ((Action)s).Invoke(), callback);
            }

            public override Task<LockToken> AsTask() => throw new NotSupportedException(nameof(AsTask));

            public override void OnCompleted(Action continuation)
            {
                if (IsCompleted)
                {
                    continuation.Invoke();
                    return;
                }
                if (Interlocked.CompareExchange(ref _continuation, continuation, null) != null)
                {
                    ThrowNotSupported();
                }

                void ThrowNotSupported() => throw new NotSupportedException($"Only one pending continuation is permitted");
            }
        }
    }
}
