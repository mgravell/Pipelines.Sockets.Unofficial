using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
        public bool ZeroLengthReads
        {
            get => HasFlag(ConnectionFlags.ZeroLengthRead);
            set => SetFlag(ConnectionFlags.ZeroLengthRead, value);
        }

        private int _flags;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasFlag(ConnectionFlags flag) => ((ConnectionFlags)Thread.VolatileRead(ref _flags) & flag) == flag;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFlag(ConnectionFlags flag, bool set = true)
        {
            // inherently multi-threaded, so: let's do it properly
            // so we can't lose any changes
            int oldValue, newValue;
            do
            {
                oldValue = Thread.VolatileRead(ref _flags);
                newValue = set ? (oldValue | (int)flag) : (oldValue & ~(int)flag);
            }
            while (oldValue == newValue ||
                Interlocked.CompareExchange(ref _flags, newValue, oldValue) == oldValue);
        }
        [Flags]
        private enum ConnectionFlags
        {
            None = 0,
            ZeroLengthRead = 1 << 0,
        }
    }
}
