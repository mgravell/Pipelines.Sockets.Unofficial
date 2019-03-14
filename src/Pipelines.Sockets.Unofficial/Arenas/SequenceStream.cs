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
    /// <summary>
    /// A Stream backed by a read-only ReadSequence of bytes
    /// </summary>
    public abstract class ReadOnlySequenceStream : Stream
    {
        /// <summary>
        /// Create a ReadOnlySequenceStream based on an existing sequence
        /// </summary>
        public static ReadOnlySequenceStream Create(in ReadOnlySequence<byte> sequence)
            => sequence.IsEmpty ? Empty : new ReadOnlySequenceStreamImpl(sequence);

        /// <summary>
        /// An empty stream
        /// </summary>
        public static ReadOnlySequenceStream Empty { get; } = new ReadOnlySequenceStreamImpl(default);

        /// <summary>
        /// Gets the underlying buffer associated with this stream
        /// </summary>
        public ReadOnlySequence<byte> GetBuffer() => GetReadOnlyBuffer();

        private protected abstract ReadOnlySequence<byte> GetReadOnlyBuffer();

        /// <summary>
        /// See Stream.CanRead
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// See Stream.CanSeek
        /// </summary>
        public override bool CanSeek => true;

        /// <summary>
        /// See Stream.FlushAsync
        /// </summary>
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// See Stream.Flush
        /// </summary>
        public override void Flush() { }

        /// <summary>
        /// See Stream.CanTimeout
        /// </summary>
        public override bool CanTimeout => false;

        /// <summary>
        /// See Stream.Seek
        /// </summary>
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
                    Position = Length + offset;
                    break;
                default:
                    Throw.ArgumentOutOfRange(nameof(origin));
                    break;
            }
            return Position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected static void CopySegment(in ReadOnlyMemory<byte> buffer, Stream destination)
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

        private protected static void CopyTo(in ReadOnlySequence<byte> sequence, Stream destination)
        {
            if (sequence.IsSingleSegment)
            {
                CopySegment(sequence.First, destination);
            }
            else
            {
                foreach (var segment in sequence)
                {
                    CopySegment(segment, destination);
                }
            }
        }
    }

    /// <summary>
    /// A Stream backed by a read-write Sequence of bytes
    /// </summary>
    public abstract class SequenceStream : ReadOnlySequenceStream
    {
        /// <summary>
        /// Create a new dynamically expandable SequenceStream
        /// </summary>
        /// <returns></returns>
        public static SequenceStream Create(long minCapacity = -1, long maxCapacity = -1)
            => maxCapacity == 0 ? s_empty : new SequenceStreamImpl(minCapacity, maxCapacity);

        /// <summary>
        /// Create a new SequenceStream based on an existing sequence; it will not be possible to expand the stream past the initial capacity
        /// </summary>
        public static SequenceStream Create(in Sequence<byte> sequence)
            => sequence.IsEmpty ? s_empty : new SequenceStreamImpl(sequence);

        private static readonly SequenceStreamImpl s_empty = new SequenceStreamImpl(0, 0);

        /// <summary>
        /// Gets the underlying buffer associated with this stream
        /// </summary>
        public new Sequence<byte> GetBuffer() => GetReadWriteBuffer();

        private protected sealed override ReadOnlySequence<byte> GetReadOnlyBuffer() => GetReadWriteBuffer();

        private protected abstract Sequence<byte> GetReadWriteBuffer();

        /// <summary>
        /// Release any unnecessary sequence segments
        /// </summary>
        public abstract void Trim();
    }

    internal sealed class SequenceStreamImpl : SequenceStream, IDisposable
    {
        private class LeasedSegment : SequenceSegment<byte>
        {
            internal static LeasedSegment Create(int minimumSize, LeasedSegment previous)
            {
                var arr = ArrayPool<byte>.Shared.Rent(minimumSize);
                return new LeasedSegment(arr, previous);
            }

            private LeasedSegment(byte[] array, LeasedSegment previous) : base(array, previous) { }

            public void Dispose()
            {
                if (MemoryMarshal.TryGetArray<byte>(ResetMemory(), out var segment))
                    ArrayPool<byte>.Shared.Return(segment.Array);
            }

            public new LeasedSegment Next
                => (LeasedSegment)((ReadOnlySequenceSegment<byte>)this).Next;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                var segment = _sequence.Start.GetObject() as LeasedSegment;
                _sequence = default;

                // now release the chain if it was leased
                while (segment != null)
                {
                    var next = segment.Next; // in case Dispose breaks the chain
                    try { segment.Dispose(); } catch { } // best efforts
                    segment = next;
                }
            }
        }

        public override bool CanWrite => true;

        private Sequence<byte> _sequence;
        private long _length, _capacity, _position;
        private readonly long _minCapacity, _maxCapacity;

        internal SequenceStreamImpl(long minCapacity, long maxCapacity)
        {
            _minCapacity = minCapacity;
            _maxCapacity = maxCapacity;
        }

        public SequenceStreamImpl(in Sequence<byte> sequence)
        {
            _length = _minCapacity = _maxCapacity = sequence.Length; // don't allow trim/expand to mess with the sequence
            _sequence = sequence;
        }

        private protected override Sequence<byte> GetReadWriteBuffer()
            => _sequence.Slice(0, _length); // don't over-report, since we don't have to

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
            if (value == _length)
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
                    if (length > _maxCapacity)
                        Throw.InvalidOperation("The stream cannot be expanded");
                    throw new NotImplementedException();
                }
                _length = length;
            }
        }

        public override void Trim()
        {
            if (_capacity <= _minCapacity) return; // can't release anything

            throw new NotImplementedException();
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
                CopyTo(_sequence.Slice(_position, _length - _position), destination);
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
            => CopyTo(_sequence.Slice(_position, _length - _position), destination);
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

    internal sealed class ReadOnlySequenceStreamImpl : ReadOnlySequenceStream
    {
        private readonly ReadOnlySequence<byte> _sequence;
        private readonly long _length;
        private long _position;

        private protected override ReadOnlySequence<byte> GetReadOnlyBuffer() => _sequence;

        internal ReadOnlySequenceStreamImpl(in ReadOnlySequence<byte> sequence)
        {
            _sequence = sequence;
            _length = sequence.Length;
        }

        public override bool CanWrite => false;

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

        public override int Read(byte[] buffer, int offset, int count)
            => ReadSpan(new Span<byte>(buffer, offset, count));

        public override int ReadByte()
        {
            Span<byte> span = stackalloc byte[1];
            return ReadSpan(span) <= 0 ? -1 : span[0];
        }

        private int ReadSpan(Span<byte> buffer)
        {
            int bytes = (int)Math.Min(buffer.Length, _length - _position);
            if (bytes <= 0) return 0;
            _sequence.Slice(_position, bytes).CopyTo(buffer);
            _position += bytes;
            return bytes;
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



        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            try
            {
                CopyTo(_sequence.Slice(_position), destination);
                return Task.CompletedTask;
            }
            catch (Exception ex)
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