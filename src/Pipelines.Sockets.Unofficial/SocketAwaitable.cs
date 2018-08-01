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

        /// <summary>
        /// Gets an awaiter that represents the pending operation
        /// </summary>
        public SocketAwaitable GetAwaiter() => this;

        /// <summary>
        /// Indicates whether the pending operation is complete
        /// </summary>
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        /// <summary>
        /// Gets the result of the pending operation
        /// </summary>
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

        /// <summary>
        /// Schedule a callback to be invoked when the operation completes
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(_callback, _callbackCompleted)
                || ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                continuation(); // sync completion; don't use scheduler
            }
        }

        /// <summary>
        /// Schedule a callback to be invoked when the operation completes
        /// </summary>
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        /// <summary>
        /// Provides a callback suitable for use with SocketAsyncEventArgs where the UserToken is a SocketAwaitable
        /// </summary>
        public static EventHandler<SocketAsyncEventArgs> Callback = (sender,args) => ((SocketAwaitable)args.UserToken).TryComplete(args.BytesTransferred, args.SocketError);

        /// <summary>
        /// Mark the pending operation as complete by reading the state of a SocketAsyncEventArgs instance where the UserToken is a SocketAwaitable
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OnCompleted(SocketAsyncEventArgs args)
            => ((SocketAwaitable)args.UserToken).TryComplete(args.BytesTransferred, args.SocketError);

        /// <summary>
        /// Mark the pending operation as complete by providing the state explicitly
        /// </summary>
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