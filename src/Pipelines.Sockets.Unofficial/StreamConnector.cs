using System.IO;
using System.IO.Pipelines;

namespace Pipelines.Sockets.Unofficial
{
    public static partial class StreamConnector
    {
        public static IDuplexPipe GetDuplex(Stream stream, PipeOptions pipeOptions = null, string name = null)
            => new AsyncStreamPipe(stream, pipeOptions, true, true, name);

        public static PipeReader GetReader(Stream stream, PipeOptions pipeOptions = null, string name = null)
            => new AsyncStreamPipe(stream, pipeOptions, true, false, name).Input;

        public static PipeWriter GetWriter(Stream stream, PipeOptions pipeOptions = null, string name = null)
            => new AsyncStreamPipe(stream, pipeOptions, false, true, name).Output;
        
        public static AsyncPipeStream GetDuplex(PipeReader reader, PipeWriter writer, string name = null)
            => new AsyncPipeStream(reader, writer, name);

        public static AsyncPipeStream GetDuplex(IDuplexPipe pipe, string name = null)
            => new AsyncPipeStream(pipe.Input, pipe.Output, name);

        public static Stream GetReader(PipeWriter writer, string name = null)
            => new AsyncPipeStream(null, writer, name);

        public static Stream GetWriter(PipeReader reader, string name = null)
            => new AsyncPipeStream(reader, null, name);
    }
}
