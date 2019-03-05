using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        public Reference(T[] array, int index) : this(index, array)
        {
            if (array == null) Throw.ArgumentNull(nameof(array));
            if (index < 0 | index >= array.Length) Throw.ArgumentOutOfRange(nameof(index));
        }

        /// <summary>
        /// Create a new reference into a memory
        /// </summary>
        public Reference(Memory<T> memory, int index)
        {
            if (MemoryMarshal.TryGetMemoryManager<T, MemoryManager<T>>(memory, out MemoryManager<T> manager, out int start, out int length))
            {
                if (index < 0 | index >= length) Throw.ArgumentOutOfRange(nameof(index));
                _obj = manager;
                _offset = start + index;
            }
            else if (MemoryMarshal.TryGetArray(memory, out ArraySegment<T> segment))
            {
                if (index < 0 | index >= segment.Count) Throw.ArgumentOutOfRange(nameof(index));
                _obj = segment.Array;
                _offset = segment.Offset + index;
            }
            else
            {
                Throw.Argument("The provided Memory instance cannot be used as a reference", nameof(memory));
                this = default;
            }
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
