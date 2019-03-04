using Benchmark;
using BenchmarkDotNet.Attributes;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class BenchmarkTests
    {
        public ITestOutputHelper Output { get; }

        public BenchmarkTests(ITestOutputHelper output) => Output = output;

        [Fact]
        public Task RunArenaBenchmarksAsTests() => Run<ArenaBenchmarks>();

        [Fact]
        public Task RunLockBenchmarksAsTests() => Run<LockBenchmarks>();

        private async Task Run<T>() where T : class, new()
        {
            var obj = new T();
            int failures = 0, total = 0;
            using (obj as IDisposable)
            {
                foreach (var method in typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (Attribute.IsDefined(method, typeof(BenchmarkAttribute)))
                    {
                        var args = method.GetParameters();
                        if (args != null && args.Length != 0)
                        {
                            Output.WriteLine($"skipping {method.Name} - unclear parameters");
                        }

                        Output.WriteLine($"running {method.Name}...");
                        try
                        {
                            var result = method.Invoke(obj, null);
                            if (result == null) { }
                            else if (result is Task t) await t;
                            else if (result is ValueTask vt)
                            {
                                await vt;
                            }
                            else
                            {
                                Type type = result.GetType();
                                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
                                {
                                    await (Task)s_await.MakeGenericMethod(type.GetGenericArguments()).Invoke(null, new object[] { result });
                                }
                            }
                            Output.WriteLine($"completed {method.Name}: {result}");
                        }
                        catch (Exception ex)
                        {
                            if (ex is TargetInvocationException tie && tie.InnerException != null)
                            {
                                ex = ex.InnerException;
                            }
                            Output.WriteLine($"faulted {method.Name}: {ex.GetType().Name} '{ex.Message}'");
                            failures++;
                        }
                        total++;
                    }
                }
            }
            Output.WriteLine($"total failures: {failures} of {total}");
            Assert.Equal(0, failures);
        }

        static readonly MethodInfo s_await = typeof(BenchmarkTests).GetMethod(nameof(AwaitTypedValueTask));
#pragma warning disable xUnit1013 // Public method should be marked as test
        public static async Task AwaitTypedValueTask<T>(ValueTask<T> vt) => await vt;
#pragma warning restore xUnit1013 // Public method should be marked as test
    }
}
