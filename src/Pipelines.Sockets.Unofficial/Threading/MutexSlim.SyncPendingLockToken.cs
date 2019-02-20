using System;
using System.Threading;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        private sealed class SyncPendingLockToken : IPendingLockToken
        {
            [ThreadStatic]
            private static SyncPendingLockToken s_perThreadLockObject;

            private void OnAssigned()
            {
                lock (this)
                {
                    Monitor.Pulse(this); // wake up a sleeper
                }
            }

            private int _token;

            void IPendingLockToken.Reset(short key) => LockState.Reset(ref _token);

            public static SyncPendingLockToken GetPerThreadLockObject() => s_perThreadLockObject ?? GetNewPerThreadLockObject();
            public static SyncPendingLockToken GetNewPerThreadLockObject() => s_perThreadLockObject = new SyncPendingLockToken();
            public static void ResetPerThreadLockObject() => s_perThreadLockObject = null;

            internal int GetResult() => LockState.GetResult(ref _token);

            bool IPendingLockToken.TrySetResult(short key, int token)
            {
                bool success = LockState.TrySetResult(ref _token, token);
                if (success) OnAssigned();
                return success;
            }

            public bool TryCancel(short key)
            {
                bool success = LockState.TryCancel(ref _token);
                if (success) OnAssigned();
                return success;
            }
        }
    }
}
