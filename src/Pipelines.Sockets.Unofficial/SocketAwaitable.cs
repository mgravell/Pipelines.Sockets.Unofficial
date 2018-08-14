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
        private static readonly Action s_callbackCompleted = NoOp; // get better debug info by avoiding inline delegate here

        private Action _callback; // null if not started, _callbackCompleted if complete, otherwise: pending
        private volatile int _bytesTransfered;
        private volatile SocketError _error;
        private readonly PipeScheduler _scheduler;

        /// <summary>
        /// Create a new SocketAwaitable instance, optionally providing a callback scheduler
        /// </summary>
        /// <param name="scheduler"></param>
        public SocketAwaitable(PipeScheduler scheduler = null)
            => _scheduler = ReferenceEquals(scheduler, PipeScheduler.Inline) ? null : scheduler;

        /// <summary>
        /// Gets an awaiter that represents the pending operation
        /// </summary>
        public SocketAwaitable GetAwaiter() => this;

        /// <summary>
        /// Indicates whether the pending operation is complete
        /// </summary>
        public bool IsCompleted => ReferenceEquals(Volatile.Read(ref _callback), s_callbackCompleted);

        /// <summary>
        /// Gets the result of the pending operation
        /// </summary>
        public int GetResult()
        {
            if (Interlocked.CompareExchange(ref _callback, null, s_callbackCompleted) != s_callbackCompleted)
                ThrowIncomplete();

            var tmp = _error;
            if (tmp != SocketError.Success) ThrowSocketError(tmp);

            return _bytesTransfered;
        }

        /// <summary>
        /// Schedule a callback to be invoked when the operation completes
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            if (continuation == null) ThrowNullContinuation();
            var was = Interlocked.CompareExchange(ref _callback, continuation, null);
            if ((object)was == null)
            {
                // was incomplete, now pending
            }
            else if ((object)was == (object)s_callbackCompleted)
            {
                // was already complete - invoke
                continuation();
            }
            else
            {
                ThrowMultipleContinuations();
            }
        }
        static void ThrowNullContinuation() => throw new ArgumentNullException("continuation");
        static void ThrowMultipleContinuations() => throw new NotSupportedException("Multiple continuations are not supported");
        static void ThrowIncomplete() => throw new InvalidOperationException("Async operation is incomplete");
        static void ThrowSocketError(SocketError error) => throw new SocketException((int)error);

        /// <summary>
        /// Schedule a callback to be invoked when the operation completes
        /// </summary>
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        /// <summary>
        /// Provides a callback suitable for use with SocketAsyncEventArgs where the UserToken is a SocketAwaitable
        /// </summary>
        public static EventHandler<SocketAsyncEventArgs> Callback = (sender, args) => ((SocketAwaitable)args.UserToken).TryComplete(args.BytesTransferred, args.SocketError);

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
            var scheduled = Interlocked.Exchange(ref _callback, s_callbackCompleted);
            if ((object)scheduled == (object)s_callbackCompleted)
            {
                return false;
            }
            _error = socketError;
            _bytesTransfered = bytesTransferred;

            if (scheduled == null)
            {   // not yet scheduled
                Helpers.Incr(Counter.SocketAwaitableCallbackNone);
            }
            else
            {
                if (_scheduler == null)
                {
                    Helpers.Incr(Counter.SocketAwaitableCallbackDirect);
                    scheduled();
                }
                else
                {
                    Helpers.Incr(Counter.SocketAwaitableCallbackSchedule);
                    _scheduler.Schedule(InvokeStateAsAction, scheduled);
                }
            }
            return true;
        }

        private static void InvokeStateAsActionImpl(object state) => ((Action)state).Invoke();
        internal static readonly Action<object> InvokeStateAsAction = InvokeStateAsActionImpl;
    }
}