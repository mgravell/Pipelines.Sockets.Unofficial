using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{

    public partial class SequenceList<T>
    {
        private protected SequenceList() { }

        /// <summary>
        /// Create a new mutable list based on a sequence
        /// </summary>
        public static SequenceList<T> Create(int capacity = 0)
            => new MutableSequenceList<T>(capacity);
    }

    internal sealed class MutableSequenceList<T> : SequenceList<T>
    {
        Sequence<T> _sequence;
        int _count, _capacity;
        FastState<T> _appendState;

        internal MutableSequenceList(int capacity)
        {
            // _sequence is default, but that is fine
            if (capacity != 0)
            {
                _sequence = _sequence.ExpandCapacity(length: capacity, maxCapacity: capacity);
                _capacity = capacity;
                Debug.Assert(capacity == checked((int)_sequence.Length), "expected capacity parity");
                InitAppendState();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void InitAppendState() => _appendState.Init(_sequence.GetPosition(_count), _capacity - _count);

        private protected override int CapacityImpl() => _capacity;
        private protected override int CountImpl() => _count;
        private protected override bool IsReadOnly => false;
        private protected override Sequence<T> GetSequence() => _sequence.Slice(0, _count);
        private protected override void ClearImpl()
        {
            if (!PerTypeHelpers<T>.IsBlittable & _count != 0)
            {   // wipe the ortion that we've used
                GetSequence().Clear();
            }
            _count = 0;
            InitAppendState();
        }

        private protected override ref T GetByIndex(int index) => ref _sequence[index];

        private protected override void AddImpl(in T value)
        {
            if (_appendState.TryAdd(in value))
            {
                _count++;
            }
            else
            {
                SlowAdd(in value);
            }
        }
        private void SlowAdd(in T value)
        {
            if (_count == _sequence.Length) Expand();
            Debug.Assert(_count < Capacity);
            _sequence[_count++] = value;
        }

        private void Expand()
        {
            _sequence = _sequence.ExpandCapacity(
                length: _capacity + 20,
                maxCapacity: int.MaxValue);
            _capacity = checked((int)_sequence.Length);
            InitAppendState();
        }

        private protected override void TrimImpl()
        {
            if (_count == _capacity | _capacity == 0) return; // nothing to do

            if (_count == 0)
            {
                var node = (LeasedSegment<T>)_sequence.Start.GetObject();
                _sequence = default;
                node?.CascadeRelease(inclusive: true);
                _capacity = 0;
            }
            else
            {
                var retain = (LeasedSegment<T>)_sequence.GetPosition(_count).GetObject();
                retain.CascadeRelease(inclusive: false);
                _capacity = checked((int)_sequence.Length);
            }
        }
    }
}
