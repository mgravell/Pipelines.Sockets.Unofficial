using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks.Sources;

namespace Pipelines.Sockets.Unofficial.Threading
{
    public partial class MutexSlim
    {
        internal static class LockState
        {   // using this as a glorified enum; can't use enum directly because
            // or Interlocked etc support
            public const int
                Timeout = 0, // we want "completed with failure" to be the implicit zero default, so default(LockToken) is a miss
                Pending = 1,
                Success = 2, // note: careful choice of numbers here allows IsCompletedSuccessfully to check whether the LSB is set
                Canceled = 3;

            //Pending = 0,
            //Canceled = 1,
            //Success = 2, // note: we make use of the fact that Success/Timeout use the
            //Timeout = 3; // 2nd bit for IsCompletedSuccessfully; don't change casually!

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int GetNextToken(int token)
                // 2 low bits are status; 16 high bits are counter ("short", basically)
                => (int)((((uint)token >> 16) + 1) << 16) | Success;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int ChangeState(int token, int state)
                => (token & ~3) | state; // retain counter portion; swap state portion

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int GetState(int token) => token & 3;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ValueTaskSourceStatus GetStatus(ref int token)
            {
                switch (LockState.GetState(Volatile.Read(ref token)))
                {
                    case LockState.Canceled:
                        return ValueTaskSourceStatus.Canceled;
                    case LockState.Pending:
                        return ValueTaskSourceStatus.Pending;
                    default: // LockState.Success, LockState.Timeout (we only have 4 bits for status)
                        return ValueTaskSourceStatus.Succeeded;
                }
            }

            // "completed", in Task/ValueTask terms, includes cancelation - only omits pending
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCompleted(int token) => (token & 3) != Pending;

            // note that "successfully" here doesn't mean "with the lock"; as per Task/ValueTask IsCompletedSuccessfully,
            // it means "completed, and not faulted or canceled"; see LockState - we can check that by testing the
            // second bit (Success=2,Timeout=3)
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCompletedSuccessfully(int token) => (token & 1) == 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCanceled(int token) => (token & 3) == Canceled;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TrySetResult(ref int token, int value)
            {
                int oldValue = Volatile.Read(ref token);
                if (LockState.GetState(oldValue) == LockState.Pending
                    && Interlocked.CompareExchange(ref token, value, oldValue) == oldValue)
                {
                    return true;
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryCancel(ref int token)
            {
                int oldValue;
                do
                {
                    // depends on the current state...
                    oldValue = Volatile.Read(ref token);
                    if (LockState.GetState(oldValue) != LockState.Pending)
                    {
                        // already fixed
                        return false;
                    }
                    // otherwise, attempt to change the field; in case of conflict; re-do from start
                } while (Interlocked.CompareExchange(ref token, LockState.ChangeState(oldValue, LockState.Canceled), oldValue) != oldValue);
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int GetResult(ref int token)
            {   // if already complete: returns the token; otherwise, dooms the operation
                int oldValue, newValue;
                do
                {
                    oldValue = Volatile.Read(ref token);
                    if (LockState.GetState(oldValue) != LockState.Pending)
                    {
                        // value is already fixed; just return it
                        return oldValue;
                    }
                    // we don't ever want to report different values from GetResult, so
                    // if you called GetResult prematurely: you doomed it to failure
                    newValue = LockState.ChangeState(oldValue, LockState.Timeout);

                    // if something changed while we were thinking, redo from start
                } while (Interlocked.CompareExchange(ref token, newValue, oldValue) != oldValue);
                return newValue;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Reset(ref int token) => Volatile.Write(ref token, LockState.Pending);

            internal static string ToString(int token)
            {
                var id = ((uint)token) >> 2;
                switch(GetState(token))
                {
                    case Timeout: return $"(#{id},timeout)";
                    case Pending: return $"(#{id},pending)";
                    case Success: return $"(#{id},success)";
                    default: return $"(#{id},canceled)";
                }
            }
        }
    }
}
