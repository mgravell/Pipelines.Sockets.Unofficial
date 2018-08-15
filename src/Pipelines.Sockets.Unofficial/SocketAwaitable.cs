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

        private Action _scheduledCallback;
        private int _bytesTransfered;
        private bool _complete;
        private SocketError _error;
        private readonly PipeScheduler _scheduler;

        private object SyncLock => this;
        /// <summary>
        /// Reset the awaitable to a pending state
        /// </summary>
        public void Reset()
        {
            lock (SyncLock)
            {
                _scheduledCallback = null;
                _complete = false;
            }
        }

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
        public bool IsCompleted
        {
            get
            {
                lock (SyncLock) { return _complete; }
            }
        }

        /// <summary>
        /// Gets the result of the pending operation
        /// </summary>
        public int GetResult()
        {
            lock (SyncLock)
            {
                if (!_complete) ThrowIncomplete();

                if (_error != SocketError.Success) ThrowSocketError(_error);
                return _bytesTransfered;
            }
        }

        /// <summary>
        /// Schedule a callback to be invoked when the operation completes
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            if (continuation == null) ThrowNullContinuation();
            lock (SyncLock)
            {
                if (_scheduledCallback != null) ThrowMultipleContinuations();

                if (_complete)
                {
                    continuation();
                }
                else
                {
                    _scheduledCallback = continuation;
                }
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
        /// Reset the awaitable to a pending state
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Reset(SocketAsyncEventArgs args) => ((SocketAwaitable)args.UserToken).Reset();

        /// <summary>
        /// Mark the pending operation as complete by providing the state explicitly
        /// </summary>
        public bool TryComplete(int bytesTransferred, SocketError socketError)
        {
            Action callbackToInvoke;
            lock (SyncLock)
            {
                if (_complete)
                {
                    return false;
                }

                _error = socketError;
                _bytesTransfered = bytesTransferred;
                callbackToInvoke = _scheduledCallback;
                _complete = true;
            }

            if (callbackToInvoke == null)
            {   // not yet scheduled
                Helpers.Incr(Counter.SocketAwaitableCallbackNone);
            }
            else
            {
                if (_scheduler == null)
                {
                    Helpers.Incr(Counter.SocketAwaitableCallbackDirect);
                    callbackToInvoke();
                }
                else
                {
                    Helpers.Incr(Counter.SocketAwaitableCallbackSchedule);
                    _scheduler.Schedule(InvokeStateAsAction, callbackToInvoke);
                }
            }
            return true;
        }

        private static void InvokeStateAsActionImpl(object state) => ((Action)state).Invoke();
        internal static readonly Action<object> InvokeStateAsAction = InvokeStateAsActionImpl;
    }
}