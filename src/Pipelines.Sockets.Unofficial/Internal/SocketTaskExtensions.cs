#if NET462

using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Internal
{
    internal static class SocketTaskExtensions
    {
        // grungy fallback for an API that evaporated
        public static Task<Socket> AcceptAsync(this Socket socket)
        {
            return Task<Socket>.Factory.FromAsync((callback, state) => ((Socket)state).BeginAccept(callback, state),
                asyncResult => ((Socket)asyncResult.AsyncState).EndAccept(asyncResult),
                state: socket);
        }
    }
}
#endif
