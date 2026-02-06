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
                Console.WriteLine($"[Info] Trading calendar file not found, creating default file: {csvPath}");
                CreateDefaultTradingCalendarFile(csvPath);
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

            Console.WriteLine($"[Info] Trading calendar loaded: {_holidays.Count} holidays, {_specialOpenDays.Count} special open days");
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

        /// <summary>
        /// 建立預設的交易日曆檔案
        /// </summary>
        private void CreateDefaultTradingCalendarFile(string csvPath)
        {
            try
            {
                var defaultContent = @"# --- 說明 ---
# 市場開休市設定檔
# 格式: YYYY-MM-DD,狀態,註解
# 狀態: 請填寫 '開市' 或 '休市'。
#開頭的該行文字均為註解

2025-01-01,休市,元旦
2025-01-23,休市,農曆春節前僅交割
2025-01-24,休市,農曆春節前僅交割
2025-01-27,休市,農曆春節 (調整放假)
2025-01-28,休市,農曆春節 (除夕)
2025-01-29,休市,農曆春節 (初一)
2025-01-30,休市,農曆春節 (初二)
2025-01-31,休市,農曆春節 (初三)
2025-02-08,休市,補行上班日，但股市不開盤
2025-02-28,休市,和平紀念日
2025-04-03,休市,兒童節及清明節 (調整放假)
2025-04-04,休市,兒童節及清明節
2025-05-01,休市,勞動節
2025-05-30,休市,端午節 (補假)
2025-10-06,休市,中秋節
2025-10-10,休市,國慶日

# --- 2026年 臺灣證券交易所/期貨交易所 公告休市日 ---
2026-01-01,休市,元旦
2026-02-16,休市,農曆春節
2026-02-17,休市,農曆春節
2026-02-18,休市,農曆春節
2026-02-19,休市,農曆春節
2026-02-20,休市,農曆春節 (補假)
2026-02-27,休市,和平紀念日 (補假)
2026-04-03,休市,兒童節 (補假)
2026-04-06,休市,清明節 (補假)
2026-05-01,休市,勞動節
2026-06-19,休市,端午節
2026-09-25,休市,中秋節
2026-09-28,休市,教師節
2026-10-09,休市,國慶日 (補假)
2026-10-26,休市,臺灣光復暨金門古寧頭大捷紀念日 (補假)
2026-12-25,休市,行憲紀念日
";

                File.WriteAllText(csvPath, defaultContent, System.Text.Encoding.UTF8);
                Console.WriteLine($"[Info] Default trading calendar file created successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to create default trading calendar file: {ex.Message}");
            }
        }
    }
}
