using System.IO;
using Utility;

namespace KChartCore
{
    /// <summary>
    /// 台指期貨市場規則 - 使用三段式時間表格判斷
    /// 完全基於用戶定義的邏輯重新實作
    /// </summary>
    public class TaifexRule : IMarketRule
    {
        // 基本開盤時段對照表：[第一段, 第二段, 第三段]
        private static readonly Dictionary<DayOfWeek, (bool s1, bool s2, bool s3)> BaseSchedule = new()
        {
            { DayOfWeek.Monday,    (false, true,  true)  },
            { DayOfWeek.Tuesday,   (true,  true,  true)  },
            { DayOfWeek.Wednesday, (true,  true,  true)  },
            { DayOfWeek.Thursday,  (true,  true,  true)  },
            { DayOfWeek.Friday,    (true,  true,  true)  },
            { DayOfWeek.Saturday,  (true,  false, false) },
            { DayOfWeek.Sunday,    (false, false, false) }
        };

        private readonly TradingCalendar? _tradingCalendar;

        public event Action<string>? DebugMsg;

        public TaifexRule(string? tradingCalendarPath = null)
        {
            try
            {
                var calendarPath = string.IsNullOrWhiteSpace(tradingCalendarPath)
                    ? Path.Combine(AppContext.BaseDirectory, "台灣國定假日定義.txt")
                    : tradingCalendarPath;
                _tradingCalendar = new TradingCalendar(calendarPath);
            }
            catch
            {
                // Ignore calendar load failures and fall back to base schedule.
            }
        }

        public string MarketName => "台灣期貨交易所";

        public IEnumerable<TradingSession> TradingSessions { get; } = new[]
        {
            new TradingSession { Name = "第一段", StartTime = new TimeSpan(0, 0, 0), EndTime = new TimeSpan(5, 0, 0), CrossDay = true },
            new TradingSession { Name = "第二段", StartTime = new TimeSpan(8, 45, 0), EndTime = new TimeSpan(13, 45, 0), CrossDay = false },
            new TradingSession { Name = "第三段", StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(24, 0, 0), CrossDay = false }
        };

        /// <summary>
        /// 核心邏輯：判斷指定時間是否為市場開市時間
        /// </summary>
        public bool IsMarketOpen(DateTime time)
        {
            // 1. 判斷屬於哪個時間段
            int segment = GetTimeSegment(time.TimeOfDay);
            if (segment == 0) return false; // 不在任何時間段內

            // 2. 查詢基本表格
            if (!BaseSchedule.TryGetValue(time.DayOfWeek, out var schedule))
                return false;

            bool baseResult = segment switch
            {
                1 => schedule.s1,
                2 => schedule.s2,
                3 => schedule.s3,
                _ => false
            };

            if (!baseResult) return false; // 基本表格就是false

            // 3. 檢查國定假日影響
            return CheckHolidayRule(time, segment);
        }

        /// <summary>
        /// 判斷時間屬於哪個段 (1=第一段, 2=第二段, 3=第三段, 0=休市段)
        /// </summary>
        private int GetTimeSegment(TimeSpan timeOfDay)
        {
            // 第一段：[00:00-05:00]
            if (timeOfDay <= new TimeSpan(5, 0, 0))
                return 1;

            // 第二段：[08:45-13:45]
            if (timeOfDay >= new TimeSpan(8, 45, 0) && timeOfDay <= new TimeSpan(13, 45, 0))
                return 2;

            // 第三段：[15:00-24:00]
            if (timeOfDay >= new TimeSpan(15, 0, 0))
                return 3;

            // 其他時間：休市段
            return 0;
        }

        /// <summary>
        /// 國定假日影響規則：
        /// 如果當天是國定假日 → 當天的第二段、第三段，還有隔天的第一段都是false
        /// </summary>
        private bool CheckHolidayRule(DateTime time, int segment)
        {
            if (_tradingCalendar == null)
                return true; // 沒有假日資料，依基本表格

            DateTime today = time.Date;
            DateTime yesterday = today.AddDays(-1);

            return segment switch
            {
                1 => _tradingCalendar.IsTradingDay(yesterday),     // 第一段：檢查昨天是否假日
                2 or 3 => _tradingCalendar.IsTradingDay(today),   // 第二段、第三段：檢查今天是否假日
                _ => false
            };
        }

        /// <summary>
        /// 關鍵時間點判斷：05:00, 08:45, 13:45, 15:00
        /// </summary>
        public bool IsMarketOpenTime(DateTime time)
        {
            TimeSpan timeOfDay = time.TimeOfDay;
            
            // 08:45 和 15:00 是開盤時刻
            if (timeOfDay == new TimeSpan(8, 45, 0) || timeOfDay == new TimeSpan(15, 0, 0))
            {
                return IsMarketOpen(time);
            }
            
            return false;
        }

