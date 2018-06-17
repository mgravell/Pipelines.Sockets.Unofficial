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
        /// Use a dedicated reader thread with all-synchronous IO
        /// </summary>
        SyncReader = 1 << 1,
        /// <summary>
        /// Use a dedicated writer thread with all-synchronous IO
        /// </summary>
        SyncWriter = 1 << 2,
    }
    partial class SocketConnection
    {
        private SocketConnectionOptions SocketConnectionOptions { get; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasFlag(SocketConnectionOptions option) => (option & SocketConnectionOptions) != 0;
        
        private bool ZeroLengthReads
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasFlag(SocketConnectionOptions.ZeroLengthReads);
        }
        
    }
}
