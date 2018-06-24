using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Text;

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
        ThreadPoolPushedToMainThreadPoop,
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

#if !SOCKET_STREAM_BUFFERS
        internal static unsafe void Convert(this Decoder decoder, ReadOnlySpan<byte> bytes, Span<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            fixed (byte* bytePtr = &MemoryMarshal.GetReference(bytes))
            fixed (char* charPtr = &MemoryMarshal.GetReference(chars))
            {
                decoder.Convert(bytePtr, bytes.Length, charPtr, chars.Length, flush, out bytesUsed, out charsUsed, out completed);
            }
        }
        internal static unsafe string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(bytes))
            {
                return encoding.GetString(ptr, bytes.Length);
            }
        }
        internal static unsafe int GetCharCount(this Decoder decoder, ReadOnlySpan<byte> bytes, bool flush)
        {
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            {
                return decoder.GetCharCount(bPtr, bytes.Length, flush);
            }
        }
        internal static unsafe int GetCharCount(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            {
                return encoding.GetCharCount(bPtr, bytes.Length);
            }
        }
        internal static unsafe void Convert(this Encoder encoder, ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            fixed (byte* bytePtr = &MemoryMarshal.GetReference(bytes))
            fixed (char* charPtr = &MemoryMarshal.GetReference(chars))
            {
                encoder.Convert(charPtr, chars.Length, bytePtr, bytes.Length, flush, out bytesUsed, out charsUsed, out completed);
            }
        }
#endif
    }
}
