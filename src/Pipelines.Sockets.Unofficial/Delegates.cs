using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial
{
    /// <summary>
    /// Provides utility methods for working with delegates
    /// </summary>
    public static class Delegates
    {
        /// <summary>
        /// Iterate over the individual elements of a multicast delegate (without allocation)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DelegateEnumerator<T> GetEnumerator<T>(this T handler) where T : MulticastDelegate
            => handler == null ? default : new(handler);

        /// <summary>
        /// Iterate over the individual elements of a multicast delegate (without allocation)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DelegateEnumerable<T> AsEnumerable<T>(this T handler) where T : MulticastDelegate
            => new(handler);

        /// <summary>
        /// Indicates whether a particular delegate is known to be a single-target delegate
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSingle(this MulticastDelegate handler)
            => s_getArr != null && s_getArr(handler) == null;

        private static readonly Func<MulticastDelegate, object> s_getArr = GetGetter<object>("_invocationList");
        private static readonly Func<MulticastDelegate, IntPtr> s_getCount = GetGetter<IntPtr>("_invocationCount");
        private static readonly bool s_isAvailable = s_getArr != null & s_getCount != null;

        /// <summary>
        /// Indicates whether optimized usage is supported on this environment; without this, it may still
        /// work, but with additional overheads at runtime.
        /// </summary>
        public static bool IsSupported => s_isAvailable;

        private static Func<MulticastDelegate, T> GetGetter<T>(string fieldName)
        {
            try
            {
                var field = typeof(MulticastDelegate).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || field.FieldType != typeof(T)) return null;
#if !NETSTANDARD2_0

#if NETCOREAPP3_0_OR_GREATER // test for AOT scenarios
                if (RuntimeFeature.IsDynamicCodeSupported)
#endif
                {
                    try // we can try use ref-emit
                    {
                        var dm = new DynamicMethod(fieldName, typeof(T), new[] { typeof(MulticastDelegate) }, typeof(MulticastDelegate), true);
                        var il = dm.GetILGenerator();
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, field);
                        il.Emit(OpCodes.Ret);
                        return (Func<MulticastDelegate, T>)dm.CreateDelegate(typeof(Func<MulticastDelegate, T>));
                    }
                    catch { }
                }
#endif
                return GetViaReflection<T>(field);
            }
            catch
            {
                return null;
            }
        }
        static Func<MulticastDelegate, T> GetViaReflection<T>(FieldInfo field)
            => handler => (T)field.GetValue(handler);

        /// <summary>
        /// Allows allocation-free enumerator over the individual elements of a multicast delegate
        /// </summary>
        public readonly struct DelegateEnumerable<T> : IEnumerable<T> where T : MulticastDelegate
        {
            private readonly T _handler;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal DelegateEnumerable(T handler) => _handler = handler;

            /// <summary>
            /// Iterate over the individual elements of a multicast delegate (without allocation)
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public DelegateEnumerator<T> GetEnumerator()
                => _handler == null ? default : new DelegateEnumerator<T>(_handler);
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// Allows allocation-free enumerator over the individual elements of a multicast delegate
        /// </summary>
        public struct DelegateEnumerator<T> : IEnumerator<T> where T : MulticastDelegate
        {
            private readonly T _handler;
            private readonly object[] _arr;
            private readonly int _count;
            private int _index;
            private T _current;
            internal DelegateEnumerator(T handler)
            {
                Debug.Assert(handler != null);
                _handler = handler;
                if (s_isAvailable)
                {
                    _arr = (object[])s_getArr(handler);
                    if (_arr == null)
                    {
                        _count = 1;
                    }
                    else
                    {
                        _count = (int)s_getCount(handler);
                    }
                }
                else
                {
                    _arr = handler.GetInvocationList();
                    _count = _arr.Length;
                }
                _current = null;
                _index = -1;
            }

            /// <summary>
            /// Provides the current value of the sequence
            /// </summary>
            public T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _current;
            }

            object IEnumerator.Current => Current;

            void IDisposable.Dispose() { }

            /// <summary>
            /// Move to the next item in the sequence
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                var next = _index + 1;
                if (next >= _count)
                {
                    _current = null;
                    return false;
                }
                _current = _arr == null ? _handler : (T)_arr[next];
                _index = next;
                return true;
            }

            /// <summary>
            /// Reset the enumerator, allowing the sequence to be repeated
            /// </summary>
            public void Reset()
            {
                _current = null;
                _index = -1;
            }
        }
    }
}