        /// <summary>
        /// 收盤時刻判斷：05:00 和 13:45
        /// </summary>
        public bool IsMarketCloseTime(DateTime time)
        {
            TimeSpan timeOfDay = time.TimeOfDay;
            
            // 05:00 和 13:45 是收盤時刻
            if (timeOfDay == new TimeSpan(5, 0, 0) || timeOfDay == new TimeSpan(13, 45, 0))
            {
                return IsMarketOpen(time);
            }
            
            return false;
        }

        /// <summary>
        /// 開盤K棒判斷
        /// </summary>
        public bool IsOpeningBar(DateTime currentTime)
        {
            DateTime oneMinuteAgo = currentTime.AddMinutes(-1);
            return !IsMarketOpen(oneMinuteAgo) && IsMarketOpen(currentTime);
        }

        /// <summary>
        /// 收盤K棒判斷
        /// </summary>
        public bool IsClosingBar(DateTime currentTime)
        {
            DateTime nextMinute = currentTime.AddMinutes(1);
            return IsMarketOpen(currentTime) && !IsMarketOpen(nextMinute);
        }

        /// <summary>
        /// K棒封存判斷：如果沒開市就根本不會有輸出
        /// </summary>
        public bool IsSealKbar(DateTime time)
        {
            // 收盤時間特殊處理：使用 TradingSessions 定義的結束時間
            foreach (var session in TradingSessions)
            {
                if (time.TimeOfDay == session.EndTime)
                {
                    return true; // 收盤時間必須封棒
                }
            }

            bool isOpen = IsMarketOpen(time);
            bool isOpenTime = IsMarketOpenTime(time);

            if (!isOpen)
                return false;

            if (isOpenTime)
                return false;

            return true;
        }

        /// <summary>
        /// 週期判斷
        /// </summary>
        public bool ShouldClosePeriod(DateTime currentTime, int periodMinutes)
        {
            if (!IsMarketOpen(currentTime))
                return false;

            DateTime periodStart = GetPeriodStartTime(currentTime, periodMinutes);
            DateTime nextPeriodStart = periodStart.AddMinutes(periodMinutes);
            return currentTime >= nextPeriodStart;
        }

        /// <summary>
        /// 週期開始時間計算
        /// </summary>
        public DateTime GetPeriodStartTime(DateTime time, int periodMinutes)
        {
            if (!IsMarketOpen(time))
                return time;

            int segment = GetTimeSegment(time.TimeOfDay);
            
            return segment switch
            {
                2 => CalculatePeriodStart(time, new TimeSpan(8, 45, 0), periodMinutes),
                3 => CalculatePeriodStart(time, new TimeSpan(15, 0, 0), periodMinutes),
                1 => CalculateNightPeriodStart(time, periodMinutes),
                _ => time
            };
        }

        private DateTime CalculatePeriodStart(DateTime currentTime, TimeSpan sessionStart, int periodMinutes)
        {
            DateTime sessionStartTime = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day,
                                                    sessionStart.Hours, sessionStart.Minutes, sessionStart.Seconds);
            
            TimeSpan elapsed = currentTime - sessionStartTime;
            int elapsedMinutes = (int)elapsed.TotalMinutes;
            int periodIndex = elapsedMinutes / periodMinutes;
            
            return sessionStartTime.AddMinutes(periodIndex * periodMinutes);
        }

        private DateTime CalculateNightPeriodStart(DateTime time, int periodMinutes)
        {
            DateTime previousDay = time.AddDays(-1);
            DateTime nightSessionStart = new DateTime(previousDay.Year, previousDay.Month, previousDay.Day, 15, 0, 0);
            
            TimeSpan totalMinutes = time - nightSessionStart;
            int totalMinutesInt = (int)totalMinutes.TotalMinutes;
            int periodIndex = totalMinutesInt / periodMinutes;
            
            return nightSessionStart.AddMinutes(periodIndex * periodMinutes);
        }

        /// <summary>
        /// 交易日判斷
        /// </summary>
        public bool IsTradingDay(DateTime date)
        {
            if (_tradingCalendar != null)
            {
                return _tradingCalendar.IsTradingDay(date);
            }
            
            // 簡化邏輯：週六、週日不交易
            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
        }

