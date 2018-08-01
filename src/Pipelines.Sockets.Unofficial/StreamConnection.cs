using System.IO;
using System.IO.Pipelines;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// Provides serves to shim between streams and pipelines
    /// </summary>
    public static partial class StreamConnection
    {
        /// <summary>
        /// Create a duplex pipe that represents the provided stream
        /// </summary>
        public static IDuplexPipe GetDuplex(Stream stream, PipeOptions pipeOptions = null, string name = null)
            => new AsyncStreamPipe(stream, pipeOptions, pipeOptions, true, true, name);

        /// <summary>
        /// Create a duplex pipe that represents the provided stream
        /// </summary>
        public static IDuplexPipe GetDuplex(Stream stream, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions, string name = null)
            => new AsyncStreamPipe(stream, sendPipeOptions, receivePipeOptions, true, true, name);

        /// <summary>
        /// Create a PipeReader that consumes the provided stream
        /// </summary>
        public static PipeReader GetReader(Stream stream, PipeOptions pipeOptions = null, string name = null)
            => new AsyncStreamPipe(stream, null, pipeOptions, true, false, name).Input;

        /// <summary>
        /// Create a PipeWriter feeds the provided stream
        /// </summary>
        public static PipeWriter GetWriter(Stream stream, PipeOptions pipeOptions = null, string name = null)
            => new AsyncStreamPipe(stream, pipeOptions, null, false, true, name).Output;

        /// <summary>
        /// Create a duplex stream that represents the provided reader and writer
        /// </summary>
        public static AsyncPipeStream GetDuplex(PipeReader reader, PipeWriter writer, string name = null)
            => new AsyncPipeStream(reader, writer, name);

        /// <summary>
        /// Create a duplex stream that represents the provided pipe
        /// </summary>
        public static AsyncPipeStream GetDuplex(IDuplexPipe pipe, string name = null)
            => new AsyncPipeStream(pipe.Input, pipe.Output, name);

        /// <summary>
        /// Create a write-only stream that feeds the provided PipeReader
        /// </summary>
        /// <param name="reader">The reader to wrap</param>
        /// <param name="name">The logical name of the reader</param>
        public static Stream GetWriter(PipeWriter writer, string name = null)
            => new AsyncPipeStream(null, writer, name);

        /// <summary>
        /// Create a read-only stream that consumes the provided PipeReader
        /// </summary>
        /// <param name="reader">The reader to wrap</param>
        /// <param name="name">The logical name of the reader</param>
        public static Stream GetReader(PipeReader reader, string name = null)
            => new AsyncPipeStream(reader, null, name);
    }
}
