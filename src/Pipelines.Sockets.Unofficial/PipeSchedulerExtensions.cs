using System;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial
{
    public static class PipeSchedulerExtensions
    {
        public static YieldAwaitable Yield(this PipeScheduler scheduler)
            => new YieldAwaitable(scheduler);

        public readonly struct YieldAwaitable : INotifyCompletion, ICriticalNotifyCompletion
        {
            private readonly PipeScheduler _scheduler;
            internal YieldAwaitable(PipeScheduler scheduler)
                => _scheduler = scheduler == PipeScheduler.Inline ? null : scheduler;

            public bool IsCompleted => _scheduler == null;

            public YieldAwaitable GetAwaiter() => this;

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
