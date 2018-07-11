// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// An awaitable reusable token that is compatible with SocketAsyncEventArgs usage
    /// </summary>
    public sealed class SocketAwaitable : ICriticalNotifyCompletion
    {
        private static void NoOp() { }
        private static readonly Action _callbackCompleted = NoOp; // get better debug info by avoiding inline delegate here

        private Action _callback;
        private int _bytesTransfered;
        private SocketError _error;
        private readonly PipeScheduler _scheduler;

        /// <summary>
        /// Create a new SocketAwaitable instance, optionally providing a callback scheduler
        /// </summary>
        /// <param name="scheduler"></param>
        public SocketAwaitable(PipeScheduler scheduler = null) => _scheduler =
            ReferenceEquals(scheduler, PipeScheduler.Inline) ? null : scheduler;

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

        public static EventHandler<SocketAsyncEventArgs> Callback = (sender,args) => ((SocketAwaitable)args.UserToken).TryComplete(args.BytesTransferred, args.SocketError);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OnCompleted(SocketAsyncEventArgs args)
            => ((SocketAwaitable)args.UserToken).TryComplete(args.BytesTransferred, args.SocketError);

        public bool TryComplete(int bytesTransferred, SocketError socketError)
        {
            var action = Interlocked.Exchange(ref _callback, _callbackCompleted);
            if ((object)action == (object)_callbackCompleted)
            {
                return false;
            }
            _error = socketError;
            _bytesTransfered = bytesTransferred;
            
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
            return true;
        }
        private static void InvokeStateAsActionImpl(object state) => ((Action)state).Invoke();
        internal static readonly Action<object> InvokeStateAsAction = InvokeStateAsActionImpl;
    }
}