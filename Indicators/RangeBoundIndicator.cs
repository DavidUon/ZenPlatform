using System;

namespace ZenPlatform.Indicators
{
    public sealed class RangeBoundIndicator
    {
        private enum TrackMode
        {
            Both,
            SeekHigh,
            SeekLow
        }

        private const int AtrPeriodValue = 14;
        private const decimal ImpulseKValue = 3.0m;
        private const decimal RetraceKValue = 2.0m;
        private const decimal MinThresholdValue = 30m;
        private const int ValueCapacity = 5;

        private readonly (decimal A, decimal V, int State)[] _values = new (decimal A, decimal V, int State)[ValueCapacity];
        private int _valueCount;
        private int _valueHead;

        private bool _initialized;
        private decimal _prevClose;
        private decimal _prevAtr;
        private int _barCount;

        private TrackMode _mode;
        private decimal _high;
        private int _highSeq;
        private decimal _low;
        private int _lowSeq;
        private int _seq;

        private decimal _lastAtr;
        private decimal _lastImpulseThreshold;
        private decimal _lastRetraceThreshold;
        private bool _lastATriggerCandidate;
        private bool _lastVTriggerCandidate;

        public int AtrPeriod => AtrPeriodValue;
        // Backward compatible alias: retrace multiplier.
        public decimal K => RetraceKValue;
        public decimal ImpulseK => ImpulseKValue;
        public decimal RetraceK => RetraceKValue;
        public decimal MinThreshold => MinThresholdValue;
        public bool IsConfigured => true;
        public bool HasValue => _valueCount > 0;

        // 最新確認的 A/V 價位（若尚未確認則為 0）
        public decimal A { get; private set; }
        public decimal V { get; private set; }

        // 0=None, 1=A, 2=V（代表本次更新是否確認到新轉折）
        public int State { get; private set; }

        public string GetDebugStatus()
        {
            return $"mode={_mode}, seq={_seq}, high={_high:0.##}, low={_low:0.##}, " +
                   $"highSeq={_highSeq}, lowSeq={_lowSeq}, atr={_lastAtr:0.####}, " +
                   $"impulseTh={_lastImpulseThreshold:0.##}, retraceTh={_lastRetraceThreshold:0.##}, " +
                   $"aCand={_lastATriggerCandidate}, vCand={_lastVTriggerCandidate}, A={A:0.##}, V={V:0.##}, state={State}";
        }

        public void Reset()
        {
            _initialized = false;
            _prevClose = 0m;
            _prevAtr = 0m;
            _barCount = 0;
            _mode = TrackMode.Both;
            _high = 0m;
            _highSeq = 0;
            _low = 0m;
            _lowSeq = 0;
            _seq = 0;
            A = 0m;
            V = 0m;
            State = 0;
            _valueCount = 0;
            _valueHead = 0;
        }

        public void Update(KBar bar)
        {
            Update(bar.High, bar.Low, bar.Close);
        }

