using System;
using System.Buffers;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    internal sealed class Stand<T> : IDisposable
    {
        int _taken, _free;
        private IMemoryOwner<T> _block;
        private Memory<T> _memory;

        public int TotalCapacity { get; }
        public int Free => _free;
        public int Taken => _taken;
        public IMemoryOwner<T> Block => _block;

        public Stand(IMemoryOwner<T> block)
        {
            _block = block;
            _memory = _block.Memory;
            TotalCapacity = _memory.Length;
            Reset();
        }

        public Memory<T> Take(long max)
        {
            var start = _taken;
            var length = unchecked((int)Math.Min(max, _free));
            _taken += length;
            _free -= length;
            return _memory.Slice(start, length);
        }
        public Memory<T> Take(int max)
        {
            var start = _taken;
            var length = Math.Min(max, _free);
            _taken += length;
            _free -= length;
            return _memory.Slice(start, length);
        }

        public void Reset()
        {
            _taken = 0;
            _free = TotalCapacity;
        }

        internal Stand<T> Next { get; set; }

        public void Dispose()
        {
            var block = _block;
            _block = null;
            block?.Dispose();
        }
    }
}
