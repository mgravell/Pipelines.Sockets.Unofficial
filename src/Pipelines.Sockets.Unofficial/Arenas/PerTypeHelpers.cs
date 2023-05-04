using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    internal static class PerTypeHelpers
    {
        // this is a clone of MemoryMarshal.Cast, but without the generic constraints - which we will
        // enforce at runtime instead (well, JIT/AOT)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<TTo> Cast<TFrom, TTo>(Span<TFrom> span)
        {
#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER // we need access to MemoryMarshal.CreateSpan to work this voodoo
            Debug.Assert(PerTypeHelpers<TFrom>.IsBlittable);
            Debug.Assert(PerTypeHelpers<TTo>.IsBlittable);

            // Use unsigned integers - unsigned division by constant (especially by power of 2)
            // and checked casts are faster and smaller.
            uint fromSize = (uint)Unsafe.SizeOf<TFrom>();
            uint toSize = (uint)Unsafe.SizeOf<TTo>();
            uint fromLength = (uint)span.Length;
            int toLength;
            if (fromSize == toSize)
            {
                // Special case for same size types - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // should be optimized to just `length` but the JIT doesn't do that today.
                toLength = (int)fromLength;
            }
            else if (fromSize == 1)
            {
                // Special case for byte sized TFrom - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // becomes `(ulong)fromLength / (ulong)toSize` but the JIT can't narrow it down to `int`
                // and can't eliminate the checked cast. This also avoids a 32 bit specific issue,
                // the JIT can't eliminate long multiply by 1.
                toLength = (int)(fromLength / toSize);
            }
            else
            {
                // Ensure that casts are done in such a way that the JIT is able to "see"
                // the uint->ulong casts and the multiply together so that on 32 bit targets
                // 32x32to64 multiplication is used.
                ulong toLengthUInt64 = (ulong)fromLength * (ulong)fromSize / (ulong)toSize;
                toLength = checked((int)toLengthUInt64);
            }

            return MemoryMarshal.CreateSpan<TTo>(
                ref Unsafe.As<TFrom, TTo>(ref MemoryMarshal.GetReference(span)),
                toLength);
#else
            unsafe
            {
                return CastCache<TFrom, TTo>.Cast(span);
            }
#endif
        }

#if !(NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER)
        // without access to MemoryMarshal.CreateSpan, we'll use evilness to invoke MemoryMarshal.Cast instead
        internal static class CastCache<TFrom, TTo>
        {
            public readonly static unsafe delegate*<Span<TFrom>, Span<TTo>> Cast;

            // alternative approach if we want to avoid function pointers
            //public delegate Span<TTo> CastHelper(Span<TFrom> span); // can't use Func<...> with Span<T>
            //public static readonly CastHelper Cast;
            static CastCache()
            {
                var concrete = CastTemplate.MakeGenericMethod(typeof(TFrom), typeof(TTo));
                unsafe
                {
                    Cast = (delegate*<Span<TFrom>, Span<TTo>>)concrete.MethodHandle.GetFunctionPointer();
                }
                //Cast = (CastHelper)Delegate.CreateDelegate(typeof(CastHelper), null, concrete);
            }
        }
        private static readonly MethodInfo CastTemplate = typeof(MemoryMarshal).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == nameof(MemoryMarshal.Cast) && method.IsGenericMethodDefinition
                    && IsParameterMatch(method) && method.GetGenericArguments().Length == 2);
        private static bool IsParameterMatch(MethodInfo method)
        {
            var args = method.GetParameters();
            if (args is null || args.Length != 1) return false;
            var type = args[0].ParameterType;
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Span<>);

        }
#endif
    }

    internal static class PerTypeHelpers<T>
    {
        private static Allocator<T> _preferUnmanaged, _preferPinned;
        public static Allocator<T> PreferUnmanaged()
        {
            return _preferUnmanaged ??= Calculate();

            static Allocator<T> Calculate()
            {
                if (IsBlittable
#if NETCOREAPP3_0_OR_GREATER
                    && RuntimeFeature.IsDynamicCodeSupported
#endif
                )
                {
                    return UnmanagedAllocator<T>.Shared;
                }
                return PreferPinned(); // safe fallback
            }
        }

        public static Allocator<T> PreferPinned()
        {
            return _preferPinned ??= Calculate();

            static Allocator<T> Calculate()
            {
                if (IsBlittable
#if NETCOREAPP3_0_OR_GREATER
                    && RuntimeFeature.IsDynamicCodeSupported
#endif
                )
                {
                    return PinnedArrayPoolAllocator<T>.Shared;
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
