using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    internal sealed class BufferedTextReader : TextReader
    {
        [Conditional("VERBOSE")]
        void DebugLog(string message, [CallerMemberName] string caller = null)
        {
            lock (Utils.DebugLog)
            {
                Utils.DebugLog.WriteLine($"{GetType().Name}/{caller}: {message}");
            }
        }

        private TextReader _source;
        private readonly bool _closeSource;
        private char[] _buffer;
        private int _offset, _remaining;

        private bool ReadMore()
        {
            Debug.Assert(_remaining == 0);
            _offset = 0;
            _remaining = _source.Read(_buffer, 0, _buffer.Length);
            return _remaining != 0;
        }
        private ValueTask<bool> ReadMoreAsync()
        {
            async ValueTask<bool> Awaited(BufferedTextReader @this, Task<int> ttask)
            {
                @this._remaining = await ttask;
                DebugLog($"{_remaining} chars read asynchronously");
                return @this._remaining != 0;
            }
            Debug.Assert(_remaining == 0);
            _offset = 0;
            DebugLog("Reading...");
            var task = _source.ReadAsync(_buffer, 0, _buffer.Length);
            if (task.IsCompleted)
            {
                _remaining = task.Result;
                DebugLog($"{_remaining} chars read synchronously");
                return new ValueTask<bool>(_remaining != 0);
            }
            return Awaited(this, task);
        }

        public override int Peek()
        {
            if (_remaining == 0 && !ReadMore()) return -1;
            return _buffer[_offset];
        }
        public override int Read()
        {
            if (_remaining == 0 && !ReadMore()) return -1;
            int val = _buffer[_offset++];
            _remaining--;
            return val;
        }
        public override int Read(char[] buffer, int index, int count)
        {
            if (_remaining == 0 && !ReadMore()) return 0;
            return ReadLocal(buffer, ref index, ref count);
        }
        static readonly Task<int> TaskZero = Task.FromResult(0);
        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            static async Task<int> Awaited(ValueTask<bool> task, BufferedTextReader @this, char[] bbuffer, int iindex, int ccount)
            {
                if (!await task) return 0;
                return @this.ReadLocal(bbuffer, ref iindex, ref ccount);
            }
            if (_remaining == 0)
            {
                var more = ReadMoreAsync();
                if (!more.IsCompletedSuccessfully) return Awaited(more, this, buffer, index, count);
                if (!more.Result) return TaskZero;
            }
            return Task.FromResult(ReadLocal(buffer, ref index, ref count));
        }
        private string LocalString()
        {
            if (_remaining == 0) return null;
            var s = new string(_buffer, _offset, _remaining);
            _remaining = 0;
            return s;
        }
        public override string ReadToEnd() => ConcatPreserveNull(LocalString(), _source.ReadToEnd());
        public override Task<string> ReadToEndAsync()
        {
            static async Task<string> Awaited(string llocal, Task<string> rremote) => ConcatPreserveNull(llocal, await rremote);

            string local = LocalString();
            var remote = _source.ReadToEndAsync();
            if (!remote.IsCompleted) return Awaited(local, remote);
            return Task.FromResult(ConcatPreserveNull(local, remote.Result));
        }

        private int ReadLocal(char[] buffer, ref int index, ref int count)
        {
            if (_remaining == 0) return 0;
            var taken = Math.Min(count, _remaining);
            Array.Copy(_buffer, _offset, buffer, index, taken);
            _remaining -= taken;
            _offset += taken;
            count -= taken;
            index += taken;
            return taken;
        }
        public override int ReadBlock(char[] buffer, int index, int count)
        {
            int taken = ReadLocal(buffer, ref index, ref count);

            if (count != 0 && count < _buffer.Length)
            {
                if (!ReadMore()) return taken;
                taken += ReadLocal(buffer, ref index, ref count);
            }
            if (count != 0) taken += _source.ReadBlock(buffer, index, count);
            return taken;
        }

        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
        {
            async Task<int> AwaitedMore(ValueTask<bool> mmore, int ttaken, BufferedTextReader @this, char[] bbuffer, int iindex, int ccount)
            {
                if (!await mmore) return ttaken;
                ttaken += @this.ReadLocal(bbuffer, ref iindex, ref ccount);
                if (count != 0) ttaken += await @this._source.ReadBlockAsync(bbuffer, iindex, ccount);
                return ttaken;
            }

            static async Task<int> AwaitedRead(Task<int> rread, int ttaken) => ttaken + await rread;

            int taken = ReadLocal(buffer, ref index, ref count);

            if (count != 0 && count < _buffer.Length)
            {
                var more = ReadMoreAsync();
                if (!more.IsCompletedSuccessfully) return AwaitedMore(more, taken, this, buffer, index, count);
                if (!more.Result) return Task.FromResult(taken);
                taken += ReadLocal(buffer, ref index, ref count);
            }
            if (count != 0)
            {
                var read = _source.ReadBlockAsync(buffer, index, count);
                if (!read.IsCompleted) return AwaitedRead(read, taken);
                taken += read.Result;
            }
            return Task.FromResult(taken);
        }
        private string ReadLineLocal(out bool trailingLF)
        {
            int offset = _offset;
            trailingLF = false;
            for (int i = 0; i < _remaining; i++)
            {
                switch(_buffer[offset++])
                {
                    case '\r':
                        string s = i == 0 ? "" : new string(_buffer, _offset, i);
                        var taken = i + 1;
                        if(i < _remaining - 1)
                        {
                            if (_buffer[offset] == '\n') taken++;
                        }
                        else
                        {
                            trailingLF = true;
                        }
                        _offset += taken;
                        _remaining -= taken;
                        return s;
                    case '\n':
                        s = i == 0 ? "" : new string(_buffer, _offset, i);
                        taken = i + 1;
                        _offset += taken;
                        _remaining -= taken;
                        return s;
                }
            }
            return null;
        }
        static string ConcatPreserveNull(string x, string y)
        {
            if (x == null) return y;
            if (y == null) return x;
            return x + y;
        }
        public override string ReadLine()
        {
            if (_remaining == 0 && !ReadMore()) return null;
            string s = ReadLineLocal(out var trailingLF);
            if (s != null)
            {
                if (trailingLF && Peek() == '\n') Read();
                return s;
            }
            return ConcatPreserveNull(LocalString(), _source.ReadLine());
        }
        static readonly Task<string> TaskNull = Task.FromResult<string>(null);
        public override Task<string> ReadLineAsync()
        {
            static async Task<string> AwaitedMore(ValueTask<bool> rread, BufferedTextReader @this)
            {
                if (!await rread) return null;
                Debug.Assert(@this._remaining != 0); // so: shouldn't be a constant loop
                return await @this.ReadLineAsync();
            }
            static async Task<string> AwaitedPeek(ValueTask<bool> rread, BufferedTextReader @this, string ss)
            {
                if (await rread && @this.Peek() == '\n') @this.Read();
                return ss;
            }

            static async Task<string> AwaitedRemote(string llocal, Task<string> rremote)
                => ConcatPreserveNull(llocal, await rremote);

            if (_remaining == 0)
            {
                var more = ReadMoreAsync();
                if (!more.IsCompleted) return AwaitedMore(more, this);
                if(!more.Result) return TaskNull;
            }
            var s = ReadLineLocal(out var trailingLF);
            if (s != null)
            {
                if (trailingLF)
                {
                    var read = ReadMoreAsync();
                    if (!read.IsCompleted) return AwaitedPeek(read, this, s);
                    if (read.Result && Peek() == '\n') Read();
                }
                return Task.FromResult(s);
            }
            string local = LocalString();
            Debug.Assert(local != null); // because we checked _remaining at the top
            var remote = _source.ReadLineAsync();
            if (!remote.IsCompleted) return AwaitedRemote(local, remote);
            return Task.FromResult(ConcatPreserveNull(local, remote.Result));            
        }

        internal const int DefaultBufferSize = 1024;
        public BufferedTextReader(TextReader source, int bufferSize = DefaultBufferSize, bool closeSource = true)
        {
            _source = source;
            _buffer = ArrayPool<char>.Shared.Rent(bufferSize);
            _closeSource = closeSource;
        }
        public override void Close()
        {
            if (_closeSource)
            {
                _source?.Close();
                _source?.Dispose();
            }
            _source = null;
            base.Close();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var tmp = _buffer;
                _buffer = null;
                if (tmp != null) ArrayPool<char>.Shared.Return(tmp);
                _source = null;
            }
            base.Dispose(disposing);
        }
