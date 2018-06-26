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
    /// <summary>
    ///  A TextWriter implementation that pushes to a PipeWriter
    /// </summary>
    public sealed class PipeTextWriter : TextWriter
    {
        private readonly PipeWriter _writer;
        private readonly Encoding _encoding;
        private readonly Encoder _encoder;
        private readonly bool _closeWriter;

        private string _newLine; // the default impl is pretty weird!
        /// <summary>
        /// Gets or sets the line-ending token to use with all 'WriteLine' methods
        /// </summary>
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
        /// <summary>
        /// Create a new instance of a PipeTextWriter
        /// </summary>
        public static TextWriter Create(PipeWriter writer, Encoding encoding, bool writeBOM = false, bool closeWriter = true, bool autoFlush = true)
            => new PipeTextWriter(writer, encoding, writeBOM, closeWriter, autoFlush);

        private bool AutoFlush { get; }

        private PipeTextWriter(PipeWriter writer, Encoding encoding, bool writeBOM, bool closeWriter, bool autoFlush)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            _encoder = encoding.GetEncoder();
            _closeWriter = closeWriter;
            AutoFlush = autoFlush;
            _newLine = Environment.NewLine;

            if(writeBOM)
            {
#if SOCKET_STREAM_BUFFERS
                ReadOnlySpan<byte> preamble = _encoding.Preamble;
#else
                ReadOnlySpan<byte> preamble = _encoding.GetPreamble();
#endif
                if (!preamble.IsEmpty)
                {
                    preamble.CopyTo(writer.GetSpan(preamble.Length));
                    writer.Advance(preamble.Length);
                    // note: no flush; defer the flush until we have something of value
                }
            }
        }
        /// <summary>
        /// Releases all resources associated with the object
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _closeWriter) _writer.Complete();
        }
        /// <summary>
        /// Gets the encoding being used by the writer
        /// </summary>
        public override Encoding Encoding => _encoding;
        /// <summary>
        /// Write a string and line-ending to the pipe, asynchronously
        /// </summary>
        public override Task WriteLineAsync(string value)
        {
            WriteImpl(value.AsSpan());
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        /// <summary>
        /// Write a string and line-ending to the pipe
        /// </summary>
        public override void WriteLine(string value)
        {
            WriteImpl(value.AsSpan());
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a string to the pipe, asynchronously
        /// </summary>
        public override Task WriteAsync(string value)
        {
            WriteImpl(value.AsSpan());
            return FlushAsyncImpl();
        }
        /// <summary>
        /// Write a string to the pipe
        /// </summary>
        public override void Write(string value)
        {
            WriteImpl(value.AsSpan());
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a buffer to the pipe, asynchronously
        /// </summary>
        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            return FlushAsyncImpl();
        }
        /// <summary>
        /// Write a buffer to the pipe
        /// </summary>
        public override void Write(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a character to the pipe, asynchronously
        /// </summary>
        public override Task WriteAsync(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            return FlushAsyncImpl();
        }
        /// <summary>
        /// Write a character to the pipe
        /// </summary>
        public override void Write(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a line-ending to the pipe, asynchronously
        /// </summary>
        public override Task WriteLineAsync()
        {
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        /// <summary>
        /// Write a line-ending to the pipe
        /// </summary>
        public override void WriteLine()
        {
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a character and line-ending to the pipe, asynchronously
        /// </summary>
        public override Task WriteLineAsync(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        /// <summary>
        /// Write a character and line-ending to the pipe
        /// </summary>
        public override void WriteLine(char value)
        {
            Span<char> span = stackalloc char[1];
            span[0] = value;
            WriteImpl(span);
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a buffer and line-ending to the pipe, asynchronously
        /// </summary>
        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl();
        }
        /// <summary>
        /// Write a buffer and line-ending to the pipe
        /// </summary>
        public override void WriteLine(char[] buffer, int index, int count)
        {
            WriteImpl(new ReadOnlySpan<char>(buffer, index, count));
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a buffer to the pipe
        /// </summary>
        public override void Write(char[] buffer)
        {
            WriteImpl(buffer);
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a buffer and line-ending to the pipe
        /// </summary>
        public override void WriteLine(char[] buffer)
        {
            WriteImpl(buffer);
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Task FlushAsyncImpl(bool forced = false, CancellationToken cancellationToken = default)
        {
            if (forced || AutoFlush)
            {
                var flush = _writer.FlushAsync(cancellationToken);
                return flush.IsCompletedSuccessfully ? Task.CompletedTask : flush.AsTask();
            }
            else
            {
                return Task.CompletedTask;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushSyncImpl(bool forced = false)
        {
            if (forced || AutoFlush)
            {
                var flush = _writer.FlushAsync();
                if (!flush.IsCompletedSuccessfully) flush.AsTask().Wait();
            }
        }

#if SOCKET_STREAM_BUFFERS
        /// <summary>
        /// Write a buffer to the pipe, asynchronously
        /// </summary>

        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteImpl(buffer.Span);
            return FlushAsyncImpl(cancellationToken: cancellationToken);
        }
        /// <summary>
        /// Write a buffer and line-ending to the pipe, asynchronously
        /// </summary>
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteImpl(buffer.Span);
            WriteImpl(NewLine.AsSpan());
            return FlushAsyncImpl(cancellationToken: cancellationToken);
        }
        /// <summary>
        /// Write a buffer to the pipe
        /// </summary>
        public override void Write(ReadOnlySpan<char> buffer)
        {
            WriteImpl(buffer);
            FlushSyncImpl();
        }
        /// <summary>
        /// Write a buffer and line-ending to the pipe
        /// </summary>
        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            WriteImpl(buffer);
            WriteImpl(NewLine.AsSpan());
            FlushSyncImpl();
        }
#endif
        private void WriteImpl(ReadOnlySpan<char> chars) => WriteImpl(_writer, chars, _encoding, _encoder);
        private static int WriteImpl(PipeWriter writer, ReadOnlySpan<char> chars, Encoding encoding, Encoder encoder)
        {
            if (chars.IsEmpty) return 0;

            int totalBytesUsed = 0;
            bool completed;
            do
            {
                var bytes = writer.GetSpan(10);

                if (totalBytesUsed == 0) // first span 
                {
                    if (encoder == null) // no encoder? check to see if we can do this without needing to create one
                    {
                        if (bytes.Length >= encoding.GetMaxByteCount(chars.Length))
                        {
                            totalBytesUsed = encoding.GetBytes(chars, bytes);
                            writer.Advance(totalBytesUsed);
                            return totalBytesUsed;
                        }
                        encoder = encoding.GetEncoder();
                    }
                    else
                    {
                        encoder.Reset();
                    }
                }
                
                encoder.Convert(chars, bytes, false, out int charsUsed, out int bytesUsed, out completed);
                Debug.Assert(bytesUsed > 0);
                writer.Advance(bytesUsed);
                totalBytesUsed += bytesUsed;
                chars = chars.Slice(charsUsed);
            }
            while (!chars.IsEmpty);
            if (!completed)
            {
                var bytes = writer.GetSpan(10);
                encoder.Convert(chars, bytes, true, out int charsUsed, out int bytesUsed, out completed);
                Debug.Assert(completed);
                writer.Advance(bytesUsed);
                totalBytesUsed += bytesUsed;

            }
            return totalBytesUsed;
        }
        /// <summary>
        /// Flush the pipe, asynchronously
        /// </summary>
        public override Task FlushAsync() => FlushAsyncImpl(forced: true);
        /// <summary>
        /// Flush the pipe
        /// </summary>
        public override void Flush() => FlushSyncImpl(forced: true);

        /// <summary>
        /// Write a buffer to a pipe in the provided encoding
        /// </summary>
        public static int Write(PipeWriter writer, ReadOnlySpan<char> value, Encoding encoding)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (encoding == null) throw new ArgumentNullException(nameof(encoding));
            if (value.IsEmpty) return 0;
            return WriteImpl(writer, value, encoding, null);

        }
        /// <summary>
        /// Write a string to a pipe in the provided encoding
        /// </summary>
        public static void Write(PipeWriter writer, string value, Encoding encoding) => Write(writer, value.AsSpan(), encoding);
    }
}
