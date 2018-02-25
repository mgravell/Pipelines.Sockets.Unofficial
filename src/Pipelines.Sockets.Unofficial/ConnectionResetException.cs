using System;
using System.IO;

namespace Pipelines.Sockets.Unofficial
{

    public sealed class ConnectionResetException : IOException
    {
        public ConnectionResetException(string message) : base(message)
        {
        }

        public ConnectionResetException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
