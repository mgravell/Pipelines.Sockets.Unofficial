using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        readonly struct PendingLockItem
        {
            public uint Start { get; }
            private readonly short _key;
            public IPendingLockToken Pending { get; }
            public bool IsAsync
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Pending is IAsyncPendingLockToken;
            }

            public PendingLockItem(uint start, short key, IPendingLockToken pending)
            {
                pending.Reset(key);
                Start = start;
                _key = key;
                Pending = pending;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool TrySetResult(int token) => Pending.TrySetResult(_key, token);

            // note: the ==/!= here don't short-circuit deliberately, to avoid branches
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator ==(PendingLockItem x, PendingLockItem y)
                => x._key == y._key & (object)x.Pending == (object)y.Pending;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator !=(PendingLockItem x, PendingLockItem y)
                => x._key != y._key | (object)x.Pending != (object)y.Pending;

            public override bool Equals(object obj) => obj is PendingLockItem other && other == this;

            public override int GetHashCode() => _key ^ (Pending?.GetHashCode() ?? 0);

            public override string ToString() => $"[{Start}]: {Pending}#{_key}";
        }

        internal interface IPendingLockToken
        {
            bool TrySetResult(short key, int token);
            bool TryCancel(short key);
            void Reset(short key);
        }

        internal interface IAsyncPendingLockToken : IPendingLockToken
        {
            ValueTask<LockToken> GetTask(short key);
            bool IsCanceled(short key);
        }
    }
}
