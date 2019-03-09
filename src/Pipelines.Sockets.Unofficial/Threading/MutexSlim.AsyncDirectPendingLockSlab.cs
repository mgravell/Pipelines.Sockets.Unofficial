using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private sealed class AsyncDirectPendingLockSlab : IAsyncPendingLockToken, IValueTaskSource<LockToken>,
            IInlineableAsyncPendingLockToken
        {
            private struct State
            {
                // public fields - used with interlocked
                public Action<object> Continuation;
                public object ContinuationState;
                public int Token;
            }

            private static readonly Action<object> s_Completed = _ => { };
            private static readonly object s_NoState = new object();
            private readonly MutexSlim _mutex;

            private readonly State[] _items;

            public const int SlabSize = 128;
            public AsyncDirectPendingLockSlab(MutexSlim mutex)
            {
                _mutex = mutex;
                _items = new State[SlabSize];
            }

            short _currentIndex;
            public short TryGetKey() => (short)(_currentIndex == SlabSize ? -1 : _currentIndex++);

            void IPendingLockToken.Reset(short key)
            {
                ref State item = ref _items[key];
                Volatile.Write(ref item.Token, LockState.Pending);
                item.Continuation = null; // continuation
                item.ContinuationState = s_NoState;
            }

            ValueTaskSourceStatus IValueTaskSource<LockToken>.GetStatus(short token)
                => LockState.GetStatus(ref _items[token].Token);

            void IValueTaskSource<LockToken>.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
            {
                if (continuation == null) return;

                // set the state first, as we'll always *read* the continuation first, so we can't get confused
                ref State item = ref _items[token];
                var oldState = Interlocked.CompareExchange(ref item.ContinuationState, state, s_NoState);
                if (oldState != s_NoState) Throw.MultipleContinuations();

                var oldContinuation = Interlocked.CompareExchange(ref item.Continuation, continuation, null);
                if (oldContinuation == s_Completed)
                {
                    // we'd already finished; invoke it inline
                    continuation.Invoke(state);
                }
                else if (oldContinuation != null) Throw.MultipleContinuations();
            }

            LockToken IValueTaskSource<LockToken>.GetResult(short token) => new LockToken(_mutex, LockState.GetResult(ref _items[token].Token));

            ValueTask<LockToken> IAsyncPendingLockToken.GetTask(short key) => new ValueTask<LockToken>(this, key);

            bool IAsyncPendingLockToken.IsCanceled(short key) => LockState.IsCanceled(Volatile.Read(ref _items[key].Token));

            bool IPendingLockToken.TrySetResult(short key, int token)
            {
                bool success = LockState.TrySetResult(ref _items[key].Token, token);
                if (success) OnAssigned(key);
                return success;
            }

            bool IInlineableAsyncPendingLockToken.TrySetResult(short key, int token, Action<object> continuation, object state)
            {
                bool success = LockState.TrySetResult(ref _items[key].Token, token);
                if (success) OnAssigned(key, continuation, state);
                return success;
            }

            bool IPendingLockToken.TryCancel(short key)
            {
                bool success = LockState.TryCancel(ref _items[key].Token);
                if (success) OnAssigned(key);
                return success;
            }
            private void OnAssigned(short key)
            {
                ref State item = ref _items[key];
                var continuation = Interlocked.Exchange(ref item.Continuation, s_Completed);
                if (continuation != null && continuation != s_Completed)
                {
                    var state = Volatile.Read(ref item.ContinuationState);
                    _mutex._scheduler.Schedule(continuation, state);
                }
            }

            private void OnAssigned(short key, Action<object> disposerContinuation, object disposerState)
            {
                ref State item = ref _items[key];
                var nextContinuation = Interlocked.Exchange(ref item.Continuation, s_Completed);

                if (disposerContinuation != null)
                {
                    // schedule the *disposer's* callback
                    _mutex._scheduler.Schedule(disposerContinuation, disposerState);
                }
                if (nextContinuation != null && nextContinuation != s_Completed)
                {
                    // directly inline the registered state - i.e. we become the next winner
                    var nextState = Volatile.Read(ref item.ContinuationState);
                    s_InlineDepth++;
                    nextContinuation.Invoke(nextState);
                    s_InlineDepth--;
                }
            }
        }
    }
}