#if SOCKET_STREAM_BUFFERS
        public override int Read(Span<char> buffer)
        {
            if (_remaining == 0 && !ReadMore()) return 0;
            return ReadLocal(buffer);
        }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            static async ValueTask<int> Awaited(ValueTask<bool> task, BufferedTextReader @this, Memory<char> bbuffer)
            {
                if (!await task) return 0;
                return @this.ReadLocal(bbuffer.Span);
            }
            if (_remaining == 0)
            {
                var more = ReadMoreAsync();
                if (!more.IsCompletedSuccessfully) return Awaited(more, this, buffer);
                if (!more.Result) return new ValueTask<int>(0);
            }
            return new ValueTask<int>(ReadLocal(buffer.Span));
        }
        public override int ReadBlock(Span<char> buffer)
        {
            int taken = ReadLocal(buffer);
            buffer = buffer.Slice(taken);

            if (!buffer.IsEmpty && buffer.Length < _buffer.Length)
            {
                if (!ReadMore()) return taken;
                taken += ReadLocal(buffer);
                buffer = buffer.Slice(taken);
            }
            if (!buffer.IsEmpty) taken += _source.ReadBlock(buffer);
            return taken;
        }
        public override ValueTask<int> ReadBlockAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            int taken = ReadLocal(buffer.Span);
            return taken == buffer.Length ? new ValueTask<int>(taken)
                : ReadBlockAsyncImpl(taken, buffer.Slice(taken), cancellationToken);
        }
        private async ValueTask<int> ReadBlockAsyncImpl(int taken, Memory<char> buffer, CancellationToken cancellationToken)
        {
            if (!buffer.IsEmpty && buffer.Length < _buffer.Length)
            {
                if (!await ReadMoreAsync()) return taken;
                taken += ReadLocal(buffer.Span);
                buffer = buffer.Slice(taken);
            }
            if (!buffer.IsEmpty) taken += await _source.ReadBlockAsync(buffer, cancellationToken);
            return taken;
        }
#endif
    }
}
