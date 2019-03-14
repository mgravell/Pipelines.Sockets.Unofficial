using Benchmark;
using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public abstract class BenchmarkTests<T> : IDisposable
        where T : BenchmarkBase, new()
    {
        private T _instance = new T();
        private readonly int _defaultTimes;
        public ITestOutputHelper Output { get; }
        protected BenchmarkTests(ITestOutputHelper output, int? defaultTimes = null)
        {
            Output = output;
            _instance.Log += s => Output.WriteLine(s);
            _defaultTimes = defaultTimes ?? 1;
        }

        public void Dispose()
        {
            using (_instance as IDisposable) { }
            _instance = null;
        }

        private void Write<TResult>(TResult value) => Output?.WriteLine(value?.ToString() ?? "(null)");

        public async Task Run<TResult>(Func<T, TResult> action, int? times = null)
        {
            int runs = times ?? _defaultTimes;
            for (int i = 0; i < runs; i++)
                Write(action(_instance));
            await Task.CompletedTask; // to get same exception/etc handling as the others
        }
        public async Task Run(Action<T> action, int? times = null)
        {
            int runs = times ?? _defaultTimes;
            for (int i = 0; i < runs; i++)
                action(_instance);
            await Task.CompletedTask; // to get same exception/etc handling as the others
        }
        public async Task Run(Func<T, Task> action, int? times = null)
        {
            int runs = times ?? _defaultTimes;
            for (int i = 0; i < runs; i++)
                await action(_instance).ConfigureAwait(false);
        }
        public async Task Run(Func<T, ValueTask> action, int? times = null)
        {
            int runs = times ?? _defaultTimes;
            for (int i = 0; i < runs; i++)
                await action(_instance);
        }
        public async Task Run<TResult>(Func<T, Task<TResult>> action, int? times = null)
        {
            int runs = times ?? _defaultTimes;
            for (int i = 0; i < runs; i++)
                Write(await action(_instance).ConfigureAwait(false));
        }
        public async Task Run<TResult>(Func<T, ValueTask<TResult>> action, int? times = null)
        {
            int runs = times ?? _defaultTimes;
            for (int i = 0; i < runs; i++)
                Write(await action(_instance).ConfigureAwait(false));
        }

        [MemberData(nameof(GetMethods))]
        [Theory]
        public void HasTestMethods(string methodName)
        {
            var found = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(found);
            Assert.Equal(typeof(Task), found.ReturnType);
        }
#pragma warning disable RCS1158 // Static member in generic type should use a type parameter.
        public static IEnumerable<object[]> GetMethods()
#pragma warning restore RCS1158 // Static member in generic type should use a type parameter.
        {
            foreach(var method in typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (Attribute.IsDefined(method, typeof(BenchmarkAttribute)))
                    yield return new object[] { method.Name };
            }
        }
    }
    public class ArenaBenchmarkTests : BenchmarkTests<ArenaBenchmarks>
    {
        public ArenaBenchmarkTests(ITestOutputHelper output) : base(output, 10) { }

        [Fact] public Task Alloc_ArenaT() => Run(_ => _.Alloc_ArenaT());
        [Fact] public Task Alloc_Arena_Default() => Run(_ => _.Alloc_Arena_Default());
        [Fact] public Task Alloc_Arena_NoPadding() => Run(_ => _.Alloc_Arena_NoPadding());
        [Fact] public Task Alloc_Arena_NoSharing() => Run(_ => _.Alloc_Arena_NoSharing());
        [Fact] public Task Alloc_Arena_Owned_Default() => Run(_ => _.Alloc_Arena_Owned_Default());
        [Fact] public Task Alloc_Arena_Owned_NoPadding() => Run(_ => _.Alloc_Arena_Owned_NoPadding());
        [Fact] public Task Alloc_Arena_Owned_NoSharing() => Run(_ => _.Alloc_Arena_Owned_NoSharing());
        [Fact] public Task Alloc_ArrayPool() => Run(_ => _.Alloc_ArrayPool());
        [Fact] public Task Alloc_NewArray() => Run(_ => _.Alloc_NewArray());
        [Fact] public Task ReadArenaForeachRefAdd() => Run(_ => _.ReadArenaForeachRefAdd());
        [Fact] public Task ReadArenaSegmentsFor() => Run(_ => _.ReadArenaSegmentsFor());
        [Fact] public Task ReadArenaSpansFor() => Run(_ => _.ReadArenaSpansFor());
        [Fact] public Task ReadArrayFor() => Run(_ => _.ReadArrayFor());
        [Fact] public Task ReadArrayForeach() => Run(_ => _.ReadArrayForeach());
        [Fact] public Task ReadArrayPoolFor() => Run(_ => _.ReadArrayPoolFor());
        [Fact] public Task ReadArrayPoolForeach() => Run(_ => _.ReadArrayPoolForeach());
        [Fact] public Task WriteArenaSegmentsFor() => Run(_ => _.WriteArenaSegmentsFor());
        [Fact] public Task WriteArenaSpansFor() => Run(_ => _.WriteArenaSpansFor());
        [Fact] public Task WriteArrayFor() => Run(_ => _.WriteArrayFor());
        [Fact] public Task WriteArrayPoolFor() => Run(_ => _.WriteArrayPoolFor());
    }
    public class LockBenchmarkTests : BenchmarkTests<LockBenchmarks>
    {
        public LockBenchmarkTests(ITestOutputHelper output) : base(output, 10) { }

        [Fact] public Task Monitor_Sync() => Run(_ => _.Monitor_Sync());
        [Fact] public Task MutexSlim_Async() => Run(_ => _.MutexSlim_Async());
        [Fact] public Task MutexSlim_Async_HotPath() => Run(_ => _.MutexSlim_Async_HotPath());
        // [Fact(Skip = "sync-over-async")] public Task MutexSlim_ConcurrentLoad() => Run(_ => _.MutexSlim_ConcurrentLoad());
        [Fact] public Task MutexSlim_ConcurrentLoadAsync() => Run(_ => _.MutexSlim_ConcurrentLoadAsync());
        [Fact] public Task MutexSlim_ConcurrentLoadAsync_DisableContext() => Run(_ => _.MutexSlim_ConcurrentLoadAsync_DisableContext());
        [Fact] public Task MutexSlim_Sync() => Run(_ => _.MutexSlim_Sync());
        [Fact] public Task SemaphoreSlim_Async() => Run(_ => _.SemaphoreSlim_Async());
        [Fact] public Task SemaphoreSlim_Async_HotPath() => Run(_ => _.SemaphoreSlim_Async_HotPath());
        // [Fact(Skip = "sync-over-async")] public Task SemaphoreSlim_ConcurrentLoad() => Run(_ => _.SemaphoreSlim_ConcurrentLoad());
        [Fact] public Task SemaphoreSlim_ConcurrentLoadAsync() => Run(_ => _.SemaphoreSlim_ConcurrentLoadAsync());
        [Fact] public Task SemaphoreSlim_Sync() => Run(_ => _.SemaphoreSlim_Sync());
    }

    public class StreamBenchmarkTests : BenchmarkTests<StreamBenchmarks>
    {
        public StreamBenchmarkTests(ITestOutputHelper output) : base(output, 10) { }
        [Fact] public Task MemoryStreamDefault() => Run(_ => _.MemoryStreamDefault());
        [Fact] public Task MemoryStreamPreSize() => Run(_ => _.MemoryStreamPreSize());
        [Fact] public Task SequenceStreamDefault() => Run(_ => _.SequenceStreamDefault());
        [Fact] public Task SequenceStreamPreSize() => Run(_ => _.SequenceStreamPreSize());
    }
}
