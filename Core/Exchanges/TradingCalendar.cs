using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ZenPlatform.Core.Exchanges
{
    public sealed class TradingCalendar
    {
        private readonly HashSet<DateTime> _holidays = new();
        private readonly HashSet<DateTime> _specialOpenDays = new();
        private readonly string _csvPath;

        public TradingCalendar(string csvPath)
        {
            _csvPath = csvPath;
            Load();
        }

        public void Reload()
        {
            Load();
        }

        public bool IsTradingDay(DateTime date)
        {
            var dateOnly = date.Date;
            if (_specialOpenDays.Contains(dateOnly))
                return true;

            if (_holidays.Contains(dateOnly))
                return false;

            return dateOnly.DayOfWeek != DayOfWeek.Saturday && dateOnly.DayOfWeek != DayOfWeek.Sunday;
        }

        private void Load()
        {
            _holidays.Clear();
            _specialOpenDays.Clear();

            if (!File.Exists(_csvPath))
            {
                CreateDefaultTradingCalendarFile(_csvPath);
            }

            foreach (var line in File.ReadLines(_csvPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    continue;
                }

                var status = parts[1].Trim();
                if (status == "休市" || status.ToUpperInvariant() == "HOLIDAY")
                {
                    _holidays.Add(date.Date);
                }
                else if (status == "開市" || status.ToUpperInvariant() == "OPEN")
                {
                    _specialOpenDays.Add(date.Date);
                }
            }
        }

        private static void CreateDefaultTradingCalendarFile(string csvPath)
        {
            var content = "" +
                          "# 市場開休市設定檔\n" +
                          "# 格式: YYYY-MM-DD,狀態,註解\n" +
                          "# 狀態: '開市' 或 '休市'\n" +
                          "\n" +
                          "2025-01-01,休市,元旦\n" +
                          "2025-02-28,休市,和平紀念日\n" +
                          "2025-04-04,休市,兒童節及清明節\n" +
                          "2025-05-01,休市,勞動節\n" +
                          "2025-10-10,休市,國慶日\n";

            try
            {
                File.WriteAllText(csvPath, content, System.Text.Encoding.UTF8);
            }
            catch
            {
                // Ignore file creation failures and rely on weekend rules.
            }
        }
    }
}
