using System;
using TaifexHisDbManager;

namespace ZenPlatform.Strategy
{
    internal static class TradingTimeService
    {
        private static readonly TimeSpan DaySessionStart = new(8, 45, 0);
        private static readonly TimeSpan DaySessionEnd = new(13, 45, 0);
        private static readonly TimeSpan NightSessionStart = new(15, 0, 0);
        private static readonly TimeSpan NightSessionEnd = new(5, 0, 0);
        private static readonly object HolidayCalendarSync = new();
        private static ZenPlatform.Core.Exchanges.TradingCalendar? _holidayCalendar;
        private static bool _holidayCalendarInitialized;

        internal static bool IsInDaySession(TimeSpan time) => time >= DaySessionStart && time <= DaySessionEnd;

        internal static bool IsInNightSession(TimeSpan time) => time >= NightSessionStart || time < NightSessionEnd;

        internal static bool IsInCreateSessionWindow(DateTime now, RuleSet ruleSet)
        {
            var time = now.TimeOfDay;
            if (IsInDaySession(time))
            {
                return IsWithinRange(time, ruleSet.DaySessionStart, ruleSet.DaySessionEnd, allowCross: false);
            }

            if (IsInNightSession(time))
            {
                return IsWithinRange(time, ruleSet.NightSessionStart, ruleSet.NightSessionEnd, allowCross: true);
            }

            return false;
        }

        internal static bool IsWithinRange(TimeSpan time, TimeSpan start, TimeSpan end, bool allowCross)
        {
            if (!allowCross || start <= end)
            {
                return time >= start && time <= end;
            }

            return time >= start || time <= end;
        }

        internal static bool ShouldCloseBeforeDaySessionEnd(DateTime now, RuleSet ruleSet)
        {
            var time = now.TimeOfDay;
            return IsInDaySession(time) &&
                   ruleSet.CloseBeforeDaySessionEnd &&
                   time >= ruleSet.DayCloseBeforeTime;
        }

        internal static bool ShouldCloseBeforeNightSessionEnd(DateTime now, RuleSet ruleSet)
        {
            var time = now.TimeOfDay;
            if (!ruleSet.CloseBeforeNightSessionEnd || !IsInNightSession(time))
            {
                return false;
            }

            var trigger = ruleSet.NightCloseBeforeTime;
            return trigger >= NightSessionStart
                ? (time >= trigger || time < NightSessionEnd)
                : (time < NightSessionEnd && time >= trigger);
        }

        internal static bool ShouldCloseBeforeLongHoliday(DateTime now, RuleSet ruleSet, int minClosedDays = 2)
        {
            if (!ruleSet.CloseBeforeLongHoliday)
            {
                return false;
            }

            var time = now.TimeOfDay;
            if (time >= NightSessionEnd || time < ruleSet.CloseBeforeLongHolidayTime)
            {
                return false;
            }

            // 00:00~04:59 belongs to previous trading date's night session.
            var tradingDate = ResolveTradingDate(now);
            if (!IsTradingDay(tradingDate))
            {
                return false;
            }

            return CountConsecutiveClosedDaysAfter(tradingDate) >= minClosedDays;
        }

        internal static DateTime ResolveTradingDate(DateTime now)
        {
            return now.TimeOfDay < NightSessionEnd ? now.Date.AddDays(-1) : now.Date;
        }

        internal static int CountConsecutiveClosedDaysAfter(DateTime tradingDate, int maxScanDays = 60)
        {
            var closedDays = 0;
            var probeDate = tradingDate.Date.AddDays(1);
            for (var i = 0; i < maxScanDays; i++)
            {
                if (IsTradingDay(probeDate))
                {
                    break;
                }

                closedDays++;
                probeDate = probeDate.AddDays(1);
            }

            return closedDays;
        }

        internal static bool IsTradingDay(DateTime date)
        {
            EnsureHolidayCalendar();
            if (_holidayCalendar != null)
            {
                return _holidayCalendar.IsTradingDay(date.Date);
            }

            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
        }

        private static void EnsureHolidayCalendar()
        {
            if (_holidayCalendarInitialized)
            {
                return;
            }

            lock (HolidayCalendarSync)
            {
                if (_holidayCalendarInitialized)
                {
                    return;
                }

                try
                {
                    var path = MagistockStoragePaths.TradingCalendarPath;
                    _holidayCalendar = new ZenPlatform.Core.Exchanges.TradingCalendar(path);
                }
                catch
                {
                    _holidayCalendar = null;
                }
                finally
                {
                    _holidayCalendarInitialized = true;
                }
            }
        }
    }
}
