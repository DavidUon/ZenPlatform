using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TaifexHisDbManager
{
    internal static class DbEvent
    {
        public const int Trade = 0;
        public const int MarketOpen = 1;
        public const int MarketClose = 2;
        public const int TimeBoundary = 4;
    }

    internal static class ProductCode
    {
        public const int Tx = 1;
        public const int Mtx = 2;
        public const int Tmf = 3;
    }

    internal class ConvertedTickData
    {
        public DateTime DateTime { get; set; }
        public decimal Price { get; set; }
        public int Volume { get; set; }
        public int Event { get; set; }
        public int ContractMonth { get; set; }
    }

    internal class Bar1m
    {
        public DateTime EndTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public int Volume { get; set; }
        public int Event { get; set; }
    }

    internal sealed class TaifexCsvImporter
    {
        public event EventHandler<ImportProgressEventArgs>? ProgressChanged;

        public ImportSummary ImportCsvToDatabase(string csvPath, string dbOutputFolder, Dictionary<int, YearDatabase> dbPool)
        {
            ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                Path.GetFileName(csvPath),
                ImportProgressStage.Start,
                0,
                3));

            int targetMonth = GetTargetContractMonthFromFileName(Path.GetFileNameWithoutExtension(csvPath));
            var ticksByProduct = ReadTradeTicks(csvPath, targetMonth);

            var summary = new ImportSummary(csvPath);

            int processedProducts = 0;
            foreach (var kvp in ticksByProduct)
            {
                int product = kvp.Key;
                var tradeTicks = kvp.Value;
                if (tradeTicks.Count == 0)
                {
                    processedProducts++;
                    continue;
                }

                tradeTicks.Sort((a, b) => a.DateTime.CompareTo(b.DateTime));

                DateTime nightStartDate = tradeTicks
                    .Where(t => t.DateTime.TimeOfDay >= TimeSpan.FromHours(15))
                    .Select(t => t.DateTime.Date)
                    .OrderBy(d => d)
                    .FirstOrDefault();

                DateTime dayDate = tradeTicks
                    .Where(t => t.DateTime.TimeOfDay >= TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(45)) &&
                                t.DateTime.TimeOfDay <= TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(45)))
                    .Select(t => t.DateTime.Date)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                var completeTicks = new List<ConvertedTickData>();

                if (nightStartDate != default)
                {
                    DateTime nightStart = new DateTime(nightStartDate.Year, nightStartDate.Month, nightStartDate.Day, 15, 0, 0);
                    DateTime nightEnd = nightStart.AddHours(14);
                    completeTicks.AddRange(GenerateCompleteTickDataForRange(tradeTicks, nightStart, nightEnd));
                }

                if (dayDate != default)
                {
                    DateTime dayStart = new DateTime(dayDate.Year, dayDate.Month, dayDate.Day, 8, 45, 0);
                    DateTime dayEnd = new DateTime(dayDate.Year, dayDate.Month, dayDate.Day, 13, 45, 0);
                    completeTicks.AddRange(GenerateCompleteTickDataForRange(tradeTicks, dayStart, dayEnd));
                }

                completeTicks.Sort((a, b) => a.DateTime.CompareTo(b.DateTime));
                var bars = GenerateBars1m(completeTicks);

                var ticksByYear = completeTicks.GroupBy(t => t.DateTime.Year);
                var barsByYear = bars.GroupBy(b => b.EndTime.Year).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var tickGroup in ticksByYear)
                {
                    int year = tickGroup.Key;
                    if (!dbPool.TryGetValue(year, out var db))
                    {
                        string dbPath = Path.Combine(dbOutputFolder, $"歷史價格資料庫.{year}.db");
                        db = new YearDatabase(dbPath);
                        dbPool.Add(year, db);
                    }

                    barsByYear.TryGetValue(year, out var yearBars);
                    db.ReplaceDataRange(product, tickGroup.ToList(), yearBars ?? new List<Bar1m>());
                }

                summary.AddProductStats(product, completeTicks, bars);
                processedProducts++;
                ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                    Path.GetFileName(csvPath),
                    ImportProgressStage.ProductCompleted,
                    processedProducts,
                    3));
            }

            ProgressChanged?.Invoke(this, new ImportProgressEventArgs(
                Path.GetFileName(csvPath),
                ImportProgressStage.Completed,
                3,
                3));
            return summary;
        }

        private static int GetTargetContractMonthFromFileName(string fileName)
        {
            DateTime tradeDate = ParseTradeDateFromFileName(fileName);
            DateTime settlementDate = GetThirdWednesday(tradeDate.Year, tradeDate.Month);

            if (tradeDate > settlementDate)
            {
                DateTime nextMonth = tradeDate.AddMonths(1);
                return int.Parse($"{nextMonth:yyyyMM}");
            }

            return int.Parse($"{tradeDate:yyyyMM}");
        }

        private static DateTime ParseTradeDateFromFileName(string fileName)
        {
            if (fileName.StartsWith("Daily_", StringComparison.OrdinalIgnoreCase))
            {
                string dateStr = fileName.Substring(6).Replace("_", "");
                if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, DateTimeStyles.None, out DateTime date))
                    return date;
            }

            throw new ArgumentException($"無法解析檔名中的日期: {fileName}");
        }

        private static DateTime GetThirdWednesday(int year, int month)
        {
            DateTime firstDay = new DateTime(year, month, 1);
            int daysUntilWednesday = (int)DayOfWeek.Wednesday - (int)firstDay.DayOfWeek;
            if (daysUntilWednesday < 0)
                daysUntilWednesday += 7;

            DateTime firstWednesday = firstDay.AddDays(daysUntilWednesday);
            return firstWednesday.AddDays(14);
        }
        private static Dictionary<int, List<ConvertedTickData>> ReadTradeTicks(string inputFilePath, int targetMonth)
        {
            var txTicks = new List<ConvertedTickData>();
            var mtxTicks = new List<ConvertedTickData>();
            var tmfTicks = new List<ConvertedTickData>();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using (var reader = new StreamReader(inputFilePath, Encoding.GetEncoding("big5")))
            {
                string? line;
                bool isFirstLine = true;

                while ((line = reader.ReadLine()) != null)
                {
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] fields = line.Split(',');
                    if (fields.Length < 6)
                        continue;

                    string dateStr = fields[0].Trim();
                    string productCode = fields[1].Trim();
                    string expireMonth = fields[2].Trim();
                    string timeStr = fields[3].Trim();
                    string priceStr = fields[4].Trim();
                    string volumeStr = fields[5].Trim();

                    if (!int.TryParse(expireMonth, out int expireMonthValue))
                        continue;
                    if (expireMonthValue != targetMonth)
                        continue;

                    if (!DateTime.TryParseExact($"{dateStr} {timeStr.PadLeft(6, '0')}", "yyyyMMdd HHmmss",
                        null, DateTimeStyles.None, out DateTime dateTime) ||
                        !decimal.TryParse(priceStr, out decimal price) ||
                        !int.TryParse(volumeStr, out int volume))
                        continue;

                    var tickData = new ConvertedTickData
                    {
                        DateTime = dateTime,
                        Price = price,
                        Volume = volume,
                        Event = DbEvent.Trade,
                        ContractMonth = expireMonthValue
                    };

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

            return new Dictionary<int, List<ConvertedTickData>>
            {
                [ProductCode.Tx] = txTicks,
                [ProductCode.Mtx] = mtxTicks,
                [ProductCode.Tmf] = tmfTicks
            };
        }

        private static List<ConvertedTickData> GenerateCompleteTickDataForRange(List<ConvertedTickData> originalTicks, DateTime startTime, DateTime endTime)
        {
            var rangeTicks = originalTicks
                .Where(t => t.DateTime >= startTime && t.DateTime <= endTime)
                .OrderBy(t => t.DateTime)
                .ToList();

            if (rangeTicks.Count == 0)
                return new List<ConvertedTickData>();

            var result = new List<ConvertedTickData>
            {
                new ConvertedTickData
                {
                    DateTime = startTime,
                    Price = rangeTicks[0].Price,
                    Volume = 0,
                    Event = DbEvent.MarketOpen,
                    ContractMonth = rangeTicks[0].ContractMonth
                }
            };

            // Keep last known values in O(1) instead of scanning result repeatedly.
            var lastKnownPrice = rangeTicks[0].Price;
            var lastKnownContractMonth = rangeTicks[0].ContractMonth;
            var tickIndex = 0;
            var tickCount = rangeTicks.Count;
            var current = startTime;
            while (current <= endTime)
            {
                var minuteStart = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0);
                var minuteEnd = minuteStart.AddMinutes(1);
                var hadTradeInMinute = false;
                while (tickIndex < tickCount)
                {
                    var tick = rangeTicks[tickIndex];
                    if (tick.DateTime >= minuteEnd)
                    {
                        break;
                    }

                    // rangeTicks is already filtered by [startTime, endTime], so this tick belongs to current minute.
                    result.Add(tick);
                    hadTradeInMinute = true;
                    lastKnownPrice = tick.Price;
                    lastKnownContractMonth = tick.ContractMonth;
                    tickIndex++;
                }

                // Start minute already has a MarketOpen marker; other empty minutes get a boundary marker.
                if (!hadTradeInMinute && minuteStart != startTime)
                {
                    result.Add(new ConvertedTickData
                    {
                        DateTime = minuteStart,
                        Price = lastKnownPrice,
                        Volume = 0,
                        Event = DbEvent.TimeBoundary,
                        ContractMonth = lastKnownContractMonth
                    });
                }

                current = current.AddMinutes(1);
            }

            if (!result.Any(t => t.DateTime == endTime && t.Event == DbEvent.MarketClose))
            {
                result.Add(new ConvertedTickData
                {
                    DateTime = endTime,
                    Price = lastKnownPrice,
                    Volume = 0,
                    Event = DbEvent.MarketClose,
                    ContractMonth = lastKnownContractMonth
                });
            }

            result.Sort((a, b) => a.DateTime.CompareTo(b.DateTime));
            return result;
        }

        private static decimal GetLastKnownPrice(List<ConvertedTickData> result)
        {
            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (result[i].Price > 0)
                    return result[i].Price;
            }
            return 0;
        }

        private static int GetLastKnownContractMonth(List<ConvertedTickData> result)
        {
            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (result[i].ContractMonth > 0)
                    return result[i].ContractMonth;
            }
            return 0;
        }


        private static List<Bar1m> GenerateBars1m(List<ConvertedTickData> ticks)
        {
            var bars = new List<Bar1m>();
            int i = 0;

            while (i < ticks.Count)
            {
                DateTime minuteStart = new DateTime(ticks[i].DateTime.Year, ticks[i].DateTime.Month, ticks[i].DateTime.Day,
                    ticks[i].DateTime.Hour, ticks[i].DateTime.Minute, 0);
                DateTime minuteEnd = minuteStart.AddMinutes(1);

                decimal open = ticks[i].Price;
                decimal high = open;
                decimal low = open;
                decimal close = open;
                int volume = 0;
                int eventMask = 0;

                int j = i;
                while (j < ticks.Count && ticks[j].DateTime < minuteEnd)
                {
                    var tick = ticks[j];
                    if (tick.Price > high)
                        high = tick.Price;
                    if (tick.Price < low)
                        low = tick.Price;
                    close = tick.Price;

                    if (tick.Event == DbEvent.Trade)
                        volume += tick.Volume;
                    else
                        eventMask |= tick.Event;

                    j++;
                }

                bars.Add(new Bar1m
                {
                    EndTime = minuteEnd,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = volume,
                    Event = eventMask
                });

                i = j;
            }

            return bars;
        }
    }

    internal enum ImportProgressStage
    {
        Start = 0,
        ProductCompleted = 1,
        Completed = 2
    }

    internal sealed class ImportProgressEventArgs : EventArgs
    {
        public string FileName { get; }
        public ImportProgressStage Stage { get; }
        public int Current { get; }
        public int Total { get; }

        public ImportProgressEventArgs(string fileName, ImportProgressStage stage, int current, int total)
        {
            FileName = fileName;
            Stage = stage;
            Current = current;
            Total = total;
        }
    }

    internal sealed class ImportSummary
    {
        public string FileName { get; }
        public HashSet<int> Years { get; } = new();
        public List<ProductImportStats> Products { get; } = new();
        public List<EventLogEntry> Events { get; } = new();

        public ImportSummary(string filePath)
        {
            FileName = Path.GetFileName(filePath);
        }

        public void AddProductStats(int product, List<ConvertedTickData> ticks, List<Bar1m> bars)
        {
            int tradeCount = 0;
            int openCount = 0;
            int closeCount = 0;
            int boundaryCount = 0;

            foreach (var tick in ticks)
            {
                Years.Add(tick.DateTime.Year);
                switch (tick.Event)
                {
                    case DbEvent.Trade:
                        tradeCount++;
                        break;
                    case DbEvent.MarketOpen:
                        openCount++;
                        Events.Add(new EventLogEntry(product, tick.DateTime, tick.Event));
                        break;
                    case DbEvent.MarketClose:
                        closeCount++;
                        Events.Add(new EventLogEntry(product, tick.DateTime, tick.Event));
                        break;
                    case DbEvent.TimeBoundary:
                        boundaryCount++;
                        Events.Add(new EventLogEntry(product, tick.DateTime, tick.Event));
                        break;
                }
            }

            foreach (var bar in bars)
            {
                Years.Add(bar.EndTime.Year);
            }

            Products.Add(new ProductImportStats(product, tradeCount, openCount, closeCount, boundaryCount, bars.Count));
        }
    }

    internal sealed class ProductImportStats
    {
        public int Product { get; }
        public int TradeCount { get; }
        public int OpenCount { get; }
        public int CloseCount { get; }
        public int BoundaryCount { get; }
        public int BarCount { get; }

        public ProductImportStats(int product, int tradeCount, int openCount, int closeCount, int boundaryCount, int barCount)
        {
            Product = product;
            TradeCount = tradeCount;
            OpenCount = openCount;
            CloseCount = closeCount;
            BoundaryCount = boundaryCount;
            BarCount = barCount;
        }
    }

    internal sealed class EventLogEntry
    {
        public int Product { get; }
        public DateTime Time { get; }
        public int Event { get; }

        public EventLogEntry(int product, DateTime time, int @event)
        {
            Product = product;
            Time = time;
            Event = @event;
        }
    }

    internal sealed class YearDatabase : IDisposable
    {
        private const int BulkInsertChunkSize = 500;
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;
        private readonly SqliteCommand _deleteTicks;
        private readonly SqliteCommand _deleteBars;

        public YearDatabase(string dbPath)
        {
            _connection = new SqliteConnection($"Data Source={dbPath}");
            _connection.Open();
            EnsureSchema();

            _transaction = _connection.BeginTransaction();

            _deleteTicks = _connection.CreateCommand();
            _deleteTicks.CommandText = "DELETE FROM ticks WHERE product = @product AND ts BETWEEN @start AND @end";
            _deleteTicks.Transaction = _transaction;
            _deleteTicks.Parameters.Add("@product", SqliteType.Integer);
            _deleteTicks.Parameters.Add("@start", SqliteType.Integer);
            _deleteTicks.Parameters.Add("@end", SqliteType.Integer);

            _deleteBars = _connection.CreateCommand();
            _deleteBars.CommandText = "DELETE FROM bars_1m WHERE product = @product AND ts BETWEEN @start AND @end";
            _deleteBars.Transaction = _transaction;
            _deleteBars.Parameters.Add("@product", SqliteType.Integer);
            _deleteBars.Parameters.Add("@start", SqliteType.Integer);
            _deleteBars.Parameters.Add("@end", SqliteType.Integer);
        }

        public void ReplaceDataRange(int product, List<ConvertedTickData> ticks, List<Bar1m> bars)
        {
            if (ticks.Count == 0 && bars.Count == 0)
                return;

            if (ticks.Count > 0)
            {
                long startTs = ToUnixSeconds(ticks[0].DateTime);
                long endTs = ToUnixSeconds(ticks[ticks.Count - 1].DateTime);

                _deleteTicks.Parameters["@product"].Value = product;
                _deleteTicks.Parameters["@start"].Value = startTs;
                _deleteTicks.Parameters["@end"].Value = endTs;
                _deleteTicks.ExecuteNonQuery();
            }

            if (bars.Count > 0)
            {
                long barStartTs = ToUnixSeconds(bars[0].EndTime);
                long barEndTs = ToUnixSeconds(bars[bars.Count - 1].EndTime);

                _deleteBars.Parameters["@product"].Value = product;
                _deleteBars.Parameters["@start"].Value = barStartTs;
                _deleteBars.Parameters["@end"].Value = barEndTs;
                _deleteBars.ExecuteNonQuery();
            }

            InsertTicksBulk(product, ticks);
            InsertBarsBulk(product, bars);
        }

        private void InsertTicksBulk(int product, List<ConvertedTickData> ticks)
        {
            if (ticks.Count == 0)
            {
                return;
            }

            for (int offset = 0; offset < ticks.Count; offset += BulkInsertChunkSize)
            {
                int end = Math.Min(offset + BulkInsertChunkSize, ticks.Count);
                var sql = new StringBuilder("INSERT INTO ticks (ts, product, price, volume, event, contract_month) VALUES ");
                for (int i = offset; i < end; i++)
                {
                    var tick = ticks[i];
                    if (i > offset)
                    {
                        sql.Append(',');
                    }

                    sql.Append('(')
                       .Append(ToUnixSeconds(tick.DateTime)).Append(',')
                       .Append(product).Append(',')
                       .Append(((double)tick.Price).ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(tick.Volume).Append(',')
                       .Append(tick.Event).Append(',')
                       .Append(tick.ContractMonth)
                       .Append(')');
                }

                using var cmd = _connection.CreateCommand();
                cmd.Transaction = _transaction;
                cmd.CommandText = sql.ToString();
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertBarsBulk(int product, List<Bar1m> bars)
        {
            if (bars.Count == 0)
            {
                return;
            }

            for (int offset = 0; offset < bars.Count; offset += BulkInsertChunkSize)
            {
                int end = Math.Min(offset + BulkInsertChunkSize, bars.Count);
                var sql = new StringBuilder("INSERT OR REPLACE INTO bars_1m (ts, product, open, high, low, close, volume, event) VALUES ");
                for (int i = offset; i < end; i++)
                {
                    var bar = bars[i];
                    if (i > offset)
                    {
                        sql.Append(',');
                    }

                    sql.Append('(')
                       .Append(ToUnixSeconds(bar.EndTime)).Append(',')
                       .Append(product).Append(',')
                       .Append(((double)bar.Open).ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(((double)bar.High).ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(((double)bar.Low).ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(((double)bar.Close).ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(bar.Volume).Append(',')
                       .Append(bar.Event)
                       .Append(')');
                }

                using var cmd = _connection.CreateCommand();
                cmd.Transaction = _transaction;
                cmd.CommandText = sql.ToString();
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA temp_store=MEMORY;
PRAGMA cache_size=-200000;

CREATE TABLE IF NOT EXISTS ticks (
    ts INTEGER NOT NULL,
    product INTEGER NOT NULL,
    price REAL NOT NULL,
    volume INTEGER NOT NULL,
    event INTEGER NOT NULL,
    contract_month INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_ticks_product_ts ON ticks (product, ts);
CREATE INDEX IF NOT EXISTS idx_ticks_ts ON ticks (ts);

CREATE TABLE IF NOT EXISTS bars_1m (
    ts INTEGER NOT NULL,
    product INTEGER NOT NULL,
    open REAL NOT NULL,
    high REAL NOT NULL,
    low REAL NOT NULL,
    close REAL NOT NULL,
    volume INTEGER NOT NULL,
    event INTEGER NOT NULL,
    PRIMARY KEY (product, ts)
);

CREATE INDEX IF NOT EXISTS idx_bars_product_ts ON bars_1m (product, ts);
CREATE INDEX IF NOT EXISTS idx_bars_ts ON bars_1m (ts);
";
            cmd.ExecuteNonQuery();
        }

        private static long ToUnixSeconds(DateTime time)
        {
            var offset = new DateTimeOffset(time, TimeSpan.FromHours(8));
            return offset.ToUnixTimeSeconds();
        }

        public void Dispose()
        {
            _transaction.Commit();
            using (var checkpoint = _connection.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }
            _deleteTicks.Dispose();
            _deleteBars.Dispose();
            _transaction.Dispose();
            _connection.Dispose();
        }
    }
}
