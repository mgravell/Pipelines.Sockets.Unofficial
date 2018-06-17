using System;

namespace Pipelines.Sockets.Unofficial
{

    /// <summary>
    /// Indicates that a connection was aborted
    /// </summary>
    public sealed class ConnectionAbortedException : OperationCanceledException
    {
        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException() :
            this("The connection was aborted")
        {

        }
        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException(string message) : base(message)
        {
        }
        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
