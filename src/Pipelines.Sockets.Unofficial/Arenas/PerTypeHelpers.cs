using System;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    internal static class PerTypeHelpers<T>
    {
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
