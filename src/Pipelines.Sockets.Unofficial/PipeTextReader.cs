using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    class PipeTextReader : TextReader
    {
        private readonly bool _closeReader;
        private readonly PipeReader _reader;
        private readonly Encoding _encoding;
        private readonly ReadOnlyMemory<byte> _lineFeed;
        static readonly ReadOnlyMemory<byte> SingleByteLineFeed = new byte[] { (byte)'\n' };
        private SkipPrefix _skipPrefix;

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
            _closeReader = closeReader;

            _lineFeed = encoding.IsSingleByte ? SingleByteLineFeed : encoding.GetBytes("\n");
            _skipPrefix = SkipPrefix.Preamble;
        }

       
        private bool NeedPrefixCheck => _skipPrefix != SkipPrefix.None;
        private bool CheckPrefix(ref ReadOnlySequence<byte> buffer)
        {
            ReadOnlySpan<byte> prefix;
            switch (_skipPrefix)
            {
                case SkipPrefix.None:
                    return true;
                case SkipPrefix.Preamble:
                    prefix = _encoding.Preamble;
                    break;
                case SkipPrefix.LineFeed:
                    prefix = _lineFeed.Span;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected skip prefix: {_skipPrefix}");
            }
            if (prefix.IsEmpty) return true;

            int chk = (int)Math.Min(buffer.Length, prefix.Length);

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
                return true;
            }
            if (prefix.Length == chk)
            {
                // complete match; absorb the bytes
                _skipPrefix = SkipPrefix.None;
                buffer = buffer.Slice(chk);
                return true;
            }
            // partial match; can't say anything more just now
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
            int checkedBytes = 0;
            while (true)
            {
                var result = await _reader.ReadAsync();

                var buffer = result.Buffer;
                if (NeedPrefixCheck) CheckPrefix(ref buffer);
                if (result.IsCanceled) throw new InvalidOperationException();
                if (result.IsCompleted) return ConsumeString(buffer); // that's everything


                int eol = TryFindEndOfLine(in buffer, decoder, out var linefeedBytes);
                if (eol < 0)
                {   // push it back
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                }
                else
                {
                    var s = GetString(buffer.Slice(eol), _encoding, decoder);
                    _reader.AdvanceTo(buffer.GetPosition(eol + linefeedBytes));
                    return s;
                }
            }
        }

        private unsafe static int TryFindEndOfLine(in ReadOnlySequence<byte> buffer,
            Encoding encoding, Decoder decoder, out int linefeedBytes)
        {
            decoder.Reset();
            Span<char> chars = stackalloc char[256];
            int offsetBytes = 0;
            foreach(var segment in buffer)
            {
                var bytes = segment.Span;
                decoder.Convert(bytes, chars, false, out var bytesUsed, out var charsUsed, out _);
                for(int i = 0; i < charsUsed; i++)
                {
                    switch(chars[i])
                    {
                        case '\r':

                            break;
                        case '\n':
                            
                            break;
                    }
                }
                offsetBytes += bytesUsed;
            }
            linefeedBytes = 0;
            return -1;
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
        private ValueTask<int> ReadAsyncImpl(Memory<char> buffer, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
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
            var s = GetString(buffer, _encoding, null);
            _reader.AdvanceTo(buffer.End);
            return s;
        }

        private static
#if !SOCKET_STREAM_BUFFERS
            unsafe
#endif
            string GetString(ReadOnlySequence<byte> buffer, Encoding encoding, Decoder decoder)
        {
            if (buffer.IsSingleSegment)
            {
                var span = buffer.First.Span;
                if (span.IsEmpty) return "";
#if SOCKET_STREAM_BUFFERS
                return encoding.GetString(span);
#else
                fixed (byte* ptr = &span[0])
                {
                    return encoding.GetString(ptr, span.Length);
                }
#endif
            }
            if (decoder == null) decoder = encoding.GetDecoder();
            else decoder.Reset();

            int charCount = 0;
            foreach (var segment in buffer)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;
#if SOCKET_STREAM_BUFFERS
                charCount += decoder.GetCharCount(span, false);
#else
                fixed (byte* bPtr = &span[0])
                {
                    charCount += decoder.GetCharCount(bPtr, span.Length, false);
                }
#endif
            }

            decoder.Reset();
            string s = new string((char)0, charCount);
#if SOCKET_STREAM_BUFFERS
            var resultSpan = MemoryMarshal.AsMemory(s.AsMemory()).Span;
            foreach (var segment in buffer)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;

                var written = decoder.GetChars(span, resultSpan, false);
                span = span.Slice(written);
            }
#else
            fixed (char* sPtr = s)
            {
                var cPtr = sPtr;
                foreach (var segment in buffer)
                {
                    var span = segment.Span;
                    if (span.IsEmpty) continue;

                    fixed (byte* bPtr = &span[0])
                    {
                        var written = decoder.GetChars(bPtr, span.Length, cPtr, charCount, false);
                        cPtr += written;
                        charCount -= written;
                    }
                }
            }
#endif
            return s;
        }
    }
}
