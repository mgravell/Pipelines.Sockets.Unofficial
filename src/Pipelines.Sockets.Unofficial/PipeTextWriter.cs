using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public sealed class PipeTextWriter : TextWriter
    {
        private readonly PipeWriter _writer;
        private readonly Encoding _encoding;
        private readonly Encoder _encoder;

        private Encoder GetEncoder()
        {
            _encoder.Reset();
            return _encoder;
        }
        public PipeTextWriter(PipeWriter writer, Encoding encoding)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _encoder = encoding.GetEncoder();
        }
        public override Encoding Encoding => _encoding;
        public override Task WriteLineAsync(string value)
        {
            WriteImpl(value.AsSpan());
            WriteImpl(Environment.NewLine.AsSpan());
            return _writer.FlushAsync().AsTask();
        }
        public override Task WriteAsync(string value)
        {
            WriteImpl(value.AsSpan());
            return _writer.FlushAsync().AsTask();
        }
        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            return _writer.FlushAsync().AsTask();
        }
        public override Task WriteAsync(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            return _writer.FlushAsync().AsTask();
        }
        public override Task WriteLineAsync()
        {
            WriteImpl(Environment.NewLine.AsSpan());
            return _writer.FlushAsync().AsTask();
        }
        public override Task WriteLineAsync(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            WriteImpl(Environment.NewLine.AsSpan());
            return _writer.FlushAsync().AsTask();
        }
        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            WriteImpl(Environment.NewLine.AsSpan());
            return _writer.FlushAsync().AsTask();
        }

#if SOCKET_STREAM_BUFFERS

        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteImpl(buffer.Span);
            return _writer.FlushAsync(cancellationToken).AsTask();
        }
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteImpl(buffer.Span);
            WriteImpl(Environment.NewLine.AsSpan());
            return _writer.FlushAsync(cancellationToken).AsTask();
        }
#endif
        private void WriteImpl(ReadOnlySpan<char> chars)
        {
            if (chars.IsEmpty) return;

            var encoder = GetEncoder();
            bool completed;
            do
            {
                var bytes = _writer.GetSpan(10);
                encoder.Convert(chars, bytes, false, out int charsUsed, out int bytesUsed, out completed);
                _writer.Advance(bytesUsed);
                chars = chars.Slice(charsUsed);
            }
            while (!chars.IsEmpty);
            if (!completed)
            {
                var bytes = _writer.GetSpan(10);
                encoder.Convert(chars, bytes, true, out int charsUsed, out int bytesUsed, out completed);
                _writer.Advance(bytesUsed);
                Debug.Assert(completed);
            }
        }
        public override Task FlushAsync() => _writer.FlushAsync().AsTask();
    }
}
