using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial
{
    internal static class Helpers
    {
        internal static ArraySegment<byte> GetArray(this Memory<byte> buffer)
        {
            if (!buffer.TryGetArray(out var segment)) throw new InvalidOperationException("Memory<byte>.TryGetArray could not provide an array");
            return segment;
        }
        internal static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> buffer)
            => GetArray(MemoryMarshal.AsMemory(buffer));

        [Conditional("DEBUG")]
        internal static void DebugLog(this TextWriter log, string message, [CallerMemberName] string caller = null)
        {
#if DEBUG
            log?.WriteLine("[" + caller + "] " + message);
#endif
        }
    }
}
