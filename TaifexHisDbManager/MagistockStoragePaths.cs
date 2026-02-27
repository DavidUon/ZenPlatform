using System;
using System.IO;

namespace TaifexHisDbManager
{
    public static class MagistockStoragePaths
    {
        private static string _rootFolder = LoadRootFolder();
        private const string CalendarFileName = "台灣國定假日定義.txt";

        // 統一 Magistock 資料庫根路徑（供歷史/回測/行事曆等使用）
        public static string MagistockLibPath => _rootFolder;
        public static string RootFolder => _rootFolder;
        public static string CalendarFolder => Path.Combine(_rootFolder, "行事曆");
        public static string TradingCalendarPath => Path.Combine(CalendarFolder, CalendarFileName);
        public static string DownloadZipFolder => Path.Combine(_rootFolder, "期貨交易所下載Zip");
        public static string BacktestDbFolder => Path.Combine(_rootFolder, "回測資料庫");
        public static string BacktestReportFolder => Path.Combine(_rootFolder, "台指二號回測報表");
        public static string ImportedFolder => Path.Combine(DownloadZipFolder, "已匯入");
        public static string CsvTempFolder => Path.Combine(DownloadZipFolder, "_CsvTemp");

        public static void EnsureInitialized()
        {
            EnsureFolders();
            EnsureTradingCalendarFile();
        }

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(_rootFolder);
            Directory.CreateDirectory(CalendarFolder);
            Directory.CreateDirectory(BacktestDbFolder);
            Directory.CreateDirectory(BacktestReportFolder);
        }

        public static bool TrySetRootFolder(string? folder, out string? error)
        {
            error = "系統資料庫路徑為固定路徑，無法修改。";
            return false;
        }

        private static string LoadRootFolder()
        {
            return ResolveDefaultRootFolder();
        }

        private static string ResolveDefaultRootFolder()
        {
            var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
            var installDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var installParent = Directory.GetParent(installDir)?.FullName;
            if (string.IsNullOrWhiteSpace(installParent))
            {
                return Path.Combine(installDir, "Magistock資料庫");
            }

            return Path.Combine(installParent, "Magistock資料庫");
        }

        private static void EnsureTradingCalendarFile()
        {
            try
            {
                if (File.Exists(TradingCalendarPath))
                {
                    return;
                }

                File.WriteAllText(TradingCalendarPath, DefaultTradingCalendarContent, System.Text.Encoding.UTF8);
            }
            catch
            {
                // best effort
            }
        }

