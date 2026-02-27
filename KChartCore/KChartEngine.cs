using Utility;

namespace KChartCore
{
    public class KChartEngine : OneMinBarBase
    {
        protected Dictionary<int, Dictionary<DateTime, bool>> Separator;
        private readonly Dictionary<int, DateTime> _lastPeriodCompletedCloseTime = new();

        // 公開基類的 protected 事件
        public new event Action<int, FunctionKBar>? OnKbarCompleted;

        protected void RaiseKbarCompleted(int period, FunctionKBar bar)
        {
            OnKbarCompleted?.Invoke(period, bar);
        }

        public KChartEngine()
        {
            Separator = new Dictionary<int, Dictionary<DateTime, bool>>();

            // 訂閱基類的 protected 事件並轉發到 public 事件
            base.OnKbarCompleted += (period, bar) => OnKbarCompleted?.Invoke(period, bar);
        }

        public void RegPeriod(int n)
        {
            if (!Separator.ContainsKey(n))
            {
                var separateTable = new Dictionary<DateTime, bool>();

                // 放入1440個時間元素 (一天24小時 * 60分鐘)
                DateTime baseTime = new DateTime(2000, 1, 1, 0, 0, 0);
                for (int minute = 0; minute < 1440; minute++)
                {
                    DateTime timeKey = baseTime.AddMinutes(minute);
                    separateTable[timeKey] = false;
                }

                // 使用繼承來的 _tfxRule 計算分割點
                _tfxRule.CalSepTable(n, ref separateTable);

                Separator[n] = separateTable;
            }

            // 顯示目前註冊的週期列表
            var periodList = "";
            foreach (var period in Separator.Keys)
            {
                periodList += period + ", ";
            }
            if (periodList.Length > 0)
                periodList = periodList.Substring(0, periodList.Length - 2); // 移除最後的 ", "
        }

        public void UnregPeriod(int n)
        {
            if (Separator.ContainsKey(n))
            {
                Separator.Remove(n);
            }

            // 顯示目前註冊的週期列表
            var periodList = "";
            foreach (var period in Separator.Keys)
            {
                periodList += period + ", ";
            }
            if (periodList.Length > 0)
                periodList = periodList.Substring(0, periodList.Length - 2); // 移除最後的 ", "
        }

        public List<FunctionKBar> GetHistoryList(int period)
        {
            // 檢查並註冊週期
            bool wasRegistered = Separator.ContainsKey(period);
            if (!wasRegistered)
            {
                RegPeriod(period);
            }

            var result = new List<FunctionKBar>();

            try
            {
                // 取得一分鐘歷史資料
                var oneMinBars = GetRawDataList(int.MaxValue); // 取得所有歷史資料

                // 取得分割表
                var separateTable = Separator[period];

                // 聚合K棒
                var currentPeriodBars = new List<FunctionKBar>();

                foreach (var bar in oneMinBars)
                {
                    currentPeriodBars.Add(bar);

                    // 檢查是否到分割點
                    DateTime barTime = new DateTime(2000, 1, 1, bar.CloseTime.Hour, bar.CloseTime.Minute, 0);

                    if (separateTable.ContainsKey(barTime) && separateTable[barTime])
                    {
                        // 到達分割點，聚合當前週期的K棒
                        if (currentPeriodBars.Count > 0)
                        {
                            // 檢查當前週期內是否有浮動K棒
                            bool hasFloatingBar = currentPeriodBars.Any(b => b.IsFloating);
                            var aggregatedBar = AggregateBars(currentPeriodBars, hasFloatingBar, period);
                            result.Add(aggregatedBar);
                            currentPeriodBars.Clear();
                        }
                    }
                }

                // 處理剩餘的餘數K棒（如果有餘數K棒，聚合）
                if (currentPeriodBars.Count > 0)
                {
                    // 檢查餘數K棒中是否有浮動的，或者本身就是餘數所以應該浮動
                    bool hasFloatingBar = currentPeriodBars.Any(b => b.IsFloating) || true; // 餘數K棒預設為浮動
                    var floatingBar = AggregateBars(currentPeriodBars, hasFloatingBar, period);
                    result.Add(floatingBar);
                }
            }
            finally
            {
                // 如果是臨時註冊的，使用完後反註冊
                if (!wasRegistered)
                {
                    UnregPeriod(period);
                }
            }

            return result;
        }

