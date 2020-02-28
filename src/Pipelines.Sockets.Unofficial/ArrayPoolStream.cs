using Pipelines.Sockets.Unofficial.Arenas;
using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// ArrayPoolStream is a lot like MemoryStream, except it rents contiguous buffers from the array pool
    /// </summary>
    public sealed class ArrayPoolStream : Stream
    {
        /// <inheritdoc/>
        public override string ToString() => $"{nameof(ArrayPoolStream)} at position {Position} of {Length}";

        private readonly ArrayPool<byte> _pool;

        private long _position, _length;

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public readonly byte[] Array;
            public readonly int Length;

            public Segment(byte[] buffer, Segment? previous)
            {
                Length = buffer.Length;
                if (Length == 0) Throw.InvalidOperation("Expected non-empty buffer");
                Array = buffer;
                Memory = buffer;
                if (previous != null)
                {
                    previous.Next = this;
                    RunningIndex = previous.RunningIndex + previous.Length;
                }
            }

            public Segment? NextSegment => Next as Segment;
        }

        Segment _start, _end, _current;
        int _currentOffset;


        /// <summary>Create a new ArrayPoolStream using the default array pool</summary>
        public ArrayPoolStream() : this(ArrayPool<byte>.Shared) { }

        /// <summary>Create a new ArrayPoolStream using the specified array pool</summary>
        public ArrayPoolStream(ArrayPool<byte> pool)
        {
            _pool = pool ?? ArrayPool<byte>.Shared;
            _start = _end = _current = new Segment(_pool.Rent(128), null);
        }

        /// <inheritdoc/>
        public override bool CanRead => true;
        /// <inheritdoc/>
        public override bool CanWrite => true;
        /// <inheritdoc/>
        public override bool CanTimeout => false;
        /// <inheritdoc/>
        public override bool CanSeek => true;

        /// <inheritdoc/>
        public override long Position
        {
            get => _position;
            set
            {
                
                if (value != _position)
                {
                    if (_position < 0 || _position > _length) Throw.ArgumentOutOfRange(nameof(Position));
                    var seqPos = GetBuffer().GetPosition(value);
                    _current = (Segment)seqPos.GetObject()!;
                    _currentOffset = seqPos.GetInteger();
                    _position = value;
                }
            }
        }

        /// <inheritdoc/>
        public override long Length => _length;

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            if (value < 0) Throw.ArgumentOutOfRange(nameof(value));
            if (value == _length)
            { } // nothing to do
            else if (value < _length) // shrink
            {
                _length = value;
                if (_position > _length) _position = _length; // leave at EOF
            }
            else // grow
            {
                Throw.NotSupported("Cannot expand via SetLength");
            }
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    return Position = offset;
                case SeekOrigin.End:
                    return Position = Length + offset;
                case SeekOrigin.Current:
                    return Position += offset;
                default:
                    Throw.ArgumentOutOfRange(nameof(origin));
                    return default;
            }
        }

        /// <summary>
        /// Exposes the underlying buffer associated with this stream, for the defined length
        /// </summary>
        public bool TryGetBuffer(out ArraySegment<byte> buffer)
        {
            buffer = default;
            var full = GetBuffer();
            return full.IsSingleSegment && MemoryMarshal.TryGetArray(full.First, out buffer);
        }

        /// <summary>
        /// Exposes the underlying buffer associated with this stream, for the defined length
        /// </summary>
        public ReadOnlySequence<byte> GetBuffer()
            => new ReadOnlySequence<byte>(_start, 0, _end, _end.Length).Slice(0, _length);

        /// <inheritdoc/>
        public override void Flush() { }
        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _position = _length = _currentOffset = -1;
                var tmp = _start;
                _start = _end = _current = null!; // after dispose, I don't mind if you see sparks from nullability
                Return(_pool, tmp);
            }
            base.Dispose(disposing);
        }

        static void Return(ArrayPool<byte> pool, Segment? segment)
        {
            while (segment != null)
            {
                var arr = segment.Array;
                if (arr != null) pool.Return(arr);
                segment = segment.NextSegment;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int WriteSpace()
        {
            var available = _current.Length - _currentOffset;
            if (available == 0)
            {
                available = TryMoveToNextBlock();
                if (available == 0) available = AppendBlock();
            }
            return available;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadSpace()
        {
            // what is available in the current buffer? if nothing, look at the next
            var available = _current.Length - _currentOffset;
            if (available == 0) available = TryMoveToNextBlock();

            // but without over-reading the defined length
            if (_position + available > _length)
            {
                available = (int)Math.Min(available, _length - _position);
            }
            return available;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int TryMoveToNextBlock()
        {
            var next = _current.NextSegment;
            if (next == null) return 0;
            _current = next;
            _currentOffset = 0;
            return next.Length;
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int take = Math.Min(ReadSpace(), count);
            Buffer.BlockCopy(_current.Array, _currentOffset, buffer, offset, take);
            Move(take);
            return take;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Move(int by)
        {
            _position += by;
            _currentOffset += by;
            if (_position > _length) _length = _position;
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            int space = WriteSpace();
            if (space >= count)
            {
                Buffer.BlockCopy(buffer, offset, _current.Array, _currentOffset, count);
                Move(count);
            }
            else
            {
                WriteSlow(new ReadOnlySpan<byte>(buffer, offset, count));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSlow(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                var space = WriteSpace();
                var target = new Span<byte>(_current.Array, _currentOffset, space);

                if (space >= buffer.Length)
                {
                    buffer.CopyTo(target);
                    Move(buffer.Length);
                    break;
                }
                else
                {
                    buffer.Slice(0, space).CopyTo(target);
                    Move(space);
                    buffer = buffer.Slice(space);
                }
            }
        }

        private int AppendBlock()
        {
            const int MAX_BLOCK_SIZE = 8 * 1024 * 1024;
            var current = _current;
            if (current.NextSegment != null) Throw.InvalidOperation("shouldn't be appending; already has tail");
            var size = Math.Min(current.Length << 1, MAX_BLOCK_SIZE);
            _end = new Segment(_pool.Rent(size), current);
            return TryMoveToNextBlock();
        }

        /// <inheritdoc/>
        public override int ReadByte()
        {
            if (ReadSpace() != 0)
            {
                var result = _current.Array[_currentOffset];
                Move(1);
                return result;
            }
            else
            {
                return -1;
            }
        }

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            WriteSpace(); // for the side-effects
            _current.Array[_currentOffset] = value;
            Move(1);
        }

        private static readonly Task<int> s_Zero = Task.FromResult<int>(0);

        private Task<int>? _lastSuccessfulReadTask; // buffer successful large reads, since they tend to be repetitive and same-sized (buffers, etc)
        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<int>(cancellationToken);
            try
            {
                int bytes = Read(buffer, offset, count);
                if (bytes == 0) return s_Zero;

                if (_lastSuccessfulReadTask != null && _lastSuccessfulReadTask.Result == bytes) return _lastSuccessfulReadTask;
                return _lastSuccessfulReadTask = Task.FromResult(bytes);
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<int>(cancellationToken);
            try
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        /// <inheritdoc/>
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);

            if (destination is MemoryStream || destination is ArrayPoolStream)
            {
                // write sync
                try
                {
                    CopyTo(destination, bufferSize);
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }
            else
            {
                var available = ReadSpace();
                if (available == 0) return Task.CompletedTask; // nothing to do

                if (_position + available == _length)
                {   // that's everything in one go
                    var task = destination.WriteAsync(_current.Array, _currentOffset, available, cancellationToken);
                    Move(available);
                    return task;
                }
                else
                {
                    return CopyToAsyncImpl(this, destination, cancellationToken);
                }
            }

            static async Task CopyToAsyncImpl(ArrayPoolStream source, Stream destination, CancellationToken cancellationToken)
            {
                int available;
                while ((available = source.ReadSpace()) != 0)
                {
                    var pending = destination.WriteAsync(source._current.Array, source._currentOffset, available, cancellationToken);
                    source.Move(available);
                    await pending.ConfigureAwait(false);
                }
            }
        }

#if SOCKET_STREAM_BUFFERS
        /// <inheritdoc/>
        public override void CopyTo(Stream destination, int bufferSize)
        {
            int available;
            while ((available = ReadSpace()) != 0)
            {
                destination.Write(_current.Array, _currentOffset, available);
                Move(available);
            }
        }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int take = Math.Min(ReadSpace(), buffer.Length);
            new Span<byte>(_current.Array, _currentOffset, take).CopyTo(buffer);
            Move(take);
            return take;
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int space = WriteSpace();
            if (space >= buffer.Length)
            {
                buffer.CopyTo(new Span<byte>(_current.Array, _currentOffset, space));
                Move(buffer.Length);
            }
            else
            {
                WriteSlow(buffer);
            }
        }

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));

            try
            {
                return new ValueTask<int>(Read(buffer.Span));
            }
            catch(Exception ex)
            {
                return new ValueTask<int>(Task.FromException<int>(ex));
            }
        }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return new ValueTask(Task.FromCanceled(cancellationToken));

            try
            {
                Write(buffer.Span);
                return default;
            }
            catch (Exception ex)
            {
                return new ValueTask(Task.FromException<int>(ex));
            }
        }
#endif
    }
}