        private const string DefaultTradingCalendarContent = @"# --- 說明 ---
# 市場開休市設定檔
# 格式: YYYY-MM-DD,狀態,註解
# 狀態: 請填寫 '開市' 或 '休市'。
#開頭的該行文字均為註解

# --- 2026年 臺灣證券交易所/期貨交易所 公告休市日 ---
2026-01-01,休市,元旦
2026-02-12,休市,市場無交易，僅辦理結算交割作業
2026-02-13,休市,市場無交易，僅辦理結算交割作業
2026-02-16,休市,農曆春節
2026-02-17,休市,農曆春節
2026-02-18,休市,農曆春節
2026-02-19,休市,農曆春節
2026-02-20,休市,農曆春節
2026-02-27,休市,和平紀念日 (補假)
2026-04-03,休市,兒童節 (補假)
2026-04-06,休市,民族掃墓節 (補假)
2026-05-01,休市,勞動節
2026-06-19,休市,端午節
2026-09-25,休市,中秋節
2026-09-28,休市,孔子誕辰紀念日
2026-10-09,休市,國慶日 (補假)
2026-10-26,休市,臺灣光復暨金門古寧頭大捷紀念日 (補假)
2026-12-25,休市,行憲紀念日

# --- 2025年 國定假日及市場無交易日 ---
2025-01-01,休市,開國紀念日
2025-01-23,休市,市場無交易，僅辦理結算交割作業
2025-01-24,休市,市場無交易，僅辦理結算交割作業
2025-01-27,休市,農曆除夕前一日
2025-01-28,休市,農曆除夕
2025-01-29,休市,農曆春節
2025-01-30,休市,農曆春節
2025-01-31,休市,農曆春節
2025-02-28,休市,和平紀念日
2025-04-03,休市,兒童節及民族掃墓節 (調整放假)
2025-04-04,休市,兒童節及民族掃墓節
2025-05-01,休市,勞動節
2025-05-30,休市,端午節 (補假)
2025-09-29,休市,孔子誕辰紀念日 (補假)
2025-10-06,休市,中秋節
2025-10-10,休市,國慶日
2025-10-24,休市,臺灣光復暨金門古寧頭大捷紀念日 (補假)
2025-12-25,休市,行憲紀念日

# --- 2024年 國定假日及市場無交易日 ---
2024-01-01,休市,開國紀念日
2024-02-06,休市,市場無交易，僅辦理結算交割作業
2024-02-07,休市,市場無交易，僅辦理結算交割作業
2024-02-08,休市,農曆除夕前一日
2024-02-09,休市,農曆除夕
2024-02-12,休市,農曆春節
2024-02-13,休市,農曆春節 (補假)
2024-02-14,休市,農曆春節 (補假)
2024-02-28,休市,和平紀念日
2024-04-04,休市,兒童節
2024-04-05,休市,民族掃墓節
2024-05-01,休市,勞動節
2024-06-10,休市,端午節
2024-09-17,休市,中秋節
2024-10-10,休市,國慶日

# --- 2023年 國定假日及市場無交易日 ---
2023-01-02,休市,開國紀念日 (補假)
2023-01-18,休市,市場無交易，僅辦理結算交割作業
2023-01-19,休市,市場無交易，僅辦理結算交割作業
2023-01-20,休市,農曆除夕前一日
2023-01-23,休市,農曆春節
2023-01-24,休市,農曆春節
2023-01-25,休市,農曆春節
2023-01-26,休市,農曆春節
2023-01-27,休市,農曆春節 (調整放假)
2023-02-27,休市,和平紀念日 (調整放假)
2023-02-28,休市,和平紀念日
2023-04-03,休市,兒童節 (調整放假)
2023-04-04,休市,兒童節
2023-04-05,休市,民族掃墓節
2023-05-01,休市,勞動節
2023-06-22,休市,端午節
2023-06-23,休市,端午節 (調整放假)
2023-09-29,休市,中秋節
2023-10-09,休市,國慶日 (調整放假)
2023-10-10,休市,國慶日

# --- 2022年 國定假日及市場無交易日 ---
2022-01-03,休市,開國紀念日 (補假)
2022-01-27,休市,市場無交易，僅辦理結算交割作業
2022-01-28,休市,市場無交易，僅辦理結算交割作業
2022-01-31,休市,農曆除夕
2022-02-01,休市,農曆春節
2022-02-02,休市,農曆春節
2022-02-03,休市,農曆春節
2022-02-04,休市,農曆春節
2022-02-28,休市,和平紀念日
2022-04-04,休市,兒童節
2022-04-05,休市,民族掃墓節
2022-05-02,休市,勞動節 (補假)
2022-06-03,休市,端午節
2022-09-09,休市,中秋節 (補假)
2022-10-10,休市,國慶日

# --- 2021年 國定假日及市場無交易日 ---
2021-01-01,休市,開國紀念日
2021-02-08,休市,市場無交易，僅辦理結算交割作業
2021-02-09,休市,市場無交易，僅辦理結算交割作業
2021-02-10,休市,農曆除夕前一日
2021-02-11,休市,農曆除夕
2021-02-12,休市,農曆春節
2021-02-15,休市,農曆春節
2021-02-16,休市,農曆春節
2021-03-01,休市,和平紀念日 (補假)
2021-04-02,休市,兒童節及民族掃墓節 (調整放假)
2021-04-05,休市,兒童節及民族掃墓節 (補假)
2021-04-30,休市,勞動節 (調整放假)
2021-06-14,休市,端午節
2021-09-20,休市,中秋節 (調整放假)
2021-09-21,休市,中秋節
2021-10-11,休市,國慶日 (補假)
2021-12-31,休市,開國紀念日 (補假)
";
    }
}