        private FunctionKBar AggregateBars(List<FunctionKBar> bars, bool isFloating, int periodMinutes)
        {
            if (bars.Count == 0)
                throw new ArgumentException("Cannot aggregate empty bar list");

            var firstBar = bars[0];
            var lastBar = bars[bars.Count - 1];

            var aggregated = new FunctionKBar
            {
                StartTime = firstBar.StartTime,
                CloseTime = lastBar.CloseTime,
                Open = firstBar.Open,
                High = bars.Max(b => b.High),
                Low = bars.Min(b => b.Low),
                Close = lastBar.Close,
                Volume = bars.Sum(b => b.Volume),
                ContainsMarketOpen = bars.Any(b => b.ContainsMarketOpen),
                ContainsMarketClose = bars.Any(b => b.ContainsMarketClose),
                IsNullBar = bars.All(b => b.IsNullBar),
                IsFloating = isFloating,
                IsAlignmentBar = bars.Any(b => b.IsAlignmentBar)
            };

            // 浮動K棒：將 CloseTime 預估為下一個週期分割點（受市場規則約束）
            if (isFloating)
            {
                aggregated.CloseTime = EstimateNextSeparationTime(lastBar.CloseTime, periodMinutes);
            }

            return aggregated;
        }

        private DateTime EstimateNextSeparationTime(DateTime referenceCloseTime, int periodMinutes)
        {
            // 起點：對齊至分，向後+1分鐘開始找
            var start = new DateTime(referenceCloseTime.Year, referenceCloseTime.Month, referenceCloseTime.Day,
                                     referenceCloseTime.Hour, referenceCloseTime.Minute, 0).AddMinutes(1);
            if (referenceCloseTime == default(DateTime) || referenceCloseTime.Year < 2001)
            {
                var ct = _currentTime == default(DateTime) ? DateTime.Now : _currentTime;
                start = new DateTime(ct.Year, ct.Month, ct.Day, ct.Hour, ct.Minute, 0).AddMinutes(1);
            }

            if (!Separator.ContainsKey(periodMinutes))
            {
                RegPeriod(periodMinutes);
            }
            var table = Separator[periodMinutes];

            // 最多掃描兩天，避免極端情況
            var t = start;
            for (int i = 0; i < 1440 * 2; i++)
            {
                var key = new DateTime(2000, 1, 1, t.Hour, t.Minute, 0);
                // 尋找符合分割表且當時為開市中的時間（不限定於開盤時刻本身）
                if (table.TryGetValue(key, out bool isSep) && isSep && _tfxRule.IsMarketOpen(t))
                {
                    return t;
                }
                t = t.AddMinutes(1);
            }
            return start;
        }

