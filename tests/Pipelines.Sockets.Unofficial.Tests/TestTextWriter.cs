using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    internal sealed class TestTextWriter : TextWriter
    {
        public static TestTextWriter Create(ITestOutputHelper log) => log == null ? null : new TestTextWriter(log);
        private readonly ITestOutputHelper _log;

        public override Encoding Encoding => Encoding.Unicode;

        private TestTextWriter(ITestOutputHelper log)
        {
            _log = log;
        }
        public override void WriteLine(string value) => _log.WriteLine(value);


        internal void DebugLog(string message, [CallerMemberName] string caller = null)
        {
            WriteLine("[" + caller + "] " + message);
        }
    }
}
