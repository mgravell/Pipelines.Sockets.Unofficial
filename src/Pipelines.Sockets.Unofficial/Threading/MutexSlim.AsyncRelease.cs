using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Pipelines.Sockets.Unofficial.Threading
{
    partial class MutexSlim
    {
        public partial struct LockToken // : IAsyncDisposable
        {
            /// <summary>
            /// Release the mutex, if obtained
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ValueTask DisposeAsync()
            {
                if (LockState.GetState(_token) == LockState.Success)
                    return _parent.ReleaseAsync(_token);
                return default;
            }
        }

        private sealed class AsyncRelease : IValueTaskSource
        {
            private readonly MutexSlim _parent;

            public AsyncRelease(MutexSlim parent)
                => _parent = parent;

            public ValueTask Create(int token)
                => new ValueTask(this, unchecked((short)(token >> 16)));

            public void GetResult(short token)
                => _parent.Release(AsSuccess(token), null, null);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int AsSuccess(short token)
                => (((int)token) << 16) | LockState.Success;

            public ValueTaskSourceStatus GetStatus(short token)
                => _parent.IsValid(AsSuccess(token))
                ? ValueTaskSourceStatus.Pending : ValueTaskSourceStatus.Succeeded;

            public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
                => _parent.Release(AsSuccess(token), continuation, state);
        }

        private AsyncRelease _asyncRelease;

        partial void InitAsyncRelease() => _asyncRelease = new AsyncRelease(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueTask ReleaseAsync(int token)
        {
            if (IsValid(token))
            {
                // only do anything different if we have something useful to do
                if ((_mayHavePendingItems & s_InlineDepth < MaxInlineDepth) // need to prevent unbounded stack dive
                    && IsNextItemInlineable()) return _asyncRelease.Create(token);
                Release(token, null, null);
            }
            return default;
        }

        private bool IsNextItemInlineable()
        {
            lock (_queue)
            {
                return _queue.Count != 0 && _queue.Peek().IsInlineable;
            }
        }

        [ThreadStatic]
        private static int s_InlineDepth;
        private const int MaxInlineDepth = 20;
    }
}
