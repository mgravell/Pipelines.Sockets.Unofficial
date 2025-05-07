using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
                var count = Volatile.Read(ref _counters[(int)enums[i]]);
                if (count != 0) sb.Append(enums[i]).Append(":\t").Append(count).AppendLine();
            }
            lock(_execCount)
            {
                foreach(var pair in _execCount)
                {
                    sb.Append(pair.Key).Append(":\t").Append(pair.Value).AppendLine();
                }
            }
            return sb.ToString();
        }
        private static readonly Dictionary<string, int> _execCount = new Dictionary<string, int>();
#endif
        [Conditional("DEBUG")]
#pragma warning disable RCS1163 // Unused parameter.
        internal static void Incr(Counter counter)
#pragma warning restore RCS1163 // Unused parameter.
        {
#if DEBUG
            Interlocked.Increment(ref _counters[(int)counter]);
#endif
        }
        [Conditional("DEBUG")]
#pragma warning disable RCS1163 // Unused parameter.
        internal static void Decr(Counter counter)
#pragma warning restore RCS1163 // Unused parameter.
        {
#if DEBUG
            Interlocked.Decrement(ref _counters[(int)counter]);
#endif
        }
        [Conditional("DEBUG")]
#pragma warning disable RCS1163 // Unused parameter.
        internal static void Incr(MethodInfo method)
#pragma warning restore RCS1163 // Unused parameter.
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

        private static string s_assemblyFailureMessssage = null;
        private static string GetAssemblyFailureMessage()
        {
            static string ComputeAssemblyFailureMessage()
            {
                List<string> failures = null;
                void AddFailure(string assembly)
                {
                    if (failures is null) failures = new List<string>();
                    failures.Add(assembly);
                }
                try { CheckPipe(); } catch { AddFailure("System.IO.Pipelines"); }
                try { CheckBuffers(); } catch { AddFailure("System.Buffers"); }
                try { CheckUnsafe(); } catch { AddFailure("System.Runtime.CompilerServices.Unsafe"); }
                try { CheckNumerics(); } catch { AddFailure("System.Numerics.Vectors"); }

                try
                {
                    ExecutePipe(out var assembly);
                    if (assembly is not null) AddFailure(assembly);
                }
                catch(Exception ex)
                {   // ExecutePipe exploded, but not in a way we expected
                    return ex.Message;
                }

                if (failures is null || failures.Count == 0) return "";

                return "The assembly for " + string.Join(" + ", failures) + " could not be loaded; this usually means a missing assembly binding redirect - try checking this, and adding any that are missing;"
                    + " note that it is not always possible to add this redirects - for example 'azure functions v1'; it looks like you may need to use 'azure functions v2' for that - sorry, but that's out of our control";
            }
            return s_assemblyFailureMessssage ??= ComputeAssemblyFailureMessage();
        }
        internal static void AssertDependencies()
        {
            string err = GetAssemblyFailureMessage();
            if (!string.IsNullOrEmpty(err)) Throw.InvalidOperation(err);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CheckPipe() => GC.KeepAlive(System.IO.Pipelines.PipeOptions.Default);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CheckBuffers()
        {
            var arr = System.Buffers.ArrayPool<byte>.Shared.Rent(64);
            GC.KeepAlive(arr);
            System.Buffers.ArrayPool<byte>.Shared.Return(arr);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CheckUnsafe() => _ = System.Runtime.CompilerServices.Unsafe.SizeOf<int>();


        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CheckNumerics() => _ = System.Numerics.Vector.IsHardwareAccelerated;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ExecutePipe(out string assembly)
        {   // try everything combined
            assembly = null;
            try
            {
                var pipe = new System.IO.Pipelines.Pipe();
                pipe.Writer.GetSpan(4);
                pipe.Writer.Advance(4);
                pipe.Writer.Complete();
                pipe.Reader.TryRead(out var _);
            }
            catch(Exception ex)
            {   // look (non-greedy) for either 'System.Blah' or 'System.Blah,...
                var match = Regex.Match(ex.Message, @"'(System\..*?)[,']");
                if (match.Success)
                {
                    assembly = match.Groups[1].Value;
                }
                else
                {
                    throw;
                }
            }
        }


#pragma warning disable RCS1231 // Make parameter ref read-only.
        internal static ArraySegment<byte> GetArray(this Memory<byte> buffer) => GetArray((ReadOnlyMemory<byte>)buffer);
        internal static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> buffer)
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            if (!MemoryMarshal.TryGetArray<byte>(buffer, out var segment)) Throw.InvalidOperation("MemoryMarshal.TryGetArray<byte> could not provide an array");
            return segment;
        }

#if DEBUG
        internal static System.IO.TextWriter Log = System.IO.TextWriter.Null;
#endif

        [Conditional("VERBOSE")]
#pragma warning disable RCS1163 // Unused parameter.
        internal static void DebugLog(string name, string message, [CallerMemberName] string caller = null)
#pragma warning restore RCS1163 // Unused parameter.
        {
#if VERBOSE
            var log = Log;
            if (log is not null)
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
