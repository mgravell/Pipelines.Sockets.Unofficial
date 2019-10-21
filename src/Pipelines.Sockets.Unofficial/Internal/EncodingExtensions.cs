#if !SOCKET_STREAM_BUFFERS
using System;
using System.Text;

namespace Pipelines.Sockets.Unofficial.Internal
{
    internal static class EncodingExtensions
    {
        // API shim
        internal static unsafe int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (char* c = chars)
            fixed (byte* b = bytes)
            {
                return encoding.GetBytes(c, chars.Length, b, bytes.Length);
            }
        }
    }
}
#endif
