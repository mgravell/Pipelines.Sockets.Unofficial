using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial
{
    // This type is largely similar to the type of the same name in KestrelHttpServer, with some minor tweaks:
    // - when scheduing a callback against an already complete task (semi-synchronous case), prefer to use the io pipe scheduler for onward continuations, not the thread pool
    // - when invoking final continuations, we detect the Inline pipe scheduler and bypass the indirection
    // - the addition of an Abort concept (which invokes any pending continuations, guaranteeing failure)

    /// <summary>
    /// Awaitable SocketAsyncEventArgs, where awaiting the args yields either the BytesTransferred or throws the relevant socket exception
    /// </summary>
    public class SocketAwaitableEventArgs : SocketAsyncEventArgs, ICriticalNotifyCompletion
    {
        /// <summary>
        /// Abort the current async operation (and prevent future operations)
        /// </summary>
        public void Abort(SocketError error = SocketError.OperationAborted)
        {
            if (error == SocketError.Success) Throw.Argument(nameof(error));
            _forcedError = error;
            OnCompleted(this);
        }

        private volatile SocketError _forcedError; // Success = 0, no field init required

        private static readonly Action _callbackCompleted = () => { };

        private readonly PipeScheduler _ioScheduler;

        private Action _callback;

        internal static readonly Action<object> InvokeStateAsAction = state => ((Action)state)();

        /// <summary>
        /// Create a new SocketAwaitableEventArgs instance, optionally providing a scheduler for callbacks
        /// </summary>
        /// <param name="ioScheduler"></param>
        public SocketAwaitableEventArgs(PipeScheduler ioScheduler = null)
        {
            // treat null and Inline interchangeably
            if ((object)ioScheduler == (object)PipeScheduler.Inline) ioScheduler = null;
            _ioScheduler = ioScheduler;
        }

        /// <summary>
        /// Get the awaiter for this instance; used as part of "await"
        /// </summary>
        public SocketAwaitableEventArgs GetAwaiter() => this;

        /// <summary>
        /// Indicates whether the current operation is complete; used as part of "await"
        /// </summary>
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        /// <summary>
        /// Gets the result of the async operation is complete; used as part of "await"
        /// </summary>
        public int GetResult()
        {
            Debug.Assert(ReferenceEquals(_callback, _callbackCompleted));

            _callback = null;

            if (_forcedError != SocketError.Success)
            {
                ThrowSocketException(_forcedError);
            }

            if (SocketError != SocketError.Success)
            {
                ThrowSocketException(SocketError);
            }

            return BytesTransferred;

            static void ThrowSocketException(SocketError e)
            {
                Throw.Socket((int)e);
            }
        }

        /// <summary>
        /// Schedules a continuation for this operation; used as part of "await"
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(Volatile.Read(ref _callback), _callbackCompleted)
                || ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                // this is the rare "kinda already complete" case; push to worker to prevent possible stack dive,
                // but prefer the custom scheduler when possible
                if (_ioScheduler == null)
                {
                    Task.Run(continuation);
                }
                else
                {
                    _ioScheduler.Schedule(InvokeStateAsAction, continuation);
                }
            }
        }

        /// <summary>
        /// Schedules a continuation for this operation; used as part of "await"
        /// </summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        /// <summary>
        /// Marks the operation as complete - this should be invoked whenever a SocketAsyncEventArgs operation returns false
        /// </summary>
        public void Complete()
        {
            OnCompleted(this);
        }

        /// <summary>
        /// Invoked automatically when an operation completes asynchronously
        /// </summary>
        protected override void OnCompleted(SocketAsyncEventArgs e)
        {
            var continuation = Interlocked.Exchange(ref _callback, _callbackCompleted);

            if (continuation != null)
            {
                if (_ioScheduler == null)
                {
                    continuation.Invoke();
                }
                else
                {
                    _ioScheduler.Schedule(InvokeStateAsAction, continuation);
                }
            }
        }
    }
}