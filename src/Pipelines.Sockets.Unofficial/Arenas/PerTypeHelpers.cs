using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    internal static class PerTypeHelpers
    {
        internal static readonly MethodInfo AllocateUnmanaged =
                    typeof(Arena).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(x => x.Name == nameof(Arena.AllocateUnmanaged)
                    && x.IsGenericMethodDefinition
                    && x.GetParameters().Length == 1);
    }

    internal static class PerTypeHelpers<T>
    {
        static PerTypeHelpers()
        {
            if (IsBlittable)
            {
                AllocateUnmanaged = TryCreateAllocate();
            }
        }
        public static readonly Func<Arena, int, Sequence<T>> AllocateUnmanaged;

        private static Allocator<T> _preferUnmanaged, _preferPinned;
        public static Allocator<T> PreferUnmanaged()
        {
            return _preferUnmanaged ?? (_preferUnmanaged = Calculate());

            Allocator<T> Calculate()
            {
                if (IsBlittable)
                {
                    try
                    {
                        typeof(UnmanagedAllocator<>).MakeGenericType(typeof(T))
                            .GetProperty(nameof(UnmanagedAllocator<int>.Shared))
                            .GetValue(null);
                    }
                    catch { }
                }
                return PreferPinned(); // safe fallback
            }
        }

        public static Allocator<T> PreferPinned()
        {
            return _preferPinned ?? (_preferPinned = Calculate());

            Allocator<T> Calculate()
            {
                if (IsBlittable)
                {
                    try
                    {
                        typeof(PinnedArrayPoolAllocator<>).MakeGenericType(typeof(T))
                            .GetProperty(nameof(PinnedArrayPoolAllocator<int>.Shared))
                            .GetValue(null);
                    }
                    catch { }
                }
                return ArrayPoolAllocator<T>.Shared; // safe fallback
            }
        }

        private static Func<Arena, int, Sequence<T>> TryCreateAllocate()
        {
            try
            {
                var arena = Expression.Parameter(typeof(Arena), "arena");
                var length = Expression.Parameter(typeof(int), "length");
                Expression body = Expression.Call(
                    instance: arena,
                    method: PerTypeHelpers.AllocateUnmanaged.MakeGenericMethod(typeof(T)),
                    arguments: new[] { length });
                return Expression.Lambda<Func<Arena, int, Sequence<T>>>(
                    body, arena, length).Compile();
            }
            catch (Exception ex)
            {
                Debug.Fail(ex.Message);
                return null; // swallow in prod
            }
        }

#if SOCKET_STREAM_BUFFERS
        public static bool IsBlittable
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        }
#else
        public static bool IsBlittable { get; } = !IsReferenceOrContainsReferences();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsReferenceOrContainsReferences()
        {
            if (typeof(T).IsValueType)
            {
                try
                {
                    unsafe
                    {
                        byte* ptr = stackalloc byte[Unsafe.SizeOf<T>()];
                        var span = new Span<T>(ptr, 1); // this will throw if not legal
                        return span.Length != 1; // we expect 1; treat anything else as failure
                    }
                }
                catch { } // swallow, this is an expected failure
            }
            return true;
        }
#endif
    }
}
