using System.Collections.Generic;

namespace ZenPlatform.Indicators
{
    public sealed class MovingAverage
    {
        private readonly Queue<decimal> _window = new();
        private decimal _sum;
        private readonly decimal[] _values = new decimal[5];
        private int _valueCount;
        private int _valueHead;
        private bool _configured;

        public MovingAverage() { }

        public int Period { get; private set; }
        public bool IsConfigured => _configured;
        public bool HasValue => _configured && _window.Count == Period;
        public decimal Value { get; private set; }

        public void SetParameter(int period)
        {
            Period = period <= 0 ? 1 : period;
            _configured = true;
            Reset();
        }

        public void Reset()
        {
            _window.Clear();
            _sum = 0;
            Value = 0;
            _valueCount = 0;
            _valueHead = 0;
        }

        public void Update(decimal close)
        {
            if (!_configured)
            {
                return;
            }

            _window.Enqueue(close);
            _sum += close;
            if (_window.Count > Period)
            {
                _sum -= _window.Dequeue();
            }

            if (_window.Count == Period)
            {
                Value = _sum / Period;
                _values[_valueHead] = Value;
                _valueHead = (_valueHead + 1) % _values.Length;
                if (_valueCount < _values.Length)
                {
                    _valueCount++;
                }
            }
        }

        public decimal GetValue(int index)
        {
            if (!_configured)
            {
                throw new System.InvalidOperationException("MA is not configured.");
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

        public decimal getvalue(int index)
        {
            return GetValue(index);
        }
    }
}
