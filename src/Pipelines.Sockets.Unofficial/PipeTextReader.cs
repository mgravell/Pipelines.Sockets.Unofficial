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
            _skipPrefix = SkipPrefix.Preamble;

#if !SOCKET_STREAM_BUFFERS
            _preamble = encoding.GetPreamble();
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
            if (buffer.First.Length <= chk)
            {
                isMatch = buffer.First.Span.Slice(chk).SequenceEqual(prefix.Slice(chk));
            }
            else
            {
                Span<byte> all = stackalloc byte[chk];
                buffer.Slice(0, chk).CopyTo(all);
                isMatch = all.SequenceEqual(prefix.Slice(chk));
            }
            if (!isMatch)
            {
                // failure - partial or complete, it doesn't matter
                _skipPrefix = SkipPrefix.None; // don't check again
                DebugLog($"Mismatch; abandoned prefix");
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
        public override void Close()
        {
            if (_closeReader) _reader.Complete();
            base.Close();
        }
        public override Task<int> ReadAsync(char[] buffer, int index, int count)
            => ReadAsyncImpl(new Memory<char>(buffer, index, count), default).AsTask();

        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
            => ReadBlockAsyncImpl(new Memory<char>(buffer, index, count), default).AsTask();

        public override async Task<string> ReadLineAsync()
        {
            var decoder = _encoding.GetDecoder();
            while (true)
            {
                var result = await _reader.ReadAsync();
                if (result.IsCanceled) throw new InvalidOperationException("Operation cancelled");

                var buffer = result.Buffer;
                var snapshot = buffer;
                try
                {
                    if (NeedPrefixCheck) CheckPrefix(ref buffer);
                    
                    var line = ReadToEndOfLine(ref buffer); // found a line

                    if (line == null)
                    {
                        if (result.IsCompleted) // that's everything
                        {
                            if(buffer.IsEmpty)
                            {
                                DebugLog("EOF, no data, returning null");
                                return null;
                            }
                            DebugLog("EOF, returning trailing data");
                            return ConsumeString(buffer);
                        }
                        DebugLog("No EOL found; awaiting more data");
                        _reader.AdvanceTo(buffer.Start, buffer.End);
                    }
                    else
                    {
                        DebugLog($"{line.Length} characters found; prefix: {_skipPrefix}; remaining buffer: {buffer.Length}");
                        _reader.AdvanceTo(buffer.Start);
                        return line;
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.StackTrace);
                    buffer = snapshot;
                    var line = ReadToEndOfLine(ref buffer); // found a line

                    throw;
                }
            }
        }

        private string ReadToEndOfLine(ref ReadOnlySequence<byte> buffer)
        {
            var decoder = GetDecoder();
            Span<char> chars = stackalloc char[256];
            int offsetBytes = 0, totalChars = 0;
            foreach (var segment in buffer)
            {
                var bytes = segment.Span;
                decoder.Convert(bytes, chars, false, out var bytesUsed, out var charsUsed, out _);
                for (int i = 0; i < charsUsed; i++)
                {
                    switch (chars[i])
                    {
                        case '\r':
                            if (i < charsUsed - 1) return ReadToEndOfLine(ref buffer, totalChars + i,
                                     chars[i + 1] == '\n' ? _crLen + _lfLen : _crLen);

                            // can't determine if there's a LF, so skip it instead
                            _skipPrefix = SkipPrefix.LineFeed;
                            return ReadToEndOfLine(ref buffer, totalChars + i, _crLen);
                        case '\n':
                            return ReadToEndOfLine(ref buffer, totalChars + i, _lfLen);
                    }
                }
                offsetBytes += bytesUsed;
                totalChars += charsUsed;
            }
            return null;
        }

        private string ReadToEndOfLine(ref ReadOnlySequence<byte> buffer, int charCount, int suffixBytes)
        {
            if(charCount == 0)
            {
                buffer = buffer.Slice(suffixBytes);
                return "";
            }
            string s = GetString(in buffer, charCount, out var payloadBytes);
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
        private ValueTask<int> ReadAsyncImpl(Memory<char> chars, CancellationToken cancellationToken)
        {
            async ValueTask<int> Awaited(Memory<char> cchars, CancellationToken ccancellationToken)
            {
                while(true)
                {
                    var result = await _reader.ReadAsync(ccancellationToken);
                    var buffer = result.Buffer;
                    if (NeedPrefixCheck) CheckPrefix(ref buffer);
                    int bytesUsed = GetString(buffer, cchars.Span, out int charsRead);

                    if ((bytesUsed != 0 && charsRead != 0) || result.IsCompleted)
                    {
                        _reader.AdvanceTo(buffer.GetPosition(bytesUsed));
                        return charsRead;
                    }
                    _reader.AdvanceTo(buffer.Start);
                }
            }

            {
                if (_reader.TryRead(out var result))
                {
                    var buffer = result.Buffer;
                    if (NeedPrefixCheck) CheckPrefix(ref buffer);
                    int bytesUsed = GetString(buffer, chars.Span, out int charsRead);

                    if ((bytesUsed != 0 && charsRead != 0) || result.IsCompleted)
                    {
                        _reader.AdvanceTo(buffer.GetPosition(bytesUsed));
                        return new ValueTask<int>(charsRead);
                    }
                    _reader.AdvanceTo(buffer.Start);
                }
                return Awaited(chars, cancellationToken);
            }
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
        public override int Read()
        {
            var arr = ArrayPool<char>.Shared.Rent(1);
            int bytes = ReadAsync(arr, 0, 1).Result;
            var result = bytes <= 0 ? -1 : arr[0];
            ArrayPool<char>.Shared.Return(arr);
            return result;
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
            if(charCount == 0)
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
            if(chars.IsEmpty)
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
