using System;
using System.Collections.Generic;
using Charts;
using KChartCore;

namespace ZenPlatform.Core
{
    public sealed class KChartBridge
    {
        private readonly KChartEngine _engine;
        private int _lastVolume;
        private bool _hasVolume;
        private DateTime? _lastSealMinute;
        private bool _volumeBaselinePending;
        private int _lastTotalVolume;
        private int _lastSealedTotalVolume;
        private int _lastFloatingVolume;

        public event Action<PriceType, string>? PriceUpdated;
        public event Action<decimal, int>? TickUpdated;
        public event Action<int, FunctionKBar>? KBarCompleted;
        public bool HistoryReady { get; set; }

        public KChartBridge()
            : this(new KChartEngine())
        {
        }

        public KChartBridge(KChartEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _engine.OnKbarCompleted += (period, bar) =>
            {
                if (period == 1)
                {
                    _lastSealedTotalVolume = _lastTotalVolume;
                }

                KBarCompleted?.Invoke(period, bar);
            };
        }

        public void RegisterPeriod(int period)
        {
            if (period <= 0)
            {
                return;
            }

            _engine.RegPeriod(period);
        }

        public void UnregisterPeriod(int period)
        {
            if (period <= 0)
            {
                return;
            }

            _engine.UnregPeriod(period);
        }

        public List<FunctionKBar> GetHistoryList(int period)
        {
            if (period <= 0)
            {
                period = 1;
            }

            return _engine.GetHistoryList(period);
        }

        public List<FunctionKBar> ImportHistory(string path, DateTime currentTime)
        {
            HistoryReady = false;
            _engine.ImportMmsHistory(path, currentTime);
            _volumeBaselinePending = true;
            HistoryReady = true;
            return _engine.GetHistoryList(1);
        }

        public void AddOneMinuteBar(FunctionKBar bar)
        {
            _engine.AddOneMinuteBar(bar);
        }

        public void Handle(CoreQueueItem item)
        {
            if (item.Type == CoreQueueType.TimeSignal)
            {
                _engine.SetCurrentTime(item.Time);
                var minuteStamp = new DateTime(item.Time.Year, item.Time.Month, item.Time.Day,
                    item.Time.Hour, item.Time.Minute, 0, item.Time.Kind);
                if (_lastSealMinute == null || _lastSealMinute.Value != minuteStamp)
                {
                    _lastSealMinute = minuteStamp;
                    _engine.SealCurrentBar();
                }
                return;
            }

            if (item.Type != CoreQueueType.PriceUpdate || item.Quote == null)
            {
                return;
            }

            var quote = item.Quote;
            switch (quote.Field)
            {
                case QuoteField.Bid:
                    PriceUpdated?.Invoke(PriceType.買價, quote.Value);
                    break;
                case QuoteField.Ask:
                    PriceUpdated?.Invoke(PriceType.賣價, quote.Value);
                    break;
                case QuoteField.Last:
                    PriceUpdated?.Invoke(PriceType.成交價, quote.Value);
                    if (decimal.TryParse(quote.Value, out var tickPrice))
                    {
                        if (quote.IsRequest)
                        {
                            // Request 快照只更新顯示，不餵入 K 棒計算。
                            TickUpdated?.Invoke(tickPrice, _hasVolume ? _lastFloatingVolume : 0);
                            break;
                        }

                        if (!_engine.IsMarketCurrentlyOpen())
                        {
                            // 休市時只更新報價顯示，不把價格餵進K棒引擎，避免生成假K棒
                            TickUpdated?.Invoke(tickPrice, 0);
                            break;
                        }

                        if (_hasVolume)
                        {
                            _engine.SetVolume(_lastVolume);
                        }

                        _engine.SetNewTick(tickPrice);
                        TickUpdated?.Invoke(tickPrice, _hasVolume ? _lastFloatingVolume : 0);
                    }
                    break;
                case QuoteField.Volume:
                    PriceUpdated?.Invoke(PriceType.成交量, quote.Value);
                    if (int.TryParse(quote.Value, out var vol))
                    {
                        if (quote.IsRequest)
                        {
                            // Request 快照只更新顯示，不更新 K 棒引擎的成交量狀態。
                            _lastVolume = vol;
                            _hasVolume = true;
                            _lastTotalVolume = vol;
                            _lastFloatingVolume = 0;
                            break;
                        }

                        if (_volumeBaselinePending)
                        {
                            _volumeBaselinePending = false;
                            _engine.SetVolumeCountBase(vol);
                            _lastTotalVolume = vol;
                            _lastSealedTotalVolume = vol;
                            _hasVolume = true;
                            _lastVolume = vol;
                            break;
                        }

                        _lastTotalVolume = vol;
                        if (quote.IsRequest)
                        {
                            _engine.SetVolumeCountBase(vol);
                            _lastSealedTotalVolume = vol;
                        }

                        _engine.SetVolume(vol);
                        _lastVolume = vol;
                        _hasVolume = true;
                        var floatingVolume = vol - _lastSealedTotalVolume;
                        _lastFloatingVolume = floatingVolume < 0 ? 0 : floatingVolume;
                    }
                    break;
                case QuoteField.Change:
                    PriceUpdated?.Invoke(PriceType.漲跌, quote.Value);
                    break;
                case QuoteField.Time:
                    PriceUpdated?.Invoke(PriceType.報價時間, quote.Value);
                    break;
            }
        }

    }
}
