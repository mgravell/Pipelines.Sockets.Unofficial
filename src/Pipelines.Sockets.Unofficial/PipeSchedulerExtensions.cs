using System;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// Extension methods for PipeScheduler
    /// </summary>
    public static class PipeSchedulerExtensions
    {
        /// <summary>
        /// Asynchronously yield to the pipe scheduler
        /// </summary>
        public static YieldAwaitable Yield(this PipeScheduler scheduler)
            => new YieldAwaitable(scheduler);

        /// <summary>
        /// Enables yielding to a pipe scheduler
        /// </summary>
        public readonly struct YieldAwaitable : INotifyCompletion, ICriticalNotifyCompletion
        {
            private readonly PipeScheduler _scheduler;
            internal YieldAwaitable(PipeScheduler scheduler)
                => _scheduler = scheduler == PipeScheduler.Inline ? null : scheduler;

            /// <summary>
            /// Indicates whether this operation completed synchronously
            /// </summary>
            public bool IsCompleted => _scheduler == null;

            /// <summary>
            /// Gets the awaiter for this operation
            /// </summary>
            public YieldAwaitable GetAwaiter() => this;

            /// <summary>
            /// Gets the result of this operation
            /// </summary>
            public void GetResult() { }

            private void Schedule(Action continuation)
            {
                if (continuation == null) return;
                if (_scheduler == null)
                {
                    continuation();
                    return;
                }
                _scheduler.Schedule(s_InvokeAction, continuation);
            }

            static readonly Action<object> s_InvokeAction = s => ((Action)s)?.Invoke();

            void ICriticalNotifyCompletion.UnsafeOnCompleted(Action continuation)
                => Schedule(continuation);
            void INotifyCompletion.OnCompleted(Action continuation)
                => Schedule(continuation);
        }
    }
}
