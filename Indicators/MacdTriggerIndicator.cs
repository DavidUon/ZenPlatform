using System;
using System.Collections.Generic;

namespace ZenPlatform.Indicators
{
    public sealed class MacdTriggerIndicator
    {
        private const int FastPeriod = 21;
        private const int SlowPeriod = 34;
        private const int SignalPeriod = 9;
        private const int PatternWindow = 5;

        private readonly MacdIndicator _macd;
        private readonly Queue<decimal> _difWindow = new();
        private readonly (int TriggerState, decimal Dif, decimal Dea, decimal Macd)[] _values =
            new (int TriggerState, decimal Dif, decimal Dea, decimal Macd)[5];
        private int _valueCount;
        private int _valueHead;

        public MacdTriggerIndicator()
        {
            _macd = new MacdIndicator();
            _macd.SetParameter(FastPeriod, SlowPeriod, SignalPeriod);
            Reset();
        }

        public int Fast => FastPeriod;
        public int Slow => SlowPeriod;
        public int Signal => SignalPeriod;
        public bool IsConfigured => _macd.IsConfigured;
        public bool HasValue => _valueCount > 0;
        public int TriggerState { get; private set; }
        public decimal Dif { get; private set; }
        public decimal Dea { get; private set; }
        public decimal Macd { get; private set; }

        public void Reset()
        {
            _macd.Reset();
            _difWindow.Clear();
            TriggerState = 0;
            Dif = 0m;
            Dea = 0m;
            Macd = 0m;
            _valueCount = 0;
            _valueHead = 0;
        }

        public void Update(KBar bar)
        {
            Update(bar.Close);
        }

        public void Update(decimal close)
        {
            _macd.Update(close);
            if (!_macd.HasValue)
            {
                return;
            }

            var current = _macd.GetValue(0);
            Dif = current.Dif;
            Dea = current.Dea;
            Macd = current.Macd;

            _difWindow.Enqueue(Dif);
            if (_difWindow.Count > PatternWindow)
            {
                _difWindow.Dequeue();
            }

            TriggerState = 0;
            if (_difWindow.Count == PatternWindow)
            {
                var values = _difWindow.ToArray();
                var a = values[0];
                var b = values[1];
                var c = values[2];
                var d = values[3];
                var e = values[4];

                // DIF 上升後轉折向下 => 空觸發
                if (a < b && b < c && c < d && d > e)
                {
                    TriggerState = 1;
                }
                // DIF 下降後轉折向上 => 多觸發
                else if (a > b && b > c && c > d && d < e)
                {
                    TriggerState = -1;
                }
            }

            _values[_valueHead] = (TriggerState, Dif, Dea, Macd);
            _valueHead = (_valueHead + 1) % _values.Length;
            if (_valueCount < _values.Length)
            {
                _valueCount++;
            }
        }

        public (int TriggerState, decimal Dif, decimal Dea, decimal Macd) GetValue(int index)
        {
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

        public (int TriggerState, decimal Dif, decimal Dea, decimal Macd) getvalue(int index)
        {
            return GetValue(index);
        }
    }
}
