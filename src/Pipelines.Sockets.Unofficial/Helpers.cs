using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{

#if DEBUG
    public static class DebugCounters
    {
        public static void Reset() => Helpers.ResetCounters();
        public static string GetSummary() => Helpers.GetCounterSummary();
    }
#endif

    internal enum Counter
    {
        SocketSendAsyncSingleSync,
        SocketSendAsyncSingleAsync,
        SocketSendAsyncMultiSync,
        SocketSendAsyncMultiAsync,



        SocketReadReadSync,
        SocketReadReadAsync,
        SocketReadSendSync,
        SocketReadSendAsync,


        SocketAwaitableCallbackNone,
        SocketAwaitableCallbackDirect,
        SocketAwaitableCallbackSchedule,
        
    }
    internal static class Helpers
    {
#if DEBUG
        private readonly static int[] _counters = new int[Enum.GetValues(typeof(Counter)).Length];
        internal static void ResetCounters() => Array.Clear(_counters, 0, _counters.Length);
        internal static string GetCounterSummary()
        {
            var enums = (Counter[])Enum.GetValues(typeof(Counter));
            var sb = new System.Text.StringBuilder();
            for(int i = 0 ; i < enums.Length ; i++)
            {
                var count = Thread.VolatileRead(ref _counters[(int)enums[i]]);
                if (count != 0) sb.AppendLine($"{enums[i]}:\t{count}");
            }
            return sb.ToString();
        }
#endif
        [Conditional("DEBUG")]
        internal static void Incr(Counter counter)
        {
#if DEBUG
            Interlocked.Increment(ref _counters[(int)counter]);
#endif
        }
        
        internal static ArraySegment<byte> GetArray(this Memory<byte> buffer) => GetArray((ReadOnlyMemory<byte>)buffer);
        internal static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> buffer)
        {
            if (!MemoryMarshal.TryGetArray<byte>(buffer, out var segment)) throw new InvalidOperationException("MemoryMarshal.TryGetArray<byte> could not provide an array");
            return segment;
        }

#if DEBUG
        internal static System.IO.TextWriter Log = Console.Out;
#endif

        [Conditional("VERBOSE")]
        internal static void DebugLog(string name, string message, [CallerMemberName] string caller = null)
        {
#if VERBOSE
                var thread = System.Threading.Thread.CurrentThread;
                var threadName = thread.Name;
                if (string.IsNullOrWhiteSpace(threadName)) threadName = thread.ManagedThreadId.ToString();

                Log?.WriteLine($"[{threadName}, {name}, {caller}]: {message}");
#endif
        }

    }
}
