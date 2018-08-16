using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{

#if DEBUG
#pragma warning disable CS1591
    public static class DebugCounters
    {
        public static void Reset() => Helpers.ResetCounters();
        public static string GetSummary() => Helpers.GetCounterSummary();

        public static void SetLog(System.IO.TextWriter log) => Helpers.Log = log ?? System.IO.TextWriter.Null;
    }
#pragma warning restore CS1591
#endif

    internal enum Counter
    {
        SocketGetBufferList,

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
        ThreadPoolPushedToMainThreadPool,
        ThreadPoolScheduled,
        ThreadPoolExecuted,

        PipeStreamWrite,
        PipeStreamWriteAsync,
        PipeStreamWriteByte,
        PipeStreamBeginWrite,
        PipeStreamWriteSpan,
        PipeStreamWriteAsyncMemory,

        PipeStreamRead,
        PipeStreamReadAsync,
        PipeStreamReadByte,
        PipeStreamBeginRead,
        PipeStreamReadSpan,
        PipeStreamReadAsyncMemory,

        PipeStreamFlush,
        PipeStreamFlushAsync,

        OpenReceiveReadAsync,
        OpenReceiveFlushAsync,
        OpenSendReadAsync,
        OpenSendWriteAsync,
        SocketConnectionCollectedWithoutDispose,
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
        internal static void Decr(Counter counter)
        {
#if DEBUG
            Interlocked.Decrement(ref _counters[(int)counter]);
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
        internal static System.IO.TextWriter Log = System.IO.TextWriter.Null;
#endif

        [Conditional("VERBOSE")]
        internal static void DebugLog(string name, string message, [CallerMemberName] string caller = null)
        {
#if VERBOSE
            
            var log = Log;
            if (log != null)
            {
                var thread = System.Threading.Thread.CurrentThread;
                var threadName = thread.Name;
                if (string.IsNullOrWhiteSpace(threadName)) threadName = thread.ManagedThreadId.ToString();
                    
                var s = $"[{threadName}, {name}, {caller}]: {message}";
                lock (log)
                {
                    try { log.WriteLine(s); }
                    catch { }
                }
            }
#endif
        }

        internal static void PipelinesFireAndForget(this Task task)
            => task?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
    }
}
