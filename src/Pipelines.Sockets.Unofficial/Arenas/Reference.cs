using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Acts as a fly-weight reference into existing data
    /// </summary>
    public readonly struct Reference<T> : IEquatable<Reference<T>>
    {
        /// <summary>
        /// Obtain a text representation of the value
        /// </summary>
        public override string ToString() => Value?.ToString();

        /// <summary>
        /// Used to compare two instances for equality
        /// </summary>
        public override bool Equals(object obj) => obj is Reference<T> other && Equals(other);

        /// <summary>
        /// Used to compare two instances for equality
        /// </summary>
        public bool Equals(Reference<T> other) => _obj == other._obj & _offset == other._offset;

        /// <summary>
        /// Used to compare two instances for equality
        /// </summary>
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(_obj) ^ _offset;

        private readonly object _obj;
        private readonly int _offset;

        /// <summary>
        /// Create a new reference into an array
        /// </summary>
        public Reference(T[] array, int offset) : this(offset, array)
        {
            if (array == null) Throw.ArgumentNull(nameof(array));
            if (offset < 0 | offset >= array.Length) Throw.ArgumentOutOfRange(nameof(offset));
        }

        /// <summary>
        /// Create a new reference into a memory
        /// </summary>
        public Reference(IMemoryOwner<T> memory, int offset) : this(offset, memory)
        {
            if (memory == null) Throw.ArgumentNull(nameof(memory));
            if (offset < 0 | offset >= memory.Memory.Length) Throw.ArgumentOutOfRange(nameof(offset));
        }

        internal Reference(int offset, object obj) // trusted .ctor
        {
            _obj = obj;
            _offset = offset;
        }
        
        /// <summary>
        /// Get a reference to the underlying value
        /// </summary>
        public ref T Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_obj is T[] arr) return ref arr[_offset];
                return ref SlowValue(_obj, _offset);

                unsafe ref T SlowValue(object obj, int offset)
                {
                    void* origin;
                    if (obj is IPinnedMemoryOwner<T> pinned && (origin = pinned.Origin) != null)
                    {
                        return ref Unsafe.AsRef<T>(Unsafe.Add<T>(origin, offset));
                    }
                    return ref ((IMemoryOwner<T>)obj).Memory.Span[offset]; // note that this will NRE correctly if obj is null
                }
            }
        }

        /// <summary>
        /// Convert a reference to the underlying type
        /// </summary>
        public static implicit operator T (Reference<T> reference) => reference.Value;
    }
}
