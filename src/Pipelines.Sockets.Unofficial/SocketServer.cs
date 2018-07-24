using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    public abstract class SocketServer : IDisposable
    {
        private Socket _listener;
        public void Listen(
            EndPoint endPoint,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            SocketType socketType = SocketType.Stream,
            ProtocolType protocolType = ProtocolType.Tcp,
            PipeOptions sendOptions = null, PipeOptions receiveOptions = null)
        {
            if (_listener != null) throw new InvalidOperationException("Server is already running");
            Socket listener = new Socket(addressFamily, socketType, protocolType);
            listener.Bind(endPoint);
            listener.Listen(20);

            _listener = listener;
            StartOnScheduler(receiveOptions?.ReaderScheduler, _ => FireAndForget(ListenForConnectionsAsync(
                sendOptions ?? PipeOptions.Default, receiveOptions ?? PipeOptions.Default)), null);

            OnStarted(endPoint);
        }
        public void Stop()
        {
            var socket = _listener;
            _listener = null;
            if (socket != null)
            {
                try { socket.Dispose(); } catch { }
            }
        }
        public void Dispose()
        {
            Stop();
            Dispose(true);
        }
        protected virtual void Dispose(bool disposing) { }
        static void FireAndForget(Task task)
        {
            // make sure that any exception is observed
            if (task == null) return;
            if (task.IsCompleted)
            {
                GC.KeepAlive(task.Exception);
                return;
            }
            task.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        }

        protected SocketServer()
        {
            RunClientAsync = async boxed =>
            {
                var client = (ClientConnection)boxed;
                try
                {
                    await OnClientConnectedAsync(client);
                    try { client.Transport.Input.Complete(); } catch { }
                    try { client.Transport.Output.Complete(); } catch { }
                }
                catch (Exception ex)
                {
                    try { client.Transport.Input.Complete(ex); } catch { }
                    try { client.Transport.Output.Complete(ex); } catch { }
                    OnClientFaulted(in client, ex);
                }
                finally
                {
                    if (client.Transport is IDisposable d)
                    {
                        try { d.Dispose(); } catch { }
                    }
                }
            };
        }
        private readonly Action<object> RunClientAsync;

        private static void StartOnScheduler(PipeScheduler scheduler, Action<object> callback, object state)
        {
            if (scheduler == PipeScheduler.Inline) scheduler = null;
            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }
        private async Task ListenForConnectionsAsync(PipeOptions sendOptions, PipeOptions receiveOptions)
        {
            try
            {
                while (true)
                {
                    var clientSocket = await _listener.AcceptAsync();
                    SocketConnection.SetRecommendedServerOptions(clientSocket);
                    var pipe = SocketConnection.Create(clientSocket, sendOptions, receiveOptions);

                    StartOnScheduler(receiveOptions.ReaderScheduler, RunClientAsync,
                        new ClientConnection(pipe, clientSocket.RemoteEndPoint)); // boxed, but only once per client
                }
            }
            catch (NullReferenceException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { OnServerFaulted(ex); }
        }
        protected virtual void OnServerFaulted(Exception exception) { }
        protected virtual void OnClientFaulted(in ClientConnection client, Exception exception) { }
        protected virtual void OnStarted(EndPoint endPoint) { }
        protected abstract Task OnClientConnectedAsync(in ClientConnection client);

        protected readonly struct ClientConnection
        {
            internal ClientConnection(IDuplexPipe transport, EndPoint remoteEndPoint)
            {
                Transport = transport;
                RemoteEndPoint = remoteEndPoint;
            }
            public IDuplexPipe Transport { get; }
            public EndPoint RemoteEndPoint { get; }
        }
    }
}
