using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private sealed class AsyncDirectPendingLockSlab : IAsyncPendingLockToken, IValueTaskSource<LockToken>
        {
            private static readonly object s_Completed = new object(), s_NoState = new object();
            private readonly MutexSlim _mutex;

            private readonly int[] _tokens;
            private readonly object[] _continuationsAndState;

            public const int SlabSize = 128;
            public AsyncDirectPendingLockSlab(MutexSlim mutex)
            {
                _mutex = mutex;
                _tokens = new int[SlabSize];
                _continuationsAndState = new object[SlabSize * 2];
            }

            short _currentIndex;
            public short TryGetKey() => (short)(_currentIndex == SlabSize ? -1 : _currentIndex++);

            void IPendingLockToken.Reset(short key)
            {
                Volatile.Write(ref _tokens[key], LockState.Pending);
                _continuationsAndState[key] = null; // continuation
                _continuationsAndState[key + SlabSize] = s_NoState;
            }

            ValueTaskSourceStatus IValueTaskSource<LockToken>.GetStatus(short key)
            {
                switch (LockState.GetState(Volatile.Read(ref _tokens[key])))
                {
                    case LockState.Canceled:
                        return ValueTaskSourceStatus.Canceled;
                    case LockState.Pending:
                        return ValueTaskSourceStatus.Pending;
                    default: // LockState.Success, LockState.Timeout (we only have 4 bits for status)
                        return ValueTaskSourceStatus.Succeeded;
                }
            }

            void IValueTaskSource<LockToken>.OnCompleted(Action<object> continuation, object state, short key, ValueTaskSourceOnCompletedFlags flags)
            {
                if (continuation == null) return;

                // set the state first, as we'll always *read* the continuation first, so we can't get confused
                var oldState = Interlocked.CompareExchange(ref _continuationsAndState[SlabSize + key], state, s_NoState);
                if (oldState != s_NoState) ThrowMultipleContinuations();

                var oldContinuation = Interlocked.CompareExchange(ref _continuationsAndState[key], continuation, null);
                if (oldContinuation == s_Completed)
                {
                    // we'd already finished; invoke it inline
                    continuation.Invoke(state);
                }
                else if (oldContinuation != null) ThrowMultipleContinuations();
            }


            private static void ThrowMultipleContinuations() => throw new InvalidOperationException($"Only one pending continuation is permitted");

            LockToken IValueTaskSource<LockToken>.GetResult(short key) => new LockToken(_mutex, LockState.GetResult(ref _tokens[key]));

            ValueTask<LockToken> IAsyncPendingLockToken.GetTask(short key) => new ValueTask<LockToken>(this, key);

            bool IAsyncPendingLockToken.IsCanceled(short key) => LockState.IsCanceled(Volatile.Read(ref _tokens[key]));

            bool IPendingLockToken.TrySetResult(short key, int token)
            {
                bool success = LockState.TrySetResult(ref _tokens[key], token);
                if (success) OnAssigned(key);
                return success;
            }

            bool IPendingLockToken.TryCancel(short key)
            {
                bool success = LockState.TryCancel(ref _tokens[key]);
                if (success) OnAssigned(key);
                return success;
            }
            private void OnAssigned(short key)
            {
                var continuation = Interlocked.Exchange(ref _continuationsAndState[key], s_Completed);
                if (continuation != null && continuation != s_Completed)
                {
                    var state = Volatile.Read(ref _continuationsAndState[SlabSize + key]);
                    _mutex._scheduler.Schedule((Action<object>)continuation, state);
                }
            }
        }
    }
}
