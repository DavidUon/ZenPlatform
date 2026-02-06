using System;

namespace ZenPlatform.Indicators
{
    public sealed class MacdIndicator
    {
        private int _fast;
        private int _slow;
        private int _signal;
        private decimal? _emaFast;
        private decimal? _emaSlow;
        private decimal? _dea;
        private readonly (decimal Dif, decimal Dea, decimal Macd)[] _values = new (decimal Dif, decimal Dea, decimal Macd)[5];
        private int _valueCount;
        private int _valueHead;
        private bool _configured;

        public MacdIndicator() { }

        public bool IsConfigured => _configured;
        public bool HasValue => _configured && _emaFast.HasValue && _emaSlow.HasValue && _dea.HasValue;
        public decimal Dif { get; private set; }
        public decimal Dea { get; private set; }
        public decimal Macd { get; private set; }

        public void SetParameter(int fast, int slow, int signal)
        {
            _fast = fast <= 0 ? 1 : fast;
            _slow = slow <= 0 ? 1 : slow;
            _signal = signal <= 0 ? 1 : signal;
            _configured = true;
            Reset();
        }

        public void Reset()
        {
            _emaFast = null;
            _emaSlow = null;
            _dea = null;
            Dif = 0;
            Dea = 0;
            Macd = 0;
            _valueCount = 0;
            _valueHead = 0;
        }

        public void Update(decimal close)
        {
            if (!_configured)
            {
                return;
            }

            _emaFast = Ema(_emaFast, close, _fast);
            _emaSlow = Ema(_emaSlow, close, _slow);
            if (!_emaFast.HasValue || !_emaSlow.HasValue)
            {
                return;
            }

            var dif = _emaFast.Value - _emaSlow.Value;
            _dea = Ema(_dea, dif, _signal);
            if (!_dea.HasValue)
            {
                return;
            }

            Dif = dif;
            Dea = _dea.Value;
            Macd = Dif - Dea;

            _values[_valueHead] = (Dif, Dea, Macd);
            _valueHead = (_valueHead + 1) % _values.Length;
            if (_valueCount < _values.Length)
            {
                _valueCount++;
            }
        }

        public (decimal Dif, decimal Dea, decimal Macd) GetValue(int index)
        {
            if (!_configured)
            {
                throw new InvalidOperationException("MACD is not configured.");
            }
            if (index < 0 || index >= _valueCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var pos = _valueHead - 1 - index;
            if (pos < 0)
            {
                pos += _values.Length;
            }
            return _values[pos];
        }

        public (decimal Dif, decimal Dea, decimal Macd) getvalue(int index)
        {
            return GetValue(index);
        }

        private static decimal Ema(decimal? prev, decimal value, int period)
        {
            var k = 2m / (period + 1);
            return prev.HasValue ? prev.Value + (value - prev.Value) * k : value;
        }
    }
}