        private FunctionKBar GetCurrentPeriodBar(int period)
        {
            var separateTable = Separator[period];
            var oneMinBars = GetRawDataList(int.MaxValue);

            if (oneMinBars.Count == 0)
                throw new InvalidOperationException("No one minute bars available");

            // 1. 先檢查當前時間是否為切割點
            DateTime currentTableTime = new DateTime(2000, 1, 1, _currentTime.Hour, _currentTime.Minute, 0);
            if (!separateTable.ContainsKey(currentTableTime) || !separateTable[currentTableTime])
            {
                throw new InvalidOperationException("Current time is not a separation point");
            }
            // 2. 在分割表中往前搜尋，找出上一個切割點
            DateTime? previousSeparationTime = null;
            for (int minutesBack = 1; minutesBack <= period; minutesBack++)
            {
                DateTime checkTime = currentTableTime.AddMinutes(-minutesBack);
                if (separateTable.ContainsKey(checkTime) && separateTable[checkTime])
                {
                    previousSeparationTime = checkTime;
                    break;
                }
            }

            // 3. 從 _oneMinuteHistory 尾端往前找，收集符合時間的K棒
            var periodBars = new List<FunctionKBar>();

            for (int i = oneMinBars.Count - 1; i >= 0; i--)
            {
                var bar = oneMinBars[i];
                int barHour = bar.CloseTime.Hour;
                int barMinute = bar.CloseTime.Minute;

                // 判斷是否在聚合範圍內（大於上一個切割點，小於等於當前時間）
                bool inRange = false;
                if (previousSeparationTime.HasValue)
                {
                    // 有找到上一個切割點：收集 > 切割點 且 <= 當前時間的K棒
                    DateTime barTableTime = new DateTime(2000, 1, 1, barHour, barMinute, 0);
                    inRange = barTableTime > previousSeparationTime && barTableTime <= currentTableTime;
                }
                else
                {
                    // 沒找到切割點：收集最近N根K棒
                    inRange = periodBars.Count < period;
                }

                if (inRange)
                {
                    periodBars.Add(bar);
                }

                // 如果已找到切割點且K棒時間小於等於切割點，停止搜尋
                if (previousSeparationTime.HasValue)
                {
                    DateTime barTableTime = new DateTime(2000, 1, 1, barHour, barMinute, 0);
                    if (barTableTime <= previousSeparationTime)
                    {
                        break;
                    }
                }

                // 如果沒找到切割點但已收集足夠K棒，停止搜尋
                if (!previousSeparationTime.HasValue && periodBars.Count >= period)
                {
                    break;
                }
            }

            if (periodBars.Count == 0)
                throw new InvalidOperationException("No bars found for current period");

            // 4. 按時間順序排列並聚合
            periodBars = periodBars.OrderBy(b => b.CloseTime).ToList();
            var result = AggregateBars(periodBars, false, period);
            return result;
        }


        public virtual void AddOneMinuteBar(FunctionKBar bar)
        {
            if (bar.IsNullBar)
            {
                return;
            }

            _currentTime = bar.CloseTime;
            AppendOneMinuteBar(bar);
            ProcessMultiPeriodAggregation();
        }

        public new void SealCurrentBar()
        {
            // 先呼叫base讓它處理一分鐘K棒並觸發正確的事件
            var sealedOneMinute = base.SealCurrentBar();

            // 只有真的封出新的1分鐘K棒，才處理多週期聚合
            if (!sealedOneMinute)
            {
                return;
            }

            ProcessMultiPeriodAggregation();
        }

        private void ProcessMultiPeriodAggregation()
        {
            // 處理已註冊的各週期聚合邏輯（跳過1分鐘週期，因為已由基類處理）
            foreach (var periodKvp in Separator)
            {
                int period = periodKvp.Key;

                // 跳過1分鐘週期，避免與基類事件重複
                if (period == 1) continue;

                var separateTable = periodKvp.Value;

                // 將當前時間轉換為分割表的時間格式 (2000/1/1 基準)
                DateTime tableTime = new DateTime(2000, 1, 1, _currentTime.Hour, _currentTime.Minute, 0);

                // 檢查當前時間是否為該週期的分割點
                if (separateTable.ContainsKey(tableTime) && separateTable[tableTime])
                {
                    // 到達分割點，取得剛完成的週期K棒並觸發事件
                    try
                    {
                        var currentPeriodBar = GetCurrentPeriodBar(period);
                        if (_lastPeriodCompletedCloseTime.TryGetValue(period, out var lastClose) &&
                            lastClose == currentPeriodBar.CloseTime)
                        {
                            continue;
                        }

                        _lastPeriodCompletedCloseTime[period] = currentPeriodBar.CloseTime;
                        OnKbarCompleted?.Invoke(period, currentPeriodBar);
                    }
                    catch (Exception ex)
                    {
                        Db.Msg($"週期 {period} 聚合失敗: {ex.Message}");
                    }
                }
            }
        }

        public void ClearAllKChartData()
        {
            // 清空所有K線資料
            ClearAllData(); // 呼叫基類 protected 方法清空一分K資料
        }
    }
}
