using System;
using System.Threading;

namespace ZenPlatform.Core
{
    public interface IExchangeClock
    {
        string Name { get; }
        bool IsMarketOpen(DateTime time);
        bool ShouldSealNow(DateTime time);
    }

    public class TimeCtrl : IDisposable
    {
        private readonly Timer _timer;
        private readonly IExchangeClock _exchangeClock;
        private DateTime? _lastMinuteStamp;
        private bool? _lastIsMarketOpen;
        private static readonly TimeZoneInfo TaipeiTimeZone = ResolveTaipeiTimeZone();

        public event EventHandler<DateTime>? TimeTick;
        public event EventHandler<bool>? MarketOpenChanged;

        public TimeCtrl(IExchangeClock exchangeClock)
        {
            _exchangeClock = exchangeClock ?? throw new ArgumentNullException(nameof(exchangeClock));
            _timer = new Timer(OnTick, null, 0, 200);
        }

        public DateTime Now => DateTime.Now;
        public bool IsMarketOpenNow => _exchangeClock.IsMarketOpen(DateTime.Now);
        public DateTime ExchangeTime => TimeZoneInfo.ConvertTime(DateTime.UtcNow, TaipeiTimeZone);
        public (int Year, int Month) GetCurrentContractMonth(DateTime? time = null)
        {
            var baseTime = time ?? ExchangeTime;
            var thirdWed = GetThirdWednesday(baseTime.Year, baseTime.Month);
            var switchTime = new DateTime(baseTime.Year, baseTime.Month, thirdWed.Day, 15, 0, 0, baseTime.Kind);
            if (baseTime >= switchTime)
            {
                var next = baseTime.AddMonths(1);
                return (next.Year, next.Month);
            }

            return (baseTime.Year, baseTime.Month);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        private void OnTick(object? state)
        {
            var now = DateTime.Now;
            TimeTick?.Invoke(this, now);

            var minuteStamp = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
            if (_lastMinuteStamp == null || _lastMinuteStamp.Value != minuteStamp)
            {
                _lastMinuteStamp = minuteStamp;
                var isOpen = _exchangeClock.IsMarketOpen(now);
                if (_lastIsMarketOpen == null || _lastIsMarketOpen.Value != isOpen)
                {
                    _lastIsMarketOpen = isOpen;
                    MarketOpenChanged?.Invoke(this, isOpen);
                }
            }
        }

        private static DateTime GetThirdWednesday(int year, int month)
        {
            var firstDay = new DateTime(year, month, 1);
            var offset = ((int)DayOfWeek.Wednesday - (int)firstDay.DayOfWeek + 7) % 7;
            var firstWednesday = firstDay.AddDays(offset);
            return firstWednesday.AddDays(14);
        }

        private static TimeZoneInfo ResolveTaipeiTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
            }
        }
    }
}
