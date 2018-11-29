using System.IO.Pipelines;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// A duplex pipe that measures the bytes sent/received
    /// </summary>
    public interface IMeasuredDuplexPipe : IDuplexPipe
    {
        /// <summary>
        /// The total number of bytes sent to the pipe
        /// </summary>
        long TotalBytesSent { get; }

        /// <summary>
        /// The total number of bytes received by the pipe
        /// </summary>
        long TotalBytesReceived { get; }
    }
}
