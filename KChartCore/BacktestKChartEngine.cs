using System;
using System.Collections.Generic;
using System.Linq;
using Utility;

namespace KChartCore
{
    public class BacktestKChartEngine : KChartEngine
    {
        private const int MaxHistoryCount = 10000;

        public override void AddOneMinuteBar(FunctionKBar bar)
        {
            if (bar.IsNullBar)
            {
                return;
            }

            _currentTime = bar.CloseTime;
            bar.IsFloating = false;
            _oneMinuteHistory.Add(bar);

            if (_oneMinuteHistory.Count > MaxHistoryCount)
            {
                _oneMinuteHistory.RemoveAt(0);
            }

            AppendOneMinuteBar(bar);
            ProcessMultiPeriodAggregationFast();
        }

        private void ProcessMultiPeriodAggregationFast()
        {
            foreach (var periodKvp in Separator)
            {
                int period = periodKvp.Key;
                if (period == 1)
                {
                    continue;
                }

                var separateTable = periodKvp.Value;
                var tableTime = new DateTime(2000, 1, 1, _currentTime.Hour, _currentTime.Minute, 0);
                if (!separateTable.TryGetValue(tableTime, out var isSep) || !isSep)
                {
                    continue;
                }

                var bar = AggregateCurrentPeriodBars(period, tableTime);
                if (bar.IsNullBar)
                {
                    continue;
                }

                RaiseKbarCompleted(period, bar);
            }
        }

        private FunctionKBar AggregateCurrentPeriodBars(int period, DateTime currentTableTime)
        {
            DateTime? previousSeparationTime = null;
            for (int minutesBack = 1; minutesBack <= period; minutesBack++)
            {
                DateTime checkTime = currentTableTime.AddMinutes(-minutesBack);
                if (Separator.TryGetValue(period, out var table) && table.TryGetValue(checkTime, out var isSep) && isSep)
                {
                    previousSeparationTime = checkTime;
                    break;
                }
            }

            var periodBars = new List<FunctionKBar>();
            for (int i = _oneMinuteHistory.Count - 1; i >= 0; i--)
            {
                var bar = _oneMinuteHistory[i];
                var barTableTime = new DateTime(2000, 1, 1, bar.CloseTime.Hour, bar.CloseTime.Minute, 0);

                bool inRange;
                if (previousSeparationTime.HasValue)
                {
                    inRange = barTableTime > previousSeparationTime && barTableTime <= currentTableTime;
                }
                else
                {
                    inRange = periodBars.Count < period;
                }

                if (inRange)
                {
                    periodBars.Add(bar);
                }

                if (previousSeparationTime.HasValue)
                {
                    if (barTableTime <= previousSeparationTime)
                    {
                        break;
                    }
                }
                else if (periodBars.Count >= period)
                {
                    break;
                }
            }

            if (periodBars.Count == 0)
            {
                return new FunctionKBar { IsNullBar = true, IsFloating = false };
            }

            periodBars.Reverse();
            var firstBar = periodBars[0];
            var lastBar = periodBars[periodBars.Count - 1];

            var aggregated = new FunctionKBar
            {
                StartTime = firstBar.StartTime,
                CloseTime = lastBar.CloseTime,
                Open = firstBar.Open,
                High = periodBars.Max(b => b.High),
                Low = periodBars.Min(b => b.Low),
                Close = lastBar.Close,
                Volume = periodBars.Sum(b => b.Volume),
                ContainsMarketOpen = periodBars.Any(b => b.ContainsMarketOpen),
                ContainsMarketClose = periodBars.Any(b => b.ContainsMarketClose),
                IsNullBar = periodBars.All(b => b.IsNullBar),
                IsFloating = false,
                IsAlignmentBar = periodBars.Any(b => b.IsAlignmentBar)
            };

            return aggregated;
        }
    }
}
