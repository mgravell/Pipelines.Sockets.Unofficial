using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial
{
    partial class SocketConnection
    {
#if DEBUG
        private TextWriter _log;
#endif
        [Conditional("DEBUG")]
        private void DebugLog(string message, [CallerMemberName] string caller = null)
            => _log?.DebugLog(message, caller);

        [Conditional("DEBUG")]
        private static void DebugLog(TextWriter log, string message, [CallerMemberName] string caller = null)
            => log?.DebugLog(message, caller);
    }

}
