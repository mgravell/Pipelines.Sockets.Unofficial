using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    internal sealed class TestTextWriter : TextWriter
    {
        public static TestTextWriter Create(ITestOutputHelper log) => log == null ? null : new TestTextWriter(log);
        private readonly ITestOutputHelper _log;
        private readonly TextWriter _text;

        public override Encoding Encoding => Encoding.Unicode;

        public TestTextWriter(ITestOutputHelper log)
        {
            _log = log;
#if DEBUG
            SocketConnection.SetLog(this);
#endif
        }
        public TestTextWriter(TextWriter text)
        {
            _text = text;
#if DEBUG
            SocketConnection.SetLog(text);
#endif
        }
        public override void WriteLine(string value)
        {
            _log?.WriteLine(value);
            _text?.WriteLine(value);
        }


        public void DebugLog(string message, [CallerMemberName] string caller = null)
        {
            var thread = Thread.CurrentThread;
            var name = thread.Name;
            if (string.IsNullOrWhiteSpace(name)) name = thread.ManagedThreadId.ToString();
            
            WriteLine($"[{name}:{caller}] {message}");
        }
    }
}
