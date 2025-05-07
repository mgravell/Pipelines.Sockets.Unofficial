using System;
using System.IO;
using System.Runtime.Serialization;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// Indicates that a connection was reset
    /// </summary>
    [Serializable]
    public sealed class ConnectionResetException : IOException
    {
        /// <summary>
        /// Create a new ConnectionResetException instance
        /// </summary>
        public ConnectionResetException() : this("The connection was reset") { }

        /// <summary>
        /// Create a new ConnectionResetException instance
        /// </summary>
        public ConnectionResetException(string message) : base(message) { }
        /// <summary>
        /// Create a new ConnectionResetException instance
        /// </summary>
        public ConnectionResetException(string message, Exception inner) : base(message, inner) { }

#pragma warning disable SYSLIB0051 // Type or member is obsolete
        private ConnectionResetException(SerializationInfo info, StreamingContext context) : base(info, context) { }
#pragma warning restore SYSLIB0051 // Type or member is obsolete
    }
}
