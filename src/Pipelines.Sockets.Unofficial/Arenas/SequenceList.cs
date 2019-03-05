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
    public class SequenceList<T> : IList<T>, IReadOnlyList<T>, IList
    {
        private static readonly SequenceList<T> s_empty = new SequenceList<T>(default);

        internal static SequenceList<T> Create(in Sequence<T> sequence)
            => sequence.IsEmpty ? s_empty: new SequenceList<T>(sequence);

        private readonly Sequence<T> _sequence;

        /// <summary>
        /// Returns the size of the list
        /// </summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => checked((int)_sequence.Length);
        }

        /// <summary>
        /// Allows a sequence to be enumerated as values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sequence<T>.Enumerator GetEnumerator() => _sequence.GetEnumerator();

        /// <summary>
        /// Provide a reference to an element by index
        /// </summary>
        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _sequence[index];
        }

        internal SequenceList(in Sequence<T> sequence) => _sequence = sequence;

        /// <summary>
        /// Get the sequence represented by this list
        /// </summary>
        public Sequence<T> ToSequence() => _sequence;

        // everything from here is to make the interfaces happy

        bool ICollection<T>.IsReadOnly => true; // means add/remove

        bool IList.IsReadOnly => false; // ambiguous - means add/remove/modify; we allow modify, so...

        bool IList.IsFixedSize => true; // means add/remove

        int ICollection.Count => Count;

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
            if (!_sequence.IsEmpty)
            {
                var comparer = EqualityComparer<T>.Default;
                if (_sequence.IsSingleSegment)
                {
                    var span = _sequence.FirstSpan;
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (comparer.Equals(item, span[i])) return i;
                    }
                }
                else
                {
                    int offset = 0;
                    foreach (var span in _sequence.Spans)
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

        void IList<T>.Insert(int index, T item) => Throw.NotSupported();

        void IList<T>.RemoveAt(int index) => Throw.NotSupported();

        void ICollection<T>.Add(T item) => Throw.NotSupported();

        void ICollection<T>.Clear() => Throw.NotSupported();

        bool ICollection<T>.Contains(T item) => IndexOf(item) >= 0;

        private void CopyTo(T[] array, int arrayIndex)
            => _sequence.CopyTo(new Span<T>(array, arrayIndex, array.Length - arrayIndex));

        void ICollection<T>.CopyTo(T[] array, int arrayIndex) => CopyTo(array, arrayIndex);

        bool ICollection<T>.Remove(T item) { Throw.NotSupported(); return default; }

        private unsafe IEnumerator<T> GetObjectEnumerator() => _sequence.GetObjectEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _sequence.GetObjectEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetObjectEnumerator();

        int IList.Add(object value) { Throw.NotSupported(); return default; }

        bool IList.Contains(object value) => IndexOf((T)value) >= 0;

        void IList.Clear() => Throw.NotSupported();

        int IList.IndexOf(object value) => IndexOf((T)value);

        void IList.Insert(int index, object value) => Throw.NotSupported();

        void IList.Remove(object value) => Throw.NotSupported();

        void IList.RemoveAt(int index) => Throw.NotSupported();

        void ICollection.CopyTo(Array array, int index) => CopyTo((T[])array, index);
    }
}
