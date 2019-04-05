using BenchmarkDotNet.Attributes;
using System;
using Pipelines.Sockets.Unofficial;
using BenchmarkDotNet.Configs;

namespace Benchmark
{
    [MemoryDiagnoser, CoreJob, ClrJob, MinColumn, MaxColumn]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class DelegateBenchmarks : BenchmarkBase
    {
        private const int PER_TEST = 5 * 1024;

        static readonly Func<int> _nil = null, _single = () => 1, _dual = _single + _single;

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetInvocationList))]
        public void GetInvocationList_Nil() => GetInvocationList(_nil).AssertIs(0);
        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetInvocationList))]
        public void GetInvocationList_Single() => GetInvocationList(_single).AssertIs(1);
        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetInvocationList))]
        public void GetInvocationList_Dual() => GetInvocationList(_dual).AssertIs(2);

        private static int GetInvocationList(Func<int> handler)
        {
            int count = -1;
            for (int i = 0; i < PER_TEST; i++)
            {
                count = 0;
                if (handler != null)
                {
                    foreach (Func<int> sub in handler.GetInvocationList())
                    {
                        count += sub();
                    }
                }
            }
            return count;
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetEnumerator))]
        public void GetEnumerator_Nil() => GetEnumerator(_nil).AssertIs(0);
        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetEnumerator))]
        public void GetEnumerator_Single() => GetEnumerator(_single).AssertIs(1);
        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetEnumerator))]
        public void GetEnumerator_Dual() => GetEnumerator(_dual).AssertIs(2);

        private static int GetEnumerator(Func<int> handler)
        {
            int count = -1;
            for (int i = 0; i < PER_TEST; i++)
            {
                count = 0;
                if (handler != null)
                {
                    foreach (var sub in handler.AsEnumerable())
                    {
                        count += sub();
                    }
                }
            }
            return count;
        }

        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetEnumerator_CheckSingle))]
        public void GetEnumerator_CheckSingle_Nil() => GetEnumerator_CheckSingle(_nil).AssertIs(0);
        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetEnumerator_CheckSingle))]
        public void GetEnumerator_CheckSingle_Single() => GetEnumerator_CheckSingle(_single).AssertIs(1);
        [Benchmark(OperationsPerInvoke = PER_TEST)]
        [BenchmarkCategory(nameof(GetEnumerator_CheckSingle))]
        public void GetEnumerator_CheckSingle_Dual() => GetEnumerator_CheckSingle(_dual).AssertIs(2);

        private static int GetEnumerator_CheckSingle(Func<int> handler)
        {
            int count = -1;
            for (int i = 0; i < PER_TEST; i++)
            {
                count = 0;
                if (handler == null) {}
                else if (handler.IsSingle())
                {
                    count = handler();
                }
                else
                {
                    foreach (var sub in handler.AsEnumerable())
                    {
                        count += sub();
                    }
                }
            }
            return count;
        }
    }
}
