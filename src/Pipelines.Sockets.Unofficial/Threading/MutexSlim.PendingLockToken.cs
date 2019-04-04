using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private readonly struct PendingLockItem
        {
            public IPendingLockToken Pending { get; }
            public uint Start { get; }
            private readonly short _key;

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
#pragma warning disable RCS1233 // Use short-circuiting operator.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator ==(in PendingLockItem x, in PendingLockItem y)
                => x._key == y._key & (object)x.Pending == (object)y.Pending;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator !=(in PendingLockItem x, in PendingLockItem y)
                => x._key != y._key | (object)x.Pending != (object)y.Pending;
#pragma warning restore RCS1233 // Use short-circuiting operator.

            public override bool Equals(object obj) => obj is PendingLockItem other && other == this;

            public override int GetHashCode() => _key ^ (Pending?.GetHashCode() ?? 0);

            public override string ToString() => $"[{Start}]: {Pending}#{_key}";

            internal bool TrySpinWait()
            {
                var wait = new SpinWait();
                do
                {
                    wait.SpinOnce();
                    if(Pending.HasResult(_key)) return true;
                } while (!wait.NextSpinWillYield);
                return false;
            }
        }

        internal interface IPendingLockToken
        {
            bool TrySetResult(short key, int token);
            bool TryCancel(short key);
            void Reset(short key);
            bool HasResult(short key);
            int GetResult(short key);
        }

        internal interface IAsyncPendingLockToken : IPendingLockToken
        {
            ValueTask<LockToken> GetTask(short key);
        }
    }
}