        /// <summary>
        /// 計算分割表：標記需要切割K棒的時間點
        /// </summary>
        public void CalSepTable(int period, ref Dictionary<DateTime, bool> table)
        {
            bool isDisMsg = true;
            DateTime baseTime = new DateTime(2000, 1, 1, 0, 0, 0);

            for (int minute = 0; minute < 1440; minute++)
            {
                DateTime timeKey = baseTime.AddMinutes(minute);
                TimeSpan timeOfDay = timeKey.TimeOfDay;
                bool shouldSeparate = false;

                // 簡化邏輯：統一以15:00為基準點對齊 + 收盤強制切割

                // 收盤強制切割
                if (timeOfDay == new TimeSpan(5, 0, 0) || timeOfDay == new TimeSpan(13, 45, 0))
                {
                    shouldSeparate = true;
                }
                // 統一15:00基準點對齊
                else
                {
                    // 計算從交易日15:00開始的分鐘數
                    int minutesFrom1500;
                    if (timeOfDay >= new TimeSpan(15, 0, 0))
                    {
                        // 夜盤當日部分 (15:00-23:59)
                        minutesFrom1500 = minute - 900; // 15:00 = 900分鐘
                    }
                    else
                    {
                        // 夜盤跨日部分 (00:00-05:00) + 日盤 (08:45-13:45)
                        // 前一天15:00到當天這個時間的總分鐘數
                        minutesFrom1500 = (24 * 60 - 900) + minute; // 前一天15:00後的分鐘數 + 今天經過的分鐘數
                    }

                    // 基於15:00基準點的週期對齊
                    if (minutesFrom1500 > 0 && minutesFrom1500 % period == 0)
                    {
                        shouldSeparate = true;
                    }
                }

                table[timeKey] = shouldSeparate;

                if (shouldSeparate)
                {
                    Db.Msg($"分割點: {timeKey:HH:mm}", isDisMsg);
                }
            }
        }

        /// <summary>
        /// 顯示完整邏輯表格 (除錯用)
        /// </summary>
        public void PrintCompleteLogic()
        {
            DebugMsg?.Invoke("=== 台指期貨開盤判斷完整邏輯 ===");
            DebugMsg?.Invoke("");
            DebugMsg?.Invoke("📅 三段式時間劃分：");
            DebugMsg?.Invoke("第一段：[00:00-05:00] - 夜盤跨日");
            DebugMsg?.Invoke("第二段：[08:45-13:45] - 日盤");
            DebugMsg?.Invoke("第三段：[15:00-24:00] - 夜盤");
            DebugMsg?.Invoke("");
            DebugMsg?.Invoke("📊 基本開盤表格：");
            DebugMsg?.Invoke("星期   | 第一段 | 第二段 | 第三段");
            DebugMsg?.Invoke("-------|--------|--------|--------");
            
            foreach (var kvp in BaseSchedule.OrderBy(x => (int)x.Key))
            {
                var (s1, s2, s3) = kvp.Value;
                string dayName = kvp.Key switch
                {
                    DayOfWeek.Monday => "週一",
                    DayOfWeek.Tuesday => "週二", 
                    DayOfWeek.Wednesday => "週三",
                    DayOfWeek.Thursday => "週四",
                    DayOfWeek.Friday => "週五",
                    DayOfWeek.Saturday => "週六",
                    DayOfWeek.Sunday => "週日",
                    _ => "未知"
                };

                string s1Str = s1 ? "✅" : "❌";
                string s2Str = s2 ? "✅" : "❌";
                string s3Str = s3 ? "✅" : "❌";

                DebugMsg?.Invoke($"{dayName,-6} | {s1Str,-6} | {s2Str,-6} | {s3Str}");
            }
            
            DebugMsg?.Invoke("");
            DebugMsg?.Invoke("🏮 國定假日影響規則：");
            DebugMsg?.Invoke("如果當天是國定假日：");
            DebugMsg?.Invoke("├─ 當天的第二段 = false");
            DebugMsg?.Invoke("├─ 當天的第三段 = false");
            DebugMsg?.Invoke("└─ 隔天的第一段 = false");
            DebugMsg?.Invoke("");
            DebugMsg?.Invoke("⏰ 關鍵時間點：");
            DebugMsg?.Invoke("05:00 → 收盤K棒");
            DebugMsg?.Invoke("08:45 → 開盤K棒");
            DebugMsg?.Invoke("13:45 → 收盤K棒");
            DebugMsg?.Invoke("15:00 → 開盤K棒");
            DebugMsg?.Invoke("");
            DebugMsg?.Invoke("🔔 輸出規則：如果沒開市就根本不會有輸出");
            DebugMsg?.Invoke("=======================================");
        }
    }
}
