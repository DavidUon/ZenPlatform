using System;
using ZenPlatform.SessionManager;
using ZenPlatform.Debug;

namespace ZenPlatform.Core
{
    public sealed class CoreEventHub
    {
        private readonly SessionManager.SessionManager[] _sessionManagers;
        private readonly KChartBridge _chartBridge;

        public CoreEventHub(SessionManager.SessionManager[] sessionManagers, KChartBridge chartBridge)
        {
            _sessionManagers = sessionManagers ?? throw new ArgumentNullException(nameof(sessionManagers));
            _chartBridge = chartBridge ?? throw new ArgumentNullException(nameof(chartBridge));
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

                if (manager.Indicators == null)
                {
                    continue;
                }

                if (manager.RuleSet.KbarPeriod != period)
                {
                    continue;
                }

                lock (manager.IndicatorSync)
                {
                    var closeStamp = new DateTime(bar.CloseTime.Year, bar.CloseTime.Month, bar.CloseTime.Day,
                        bar.CloseTime.Hour, bar.CloseTime.Minute, 0, DateTimeKind.Unspecified);
                    if (manager.LastIndicatorBarCloseTime.HasValue && closeStamp <= manager.LastIndicatorBarCloseTime.Value)
                    {
                        continue;
                    }

                    manager.Indicators.Update(new ZenPlatform.Indicators.KBar(bar.StartTime, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
                    if (manager.SuppressIndicatorLog)
                    {
                        // Suppressed during history import.
                    }
                    // KDJ log comes only from RebuildIndicators for a single source of truth.

                    manager.LastIndicatorBarCloseTime = closeStamp;
                    if (manager.IndicatorsReadyForLog && !manager.SuppressIndicatorLog)
                    {
                        DebugBus.Send($"[M{manager.Index + 1}] {manager.BuildIndicatorSnapshot()}", "指標");
                    }
                }
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
                foreach (var manager in _sessionManagers)
                {
                    if (manager.IsStrategyRunning && manager.AcceptPriceTicks)
                    {
                        if (manager.IsBacktestActive)
                        {
                            continue;
                        }
                        manager.OnTick(item.Quote);
                    }
                }
            }

        _chartBridge.Handle(item);
            ItemReceived?.Invoke(this, item);
            ItemDispatched?.Invoke(this, item);
        }
    }
}
