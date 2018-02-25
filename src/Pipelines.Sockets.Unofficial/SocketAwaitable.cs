// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    internal sealed class SocketAwaitable : ICriticalNotifyCompletion
    {
        private static readonly Action _callbackCompleted = () => { };

        private Action _callback;
        private int _bytesTransfered;
        private SocketError _error;

        public SocketAwaitable GetAwaiter() => this;
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        public int GetResult()
        {
            Debug.Assert(ReferenceEquals(_callback, _callbackCompleted));

            _callback = null;

            if (_error != SocketError.Success)
            {
                throw new SocketException((int)_error);
            }

            return _bytesTransfered;
        }

        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(_callback, _callbackCompleted) ||
                ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                continuation();
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void Complete()
        {
            Interlocked.Exchange(ref _callback, _callbackCompleted)?.Invoke();
        }
        public void Complete(int bytesTransferred, SocketError socketError)
        {
            _error = socketError;
            _bytesTransfered = bytesTransferred;
            Interlocked.Exchange(ref _callback, _callbackCompleted)?.Invoke();
        }
    }
}