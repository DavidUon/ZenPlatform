using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace KChartCore
{
    /// <summary>
    /// 負責管理市場的交易日曆規則。
    /// 唯一的職責是從外部檔案載入規則，並判斷某一天是否為交易日。
    /// </summary>
    public class TradingCalendar
    {
        private readonly HashSet<DateTime> _holidays = new();
        private readonly HashSet<DateTime> _specialOpenDays = new();
        /// <summary>
        /// 建立一個交易日曆物件，並從指定的 CSV 檔案載入規則。
        /// </summary>
        /// <param name="csvPath">交易日曆設定檔的路徑。</param>
        public TradingCalendar(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                return;
            }

            // 讀取 CSV 檔案，跳過註解行
            var lines = File.ReadLines(csvPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                var parts = line.Split(',');
                if (parts.Length < 2) continue;

                if (DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    var status = parts[1].Trim();
                    if (status == "休市" || status.ToUpperInvariant() == "HOLIDAY")
                    {
                        _holidays.Add(date);
                    }
                    else if (status == "開市" || status.ToUpperInvariant() == "OPEN")
                    {
                        _specialOpenDays.Add(date);
                    }
                }
            }

            // Swallow logging here to avoid console dependency in WPF.
        }

        /// <summary>
        /// 根據已載入的規則，判斷指定的日期是否為交易日。
        /// </summary>
        /// <param name="date">要檢查的日期 (時間部分會被忽略)。</param>
        /// <returns>如果是交易日則為 true，否則為 false。</returns>
        public bool IsTradingDay(DateTime date)
        {
            // 只取日期部分進行比較
            var dateOnly = date.Date;

            // 規則 1：優先檢查「強制開市」的例外清單
            if (_specialOpenDays.Contains(dateOnly))
            {
                return true;
            }

            // 規則 2：接著檢查「強制休市」的例外清單
            if (_holidays.Contains(dateOnly))
            {
                return false;
            }

            // 規則 3：如果沒有任何例外，套用預設的週末規則
            var dayOfWeek = dateOnly.DayOfWeek;
            if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
            {
                return false;
            }

            // 規則 4：最後，如果以上都不是，那它就是一個正常的交易日
            return true;
        }

    }
}
