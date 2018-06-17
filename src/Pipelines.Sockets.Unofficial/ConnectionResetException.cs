using System;
using System.IO;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// Indicates that a connection was reset
    /// </summary>
    public sealed class ConnectionResetException : IOException
    {
        /// <summary>
        /// Create a new ConnectionResetException instance
        /// </summary>
        public ConnectionResetException(string message) : base(message)
        {
        }
        /// <summary>
        /// Create a new ConnectionResetException instance
        /// </summary>
        public ConnectionResetException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
