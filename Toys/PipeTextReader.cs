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
    /// <summary>
    /// A TextReader implementation that pulls from a PipeReader
    /// </summary>
    public sealed class PipeTextReader : TextReader
    {
        private readonly bool _closeReader, _useFastNewlineCheck;
        private readonly PipeReader _reader;
        private readonly Decoder _decoder;
        private readonly Encoding _encoding;
        // private readonly int _maxBytesPerChar;
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
            Utils.DebugLog.WriteLine($"{GetType().Name}/{caller}: {message}");
        }

        enum SkipPrefix
        {
            None,
            Preamble,
            LineFeed
        }
        /// <summary>
        /// Create a new PipeTextReader instance
        /// </summary>
        public static TextReader Create(PipeReader reader, Encoding encoding, bool closeReader = true, int bufferSize = BufferedTextReader.DefaultBufferSize)
        {
            TextReader tr = new PipeTextReader(reader, encoding, closeReader);
            return bufferSize > 0 ? new BufferedTextReader(tr, bufferSize, closeReader) : tr;
        }
        private PipeTextReader(PipeReader reader, Encoding encoding, bool closeReader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _decoder = encoding.GetDecoder();
            _closeReader = closeReader;

            if (encoding is UTF8Encoding || encoding.IsSingleByte)
            {
                _useFastNewlineCheck = true;
                _lineFeed = SingleByteLineFeed;
                _crLen = _lfLen = 1;
            }
            else
            {
                _lineFeed = encoding.GetBytes("\n");
                _crLen = encoding.GetByteCount("\r");
                _lfLen = _lineFeed.Length;
            }
            // _maxBytesPerChar = encoding.GetMaxByteCount(1); // over-reports, but... meh


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
        /// <summary>
        /// Release any resources held by the object
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _closeReader) _reader.Complete();
        }
        /// <summary>
        /// Read some character data into a buffer from the pipe, asynchronously
        /// </summary>
        public override Task<int> ReadAsync(char[] buffer, int index, int count)
            => ReadAsyncImpl(new Memory<char>(buffer, index, count), default).AsTask();

        /// <summary>
        /// Attempt to fully populate a buffer with character data from the pipe, asynchronously
        /// </summary>
        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
            => ReadBlockAsyncImpl(new Memory<char>(buffer, index, count), default).AsTask();

        static readonly Task<string>
            TaskEmptyString = Task.FromResult(""),
            TaskNullString = Task.FromResult<string>(null);
        /// <summary>
        /// Attempt to read a line from the pipe, asynchronously
        /// </summary>
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

            if (NeedPrefixCheck)
            {
                if(!CheckPrefix(ref buffer) && !result.IsCompleted)
                {
                    // not enough data
                    s = null;
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                    return false; 
                }
            }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ReadToEndOfLine(ref ReadOnlySequence<byte> buffer)
            => _useFastNewlineCheck ? ReadToEndOfLineFast(ref buffer) : ReadToEndOfLineSlow(ref buffer);
        
        private string ReadToEndOfLineFast(ref ReadOnlySequence<byte> buffer)
        {
            var index = FindEndOfLineIndex(in buffer, out var style);
            if (index < 0) return null;

            string s;
            if (index == 0)
            {
                s = "";
            }
            else
            {
                var payload = buffer.Slice(0, index);
                var decoder = _decoder;
                int charCount = GetCharCount(in payload, _encoding, ref decoder);
                s = new string((char)0, charCount);

                var chars = MemoryMarshal.AsMemory(s.AsMemory()).Span;
                var totalBytes = GetString(in payload, chars, out int actualChars, _encoding, decoder, true);
                Debug.Assert(actualChars == charCount);
                Debug.Assert(totalBytes == index);
            }
            if (style == "\r") _skipPrefix = SkipPrefix.LineFeed;
            buffer = buffer.Slice(index + style.Length);
            return s;
        }
        private static int FindEndOfLineIndex(in ReadOnlySequence<byte> buffer, out string style)
        {
            if (buffer.IsSingleSegment) return FindEndOfLineIndex(buffer.First.Span, out style);

            int offset = 0;
            foreach(var segment in buffer)
            {
                var span = segment.Span;
                var index = FindEndOfLineIndex(span, out style);
                if (index >= 0) return offset + index;
                offset += span.Length;
            }
            style = default;
            return -1;
        }
        private static int FindEndOfLineIndex(ReadOnlySpan<byte> span, out string style)
        {
            for(int i = 0; i < span.Length; i++)
            {
                switch(span[i])
                {
                    case (byte)'\r':
                        style = i < span.Length - 1 && span[i + 1] == '\r' ? "\r\n" : "\r";
                        return i;
                    case (byte)'\n':
                        style = "\n";
                        return i;
                }
            }
            style = default;
            return -1;
        }
        private string ReadToEndOfLineSlow(ref ReadOnlySequence<byte> buffer)
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
            string s = GetString(in buffer, charCount, out var payloadBytes, _encoding, _decoder, false);
            DebugLog($"Consuming {payloadBytes + suffixBytes} bytes and yielding {charCount} characters");
            buffer = buffer.Slice(payloadBytes + suffixBytes);
            return s;
        }
        /// <summary>
        /// Read to the end of the pipe, asynchronously
        /// </summary>
        public override async Task<string> ReadToEndAsync()
        {
            ReadResult result;
            while (true)
            {
                result = await _reader.ReadAsync();
                if (result.IsCanceled) throw new InvalidOperationException();

                var buffer = result.Buffer;
                if (NeedPrefixCheck)
                {
                    if(!CheckPrefix(ref buffer) && !result.IsCompleted)
                    {
                        // need more data
                        _reader.AdvanceTo(buffer.Start, buffer.End);
                        continue;
                    }
                }

                if (result.IsCompleted) return ConsumeString(buffer);
                _reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        bool ReadConsume(ReadResult result, Span<char> chars, bool peek, out int charsRead)
        {
            var buffer = result.Buffer;
            if (NeedPrefixCheck)
            {
                if(!CheckPrefix(ref buffer) && !result.IsCompleted)
                {
                    // need more data
                    charsRead = 0;
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                    return false;
                }
            }
            int bytesUsed = GetString(buffer, chars, out charsRead, _encoding, _decoder, false);

            if ((bytesUsed != 0 && charsRead != 0) || result.IsCompleted)
            {
                _reader.AdvanceTo(peek ? buffer.Start : buffer.GetPosition(bytesUsed));
                return true;
            }
            _reader.AdvanceTo(buffer.Start, buffer.End);
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
        /// <summary>
        /// Attempt to partially populate a buffer with character data from the pipe, asynchronously
        /// </summary>
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
            => ReadAsyncImpl(buffer, cancellationToken);
        /// <summary>
        /// Attempt to fully populate a buffer with character data from the pipe, asynchronously
        /// </summary>
        public override ValueTask<int> ReadBlockAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
            => ReadBlockAsyncImpl(buffer, cancellationToken);

        /// <summary>
        /// Attempt to partially populate a buffer with character data from the pipe
        /// </summary>
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
        /// <summary>
        /// Attempt to fully populate a buffer with character data from the pipe
        /// </summary>
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

        /// <summary>
        /// Attempt to read a line from the pipe
        /// </summary>
        public override string ReadLine() => ReadLineAsync().Result;
        /// <summary>
        /// Attempt to fully populate a buffer with character data from the pipe
        /// </summary>
        public override int ReadBlock(char[] buffer, int index, int count) => ReadBlockAsync(buffer, index, count).Result;
        /// <summary>
        /// Attempt to partially populate a buffer with character data from the pipe
        /// </summary>
        public override int Read(char[] buffer, int index, int count) => ReadAsync(buffer, index, count).Result;
        /// <summary>
        /// Read to the end of the pipe
        /// </summary>
        public override string ReadToEnd() => ReadToEndAsync().Result;
        /// <summary>
        /// Read a single character from the pipe
        /// </summary>
        public override int Read() => ReadSingleChar(false);
        /// <summary>
        /// Read a single character from the pipe without consuming it
        /// </summary>
        public override int Peek() => ReadSingleChar(true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadSingleChar(bool peek)
            => ReadSingleCharFast(peek) ?? ReadSingleCharSlow(peek);

        private int? ReadSingleCharFast(bool peek)
        {
            if (!_reader.TryRead(out var result)) return null;
            
            var buffer = result.Buffer;
            if (NeedPrefixCheck)
            {
                if (!CheckPrefix(ref buffer) && !result.IsCompleted)
                {   // not enough data
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                    return null;
                }
            }
            Span<char> chars = stackalloc char[1];
            var decoder = GetDecoder();
            if(buffer.IsSingleSegment)
            {
                decoder.Convert(buffer.First.Span, chars, false, out int bytesUsed, out int charsUsed, out _);
                if(charsUsed == 1)
                {
                    _reader.AdvanceTo(peek ? buffer.Start : buffer.GetPosition(bytesUsed), buffer.End);
                    return chars[0];
                }
            }
            else
            {
                int totalBytes = 0;
                foreach(var segment in buffer)
                {
                    decoder.Convert(segment.Span, chars, false, out int bytesUsed, out int charsUsed, out _);
                    totalBytes += bytesUsed;
                    if (charsUsed == 1)
                    {
                        _reader.AdvanceTo(peek ? buffer.Start : buffer.GetPosition(totalBytes), buffer.End);
                        return chars[0];
                    }
                }
            }
            _reader.AdvanceTo(buffer.Start, buffer.End);
            return null;
        }
        private int ReadSingleCharSlow(bool peek)
        {
            var arr = ArrayPool<char>.Shared.Rent(1);
            int bytes = ReadAsyncImplAwaited(new Memory<char>(arr, 0, 1), default, peek).Result;
            var finalResult = bytes <= 0 ? -1 : arr[0];
            ArrayPool<char>.Shared.Return(arr);
            return finalResult;
        }
        private string ConsumeString(in ReadOnlySequence<byte> buffer)
        {
            var s = GetString(buffer, _encoding, _decoder, false);
            _reader.AdvanceTo(buffer.End);
            return s;
        }

        private static int GetCharCount(in ReadOnlySequence<byte> buffer, Encoding encoding, ref Decoder decoder)
        {
            checked
            {
                if (encoding.IsSingleByte) return (int)buffer.Length;
                if (encoding is UnicodeEncoding) return (int)(buffer.Length >> 1);
                if (encoding is UTF32Encoding) return (int)(buffer.Length >> 2);
            }

            if (buffer.IsSingleSegment)
            {
                var span = buffer.First.Span;
                return span.IsEmpty ? 0 : encoding.GetCharCount(span);
            }

            int charCount = 0;
            if (decoder == null) decoder = encoding.GetDecoder();
            else decoder.Reset();
            foreach (var segment in buffer)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;
                charCount += decoder.GetCharCount(span, false);
            }
            return charCount;

        }
        private static string GetString(in ReadOnlySequence<byte> buffer, Encoding encoding, Decoder decoder, bool consumeEntireBuffer)
        {
            if (buffer.IsSingleSegment)
            {
                var span = buffer.First.Span;
                if (span.IsEmpty) return "";
                return encoding.GetString(span);
            }

            var charCount = GetCharCount(in buffer, encoding, ref decoder);
            var s = GetString(in buffer, charCount, out int actualCharCount, encoding, decoder, consumeEntireBuffer);
            Debug.Assert(actualCharCount == charCount);
            return s;
        }
        static string GetString(in ReadOnlySequence<byte> buffer, int charCount, out int totalBytes, Encoding encoding, Decoder decoder, bool consumeEntireBuffer)
        {
            if (charCount == 0)
            {
                totalBytes = 0;
                return "";
            }
            string s = new string((char)0, charCount);
            var chars = MemoryMarshal.AsMemory(s.AsMemory()).Span;
            totalBytes = GetString(in buffer, chars, out int actualChars, encoding, decoder, consumeEntireBuffer);
            Debug.Assert(actualChars == charCount);
            return s;
        }
        static int GetString(in ReadOnlySequence<byte> buffer, Span<char> chars, out int charsRead, Encoding encoding, Decoder decoder, bool consumeEntireBuffer)
        {
            if (chars.IsEmpty)
            {
                charsRead = 0;
                return 0;
            }

            // see if we can do this without touching a decoder
            if (consumeEntireBuffer && buffer.IsSingleSegment && decoder == null)
            {
                var bytes = buffer.First.Span;
                // we need to be sure we have enough space in the output buffer
                if (chars.Length >= encoding.GetMaxCharCount(bytes.Length)   // worst-case is fine
                    || chars.Length >= encoding.GetCharCount(bytes))         // *actually* fine
                {
                    charsRead = encoding.GetChars(bytes, chars);
                    return bytes.Length;
                }
            }
            if (decoder == null) decoder = encoding.GetDecoder();
            else decoder.Reset();

            int totalBytes = 0;
            charsRead = 0;
            bool isComplete = true;
            foreach (var segment in buffer)
            {
                var bytes = segment.Span;
                if (bytes.IsEmpty) continue;
                decoder.Convert(bytes, chars, false, out var bytesUsed, out var charsUsed, out isComplete);
                totalBytes += bytesUsed;
                charsRead += charsUsed;
                chars = chars.Slice(charsUsed);
                if (chars.IsEmpty) break;
            }
            if (consumeEntireBuffer)
            {
                if (!isComplete || totalBytes != buffer.Length)
                {
                    throw new InvalidOperationException("Incomplete decoding frame");
                }
            }
            return totalBytes;
        }

        /// <summary>
        /// Decode a buffer as a string using the provided encoding
        /// </summary>
        public static string ReadString(in ReadOnlySequence<byte> buffer, Encoding encoding)
        {
            if (encoding == null) throw new ArgumentNullException(nameof(encoding));
            return GetString(in buffer, encoding, null, true);
        }
    }
}
