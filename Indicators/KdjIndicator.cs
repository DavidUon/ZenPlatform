using System.Collections.Generic;

namespace ZenPlatform.Indicators
{
    public sealed class KdjIndicator
    {
        private readonly Queue<decimal> _highs = new();
        private readonly Queue<decimal> _lows = new();
        private decimal _k;
        private decimal _d;
        private readonly (decimal K, decimal D)[] _values = new (decimal K, decimal D)[5];
        private int _valueCount;
        private int _valueHead;
        private bool _configured;

        public KdjIndicator() { }

        public int KPeriod { get; private set; }
        public int DPeriod { get; private set; }
        public int RsvPeriod { get; private set; }
        public bool IsConfigured => _configured;

        public bool HasValue => _configured && _highs.Count == RsvPeriod && _lows.Count == RsvPeriod;
        public decimal K { get; private set; }
        public decimal D { get; private set; }
        public decimal J { get; private set; }

        public void SetParameter(int kPeriod, int dPeriod, int rsvPeriod)
        {
            KPeriod = kPeriod <= 0 ? 1 : kPeriod;
            DPeriod = dPeriod <= 0 ? 1 : dPeriod;
            RsvPeriod = rsvPeriod <= 0 ? 1 : rsvPeriod;
            _configured = true;
            Reset();
        }

        public void Reset()
        {
            _highs.Clear();
            _lows.Clear();
            _k = 0;
            _d = 0;
            K = 0;
            D = 0;
            J = 0;
            _valueCount = 0;
            _valueHead = 0;
        }

        public void Update(decimal high, decimal low, decimal close)
        {
            if (!_configured)
            {
                return;
            }

            _highs.Enqueue(high);
            _lows.Enqueue(low);
            if (_highs.Count > RsvPeriod)
            {
                _highs.Dequeue();
            }
            if (_lows.Count > RsvPeriod)
            {
                _lows.Dequeue();
            }

            if (!HasValue)
            {
                return;
            }

            var highest = Max(_highs);
            var lowest = Min(_lows);
            if (highest == lowest)
            {
                return;
            }

            var rsv = (close - lowest) / (highest - lowest) * 100m;
            _k = (_k * (KPeriod - 1) + rsv) / KPeriod;
            _d = (_d * (DPeriod - 1) + _k) / DPeriod;

            K = _k;
            D = _d;
            J = 3 * _k - 2 * _d;

            _values[_valueHead] = (K, D);
            _valueHead = (_valueHead + 1) % _values.Length;
            if (_valueCount < _values.Length)
            {
                _valueCount++;
            }
        }

        public (decimal K, decimal D) GetValue(int index)
        {
            if (!_configured)
            {
                throw new System.InvalidOperationException("KDJ is not configured.");
            }
            if (index < 0 || index >= _valueCount)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }

            var pos = _valueHead - 1 - index;
            if (pos < 0)
            {
                pos += _values.Length;
            }
            return _values[pos];
        }

        public (decimal K, decimal D) getvalue(int index)
        {
            return GetValue(index);
        }

        private static decimal Max(IEnumerable<decimal> values)
        {
            var hasValue = false;
            var max = 0m;
            foreach (var value in values)
            {
                if (!hasValue || value > max)
                {
                    max = value;
                    hasValue = true;
                }
            }
            return max;
        }

        private static decimal Min(IEnumerable<decimal> values)
        {
            var hasValue = false;
            var min = 0m;
            foreach (var value in values)
            {
                if (!hasValue || value < min)
                {
                    min = value;
                    hasValue = true;
                }
            }
            return min;
        }
    }
}
