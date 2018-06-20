using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Pipelines.Sockets.Unofficial
{
    public sealed class DedicatedThreadPoolPipeScheduler : PipeScheduler, IDisposable
    {
        public override string ToString() => Name;
        public int MaxThreads { get; }
        public int MinThreads { get; }

        public int UseThreadPoolQueueLength { get; }
        public ThreadPriority Priority { get; }

        public string Name { get; }
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
                var thread = new Thread(ThreadRunWorkLoop);
                thread.Name = Name;
                thread.Priority = Priority;
                thread.IsBackground = true;
                thread.Start(this);
            }
            return create;
        }
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
            if (queueLength != 0) // was it swallowed in the pulse?
            {
                // if disposed: always ask the thread pool
                // otherwise; 
                if (_disposed || (!StartWorker() && queueLength >= UseThreadPoolQueueLength))
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(ThreadPoolRunSingleItem, this);
                }
            }
        }
        static readonly ParameterizedThreadStart ThreadRunWorkLoop = state => ((DedicatedThreadPoolPipeScheduler)state).RunWorkLoop();
        static readonly WaitCallback ThreadPoolRunSingleItem = state => ((DedicatedThreadPoolPipeScheduler)state).RunSingleItem();

        private int _busyCount;
        public int BusyCount => Thread.VolatileRead(ref _busyCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Execute(Action<object> action, object state)
        {
            try { action(state); }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
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
