using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public sealed class PipeTextReader : TextReader
    {
        private readonly bool _closeReader;
        private readonly PipeReader _reader;
        private readonly Decoder _decoder;
        private readonly Encoding _encoding;
        private readonly ReadOnlyMemory<byte> _lineFeed;
#if !SOCKET_STREAM_BUFFERS
        private readonly ReadOnlyMemory<byte> _preamble;
#endif
        static readonly ReadOnlyMemory<byte> SingleByteLineFeed = new byte[] { (byte)'\n' };
        private readonly int _crLen, _lfLen;

        private SkipPrefix _skipPrefix;

        private Decoder GetDecoder()
        {
            _decoder.Reset();
            return _decoder;
        }

        [Conditional("VERBOSE")]
        void DebugLog(string message, [CallerMemberName] string caller = null)
        {
            Helpers.DebugLog(GetType().Name, message, caller);
        }

        enum SkipPrefix
        {
            None,
            Preamble,
            LineFeed
        }
        public PipeTextReader(PipeReader reader, Encoding encoding, bool closeReader = true)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _decoder = encoding.GetDecoder();
            _closeReader = closeReader;

            if (encoding.IsSingleByte || encoding is UTF8Encoding)
            {
                _lineFeed = SingleByteLineFeed;
                _crLen = _lfLen = 1;
            }
            else
            {
                _lineFeed = encoding.GetBytes("\n");
                _crLen = encoding.GetByteCount("\r");
                _lfLen = _lineFeed.Length;
            }


#if SOCKET_STREAM_BUFFERS
            _skipPrefix = encoding.Preamble.IsEmpty ? SkipPrefix.None : SkipPrefix.Preamble;
#else
            _preamble = encoding.GetPreamble();
            _skipPrefix = _preamble.IsEmpty ? SkipPrefix.None : SkipPrefix.Preamble;
#endif
        }


        private bool NeedPrefixCheck
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _skipPrefix != SkipPrefix.None;
        }

        private bool CheckPrefix(ref ReadOnlySequence<byte> buffer)
        {
            // this method deals with the awkwardness of things like BOMs and
            // trailing line-feed when we see a CR at the end of a chunk (and
            // don't want to block on another chunk to dismiss a LF); instead,
            // we allow expected prefixes to be silently dropped
            ReadOnlySpan<byte> prefix;
            switch (_skipPrefix)
            {
                case SkipPrefix.None:
                    return true;
                case SkipPrefix.Preamble:
#if SOCKET_STREAM_BUFFERS
                    prefix = _encoding.Preamble;
#else
                    prefix = _preamble.Span;
#endif
                    break;
                case SkipPrefix.LineFeed:
                    prefix = _lineFeed.Span;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected skip prefix: {_skipPrefix}");
            }
            if (prefix.IsEmpty) return true;

            int chk = (int)Math.Min(buffer.Length, prefix.Length);
            DebugLog($"Checking {chk} of {buffer.Length} bytes for prefix: {_skipPrefix}");

            bool isMatch;
            if (chk <= buffer.First.Length)
            {
                isMatch = buffer.First.Span.Slice(0, chk).SequenceEqual(prefix.Slice(0, chk));
            }
            else
            {
                Span<byte> all = stackalloc byte[chk];
                buffer.Slice(0, chk).CopyTo(all);
                isMatch = all.SequenceEqual(prefix.Slice(0, chk));
            }
            if (!isMatch)
            {
                // failure - partial or complete, it doesn't matter
                DebugLog($"Mismatch; abandoned prefix: {_skipPrefix}");
                _skipPrefix = SkipPrefix.None; // don't check again

                return true;
            }
            if (prefix.Length == chk)
            {
                // complete match; absorb the bytes
                _skipPrefix = SkipPrefix.None;
                buffer = buffer.Slice(chk);
                DebugLog($"Match; absorbed prefix; buffer now {buffer.Length} bytes");
                return true;
            }
            // partial match; can't say anything more just now
            DebugLog("Partial match; unable to absorb or abandon yet");
            return false;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _closeReader) _reader.Complete();
        }
        public override Task<int> ReadAsync(char[] buffer, int index, int count)
            => ReadAsyncImpl(new Memory<char>(buffer, index, count), default).AsTask();

        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
            => ReadBlockAsyncImpl(new Memory<char>(buffer, index, count), default).AsTask();

        static readonly Task<string>
            TaskEmptyString = Task.FromResult(""),
            TaskNullString = Task.FromResult<string>(null);
        public override Task<string> ReadLineAsync()
        {
            async Task<string> Awaited()
            {
                while (true)
                {
                    var result = await _reader.ReadAsync();
                    if (ReadLineAsync(in result, out var s))
                        return s;
                }
            }
            {
                if (_reader.TryRead(out var result) && ReadLineAsync(in result, out var s))
                {
                    if (s == null) return TaskNullString;
                    if (s.Length == 0) return TaskEmptyString;
                    return Task.FromResult(s);
                }
                return Awaited();
            }
        }

        private bool ReadLineAsync(in ReadResult result, out string s)
        {
            if (result.IsCanceled) throw new InvalidOperationException("Operation cancelled");

            var buffer = result.Buffer;

            if (NeedPrefixCheck) CheckPrefix(ref buffer);

            var line = ReadToEndOfLine(ref buffer); // found a line

            if (line != null)
            {
                DebugLog($"{line.Length} characters found; prefix: {_skipPrefix}; remaining buffer: {buffer.Length}");
                _reader.AdvanceTo(buffer.Start);
                s = line;
                return true;
            }
            if (result.IsCompleted) // that's everything
            {
                if (buffer.IsEmpty)
                {
                    DebugLog("EOF, no data, returning null");
                    s = null;
                    return true;
                }
                DebugLog("EOF, returning trailing data");
                s = ConsumeString(buffer);
                return true;
            }
            DebugLog("No EOL found; awaiting more data");
            _reader.AdvanceTo(buffer.Start, buffer.End);
            s = default;
            return false;
        }

        private string ReadToEndOfLine(ref ReadOnlySequence<byte> buffer)
        {
            //TODO: optimize for single-byte encodings - just hunt for bytes 10/13
            var decoder = GetDecoder();
            Span<char> chars = stackalloc char[256];
            int totalChars = 0;

            foreach (var segment in buffer)
            {
                var bytes = segment.Span;
                while (!bytes.IsEmpty)
                {
                    decoder.Convert(bytes, chars, false, out var bytesUsed, out var charsUsed, out var completed);
                    for (int i = 0; i < charsUsed; i++)
                    {
                        switch (chars[i])
                        {
                            case '\r':
                                if (i < charsUsed - 1)
                                {
                                    DebugLog($"found {(chars[i + 1] == '\n' ? "\\r\\n" : "\\r")} at char-offset {totalChars + i}");
                                    if (totalChars == 0 && bytesUsed == bytes.Length && completed)
                                        return ReadToEndOfLine(ref buffer, chars.Slice(0, i), bytesUsed);
                                    return ReadToEndOfLine(ref buffer, totalChars + i,
                                         chars[i + 1] == '\n' ? _crLen + _lfLen : _crLen);
                                }

                                // can't determine if there's a LF, so skip it instead
                                _skipPrefix = SkipPrefix.LineFeed;
                                DebugLog($"Found \\r at char-offset {totalChars + i}, trailing prefix: {_skipPrefix}");
                                if (totalChars == 0 && bytesUsed == bytes.Length && completed)
                                    return ReadToEndOfLine(ref buffer, chars.Slice(0, i), bytesUsed);
                                return ReadToEndOfLine(ref buffer, totalChars + i, _crLen);
                            case '\n':
                                DebugLog($"Found \\n at char-offset {totalChars + i}");
                                if (totalChars == 0 && bytesUsed == bytes.Length && completed)
                                    return ReadToEndOfLine(ref buffer, chars.Slice(0, i), bytesUsed);
                                return ReadToEndOfLine(ref buffer, totalChars + i, _lfLen);
                        }
                    }
                    bytes = bytes.Slice(bytesUsed);
                    totalChars += charsUsed;
                }
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ReadToEndOfLine(ref ReadOnlySequence<byte> buffer, ReadOnlySpan<char> chars, int bytesUsed)
        {
            // optimization when the line was *all* of the first span; this is actually pretty common for 
            // small messages in a line-terminated socket server conversation, for example
            buffer = buffer.Slice(bytesUsed);
            return chars.IsEmpty ? "" : chars.AsString();
        }

        private string ReadToEndOfLine(ref ReadOnlySequence<byte> buffer, int charCount, int suffixBytes)
        {
            if (charCount == 0)
            {
                DebugLog($"Consuming {suffixBytes} bytes and yielding empty string");
                buffer = buffer.Slice(suffixBytes);
                return "";
            }
            string s = GetString(in buffer, charCount, out var payloadBytes);
            DebugLog($"Consuming {payloadBytes + suffixBytes} bytes and yielding {charCount} characters");
            buffer = buffer.Slice(payloadBytes + suffixBytes);
            return s;
        }

        public override async Task<string> ReadToEndAsync()
        {
            ReadResult result;
            while (true)
            {
                result = await _reader.ReadAsync();
                if (result.IsCanceled) throw new InvalidOperationException();

                var buffer = result.Buffer;
                if (NeedPrefixCheck) CheckPrefix(ref buffer);

                if (result.IsCompleted) return ConsumeString(buffer);
                _reader.AdvanceTo(buffer.Start);
            }
        }

        bool ReadConsume(ReadResult result, Span<char> chars, bool peek, out int charsRead)
        {
            var buffer = result.Buffer;
            if (NeedPrefixCheck) CheckPrefix(ref buffer);
            int bytesUsed = GetString(buffer, chars, out charsRead);

            if ((bytesUsed != 0 && charsRead != 0) || result.IsCompleted)
            {
                _reader.AdvanceTo(peek ? buffer.Start : buffer.GetPosition(bytesUsed));
                return true;
            }
            _reader.AdvanceTo(buffer.Start);
            return false;
        }
        private async ValueTask<int> ReadAsyncImplAwaited(Memory<char> chars, CancellationToken cancellationToken, bool peek = false)
        {
            while (true)
            {
                var result = await _reader.ReadAsync(cancellationToken);
                if (ReadConsume(result, chars.Span, peek, out var charsRead))
                    return charsRead;
            }
        }
        private ValueTask<int> ReadAsyncImpl(Memory<char> chars, CancellationToken cancellationToken, bool peek = false)
        {
            if (_reader.TryRead(out var result) && ReadConsume(result, chars.Span, peek, out var charsRead))
                return new ValueTask<int>(charsRead);

            return ReadAsyncImplAwaited(chars, cancellationToken, peek);
        }
        private async ValueTask<int> ReadBlockAsyncImpl(Memory<char> buffer, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (!buffer.IsEmpty)
            {
                var chunk = await ReadAsyncImpl(buffer, cancellationToken);
                if (chunk <= 0) break;

                totalRead += chunk;
                if (chunk == buffer.Length) break; // just to avoid a final "slice"
                buffer = buffer.Slice(chunk);
            }
            return totalRead;
        }
#if SOCKET_STREAM_BUFFERS
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
            => ReadAsyncImpl(buffer, cancellationToken);
        public override ValueTask<int> ReadBlockAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
            => ReadBlockAsyncImpl(buffer, cancellationToken);
        public override int Read(Span<char> buffer)
        {
            // sync over async and involves a copy; best we can do, considering
            if (buffer.IsEmpty) return 0;
            var arr = ArrayPool<char>.Shared.Rent(buffer.Length);
            int bytes = ReadAsyncImpl(new Memory<char>(arr, 0, buffer.Length), default).Result;
            if (bytes > 0) new Span<char>(arr, 0, bytes).CopyTo(buffer);
            ArrayPool<char>.Shared.Return(arr);
            return bytes;
        }
        public override int ReadBlock(Span<char> buffer)
        {
            // sync over async and involves a copy; best we can do, considering
            if (buffer.IsEmpty) return 0;
            var arr = ArrayPool<char>.Shared.Rent(buffer.Length);
            int bytes = ReadBlockAsync(new Memory<char>(arr, 0, buffer.Length)).Result;
            if (bytes > 0) new Span<char>(arr, 0, bytes).CopyTo(buffer);
            ArrayPool<char>.Shared.Return(arr);
            return bytes;
        }
#endif

        public override string ReadLine() => ReadLineAsync().Result;
        public override int ReadBlock(char[] buffer, int index, int count) => ReadBlockAsync(buffer, index, count).Result;
        public override int Read(char[] buffer, int index, int count) => ReadAsync(buffer, index, count).Result;
        public override string ReadToEnd() => ReadToEndAsync().Result;
        public override int Read() => ReadSingleChar(false);
        public override int Peek() => ReadSingleChar(true);

        private int ReadSingleChar(bool peek)
        {
            if (_reader.TryRead(out var result))
            {
                Span<char> chars = stackalloc char[1];
                if (ReadConsume(result, chars, peek, out var charsRead))
                    return charsRead <= 0 ? -1 : chars[0];
            }

            var arr = ArrayPool<char>.Shared.Rent(1);
            int bytes = ReadAsyncImplAwaited(new Memory<char>(arr, 0, 1), default, peek).Result;
            var finalResult = bytes <= 0 ? -1 : arr[0];
            ArrayPool<char>.Shared.Return(arr);
            return finalResult;
        }
        private string ConsumeString(ReadOnlySequence<byte> buffer)
        {
            var s = GetString(buffer);
            _reader.AdvanceTo(buffer.End);
            return s;
        }

        private int GetCharCount(in ReadOnlySequence<byte> buffer)
        {
            var enc = _encoding;

            if (enc.IsSingleByte) return (int)checked(buffer.Length);

            if (enc is UnicodeEncoding) return (int)checked(buffer.Length / 2);

            if (buffer.IsSingleSegment) return buffer.IsEmpty ? 0 : enc.GetCharCount(buffer.First.Span);

            int charCount = 0;
            var decoder = GetDecoder();
            foreach (var segment in buffer)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;
                charCount += decoder.GetCharCount(span, false);
            }
            return charCount;

        }
        private string GetString(in ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                var span = buffer.First.Span;
                if (span.IsEmpty) return "";
                return _encoding.GetString(span);
            }

            var charCount = GetCharCount(in buffer);
            var s = GetString(in buffer, charCount, out int actualCharCount);
            Debug.Assert(actualCharCount == charCount);
            return s;
        }
        string GetString(in ReadOnlySequence<byte> buffer, int charCount, out int totalBytes)
        {
            if (charCount == 0)
            {
                totalBytes = 0;
                return "";
            }
            string s = new string((char)0, charCount);
            var chars = MemoryMarshal.AsMemory(s.AsMemory()).Span;
            totalBytes = GetString(in buffer, chars, out int actualChars);
            Debug.Assert(actualChars == charCount);
            return s;
        }
        int GetString(in ReadOnlySequence<byte> buffer, Span<char> chars, out int charsRead)
        {
            if (chars.IsEmpty)
            {
                charsRead = 0;
                return 0;
            }
            var decoder = GetDecoder();
            int totalBytes = 0;
            charsRead = 0;

            foreach (var segment in buffer)
            {
                var bytes = segment.Span;
                if (bytes.IsEmpty) continue;
                decoder.Convert(bytes, chars, false, out var bytesUsed, out var charsUsed, out _);
                totalBytes += bytesUsed;
                charsRead += charsUsed;
                chars = chars.Slice(charsUsed);
                if (chars.IsEmpty) break;
            }
            return totalBytes;
        }
    }
}
