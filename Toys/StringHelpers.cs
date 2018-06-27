using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Pipelines.Sockets.Unofficial
{
    internal static class StringHelpers
    {
#if SOCKET_STREAM_BUFFERS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string AsString(this ReadOnlySpan<char> chars) => new string(chars);
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe string AsString(this ReadOnlySpan<char> chars)
        {
            fixed (char* charPtr = &MemoryMarshal.GetReference(chars))
            {
                return new string(charPtr, 0, chars.Length);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Convert(this Decoder decoder, ReadOnlySpan<byte> bytes, Span<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            fixed (byte* bytePtr = &MemoryMarshal.GetReference(bytes))
            fixed (char* charPtr = &MemoryMarshal.GetReference(chars))
            {
                decoder.Convert(bytePtr, bytes.Length, charPtr, chars.Length, flush, out bytesUsed, out charsUsed, out completed);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(bytes))
            {
                return encoding.GetString(ptr, bytes.Length);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int GetCharCount(this Decoder decoder, ReadOnlySpan<byte> bytes, bool flush)
        {
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            {
                return decoder.GetCharCount(bPtr, bytes.Length, flush);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int GetCharCount(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            {
                return encoding.GetCharCount(bPtr, bytes.Length);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int GetChars(this Encoding encoding, ReadOnlySpan<byte> bytes, Span<char> chars)
        {
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            fixed (char* cPtr = &MemoryMarshal.GetReference(chars))
            {
                return encoding.GetChars(bPtr, bytes.Length, cPtr, chars.Length);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            fixed (char* cPtr = &MemoryMarshal.GetReference(chars))
            {
                return encoding.GetBytes(cPtr, chars.Length, bPtr, bytes.Length);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Convert(this Encoder encoder, ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            fixed (char* cPtr = &MemoryMarshal.GetReference(chars))
            {
                encoder.Convert(cPtr, chars.Length, bPtr, bytes.Length, flush, out bytesUsed, out charsUsed, out completed);
            }
        }
#endif
    }
}
