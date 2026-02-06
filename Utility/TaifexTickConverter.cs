using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
    /// <summary>
    /// 轉換後的 Tick 資料結構
    /// </summary>
    public class ConvertedTickData
    {
        public DateTime DateTime { get; set; }
        public decimal Price { get; set; }
        public int Volume { get; set; }
        public string EventType { get; set; } = "";
    }

    /// <summary>
    /// 期交所 Tick 資料轉換器 - 重新設計版本
    /// 從期交所原始 CSV 檔案中擷取當月台指期貨資料(TX, MTX, TMF)
    /// 確保每個檔案只包含一個交易日的資料
    /// </summary>
    public class TaifexTickConverter
    {
        /// <summary>
        /// 轉換期交所 Tick 資料
        /// </summary>
        /// <param name="inputFilePath">輸入 CSV 檔案完整路徑</param>
        /// <param name="outputDirectory">輸出目錄</param>
        /// <returns>轉換結果：(轉換檔案數量, 生成的檔案名稱列表)</returns>
        public (int, List<string>) ConvertFile(string inputFilePath, string outputDirectory)
        {
            string fileName = Path.GetFileName(inputFilePath);

            if (!File.Exists(inputFilePath))
            {
                throw new FileNotFoundException($"輸入檔案不存在: {inputFilePath}");
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // 從檔名解析交易日期
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFilePath);
            DateTime tradeDate = ParseTradeDateFromFileName(fileNameWithoutExt);

            // 計算當月合約月份
            string currentMonthContract = GetCurrentMonthContract(tradeDate);

            // 輸出檔案前綴
            string outputPrefix = $"{tradeDate:yyyyMMdd}";

            // 讀取並轉換資料
            ConvertTicks(inputFilePath, outputDirectory, outputPrefix, currentMonthContract, tradeDate);

            return (1, new List<string> { fileName });
        }

        /// <summary>
        /// 從檔名解析交易日期
        /// </summary>
        private DateTime ParseTradeDateFromFileName(string fileName)
        {
            // 期交所檔名格式: Daily_2025_09_10
            if (fileName.StartsWith("Daily_"))
            {
                string dateStr = fileName.Substring(6).Replace("_", "");
                if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, DateTimeStyles.None, out DateTime date))
                    return date;
            }

            throw new ArgumentException($"無法解析檔名中的日期: {fileName}");
        }

        /// <summary>
        /// 計算指定交易日的當月合約月份
        /// </summary>
        private string GetCurrentMonthContract(DateTime tradeDate)
        {
            // 計算該月第三個星期三（結算日）
            DateTime settlementDate = GetThirdWednesday(tradeDate.Year, tradeDate.Month);

            // 如果交易日在結算日之後，使用下個月合約
            if (tradeDate > settlementDate)
            {
                DateTime nextMonth = tradeDate.AddMonths(1);
                return $"{nextMonth:yyyyMM}";
            }
            else
            {
                return $"{tradeDate:yyyyMM}";
            }
        }

        /// <summary>
        /// 計算指定年月的第三個星期三
        /// </summary>
        private DateTime GetThirdWednesday(int year, int month)
        {
            DateTime firstDay = new DateTime(year, month, 1);

            // 找到第一個星期三
            int daysUntilWednesday = (int)DayOfWeek.Wednesday - (int)firstDay.DayOfWeek;
            if (daysUntilWednesday < 0)
                daysUntilWednesday += 7;

            DateTime firstWednesday = firstDay.AddDays(daysUntilWednesday);

            // 第三個星期三
            return firstWednesday.AddDays(14);
        }

        /// <summary>
        /// 讀取並轉換 Tick 資料
        /// </summary>
        private void ConvertTicks(string inputFilePath, string outputDirectory, string outputPrefix,
            string targetMonth, DateTime tradeDate)
        {
            // 三種商品的資料儲存
            var txTicks = new List<ConvertedTickData>();
            var mtxTicks = new List<ConvertedTickData>();
            var tmfTicks = new List<ConvertedTickData>();

            // 讀取檔案（Big5 編碼）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            int totalLines = 0;
            int validLines = 0;

            using (var reader = new StreamReader(inputFilePath, Encoding.GetEncoding("big5")))
            {
                string? line;
                bool isFirstLine = true;

                while ((line = reader.ReadLine()) != null)
                {
                    totalLines++;

                    // 跳過標題行
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // 解析 CSV 行
                    string[] fields = line.Split(',');
                    if (fields.Length < 6)
                        continue;

                    string dateStr = fields[0].Trim();
                    string productCode = fields[1].Trim();
                    string expireMonth = fields[2].Trim();
                    string timeStr = fields[3].Trim();
                    string priceStr = fields[4].Trim();
                    string volumeStr = fields[5].Trim();

                    // 只處理目標月份的資料
                    if (expireMonth != targetMonth)
                        continue;

                    // 處理跨日邏輯：檔案名稱的日期對應夜盤結束日期
                    // 但CSV中的資料可能包含前一天和當天的資料
                    DateTime csvDate;
                    if (!DateTime.TryParseExact(dateStr, "yyyyMMdd", null, DateTimeStyles.None, out csvDate))
                        continue;

                    // 檢查日期是否在合理範圍內（前一天或當天）
                    if (csvDate != tradeDate.AddDays(-1) && csvDate != tradeDate)
                        continue;

                    // 解析時間、價格、成交量
                    if (!DateTime.TryParseExact($"{dateStr} {timeStr.PadLeft(6, '0')}", "yyyyMMdd HHmmss",
                        null, DateTimeStyles.None, out DateTime dateTime) ||
                        !decimal.TryParse(priceStr, out decimal price) ||
                        !int.TryParse(volumeStr, out int volume))
                        continue;

                    validLines++;

                    var tickData = new ConvertedTickData
                    {
                        DateTime = dateTime,
                        Price = price,
                        Volume = volume,
                        EventType = ""
                    };

                    // 根據商品代號分類
                    switch (productCode)
                    {
                        case "TX":
                            txTicks.Add(tickData);
                            break;
                        case "MTX":
                            mtxTicks.Add(tickData);
                            break;
                        case "TMF":
                            tmfTicks.Add(tickData);
                            break;
                    }
                }
            }

            // 建立三個子資料夾
            string txFolder = Path.Combine(outputDirectory, "大型台指");
            string mtxFolder = Path.Combine(outputDirectory, "小型台指");
            string tmfFolder = Path.Combine(outputDirectory, "微型台指");

            Directory.CreateDirectory(txFolder);
            Directory.CreateDirectory(mtxFolder);
            Directory.CreateDirectory(tmfFolder);

            // 處理和輸出檔案
            ProcessAndWriteTickFile(Path.Combine(txFolder, $"TX_{outputPrefix}.ztick"), txTicks, tradeDate);
            ProcessAndWriteTickFile(Path.Combine(mtxFolder, $"MTX_{outputPrefix}.ztick"), mtxTicks, tradeDate);
            ProcessAndWriteTickFile(Path.Combine(tmfFolder, $"TMF_{outputPrefix}.ztick"), tmfTicks, tradeDate);

            // 輸出統計資訊
            Console.WriteLine($"轉換完成: {tradeDate:yyyy/MM/dd}");
            Console.WriteLine($"當月合約: {targetMonth}");
            Console.WriteLine($"TX 筆數: {txTicks.Count:N0}");
            Console.WriteLine($"MTX 筆數: {mtxTicks.Count:N0}");
            Console.WriteLine($"TMF 筆數: {tmfTicks.Count:N0}");
        }

        /// <summary>
        /// 處理並寫入 Tick 檔案
        /// </summary>
        private void ProcessAndWriteTickFile(string filePath, List<ConvertedTickData> ticks, DateTime tradeDate)
        {
            if (ticks.Count == 0)
                return;

            // 按時間排序
            var sortedTicks = ticks.OrderBy(t => t.DateTime).ToList();

            // 檢測是否為純日盤資料（第一筆時間是08:xx開頭）
            bool isDaySessionOnly = sortedTicks.Count > 0 && sortedTicks.First().DateTime.Hour == 8;

            if (isDaySessionOnly)
            {
                Console.WriteLine($"    ✓ 檢測到純日盤資料，第一筆時間: {sortedTicks.First().DateTime:HH:mm:ss}");
                Console.WriteLine($"    ✓ 將使用純日盤模式 (08:45→13:45，只有1個OPEN和1個CLOSE)");
            }
            else
            {
                Console.WriteLine($"    ✓ 檢測到完整交易日資料，第一筆時間: {sortedTicks.First().DateTime:HH:mm:ss}");
                Console.WriteLine($"    ✓ 將使用完整模式 (前日15:00→當日13:45，包含夜盤和日盤)");
            }

            // 生成完整的交易時間資料（包含市場事件和時間邊界）
            var completeTicks = GenerateCompleteTickData(sortedTicks, tradeDate, isDaySessionOnly);

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                // 寫入標題行
                writer.WriteLine("時間,價格,成交量,備註");

                // 寫入資料
                foreach (var tick in completeTicks)
                {
                    writer.WriteLine($"{tick.DateTime:yyyy/MM/dd HH:mm:ss},{tick.Price},{tick.Volume},{tick.EventType}");
                }
            }
        }

        /// <summary>
        /// 生成完整的交易時間資料（包含市場事件和時間邊界填充）
        /// </summary>
        private List<ConvertedTickData> GenerateCompleteTickData(List<ConvertedTickData> originalTicks, DateTime tradeDate, bool isDaySessionOnly = false)
        {
            var result = new List<ConvertedTickData>();

            DateTime startTime;
            DateTime endTime;

            if (isDaySessionOnly)
            {
                // 純日盤模式：只有 08:45→13:45
                startTime = new DateTime(tradeDate.Year, tradeDate.Month, tradeDate.Day, 8, 45, 0);
                endTime = new DateTime(tradeDate.Year, tradeDate.Month, tradeDate.Day, 13, 45, 0);

                // 添加開盤標記 (08:45:00)
                result.Add(new ConvertedTickData
                {
                    DateTime = startTime,
                    Price = originalTicks.Count > 0 ? originalTicks.First().Price : 0,
                    Volume = 0,
                    EventType = "MARKET_OPEN"
                });
            }
            else
            {
                // 完整交易日模式：夜盤(前一日15:00→當日05:00) + 日盤(當日08:45→當日13:45)
                startTime = new DateTime(tradeDate.AddDays(-1).Year, tradeDate.AddDays(-1).Month, tradeDate.AddDays(-1).Day, 15, 0, 0);
                endTime = new DateTime(tradeDate.Year, tradeDate.Month, tradeDate.Day, 13, 45, 0);

                // 添加開盤標記 (前一日15:00:00)
                result.Add(new ConvertedTickData
                {
                    DateTime = startTime,
                    Price = originalTicks.Count > 0 ? originalTicks.First().Price : 0,
                    Volume = 0,
                    EventType = "MARKET_OPEN"
                });
            }

            // 按分鐘處理資料
            var current = startTime;

            while (current <= endTime)
            {
                var minuteStart = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0);
                var minuteEnd = minuteStart.AddMinutes(1);

                // 檢查是否需要加入市場事件標記（在處理tick之前）
                AddMarketEventIfNeeded(result, minuteStart, isDaySessionOnly);

                // 取得這一分鐘內的所有tick
                var minuteTicks = originalTicks.Where(t => t.DateTime >= minuteStart && t.DateTime < minuteEnd).ToList();

                if (minuteTicks.Any())
                {
                    // 有實際交易資料，直接加入所有tick（不管是否在交易時間內）
                    result.AddRange(minuteTicks);
                }
                else if (IsTradingTime(minuteStart, isDaySessionOnly))
                {
                    // 檢查是否已經有市場事件在這個時間點
                    bool hasMarketEvent = result.Any(t => t.DateTime == minuteStart && !string.IsNullOrEmpty(t.EventType));
                    if (!hasMarketEvent)
                    {
                        // 沒有交易資料也沒有市場事件，加入TIME_BOUNDARY
                        result.Add(new ConvertedTickData
                        {
                            DateTime = minuteStart,
                            Price = GetLastKnownPrice(result),
                            Volume = 0,
                            EventType = "TIME_BOUNDARY"
                        });
                    }
                }

                current = current.AddMinutes(1);
            }

            // 檢查是否已經有收盤標記，如果沒有才添加
            if (!result.Any(t => t.DateTime == endTime && t.EventType == "MARKET_CLOSE"))
            {
                result.Add(new ConvertedTickData
                {
                    DateTime = endTime,
                    Price = GetLastKnownPrice(result),
                    Volume = 0,
                    EventType = "MARKET_CLOSE"
                });
            }

            return result.OrderBy(t => t.DateTime).ToList();
        }

        /// <summary>
        /// 添加市場事件標記（如果需要）
        /// </summary>
        private void AddMarketEventIfNeeded(List<ConvertedTickData> result, DateTime time, bool isDaySessionOnly = false)
        {
            var timeOfDay = time.TimeOfDay;

            if (isDaySessionOnly)
            {
                // 純日盤模式：只有日盤收盤 13:45:00
                if (timeOfDay == TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(45)))
                {
                    result.Add(new ConvertedTickData
                    {
                        DateTime = time,
                        Price = GetLastKnownPrice(result),
                        Volume = 0,
                        EventType = "MARKET_CLOSE"
                    });
                }
            }
            else
            {
                // 完整交易日模式
                // 夜盤收盤 05:00:00
                if (timeOfDay == TimeSpan.FromHours(5))
                {
                    result.Add(new ConvertedTickData
                    {
                        DateTime = time,
                        Price = GetLastKnownPrice(result),
                        Volume = 0,
                        EventType = "MARKET_CLOSE"
                    });
                }
                // 日盤開盤 08:45:00
                else if (timeOfDay == TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(45)))
                {
                    result.Add(new ConvertedTickData
                    {
                        DateTime = time,
                        Price = GetLastKnownPrice(result),
                        Volume = 0,
                        EventType = "MARKET_OPEN"
                    });
                }
                // 日盤收盤 13:45:00
                else if (timeOfDay == TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(45)))
                {
                    result.Add(new ConvertedTickData
                    {
                        DateTime = time,
                        Price = GetLastKnownPrice(result),
                        Volume = 0,
                        EventType = "MARKET_CLOSE"
                    });
                }
            }
        }

        /// <summary>
        /// 取得最後已知價格
        /// </summary>
        private decimal GetLastKnownPrice(List<ConvertedTickData> result)
        {
            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (result[i].Price > 0)
                    return result[i].Price;
            }
            return 0;
        }

        /// <summary>
        /// 判斷是否為交易時間
        /// </summary>
        private bool IsTradingTime(DateTime time, bool isDaySessionOnly = false)
        {
            var timeOfDay = time.TimeOfDay;

            if (isDaySessionOnly)
            {
                // 純日盤模式：只有 08:45-13:45
                if (timeOfDay >= TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(45)) &&
                    timeOfDay <= TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(45)))
                    return true;
            }
            else
            {
                // 完整交易日模式
                // 夜盤：15:00-05:00（跨日）
                if (timeOfDay >= TimeSpan.FromHours(15) || timeOfDay <= TimeSpan.FromHours(5))
                    return true;

                // 日盤：08:45-13:45
                if (timeOfDay >= TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(45)) &&
                    timeOfDay <= TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(45)))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 批次轉換整個目錄
        /// </summary>
        /// <param name="inputDirectory">輸入目錄（期交所歷史資料）</param>
        /// <param name="outputDirectory">輸出目錄</param>
        /// <returns>轉換結果：(轉換檔案數量, 處理過的檔案名稱列表)</returns>
        public (int convertedCount, List<string> processedFiles) ConvertDirectory(string inputDirectory, string outputDirectory)
        {
            if (!Directory.Exists(inputDirectory))
            {
                throw new DirectoryNotFoundException($"輸入目錄不存在: {inputDirectory}");
            }

            var csvFiles = Directory.GetFiles(inputDirectory, "Daily_*.csv")
                .OrderBy(f => f)
                .ToArray();

            Console.WriteLine($"找到 {csvFiles.Length} 個檔案準備轉換...");

            var processedFiles = new List<string>();
            int convertedCount = 0;

            foreach (string csvFile in csvFiles)
            {
                string fileName = Path.GetFileName(csvFile);

                try
                {
                    ConvertFile(csvFile, outputDirectory);
                    processedFiles.Add(fileName);
                    convertedCount++;
                    Console.WriteLine($"  ✓ {fileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ 轉換檔案失敗 {fileName}: {ex.Message}");
                }
            }

            Console.WriteLine($"\n批次轉換完成! 成功轉換: {convertedCount}/{csvFiles.Length}");
            return (convertedCount, processedFiles);
        }
    }
}