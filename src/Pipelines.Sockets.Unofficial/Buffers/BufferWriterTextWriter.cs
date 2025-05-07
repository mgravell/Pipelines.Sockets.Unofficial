using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Buffers
{
    /// <summary>
    /// Implements a <see cref="TextWriter"/> over an <see cref="IBufferWriter{T}"/>
    /// </summary>
    public sealed class BufferWriterTextWriter : TextWriter
    {
        /// <summary>
        /// Creates a new instance
        /// </summary>
        public static TextWriter Create(IBufferWriter<byte> output, Encoding encoding = null)
        {
            encoding ??= Encoding.UTF8;
            if (output is null) Throw.ArgumentNull(nameof(output));
            return new BufferWriterTextWriter(output, encoding);
        }

        private readonly IBufferWriter<byte> _output;
        private readonly Encoding _encoding;

        private BufferWriterTextWriter(IBufferWriter<byte> output, Encoding encoding)
        {
            _output = output;
            _encoding = encoding;
        }

        /// <inheritdoc/>
        public override Encoding Encoding => _encoding;

        /// <inheritdoc/>
        public override void Write(string value)
            => WriteCore(value.AsSpan());
        /// <inheritdoc/>
        public override void WriteLine(string value)
        {
            WriteCore(value.AsSpan());
            WriteCore(CoreNewLine);
        }
        /// <inheritdoc/>
        public override void Write(char[] buffer)
            => WriteCore(buffer);
        /// <inheritdoc/>
        public override void Write(char value)
        {
            ReadOnlySpan<char> ros = stackalloc char[] { value };
            WriteCore(ros);
        }
        /// <inheritdoc/>
        public override void WriteLine()
            => WriteCore(CoreNewLine);
        /// <inheritdoc/>
        public override void WriteLine(char value)
        {
            ReadOnlySpan<char> ros = stackalloc char[] { value };
            WriteCore(ros);
            WriteCore(CoreNewLine);
        }
        /// <inheritdoc/>
        public override Task WriteLineAsync()
        {
            WriteCore(CoreNewLine);
            return Task.CompletedTask;
        }
        /// <inheritdoc/>
        public override Task WriteLineAsync(char value)
        {
            ReadOnlySpan<char> ros = stackalloc char[] { value };
            WriteCore(ros);
            WriteCore(CoreNewLine);
            return Task.CompletedTask;
        }
        /// <inheritdoc/>
        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            WriteCore(new ReadOnlySpan<char>(buffer, index, count));
            WriteCore(CoreNewLine);
            return Task.CompletedTask;
        }
        /// <inheritdoc/>
        public override void Write(char[] buffer, int index, int count)
            => WriteCore(new ReadOnlySpan<char>(buffer, index, count));
        /// <inheritdoc/>
        public override Task WriteLineAsync(string value)
        {
            WriteCore(value.AsSpan());
            WriteCore(CoreNewLine);
            return Task.CompletedTask;
        }
        /// <inheritdoc/>
        public override Task WriteAsync(string value)
        {
            WriteCore(value.AsSpan());
            return Task.CompletedTask;
        }
        /// <inheritdoc/>
        public override void WriteLine(char[] buffer)
        {
            WriteCore(buffer);
            WriteCore(CoreNewLine);
        }
        /// <inheritdoc/>
        public override void WriteLine(char[] buffer, int index, int count)
        {
            WriteCore(new ReadOnlySpan<char>(buffer, index, count));
            WriteCore(CoreNewLine);
        }
        /// <inheritdoc/>
        public override void Flush() { }
        /// <inheritdoc/>
        public override Task FlushAsync()
            => Task.CompletedTask;
        /// <inheritdoc/>
        public override Task WriteAsync(char value)
        {
            ReadOnlySpan<char> ros = stackalloc char[] { value };
            WriteCore(ros);
            return Task.CompletedTask;
        }
        /// <inheritdoc/>
        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            WriteCore(new ReadOnlySpan<char>(buffer, index, count));
            return Task.CompletedTask;
        }


        private void WriteCore(ReadOnlySpan<char> value)
        {
            if (!value.IsEmpty)
            {
                int maxLength = _encoding.GetMaxByteCount(value.Length);
                var span = _output.GetSpan(1);
                if (span.Length >= maxLength)
                {
                    _output.Advance(_encoding.GetBytes(value, span));
                }
                else
                {
                    WriteFallback(value, maxLength);
                }
            }
        }

        private void WriteFallback(ReadOnlySpan<char> value, int byteCount)
        {
            if (byteCount <= 512)
            {
                Span<byte> stack = stackalloc byte[byteCount];
                byteCount = _encoding.GetBytes(value, stack);
                _output.Write(stack.Slice(0, byteCount));
            }
            else
            {
                var arr = ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    Span<byte> bytes = arr;
                    byteCount = _encoding.GetBytes(value, bytes);
                    _output.Write(bytes.Slice(0, byteCount));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(arr);
                }
            }
        }

#if SOCKET_STREAM_BUFFERS

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<char> buffer)
            => WriteCore(buffer);
        /// <inheritdoc/>
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteCore(buffer.Span);
            WriteCore(CoreNewLine);
            return Task.CompletedTask;
        }
        /// <inheritdoc/>
        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            WriteCore(buffer);
            WriteCore(CoreNewLine);
        }
        /// <inheritdoc/>
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            WriteCore(buffer.Span);
            return Task.CompletedTask;
        }
#endif
    }
}
