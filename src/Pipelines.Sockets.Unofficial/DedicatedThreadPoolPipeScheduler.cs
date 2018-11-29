using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// An implementation of a pipe-scheduler that uses a dedicated pool of threads, deferring to
    /// the thread-pool if that becomes too backlogged
    /// </summary>
    public sealed class DedicatedThreadPoolPipeScheduler : PipeScheduler, IDisposable
    {
        /// <summary>
        /// Reusable shared scheduler instance
        /// </summary>
        public static DedicatedThreadPoolPipeScheduler Default => StaticContext.Instance;

        private static class StaticContext
        {   // locating here rather than as a static field on DedicatedThreadPoolPipeScheduler so that it isn't instantiated too eagerly
            internal static readonly DedicatedThreadPoolPipeScheduler Instance = new DedicatedThreadPoolPipeScheduler(nameof(Default));
        }

        /// <summary>
        /// The name of the pool
        /// </summary>
        public override string ToString() => Name;

        /// <summary>
        /// The number of workers associated with this pool
        /// </summary>
        public int WorkerCount { get; }

        private int UseThreadPoolQueueLength { get; }

        private ThreadPriority Priority { get; }

        private string Name { get; }

        /// <summary>
        /// Create a new dedicated thread-pool
        /// </summary>
        public DedicatedThreadPoolPipeScheduler(string name = null, int workerCount = 5, int useThreadPoolQueueLength = 10,
            ThreadPriority priority = ThreadPriority.Normal)
        {
            if (workerCount < 0) throw new ArgumentNullException(nameof(workerCount));

            WorkerCount = workerCount;
            UseThreadPoolQueueLength = useThreadPoolQueueLength;
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name.Trim();
            Priority = priority;
            for (int i = 0; i < workerCount; i++)
            {
                StartWorker(i);
            }
        }

        private long _totalServicedByQueue, _totalServicedByPool;

        /// <summary>
        /// The total number of operations serviced by the queue
        /// </summary>
        public long TotalServicedByQueue => Volatile.Read(ref _totalServicedByQueue);

        /// <summary>
        /// The total number of operations that could not be serviced by the queue, but which were sent to the thread-pool instead
        /// </summary>
        public long TotalServicedByPool => Volatile.Read(ref _totalServicedByPool);

        private readonly struct WorkItem
        {
            public readonly Action<object> Action;
            public readonly object State;
            public WorkItem(Action<object> action, object state)
            {
                Action = action;
                State = state;
            }
        }

        private volatile bool _disposed;

        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private void StartWorker(int id)
        {
            var thread = new Thread(ThreadRunWorkLoop)
            {
                Name = $"{Name}:{id}",
                Priority = Priority,
                IsBackground = true
            };
            thread.Start(this);
            Helpers.Incr(Counter.ThreadPoolWorkerStarted);
        }

        /// <summary>
        /// Requests <paramref name="action"/> to be run on scheduler with <paramref name="state"/> being passed in
        /// </summary>
        public override void Schedule(Action<object> action, object state)
        {
            if (action == null) return; // nothing to do
            int queueLength;
            lock (_queue)
            {
                _queue.Enqueue(new WorkItem(action, state));
                if (_availableCount != 0)
                {
                    Monitor.Pulse(_queue); // wake up someone
                }
                queueLength = _queue.Count;
            }

            if (_disposed || queueLength > UseThreadPoolQueueLength)
            {
                Helpers.Incr(Counter.ThreadPoolPushedToMainThreadPool);
                System.Threading.ThreadPool.QueueUserWorkItem(ThreadPoolRunSingleItem, this);
            }
            else
            {
                Helpers.Incr(Counter.ThreadPoolScheduled);
            }
        }

        private static readonly ParameterizedThreadStart ThreadRunWorkLoop = state => ((DedicatedThreadPoolPipeScheduler)state).RunWorkLoop();
        private static readonly WaitCallback ThreadPoolRunSingleItem = state => ((DedicatedThreadPoolPipeScheduler)state).RunSingleItem();

        private int _availableCount;
        /// <summary>
        /// The number of workers currently actively engaged in work
        /// </summary>
        public int AvailableCount => Thread.VolatileRead(ref _availableCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Execute(Action<object> action, object state)
        {
            try
            {
                action(state);
                Helpers.Incr(Counter.ThreadPoolExecuted);
                Helpers.Incr(action == SocketAwaitableEventArgs.InvokeStateAsAction ? ((Action)state).Method : action.Method);
            }
            catch (Exception ex)
            {
                Helpers.DebugLog(Name, ex.Message);
            }
        }

        private void RunSingleItem()
        {
            WorkItem next;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                next = _queue.Dequeue();
            }
            Interlocked.Increment(ref _totalServicedByPool);
            Execute(next.Action, next.State);
        }
        private void RunWorkLoop()
        {
            while (true)
            {
                WorkItem next;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        do
                        {
                            if (_disposed) break;
                            _availableCount++;
                            Monitor.Wait(_queue);
                            _availableCount--;
                        } while (_queue.Count == 0);
                    }
                    if(_queue.Count == 0)
                    {
                        if (_disposed) break;
                        else continue;
                    }
                    next = _queue.Dequeue();
                }
                Interlocked.Increment(ref _totalServicedByQueue);
                Execute(next.Action, next.State);
            }
        }
        /// <summary>
        /// Release the threads associated with this pool; if additional work is requested, it will
        /// be sent to the main thread-pool
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            lock (_queue)
            {
                Monitor.PulseAll(_queue);
            }
        }
    }
}
