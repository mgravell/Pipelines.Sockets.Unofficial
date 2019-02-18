using System;
using System.IO.Pipelines;
using System.Threading;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        internal sealed class SyncPendingLockToken : PendingLockToken
        {
            [ThreadStatic]
            private static SyncPendingLockToken s_perThreadLockObject;

            protected override void OnAssigned()
            {
                lock (this)
                {
                    Monitor.Pulse(this); // wake up a sleeper
                }
            }

            new public void Reset(uint start) => base.Reset(start);

            public static SyncPendingLockToken GetPerThreadLockObject() => s_perThreadLockObject ?? GetNewPerThreadLockObject();
            public static SyncPendingLockToken GetNewPerThreadLockObject() => s_perThreadLockObject = new SyncPendingLockToken();
            public static void ResetPerThreadLockObject() => s_perThreadLockObject = null;
        }
    }
}
