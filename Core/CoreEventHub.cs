using System;
using ZenPlatform.SessionManager;
using ZenPlatform.Trade;

namespace ZenPlatform.Core
{
    public sealed class CoreEventHub
    {
        private readonly SessionManager.SessionManager[] _sessionManagers;
        private readonly KChartBridge _chartBridge;
        private readonly TradeCtrl _tradeCtrl;

        public CoreEventHub(SessionManager.SessionManager[] sessionManagers, KChartBridge chartBridge, TradeCtrl tradeCtrl)
        {
            _sessionManagers = sessionManagers ?? throw new ArgumentNullException(nameof(sessionManagers));
            _chartBridge = chartBridge ?? throw new ArgumentNullException(nameof(chartBridge));
            _tradeCtrl = tradeCtrl ?? throw new ArgumentNullException(nameof(tradeCtrl));
            _chartBridge.KBarCompleted += OnKBarCompleted;
        }

        public event EventHandler<CoreQueueItem>? ItemReceived;
        public event EventHandler<CoreQueueItem>? ItemDispatched;

        private void OnKBarCompleted(int period, KChartCore.FunctionKBar bar)
        {
            if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
            {
                return;
            }

            foreach (var manager in _sessionManagers)
            {
                if (!manager.IsStrategyRunning)
                {
                    continue;
                }

                if (manager.IsBacktestActive)
                {
                    continue;
                }

                if (!manager.IsKBarPeriodRegistered(period))
                {
                    continue;
                }

                var kbar = new ZenPlatform.Strategy.KBar(bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
                manager.OnKBarCompleted(period, kbar);
            }
        }

        public void Dispatch(CoreQueueItem item)
        {
            if (item.Type == CoreQueueType.TimeSignal)
            {
                foreach (var manager in _sessionManagers)
                {
                    if (manager.IsStrategyRunning && manager.AcceptSecondTicks)
                    {
                        if (manager.IsBacktestActive)
                        {
                            continue;
                        }
                        manager.OnSecond(item.Time);
                    }
                }
            }

            if (item.Type == CoreQueueType.PriceUpdate && item.Quote != null)
            {
                var quote = item.Quote;
                if (!quote.IsRequest)
                {
                    _tradeCtrl.OnQuote(quote);
                    foreach (var manager in _sessionManagers)
                    {
                        if (manager.IsStrategyRunning && manager.AcceptPriceTicks)
                        {
                            if (manager.IsBacktestActive)
                            {
                                continue;
                            }
                            manager.OnTick(quote);
                            continue;
                        }

                    }
                }
            }

            _chartBridge.Handle(item);
            ItemReceived?.Invoke(this, item);
            ItemDispatched?.Invoke(this, item);
        }
    }
}
