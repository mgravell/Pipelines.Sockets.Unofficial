// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    internal sealed class SocketAwaitable : ICriticalNotifyCompletion
    {
        private static void NoOp() { }
        private static readonly Action _callbackCompleted = NoOp; // get better debug info by avoiding inline delegate here

        private Action _callback;
        private int _bytesTransfered;
        private SocketError _error;
        private readonly PipeScheduler _scheduler;

        public SocketAwaitable(PipeScheduler scheduler) => _scheduler = scheduler;

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
                continuation(); // sync completion; don't use scheduler
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
            var action = Interlocked.Exchange(ref _callback, _callbackCompleted);
            if (action == null)
            {
                Helpers.Incr(Counter.SocketAwaitableCallbackNone);
            }
            else
            {
                if (_scheduler == null)
                {
                    Helpers.Incr(Counter.SocketAwaitableCallbackDirect);
                    action();
                }
                else
                {
                    Helpers.Incr(Counter.SocketAwaitableCallbackSchedule);
                    _scheduler.Schedule(InvokeStateAsAction, action);
                }
            }
        }
        private static void InvokeStateAsActionImpl(object state) => ((Action)state).Invoke();
        internal static readonly Action<object> InvokeStateAsAction = InvokeStateAsActionImpl;
    }
}