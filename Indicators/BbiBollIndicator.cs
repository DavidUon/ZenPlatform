using System;
using System.Collections.Generic;

namespace ZenPlatform.Indicators
{
    public sealed class BbiBollIndicator
    {
        private readonly Queue<decimal>[] _bbiPriceWindows = new Queue<decimal>[4];
        private readonly decimal[] _bbiPriceSums = new decimal[4];
        private readonly int[] _bbiPeriods = new int[4];
        private readonly Queue<decimal> _bollCloseWindow = new();
        private decimal _bollCloseSum;
        private decimal _bollCloseSumSq;
        private readonly (decimal Bbi, decimal Mid, decimal Up, decimal Dn)[] _values = new (decimal Bbi, decimal Mid, decimal Up, decimal Dn)[5];
        private int _valueCount;
        private int _valueHead;
        private bool _configured;

        public BbiBollIndicator()
        {
            SetParameter(13, 56, 89, 144, 14, 1m);
        }

        public int BbiPeriod1 { get; private set; }
        public int BbiPeriod2 { get; private set; }
        public int BbiPeriod3 { get; private set; }
        public int BbiPeriod4 { get; private set; }
        public int BollPeriod { get; private set; }
        public decimal BollK { get; private set; }
        public bool IsConfigured => _configured;
        public bool HasValue => _configured && _bollCloseWindow.Count > 0;
        public decimal Bbi { get; private set; }
        public decimal Mid { get; private set; }
        public decimal Up { get; private set; }
        public decimal Dn { get; private set; }

        public void SetParameter(int bbi1, int bbi2, int bbi3, int bbi4, int bollPeriod, decimal bollK)
        {
            BbiPeriod1 = bbi1 <= 0 ? 1 : bbi1;
            BbiPeriod2 = bbi2 <= 0 ? 1 : bbi2;
            BbiPeriod3 = bbi3 <= 0 ? 1 : bbi3;
            BbiPeriod4 = bbi4 <= 0 ? 1 : bbi4;
            BollPeriod = bollPeriod <= 0 ? 1 : bollPeriod;
            BollK = bollK < 0 ? 0 : bollK;
            _bbiPeriods[0] = BbiPeriod1;
            _bbiPeriods[1] = BbiPeriod2;
            _bbiPeriods[2] = BbiPeriod3;
            _bbiPeriods[3] = BbiPeriod4;
            _configured = true;
            Reset();
        }

        public void Reset()
        {
            for (var i = 0; i < _bbiPriceWindows.Length; i++)
            {
                _bbiPriceWindows[i] = new Queue<decimal>();
                _bbiPriceSums[i] = 0m;
            }

            _bollCloseWindow.Clear();
            _bollCloseSum = 0m;
            _bollCloseSumSq = 0m;
            Bbi = 0m;
            Mid = 0m;
            Up = 0m;
            Dn = 0m;
            _valueCount = 0;
            _valueHead = 0;
        }

        public void Update(KBar bar)
        {
            Update(bar.Close);
        }

        public void Update(decimal close)
        {
            if (!_configured)
            {
                return;
            }

            decimal bbiSum = 0m;
            for (var i = 0; i < _bbiPriceWindows.Length; i++)
            {
                var period = _bbiPeriods[i];
                var win = _bbiPriceWindows[i];
                win.Enqueue(close);
                _bbiPriceSums[i] += close;
                if (win.Count > period)
                {
                    _bbiPriceSums[i] -= win.Dequeue();
                }

                bbiSum += _bbiPriceSums[i] / win.Count;
            }

            var bbi = bbiSum / _bbiPriceWindows.Length;
            _bollCloseWindow.Enqueue(close);
            _bollCloseSum += close;
            _bollCloseSumSq += close * close;
            if (_bollCloseWindow.Count > BollPeriod)
            {
                var removed = _bollCloseWindow.Dequeue();
                _bollCloseSum -= removed;
                _bollCloseSumSq -= removed * removed;
            }

            var windowCount = _bollCloseWindow.Count;
            var closeMean = windowCount > 0 ? _bollCloseSum / windowCount : 0m;
            var variance = windowCount > 0 ? (_bollCloseSumSq / windowCount) - (closeMean * closeMean) : 0m;
            if (variance < 0m)
            {
                variance = 0m;
            }

            var std = (decimal)Math.Sqrt((double)variance);
            Bbi = bbi;
            Mid = bbi;
            Up = Mid + BollK * std;
            Dn = Mid - BollK * std;

            _values[_valueHead] = (Bbi, Mid, Up, Dn);
            _valueHead = (_valueHead + 1) % _values.Length;
            if (_valueCount < _values.Length)
            {
                _valueCount++;
            }
        }

        public (decimal Bbi, decimal Mid, decimal Up, decimal Dn) GetValue(int index)
        {
            if (!_configured)
            {
                throw new InvalidOperationException("BBIBOLL is not configured.");
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

        public (decimal Bbi, decimal Mid, decimal Up, decimal Dn) getvalue(int index)
        {
            return GetValue(index);
        }
    }
}
