using System;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// Flags that influence the behavior of SocketConnection
    /// </summary>
    [Flags]
    public enum SocketConnectionOptions
    {
        /// <summary>
        /// Default
        /// </summary>
        None = 0,
        /// <summary>
        /// When no data is currently available, perform a zero-length read as a buffer-free wait mechanism
        /// </summary>
        ZeroLengthReads = 1 << 0,

        /// <summary>
        /// During async reads, the awaiter should continue on the IO thread
        /// </summary>
        InlineReads = 1 << 1,

        /// <summary>
        /// During async writes, the awaiter should continue on the IO thread
        /// </summary>
        InlineWrites = 1 << 2,

        /// <summary>
        /// During async connects, the awaiter should continue on the IO thread
        /// </summary>
        InlineConnect = 1 << 3,
    }
    public partial class SocketConnection
    {
        private SocketConnectionOptions SocketConnectionOptions { get; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasFlag(SocketConnectionOptions option) => (option & SocketConnectionOptions) != 0;

        private bool ZeroLengthReads
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasFlag(SocketConnectionOptions.ZeroLengthReads);
        }

        private bool InlineReads
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasFlag(SocketConnectionOptions.InlineReads);
        }

        private bool InlineWrites
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasFlag(SocketConnectionOptions.InlineWrites);
        }
    }
}
