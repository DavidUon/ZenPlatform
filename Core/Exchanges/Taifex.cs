using System;
using System.Collections.Generic;
using TaifexHisDbManager;

namespace ZenPlatform.Core.Exchanges
{
    public sealed class Taifex : IExchangeClock
    {
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

        private readonly TradingCalendar? _calendar;
        private readonly string _calendarPath;
        private readonly List<TradingWindow> _tradingWindows = new();

        public Taifex(string? tradingCalendarPath = null)
        {
            _calendarPath = string.IsNullOrWhiteSpace(tradingCalendarPath)
                ? MagistockStoragePaths.TradingCalendarPath
                : tradingCalendarPath;
            try
            {
                _calendar = new TradingCalendar(_calendarPath);
            }
            catch
            {
                _calendar = null;
            }
        }

        public string Name => "TAIFEX";

        public void ReloadCalendar()
        {
            _calendar?.Reload();
        }

        public void SetTradingWindow(TimeSpan start, TimeSpan end)
        {
            _tradingWindows.Add(new TradingWindow(start, end));
        }

        public void ClearTradingWindows()
        {
            _tradingWindows.Clear();
        }

        public bool CanTrade(DateTime time)
        {
            if (_tradingWindows.Count == 0)
                return false;

            if (!IsMarketOpen(time))
                return false;

            var t = NormalizeToMinute(time).TimeOfDay;
            foreach (var window in _tradingWindows)
            {
                if (window.Contains(t))
                    return true;
            }

            return false;
        }

        public bool IsMarketOpen(DateTime time)
        {
            time = NormalizeToMinute(time);
            var segment = GetTimeSegment(time.TimeOfDay);
            if (segment == 0)
                return false;

            if (!BaseSchedule.TryGetValue(time.DayOfWeek, out var schedule))
                return false;

            var baseResult = segment switch
            {
                1 => schedule.s1,
                2 => schedule.s2,
                3 => schedule.s3,
                _ => false
            };

            if (!baseResult)
                return false;

            return CheckHolidayRule(time, segment);
        }

        public bool ShouldSealNow(DateTime time)
        {
            time = NormalizeToMinute(time);
            if (time.Second != 0)
                return false;

            if (IsMarketCloseTime(time))
                return IsMarketOpen(time.AddMinutes(-1));

            if (!IsMarketOpen(time))
                return false;

            if (IsMarketOpenTime(time))
                return false;

            return true;
        }

        public bool IsMarketCloseTime(DateTime time)
        {
            time = NormalizeToMinute(time);
            var t = time.TimeOfDay;
            return t == new TimeSpan(5, 0, 0) || t == new TimeSpan(13, 45, 0);
        }

        public bool IsMarketOpenTime(DateTime time)
        {
            time = NormalizeToMinute(time);
            var t = time.TimeOfDay;
            if (t == new TimeSpan(8, 45, 0) || t == new TimeSpan(15, 0, 0))
                return IsMarketOpen(time);

            return false;
        }

        public bool IsSplitNow(DateTime time)
        {
            return ShouldSealNow(time);
        }

        private static int GetTimeSegment(TimeSpan timeOfDay)
        {
            if (timeOfDay < new TimeSpan(5, 0, 0))
                return 1;

            if (timeOfDay >= new TimeSpan(8, 45, 0) && timeOfDay <= new TimeSpan(13, 45, 0))
                return 2;

            if (timeOfDay >= new TimeSpan(15, 0, 0))
                return 3;

            return 0;
        }

        private bool CheckHolidayRule(DateTime time, int segment)
        {
            if (_calendar == null)
                return true;

            var today = time.Date;
            var yesterday = today.AddDays(-1);

            return segment switch
            {
                1 => _calendar.IsTradingDay(yesterday),
                2 or 3 => _calendar.IsTradingDay(today),
                _ => false
            };
        }

        private static DateTime NormalizeToMinute(DateTime time)
        {
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
        }

        private readonly record struct TradingWindow(TimeSpan Start, TimeSpan End)
        {
            public bool Contains(TimeSpan time)
            {
                if (Start == End)
                    return true;

                if (Start < End)
                    return time >= Start && time < End;

                return time >= Start || time < End;
            }
        }
    }
}