        public void Update(decimal high, decimal low, decimal close)
        {
            _seq++;

            decimal tr;
            if (!_initialized)
            {
                tr = high - low;
            }
            else
            {
                var hl = high - low;
                var hc = Math.Abs(high - _prevClose);
                var lc = Math.Abs(low - _prevClose);
                tr = Math.Max(hl, Math.Max(hc, lc));
            }

            _barCount++;
            var n = AtrPeriodValue;
            var atr = !_initialized
                ? tr
                : (_prevAtr * (n - 1) + tr) / n;
            _prevAtr = atr;
            _lastAtr = atr;

            var impulseThreshold = atr * ImpulseKValue;
            if (impulseThreshold < MinThresholdValue)
            {
                impulseThreshold = MinThresholdValue;
            }
            _lastImpulseThreshold = impulseThreshold;

            var retraceThreshold = atr * RetraceKValue;
            if (retraceThreshold < MinThresholdValue)
            {
                retraceThreshold = MinThresholdValue;
            }
            _lastRetraceThreshold = retraceThreshold;

            State = 0;
            _lastATriggerCandidate = false;
            _lastVTriggerCandidate = false;

            if (!_initialized)
            {
                _initialized = true;
                _high = high;
                _low = low;
                _highSeq = _seq;
                _lowSeq = _seq;
                _mode = TrackMode.Both;
            }
            else
            {
                if (_mode == TrackMode.Both)
                {
                    if (high >= _high)
                    {
                        _high = high;
                        _highSeq = _seq;
                    }
                    if (low <= _low)
                    {
                        _low = low;
                        _lowSeq = _seq;
                    }

                    // A 點：先有一段上漲（low -> high >= 3*ATR），再回檔（high -> low >= 2*ATR）
                    var aTrig = _lowSeq < _highSeq &&
                                (_high - _low) >= impulseThreshold &&
                                (_high - low) >= retraceThreshold;
                    // V 點：先有一段下跌（high -> low >= 3*ATR），再反彈（low -> high >= 2*ATR）
                    var vTrig = _highSeq < _lowSeq &&
                                (_high - _low) >= impulseThreshold &&
                                (high - _low) >= retraceThreshold;
                    _lastATriggerCandidate = aTrig;
                    _lastVTriggerCandidate = vTrig;

                    if (aTrig && vTrig)
                    {
                        if (_highSeq <= _lowSeq)
                        {
                            ConfirmA(_high);
                            _mode = TrackMode.SeekLow;
                            _low = low;
                            _lowSeq = _seq;
                        }
                        else
                        {
                            ConfirmV(_low);
                            _mode = TrackMode.SeekHigh;
                            _high = high;
                            _highSeq = _seq;
                        }
                    }
                    else if (aTrig)
                    {
                        ConfirmA(_high);
                        _mode = TrackMode.SeekLow;
                        _low = low;
                        _lowSeq = _seq;
                    }
                    else if (vTrig)
                    {
                        ConfirmV(_low);
                        _mode = TrackMode.SeekHigh;
                        _high = high;
                        _highSeq = _seq;
                    }
                }
                else if (_mode == TrackMode.SeekHigh)
                {
                    // Keep both extremes fresh while waiting for next A confirmation.
                    if (low <= _low)
                    {
                        _low = low;
                        _lowSeq = _seq;
                    }

                    if (high >= _high)
                    {
                        _high = high;
                        _highSeq = _seq;
                    }
                    if ((_high - _low) >= impulseThreshold &&
                        (_high - low) >= retraceThreshold)
                    {
                        _lastATriggerCandidate = true;
                        ConfirmA(_high);
                        _mode = TrackMode.SeekLow;
                        _low = low;
                        _lowSeq = _seq;
                    }
                }
                else
                {
                    // Keep both extremes fresh while waiting for next V confirmation.
                    if (high >= _high)
                    {
                        _high = high;
                        _highSeq = _seq;
                    }

                    if (low <= _low)
                    {
                        _low = low;
                        _lowSeq = _seq;
                    }
                    if ((_high - _low) >= impulseThreshold &&
                        (high - _low) >= retraceThreshold)
                    {
                        _lastVTriggerCandidate = true;
                        ConfirmV(_low);
                        _mode = TrackMode.SeekHigh;
                        _high = high;
                        _highSeq = _seq;
                    }
                }
            }

            _prevClose = close;
            _values[_valueHead] = (A, V, State);
            _valueHead = (_valueHead + 1) % _values.Length;
            if (_valueCount < _values.Length)
            {
                _valueCount++;
            }
        }

        public (decimal A, decimal V, int State) GetValue(int index)
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

        public (decimal A, decimal V, int State) getvalue(int index)
        {
            return GetValue(index);
        }

        private void ConfirmA(decimal price)
        {
            A = price;
            State = 1;
        }

        private void ConfirmV(decimal price)
        {
            V = price;
            State = 2;
        }
    }
}
