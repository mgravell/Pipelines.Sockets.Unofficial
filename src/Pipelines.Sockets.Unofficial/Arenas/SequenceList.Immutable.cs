using Pipelines.Sockets.Unofficial.Internal;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    public abstract partial class SequenceList<T>
    {
        /// <summary>
        /// Provides an empty immutable list
        /// </summary>
        public static SequenceList<T> Empty { get; } = new ImmutableSequenceList<T>(default);

        /// <summary>
        /// Create a new non-appendable list based on a pre-existing sequences
        /// </summary>
        /// <param name="sequence"></param>
        public static SequenceList<T> Create(in Sequence<T> sequence)
            => sequence.IsEmpty ? Empty : new ImmutableSequenceList<T>(in sequence);
    }

    internal sealed class ImmutableSequenceList<T> : SequenceList<T>
    {
        private readonly Sequence<T> _sequence;

        internal ImmutableSequenceList(in Sequence<T> sequence) => _sequence = sequence;

        private protected override ref T GetByIndex(int index) => ref _sequence[index];

        private protected override int CapacityImpl() => checked((int)_sequence.Length);
        private protected override int CountImpl() => checked((int)_sequence.Length);
        private protected override Sequence<T> GetSequence() => _sequence;
        private protected override bool IsReadOnly => true;
        private protected override void ClearImpl() => Throw.NotSupported();
        private protected override void AddImpl(in T value) => Throw.NotSupported();
    }
}
