using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial
{
    internal static class Helpers
    {
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
        internal static void DebugLog(string message = "", [CallerMemberName] string caller = null)
        {
#if VERBOSE
                var thread = System.Threading.Thread.CurrentThread;
                var name = thread.Name;
                if (string.IsNullOrWhiteSpace(name)) name = thread.ManagedThreadId.ToString();

                Log?.WriteLine($"[{name}, {caller}]: {message}");
#endif
        }

    }
}
