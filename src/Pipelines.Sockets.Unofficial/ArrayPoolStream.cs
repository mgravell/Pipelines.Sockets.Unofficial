using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
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
        private byte[] _array = Array.Empty<byte>();
        private int _position, _length;

        /// <summary>Create a new ArrayPoolStream using the default array pool</summary>
        public ArrayPoolStream() => _pool = ArrayPool<byte>.Shared;

        /// <summary>Create a new ArrayPoolStream using the specified array pool</summary>
        public ArrayPoolStream(ArrayPool<byte> pool) => _pool = pool ?? ArrayPool<byte>.Shared;

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
                if (_position < 0 || _position > _length) Throw.ArgumentOutOfRange(nameof(Position));
                _position = (int)value;
            }
        }

        /// <inheritdoc/>
        public override long Length => _length;

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            if (value < 0 || value > int.MaxValue) Throw.ArgumentOutOfRange(nameof(value));
            if (value == _length)
            { } // nothing to do
            else if (value < _length) // shrink
            {
                _length = (int)value;
                if (_position > _length) _position = _length; // leave at EOF
            }
            else // grow
            {
                var oldLen = _length;
                ExpandCapacity((int)value);
                new Span<byte>(_array, oldLen, _length).Clear(); // zero the contents when growing this way
            }
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch(origin)
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
            buffer = new ArraySegment<byte>(_array, 0, _length);
            return _length >= 0;
        }

        /// <inheritdoc/>
        public override void Flush() { }
        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _position = _length = -1;
                var arr = _array;
                _array = Array.Empty<byte>();

                if (arr.Length != 0) _pool.Return(arr);
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int take = Math.Min(count, _length - _position);
            Buffer.BlockCopy(_array, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            var positionAfter = _position + count;
            if (positionAfter < 0) Throw.InvalidOperation();
            if (positionAfter > _length) ExpandCapacity(positionAfter);
            Buffer.BlockCopy(buffer, offset, _array, _position, count);
            _position = positionAfter;
        }

        /// <inheritdoc/>
        public override int ReadByte()
            => _position >= _length ? -1 : _array[_position++];

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            if (_position >= _length) ExpandCapacity(_length + 1);
            _array[_position++] = value;
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
            catch(Exception ex)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExpandCapacity(int capacity)
        {
            if (capacity > _array.Length) TakeNewBuffer(capacity);
            _length = capacity;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void TakeNewBuffer(int capacity)
        {
            var oldArr = _array;
            var newArr = _pool.Rent(RoundUp(capacity));
            if (_length != 0) Buffer.BlockCopy(oldArr, 0, newArr, 0, _length);
            _array = newArr;
            if (oldArr.Length != 0) _pool.Return(oldArr);
        }

        internal static int RoundUp(int capacity)
        {
            if (capacity <= 1) return capacity;

            // we need to do this because array-pools stop buffering beyond
            // a certain point, and just give us what we ask for; if we don't
            // apply upwards rounding *ourselves*, then beyond that limit, we
            // end up *constantly* allocating/copying arrays, on each copy

            // note we subtract one because it is easier to round up to the *next* bucket size, and
            // subtracting one guarantees that this will work

            // if we ask for, say, 913; take 1 for 912; that's 0000 0000 0000 0000 0000 0011 1001 0000
            // so lz is 22; 32-22=10, 1 << 10= 1024

            // or for 2: lz of 2-1 is 31, 32-31=1; 1<<1=2
            int limit = 1 << (32 - LeadingZeros(capacity - 1));
            return limit < 0 ? int.MaxValue : limit;

            static int LeadingZeros(int x) // https://stackoverflow.com/questions/10439242/count-leading-zeroes-in-an-int32
            {
#if LZCNT
                if (System.Runtime.Intrinsics.X86.Lzcnt.IsSupported)
                {
                    return (int)System.Runtime.Intrinsics.X86.Lzcnt.LeadingZeroCount((uint)x);
                }
                else
#endif
                {
                    const int numIntBits = sizeof(int) * 8; //compile time constant
                                                            //do the smearing
                    x |= x >> 1;
                    x |= x >> 2;
                    x |= x >> 4;
                    x |= x >> 8;
                    x |= x >> 16;
                    //count the ones
                    x -= x >> 1 & 0x55555555;
                    x = (x >> 2 & 0x33333333) + (x & 0x33333333);
                    x = (x >> 4) + x & 0x0f0f0f0f;
                    x += x >> 8;
                    x += x >> 16;
                    return numIntBits - (x & 0x0000003f); //subtract # of 1s from 32
                }
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
                    destination.Write(_array, _position, _length - _position);
                    _position = _length;
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }
            else
            {
                var task = destination.WriteAsync(_array, _position, _length - _position);
                _position = _length;
                return task;
            }
        }

#if SOCKET_STREAM_BUFFERS
        /// <inheritdoc/>
        public override void CopyTo(Stream destination, int bufferSize)
        {
            destination.Write(_array, _position, _length - _position);
            _position = _length;
        }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int take = Math.Min(buffer.Length, _length - _position);
            new Span<byte>(_array, _position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var positionAfter = _position + buffer.Length;
            if (positionAfter < 0) Throw.InvalidOperation();
            if (positionAfter > _length) ExpandCapacity(positionAfter);
            buffer.CopyTo(new Span<byte>(_array, _position, buffer.Length));
            _position = positionAfter;
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
