using BenchmarkDotNet.Running;
using System;
using System.Buffers;

namespace Benchmark
{
    unsafe class Owner : MemoryManager<int>
    {
        int* _ptr;
        int _size;
        
        public Owner(int* ptr, int size)
        {
            _ptr = ptr;
            _size = size;
        }

        public override Span<int> GetSpan() => new Span<int>(_ptr, _size);

        public override MemoryHandle Pin(int elementIndex = 0) => default;

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }
    internal static class Program
    {
        //private static void Main(string[] args)
        //    => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

        private static unsafe void Main()
        {
            var arr = new int[42];
            var ros = new ReadOnlySequence<int>(arr);
            Show(ros);

            var rom = new ReadOnlyMemory<int>(arr, 1, 12);
            ros = new ReadOnlySequence<int>(arr);
            Show(ros);

            int* ptr = stackalloc int[100];
            var owner = new Owner(ptr, 100);
            ros = new ReadOnlySequence<int>(owner.Memory);
            Show(ros);

            void Show(in ReadOnlySequence<int> a)
            {
                var pos = a.Start;
                Console.WriteLine($"{pos.GetInteger()}/{pos.GetObject()}");
                pos = a.End;
                Console.WriteLine($"{pos.GetInteger()}/{pos.GetObject()}");
                Console.WriteLine();
            }
        }

        public static int AssertIs(this int actual, int expected)
        {
            if (actual != expected) throw new InvalidOperationException($"expected {expected} but was {actual}");
            return actual;
        }

        public static long AssertIs(this long actual, long expected)
        {
            if (actual != expected) throw new InvalidOperationException($"expected {expected} but was {actual}");
            return actual;
        }
    }
}
