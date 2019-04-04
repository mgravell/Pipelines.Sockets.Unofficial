using Pipelines.Sockets.Unofficial.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    // not sealed - leaving room here to detect IPinnedMemoryOwner and provide some optimized implementations

    /// <summary>
    /// A list-like reference type that can be used in most APIs that expect a list object
    /// </summary>
    public abstract partial class SequenceList<T> : IList<T>, IReadOnlyList<T>, IList
    {
        private protected abstract bool IsReadOnly { get; }
        private protected abstract int CountImpl();
        private protected abstract int CapacityImpl();
        private protected abstract Sequence<T> GetSequence();

        private protected abstract void AddImpl(in T element);
        private protected abstract void ClearImpl();

        /// <summary>
        /// Release any unused segments used by this list
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Trim() => TrimImpl();
        private protected virtual void TrimImpl() { }

        /// <summary>
        /// Returns the size of the list
        /// </summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CountImpl();
        }

        /// <summary>
        /// Returns the capacity of the underlying sequence
        /// </summary>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CapacityImpl();
        }

        /// <summary>
        /// Allows a sequence to be enumerated as values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T>.Enumerator GetEnumerator() => GetSequence().GetEnumerator();

        /// <summary>
        /// Provide a reference to an element by index
        /// </summary>
        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref GetByIndex(index);
        }

        private protected abstract ref T GetByIndex(int index);

        /// <summary>
        /// Get the sequence represented by this list
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T> ToSequence() => GetSequence();


        bool ICollection<T>.IsReadOnly => IsReadOnly; // means add/remove

        bool IList.IsReadOnly => false; // ambiguous - means add/remove/modify; we allow modify, so...

        bool IList.IsFixedSize => IsReadOnly; // means add/remove

        object ICollection.SyncRoot => this;

        bool ICollection.IsSynchronized => false;

        object IList.this[int index]
        {
            get => this[index];
            set => this[index] = (T)value;
        }

        T IList<T>.this[int index]
        {
            get => this[index];
            set => this[index] = value;
        }
        T IReadOnlyList<T>.this[int index]
        {
            get => this[index];
        }

        private int IndexOf(T item)
        {
            var sequence = GetSequence();
            if (!sequence.IsEmpty)
            {
                var comparer = EqualityComparer<T>.Default;
                if (sequence.IsSingleSegment)
                {
                    var span = sequence.FirstSpan;
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (comparer.Equals(item, span[i])) return i;
                    }
                }
                else
                {
                    int offset = 0;
                    foreach (var span in sequence.Spans)
                    {
                        for (int i = 0; i < span.Length; i++)
                        {
                            if (comparer.Equals(item, span[i])) return offset + i;
                        }
                        offset += span.Length;
                    }
                }
            }
            return -1;
        }

        int IList<T>.IndexOf(T item) => IndexOf(item);

        void IList<T>.Insert(int index, T item) {
            if (index == CountImpl()) AddImpl(item);
            else Throw.NotSupported();
        }

        void IList<T>.RemoveAt(int index) => Throw.NotSupported();

        /// <summary>
        /// An a new element to the list
        /// </summary>
        [CLSCompliant(false)]
        public void Add(in T value) => AddImpl(in value);

        void ICollection<T>.Add(T value) => AddImpl(in value);

        void ICollection<T>.Clear() => ClearImpl();
        void IList.Clear() => ClearImpl();

        /// <summary>
        /// Remove all elements from the list, optionally releasing all segments
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear(bool trim = false)
        {
            ClearImpl();
            if (trim) TrimImpl();
        }

        bool ICollection<T>.Contains(T item) => IndexOf(item) >= 0;

        private void CopyTo(T[] array, int arrayIndex)
            => GetSequence().CopyTo(new Span<T>(array, arrayIndex, array.Length - arrayIndex));

        void ICollection<T>.CopyTo(T[] array, int arrayIndex) => CopyTo(array, arrayIndex);

        bool ICollection<T>.Remove(T item) { Throw.NotSupported(); return default; }

        private unsafe IEnumerator<T> GetObjectEnumerator() => GetSequence().GetObjectEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetSequence().GetObjectEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetObjectEnumerator();

        int IList.Add(object value) { Add((T)value); return CountImpl() - 1; }

        bool IList.Contains(object value) => IndexOf((T)value) >= 0;

        int IList.IndexOf(object value) => IndexOf((T)value);

        void IList.Insert(int index, object value)
        {
            if (index == CountImpl()) AddImpl((T)value);
            else Throw.NotSupported();
        }

        void IList.Remove(object value) => Throw.NotSupported();

        void IList.RemoveAt(int index) => Throw.NotSupported();

        void ICollection.CopyTo(Array array, int index) => CopyTo((T[])array, index);
    }
}