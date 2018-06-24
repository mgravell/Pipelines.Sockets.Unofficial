using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
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
        private readonly bool _closeWriter;

        private string _newLine; // the default impl is pretty weird!
        public override string NewLine
        {
            get => _newLine;
            set => _newLine = value ?? ""; // if someone tries to set to null, assume they meant empty
        }
        private Encoder GetEncoder()
        {
            _encoder.Reset();
            return _encoder;
        }
        public PipeTextWriter(PipeWriter writer, Encoding encoding, bool closeWriter = true)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _encoder = encoding.GetEncoder();
            _closeWriter = closeWriter;
            _newLine = Environment.NewLine;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _closeWriter) _writer.Complete();
        }
        public override Encoding Encoding => _encoding;
        public override Task WriteLineAsync(string value)
        {
            WriteImpl(value.AsSpan());
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        public override void WriteLine(string value)
        {
            WriteImpl(value.AsSpan());
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
        public override Task WriteAsync(string value)
        {
            WriteImpl(value.AsSpan());
            return FlushAsyncImpl();
        }
        public override void Write(string value)
        {
            WriteImpl(value.AsSpan());
            FlushSyncImpl();
        }
        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            return FlushAsyncImpl();
        }
        public override void Write(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            FlushSyncImpl();
        }
        public override Task WriteAsync(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            return FlushAsyncImpl();
        }
        public override void Write(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            FlushSyncImpl();
        }
        public override Task WriteLineAsync()
        {
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        public override void WriteLine()
        {
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
        public override Task WriteLineAsync(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        public override void WriteLine(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        public override void WriteLine(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }

        public override void Write(char[] buffer)
        {
            WriteImpl(buffer);
            FlushSyncImpl();
        }
        public override void WriteLine(char[] buffer)
        {
            WriteImpl(buffer);
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Task FlushAsyncImpl(CancellationToken cancellationToken = default)
        {
            var flush = _writer.FlushAsync(cancellationToken);
            return flush.IsCompletedSuccessfully ? Task.CompletedTask : flush.AsTask();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushSyncImpl()
        {
            var flush = _writer.FlushAsync();
            if (!flush.IsCompletedSuccessfully) flush.AsTask().Wait();
        }

#if SOCKET_STREAM_BUFFERS

        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteImpl(buffer.Span);
            return FlushAsyncImpl(cancellationToken);
        }
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteImpl(buffer.Span);
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl(cancellationToken);
        }
        public override void Write(ReadOnlySpan<char> buffer)
        {
            WriteImpl(buffer);
            FlushSyncImpl();
        }
        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            WriteImpl(buffer);
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
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
        public override Task FlushAsync() => FlushAsyncImpl();
        public override void Flush() => FlushSyncImpl();
    }
}
