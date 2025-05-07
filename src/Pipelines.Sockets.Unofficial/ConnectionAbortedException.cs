using System;
using System.Runtime.Serialization;

namespace Pipelines.Sockets.Unofficial
{
#pragma warning disable RCS1194 // exception constructors
    /// <summary>
    /// Indicates that a connection was aborted
    /// </summary>
    [Serializable]
    public sealed class ConnectionAbortedException : OperationCanceledException
    {
        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException() : this("The connection was aborted") { }

        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException(string message) : base(message) { }

        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException(string message, Exception inner) : base(message, inner) { }

#pragma warning disable SYSLIB0051 // Type or member is obsolete
        private ConnectionAbortedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
#pragma warning restore SYSLIB0051 // Type or member is obsolete
    }
}
