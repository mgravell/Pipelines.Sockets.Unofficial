using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// The name of the pool
        /// </summary>
        public override string ToString() => Name;
        private int MaxThreads { get; }
        
        private int MinThreads { get; }

        private int UseThreadPoolQueueLength { get; }
       
        private ThreadPriority Priority { get; }

        private string Name { get; }
        /// <summary>
        /// Create a new dedicated thread-pool
        /// </summary>
        public DedicatedThreadPoolPipeScheduler(string name = null, int minThreads = 1, int maxThreads = 5, int useThreadPoolQueueLength = 10,
            ThreadPriority priority = ThreadPriority.Normal)
        {
            if (minThreads < 0) throw new ArgumentNullException(nameof(minThreads));
            if (minThreads < maxThreads) minThreads = maxThreads;
            MinThreads = minThreads;
            MaxThreads = maxThreads;
            UseThreadPoolQueueLength = useThreadPoolQueueLength;
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name.Trim();
            for (int i = 0; i < minThreads; i++)
            {
                StartWorker();
            }
        }

        private volatile bool _disposed;
        private readonly object ThreadSyncLock = new object();
        private int _threadCount;
        readonly Queue<(Action<object> action, object state)> _queue = new Queue<(Action<object> action, object state)>();
        private bool StartWorker()
        {
            bool create = false;
            if (_threadCount < MaxThreads)
            {
                lock (ThreadSyncLock)
                {
                    if (_threadCount < MaxThreads) // double-check
                    {
                        create = true;
                        _threadCount++;

                    }
                }
            }

            if (create)
            {
                var thread = new Thread(ThreadRunWorkLoop)
                {
                    Name = Name,
                    Priority = Priority,
                    IsBackground = true
                };
                thread.Start(this);
                Helpers.Incr(Counter.ThreadPoolWorkerStarted);
            }
            return create;
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
                _queue.Enqueue((action, state));
                if(_queue.Count == 1)
                {
                    Monitor.Pulse(_queue); // just the one? pulse a single worker
                }
                else
                {
                    Monitor.PulseAll(_queue); // wakey wakey!
                }
                queueLength = _queue.Count;
            }
            if (queueLength == 0) // was it swallowed in the pulse?
            {
                Helpers.Incr(Counter.ThreadPoolScheduled);
            }
            else
            {
                // if disposed: always ask the thread pool
                // otherwise; 
                if (_disposed || (!StartWorker() && queueLength >= UseThreadPoolQueueLength))
                {
                    Helpers.Incr(Counter.ThreadPoolPushedToMainThreadPoop);
                    Helpers.DebugLog(Name, $"requesting help form thread-pool; queue length: {queueLength}");
                    System.Threading.ThreadPool.QueueUserWorkItem(ThreadPoolRunSingleItem, this);
                }
            }
        }
        static readonly ParameterizedThreadStart ThreadRunWorkLoop = state => ((DedicatedThreadPoolPipeScheduler)state).RunWorkLoop();
        static readonly WaitCallback ThreadPoolRunSingleItem = state => ((DedicatedThreadPoolPipeScheduler)state).RunSingleItem();

        private int _busyCount;
        /// <summary>
        /// The number of workers currently actively engaged in work
        /// </summary>
        public int BusyCount => Thread.VolatileRead(ref _busyCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Execute(Action<object> action, object state)
        {
            try
            {
                action(state);
                Helpers.Incr(Counter.ThreadPoolExecuted);
            }
            catch (Exception ex)
            {
                Helpers.DebugLog(Name, ex.Message);
            }
        }

        private void RunSingleItem()
        {
            (Action<object> action, object state) next;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                next = _queue.Dequeue();
            }
            Execute(next.action, next.state);
        }
        private void RunWorkLoop()
        {
            Interlocked.Increment(ref _busyCount);
            while (true)
            {
                (Action<object> action, object state) next;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        Interlocked.Decrement(ref _busyCount);
                        do
                        {
                            if (_disposed) break;
                            Monitor.Wait(_queue);
                        } while (_queue.Count == 0);
                        Interlocked.Increment(ref _busyCount);
                    }
                    next = _queue.Dequeue();
                }
                Execute(next.action, next.state);
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
