using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    public sealed class SequenceStream : Stream
    {
        private readonly Sequence<byte> _sequence;
        private long _length, _capacity, _position;

        public SequenceStream()
        {
            _sequence = default; // sequence;
        }

        public Sequence<byte> GetBuffer() => _sequence.Slice(0, _length); // don't over-report, since we don't have to

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Flush() { }
        public override bool CanTimeout => false;
        public override void Write(byte[] buffer, int offset, int count) => Throw.NotSupported();
        public override long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position;
            set
            {
                if (_position < 0 | _position > _length) Throw.ArgumentOutOfRange(nameof(Position));
                _position = value;
            }
        }

        public override long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        public override void SetLength(long value)
        {
            if(value == _length)
            {
                // nothing to do
            }
            else if (value < _length)
            {
                // getting smaller; simple
                _position = Math.Min(_position, value);
                _length = value;
            }
            else
            {
                // getting bigger
                var oldLen = _length;
                Expand(value);
                _sequence.Slice(oldLen, value - oldLen).Clear(); // wipe
            }
        }

        private void Expand(long length)
        {
            if (length < _length)
            {
                if (_capacity < length)
                {
                    throw new NotImplementedException();
                }
                _length = length;
            }
        }


        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.End:
                    Position = _length + offset;
                    break;
                default:
                    Throw.ArgumentOutOfRange(nameof(origin));
                    break;
            }
            return Position;
        }
        public override int Read(byte[] buffer, int offset, int count)
            => ReadSpan(new Span<byte>(buffer, offset, count));

        public override int ReadByte()
        {
            Span<byte> span = stackalloc byte[1];
            return ReadSpan(span) <= 0 ? -1 : span[0];
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(Read(buffer, offset, count));
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        private int ReadSpan(Span<byte> buffer)
        {
            int bytes = (int)Math.Min(buffer.Length, _length - _position);
            if (bytes <= 0) return 0;
            _sequence.Slice(_position, bytes).CopyTo(buffer);
            _position += bytes;
            return bytes;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            try
            {
                ReadOnlySequenceStream.CopyTo(_sequence.Slice(_position, _length - _position), destination);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        public override void WriteByte(byte value)
        {
            ReadOnlySpan<byte> span = stackalloc byte[1] { value };
            WriteSpan(span);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            try
            {
                WriteSpan(new Span<byte>(buffer, offset, count));
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        private void WriteSpan(ReadOnlySpan<byte> span)
        {
            if (!span.IsEmpty)
            {
                Expand(_position + span.Length);
                span.CopyTo(_sequence.Slice(_position));
                _position += span.Length;
            }
        }

#if SOCKET_STREAM_BUFFERS
        public override void CopyTo(Stream destination, int bufferSize)
            => ReadOnlySequenceStream.CopyTo(_sequence.Slice(_position, _length - _position), destination);
        public override int Read(Span<byte> buffer) => ReadSpan(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                return new ValueTask<int>(ReadSpan(buffer.Span));
            }
            catch (Exception ex)
            {
                return new ValueTask<int>(Task.FromException<int>(ex));
            }
        }
        public override void Write(ReadOnlySpan<byte> buffer) => WriteSpan(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                WriteSpan(buffer.Span);
                return default;
            }
            catch (Exception ex)
            {
                return new ValueTask(Task.FromException(ex));
            }
        }
#endif
    }

    public sealed class ReadOnlySequenceStream : Stream
    {
        private readonly ReadOnlySequence<byte> _sequence;
        private readonly long _length;
        private long _position;

        public ReadOnlySequenceStream(ReadOnlySequence<byte> sequence)
        {
            _sequence = sequence;
            _length = sequence.Length;
        }

        public ReadOnlySequence<byte> GetBuffer() => _sequence;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Flush() { }
        public override bool CanTimeout => false;
        public override void Write(byte[] buffer, int offset, int count) => Throw.NotSupported();
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            Throw.NotSupported();
            return default;
        }
        public override void EndWrite(IAsyncResult asyncResult)
            => Throw.NotSupported();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Throw.NotSupported();
            return default;
        }
        public override void WriteByte(byte value) => Throw.NotSupported();
        public override long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position;
            set
            {
                if (_position < 0 | _position > _length) Throw.ArgumentOutOfRange(nameof(Position));
                _position = value;
            }
        }

        public override long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        public override void SetLength(long value)
        {
            if (value != _length) Throw.NotSupported();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.End:
                    Position = _length + offset;
                    break;
                default:
                    Throw.ArgumentOutOfRange(nameof(origin));
                    break;
            }
            return Position;
        }
        public override int Read(byte[] buffer, int offset, int count)
            => ReadSpan(new Span<byte>(buffer, offset, count));

        public override int ReadByte()
        {
            Span<byte> span = stackalloc byte[1];
            return ReadSpan(span) <= 0 ? -1 : span[0];
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(Read(buffer, offset, count));
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        private int ReadSpan(Span<byte> buffer)
        {
            int bytes = (int)Math.Min(buffer.Length, _length - _position);
            if (bytes <= 0) return 0;
            _sequence.Slice(_position, bytes).CopyTo(buffer);
            _position += bytes;
            return bytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void CopySegment(in ReadOnlyMemory<byte> buffer, Stream destination)
        {
#if SOCKET_STREAM_BUFFERS
            destination.Write(buffer.Span);
#else
            if(MemoryMarshal.TryGetArray(buffer, out var segment))
            {
                destination.Write(segment.Array, segment.Offset, segment.Count);
            }
            else
            {
                var span = buffer.Span;
                var arr = ArrayPool<byte>.Shared.Rent(span.Length);
                span.CopyTo(arr);
                destination.Write(arr, 0, span.Length);
                ArrayPool<byte>.Shared.Return(arr);
            }
#endif
        }

        internal static void CopyTo(in ReadOnlySequence<byte> sequence, Stream destination)
        {
            if (sequence.IsSingleSegment)
            {
                CopySegment(sequence.First, destination);
            }
            else
            {
                foreach(var segment in sequence)
                {
                    CopySegment(segment, destination);
                }
            }
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            try
            {
                CopyTo(_sequence.Slice(_position), destination);
                return Task.CompletedTask;
            }
            catch(Exception ex)
            {
                return Task.FromException(ex);
            }
        }

#if SOCKET_STREAM_BUFFERS
        public override void CopyTo(Stream destination, int bufferSize) => CopyTo(_sequence.Slice(_position), destination);
        public override int Read(Span<byte> buffer) => ReadSpan(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                return new ValueTask<int>(ReadSpan(buffer.Span));
            }
            catch (Exception ex)
            {
                return new ValueTask<int>(Task.FromException<int>(ex));
            }
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Throw.NotSupported();
            return default;
        }
        public override void Write(ReadOnlySpan<byte> buffer) => Throw.NotSupported();
#endif
    }
}