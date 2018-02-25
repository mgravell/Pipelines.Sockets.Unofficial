using System;
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
    }
}
