using System;
using System.Diagnostics;
using System.IO;
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


        [Conditional("DEBUG")]
        internal static void DebugLog(this TextWriter log, string message, [CallerMemberName] string caller = null)
        {
#if DEBUG
            log?.WriteLine("[" + caller + "] " + message);
#endif
        }
    }
}
