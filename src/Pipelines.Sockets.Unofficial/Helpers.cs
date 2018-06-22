using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

namespace Pipelines.Sockets.Unofficial
{

#if DEBUG
#pragma warning disable CS1591
    public static class DebugCounters
    {
        public static void Reset() => Helpers.ResetCounters();
        public static string GetSummary() => Helpers.GetCounterSummary();
    }
#pragma warning restore CS1591
#endif

    internal enum Counter
    {
        SocketSendAsyncSingleSync,
        SocketSendAsyncSingleAsync,
        SocketSendAsyncMultiSync,
        SocketSendAsyncMultiAsync,


        SocketPipeReadReadSync,
        SocketPipeReadReadAsync,
        SocketPipeFlushSync,
        SocketPipeFlushAsync,


        SocketReceiveSync,
        SocketReceiveAsync,
        SocketZeroLengthReceiveSync,
        SocketZeroLengthReceiveAsync,
        SocketSendAsyncSync,
        SocketSendAsyncAsync,


        SocketAwaitableCallbackNone,
        SocketAwaitableCallbackDirect,
        SocketAwaitableCallbackSchedule,


        ThreadPoolWorkerStarted,
        ThreadPoolPushedToMainThreadPoop,
        ThreadPoolScheduled,
        ThreadPoolExecuted,

        

    }
    internal static class Helpers
    {
#if DEBUG
        private readonly static int[] _counters = new int[Enum.GetValues(typeof(Counter)).Length];
        internal static void ResetCounters()
        {
            Array.Clear(_counters, 0, _counters.Length);
            lock(_execCount) { _execCount.Clear(); }
        }
        internal static string GetCounterSummary()
        {
            var enums = (Counter[])Enum.GetValues(typeof(Counter));
            var sb = new System.Text.StringBuilder();
            for(int i = 0 ; i < enums.Length ; i++)
            {
                var count = Thread.VolatileRead(ref _counters[(int)enums[i]]);
                if (count != 0) sb.AppendLine($"{enums[i]}:\t{count}");
            }
            lock(_execCount)
            {
                foreach(var pair in _execCount)
                {
                    sb.AppendLine($"{pair.Key}:\t{pair.Value}");
                }
            }
            return sb.ToString();
        }
        static readonly Dictionary<string, int> _execCount = new Dictionary<string, int>();
#endif
        [Conditional("DEBUG")]
        internal static void Incr(Counter counter)
        {
#if DEBUG
            Interlocked.Increment(ref _counters[(int)counter]);
#endif
        }
        [Conditional("DEBUG")]
        internal static void Incr(MethodInfo method)
        {
#if DEBUG
            lock(_execCount)
            {
                var name = $"{method.DeclaringType.FullName}.{method.Name}";
                if (!_execCount.TryGetValue(name, out var count)) count = 0;
                _execCount[name] = count + 1;
            }
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
