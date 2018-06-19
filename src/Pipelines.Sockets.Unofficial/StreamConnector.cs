using System.IO;
using System.IO.Pipelines;

namespace Pipelines.Sockets.Unofficial
{
    static partial class StreamConnector
    {
        public static IDuplexPipe GetDuplex(Stream stream, PipeOptions pipeOptions = null)
            => new AsyncStreamPipe(stream, pipeOptions, true, true);

        public static PipeReader GetReader(Stream stream, PipeOptions pipeOptions = null)
            => new AsyncStreamPipe(stream, pipeOptions, true, false).Input;

        public static PipeWriter GetWriter(Stream stream, PipeOptions pipeOptions = null)
            => new AsyncStreamPipe(stream, pipeOptions, false, true).Output;
        
        public static Stream GetDuplex(PipeReader reader, PipeWriter writer)
            => new AsyncPipeStream(reader, writer);

        public static Stream GetDuplex(IDuplexPipe pipe)
            => new AsyncPipeStream(pipe.Input, pipe.Output);

        public static Stream GetReader(PipeWriter writer)
            => new AsyncPipeStream(null, writer);

        public static Stream GetWriter(PipeReader reader)
            => new AsyncPipeStream(reader, null);
        
    }

}
