using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        readonly struct PendingLockItem
        {
            private IPendingLockToken Pending { get; }
            public uint Start { get; }
            private short Key { get; }
            private readonly PendingFlags _flags;
            public bool IsAsync
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (_flags & PendingFlags.IsAsync) != 0;
            }
            public bool IsInlineable
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (_flags & PendingFlags.IsInlineable) != 0;
            }
            [Flags]
            private enum PendingFlags : short
            {
                None = 0,
                IsAsync = 1,
                IsInlineable = 2,
            }
            public PendingLockItem(uint start, short key, IPendingLockToken pending, WaitOptions options)
            {
                pending.Reset(key);
                Start = start;
                Key = key;
                Pending = pending;
                _flags = default;
                if (pending is IAsyncPendingLockToken)
                {
                    _flags |= PendingFlags.IsAsync;
                    if (pending is IInlineableAsyncPendingLockToken & (options & WaitOptions.EvilMode) != 0)
                        _flags |= PendingFlags.IsInlineable;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool TrySetResult(int token) => Pending.TrySetResult(Key, token);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool TrySetResult(int token, Action<object> continuation, object state) =>
                ((IInlineableAsyncPendingLockToken) Pending).TrySetResult(Key, token, continuation, state);

            // note: the ==/!= here don't short-circuit deliberately, to avoid branches
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator ==(PendingLockItem x, PendingLockItem y)
                => x.Key == y.Key & (object)x.Pending == (object)y.Pending;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator !=(PendingLockItem x, PendingLockItem y)
                => x.Key != y.Key | (object)x.Pending != (object)y.Pending;

            public override bool Equals(object obj) => obj is PendingLockItem other && other == this;

            public override int GetHashCode() => Key ^ (Pending?.GetHashCode() ?? 0);

            public override string ToString() => $"[{Start}]: {Pending}#{Key}";
        }

        internal interface IPendingLockToken
        {
            bool TrySetResult(short key, int token);
            bool TryCancel(short key);
            void Reset(short key);
        }
        internal interface IInlineableAsyncPendingLockToken : IAsyncPendingLockToken
        {
            bool TrySetResult(short key, int token, Action<object> continuation, object state);
        }

        internal interface IAsyncPendingLockToken : IPendingLockToken
        {
            ValueTask<LockToken> GetTask(short key);
            bool IsCanceled(short key);
        }
    }
}
