using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ZenPlatform.SessionManager.Backtest
{
    public sealed class HisDbDataSource
    {
        public sealed record BacktestTick(
            DateTime Time,
            decimal Price,
            int Volume,
            int Event,
            int ContractMonth);

        public sealed record BacktestBar1m(
            DateTime EndTime,
            decimal Open,
            decimal High,
            decimal Low,
            decimal Close,
            int Volume,
            int Event);

        public static class ProductCode
        {
            public const int Tx = 1;  // 大型台指
            public const int Mtx = 2; // 小型台指
            public const int Tmf = 3; // 微型台指
        }

        private readonly string _dbFolder;
        private static readonly TimeSpan ExchangeOffset = TimeSpan.FromHours(8);

        public HisDbDataSource(string dbFolder)
        {
            _dbFolder = dbFolder ?? throw new ArgumentNullException(nameof(dbFolder));
        }

        public IEnumerable<BacktestTick> ReadTicks(int product, DateTime start, DateTime end, int? contractMonth = null)
        {
            if (start > end)
            {
                yield break;
            }

            long startTs = ToUnixSeconds(start);
            long endTs = ToUnixSeconds(end);

            foreach (var dbPath in EnumerateDbFiles(start, end))
            {
                if (!File.Exists(dbPath))
                {
                    continue;
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ts, price, volume, event, contract_month FROM ticks WHERE product = @product AND ts BETWEEN @start AND @end ORDER BY ts ASC;";

                cmd.Parameters.AddWithValue("@product", product);
                cmd.Parameters.AddWithValue("@start", startTs);
                cmd.Parameters.AddWithValue("@end", endTs);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long ts = reader.GetInt64(0);
                    var price = Convert.ToDecimal(reader.GetDouble(1));
                    int volume = reader.GetInt32(2);
                    int ev = reader.GetInt32(3);
                    int contract = reader.GetInt32(4);

                    yield return new BacktestTick(
                        FromUnixSeconds(ts),
                        price,
                        volume,
                        ev,
                        contract);
                }
            }
        }

        public IEnumerable<List<BacktestTick>> ReadTickBatches(int product, DateTime start, DateTime end, int batchSize = 1000, int? contractMonth = null)
        {
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            var batch = new List<BacktestTick>(batchSize);
            foreach (var tick in ReadTicks(product, start, end, contractMonth))
            {
                batch.Add(tick);
                if (batch.Count >= batchSize)
                {
                    yield return batch;
                    batch = new List<BacktestTick>(batchSize);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        public IEnumerable<BacktestBar1m> ReadBars1m(int product, DateTime start, DateTime end)
        {
            if (start > end)
            {
                yield break;
            }

            long startTs = ToUnixSeconds(start);
            long endTs = ToUnixSeconds(end);

            foreach (var dbPath in EnumerateDbFiles(start, end))
            {
                if (!File.Exists(dbPath))
                {
                    continue;
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ts, open, high, low, close, volume, event FROM bars_1m WHERE product = @product AND ts BETWEEN @start AND @end ORDER BY ts ASC;";

                cmd.Parameters.AddWithValue("@product", product);
                cmd.Parameters.AddWithValue("@start", startTs);
                cmd.Parameters.AddWithValue("@end", endTs);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long ts = reader.GetInt64(0);
                    var open = Convert.ToDecimal(reader.GetDouble(1));
                    var high = Convert.ToDecimal(reader.GetDouble(2));
                    var low = Convert.ToDecimal(reader.GetDouble(3));
                    var close = Convert.ToDecimal(reader.GetDouble(4));
                    int volume = reader.GetInt32(5);
                    int ev = reader.GetInt32(6);

                    yield return new BacktestBar1m(
                        FromUnixSeconds(ts),
                        open,
                        high,
                        low,
                        close,
                        volume,
                        ev);
                }
            }
        }

        public IEnumerable<List<BacktestBar1m>> ReadBar1mBatches(int product, DateTime start, DateTime end, int batchSize = 10000)
        {
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            var batch = new List<BacktestBar1m>(batchSize);
            foreach (var bar in ReadBars1m(product, start, end))
            {
                batch.Add(bar);
                if (batch.Count >= batchSize)
                {
                    yield return batch;
                    batch = new List<BacktestBar1m>(batchSize);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        public long CountTicks(int product, DateTime start, DateTime end, int? contractMonth = null)
        {
            if (start > end)
            {
                return 0;
            }

            long startTs = ToUnixSeconds(start);
            long endTs = ToUnixSeconds(end);
            long total = 0;

            foreach (var dbPath in EnumerateDbFiles(start, end))
            {
                if (!File.Exists(dbPath))
                {
                    continue;
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM ticks WHERE product = @product AND ts BETWEEN @start AND @end;";

                cmd.Parameters.AddWithValue("@product", product);
                cmd.Parameters.AddWithValue("@start", startTs);
                cmd.Parameters.AddWithValue("@end", endTs);
                var count = cmd.ExecuteScalar();
                if (count != null && count != System.DBNull.Value)
                {
                    total += System.Convert.ToInt64(count);
                }
            }

            return total;
        }

        public long CountTicksAllContracts(int product, DateTime start, DateTime end)
        {
            return CountTicks(product, start, end, null);
        }

        public (DateTime? Min, DateTime? Max) GetTickRange(int product, int? contractMonth = null)
        {
            DateTime? min = null;
            DateTime? max = null;

            foreach (var dbPath in EnumerateDbFilesForAllYears())
            {
                if (!File.Exists(dbPath))
                {
                    continue;
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT MIN(ts), MAX(ts) FROM ticks WHERE product = @product;";

                cmd.Parameters.AddWithValue("@product", product);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    continue;
                }

                if (!reader.IsDBNull(0))
                {
                    var ts = reader.GetInt64(0);
                    var dt = FromUnixSeconds(ts);
                    if (min == null || dt < min.Value) min = dt;
                }
                if (!reader.IsDBNull(1))
                {
                    var ts = reader.GetInt64(1);
                    var dt = FromUnixSeconds(ts);
                    if (max == null || dt > max.Value) max = dt;
                }
            }

            return (min, max);
        }

        public DateTime? FindPreloadStartByTime(int product, DateTime start, int preloadDays)
        {
            if (preloadDays <= 0)
            {
                return null;
            }

            var targetTime = start.ToString("HH:mm");
            var remaining = preloadDays;
            var matches = new List<DateTime>(preloadDays);
            var startTs = ToUnixSeconds(start);

            for (var year = start.Year; year >= 1990 && remaining > 0; year--)
            {
                var dbPath = Path.Combine(_dbFolder, $"歷史價格資料庫.{year}.db");
                if (!File.Exists(dbPath))
                {
                    continue;
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT ts
FROM bars_1m
WHERE product = @product
  AND ts < @end
  AND strftime('%H:%M', datetime(ts, 'unixepoch', '+8 hours')) = @time
ORDER BY ts DESC
LIMIT @limit;".Trim();

                cmd.Parameters.AddWithValue("@product", product);
                cmd.Parameters.AddWithValue("@end", year == start.Year ? startTs : long.MaxValue);
                cmd.Parameters.AddWithValue("@time", targetTime);
                cmd.Parameters.AddWithValue("@limit", remaining);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ts = reader.GetInt64(0);
                    matches.Add(FromUnixSeconds(ts));
                    remaining--;
                    if (remaining == 0)
                    {
                        break;
                    }
                }
            }

            if (matches.Count < preloadDays)
            {
                return null;
            }

            // matches are collected in descending order.
            return matches[preloadDays - 1];
        }

        public long CountBars1m(int product, DateTime start, DateTime end)
        {
            if (start > end)
            {
                return 0;
            }

            long startTs = ToUnixSeconds(start);
            long endTs = ToUnixSeconds(end);
            long total = 0;

            foreach (var dbPath in EnumerateDbFiles(start, end))
            {
                if (!File.Exists(dbPath))
                {
                    continue;
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM bars_1m WHERE product = @product AND ts BETWEEN @start AND @end;";

                cmd.Parameters.AddWithValue("@product", product);
                cmd.Parameters.AddWithValue("@start", startTs);
                cmd.Parameters.AddWithValue("@end", endTs);

                var count = cmd.ExecuteScalar();
                if (count != null && count != System.DBNull.Value)
                {
                    total += System.Convert.ToInt64(count);
                }
            }

            return total;
        }

        public (DateTime? Min, DateTime? Max) GetBarRange1m(int product)
        {
            DateTime? min = null;
            DateTime? max = null;

            foreach (var dbPath in EnumerateDbFilesForAllYears())
            {
                if (!File.Exists(dbPath))
                {
                    continue;
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT MIN(ts), MAX(ts) FROM bars_1m WHERE product = @product;";
                cmd.Parameters.AddWithValue("@product", product);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    continue;
                }

                if (!reader.IsDBNull(0))
                {
                    var ts = reader.GetInt64(0);
                    var dt = FromUnixSeconds(ts);
                    if (min == null || dt < min.Value) min = dt;
                }
                if (!reader.IsDBNull(1))
                {
                    var ts = reader.GetInt64(1);
                    var dt = FromUnixSeconds(ts);
                    if (max == null || dt > max.Value) max = dt;
                }
            }

            return (min, max);
        }

        private IEnumerable<string> EnumerateDbFiles(DateTime start, DateTime end)
        {
            int startYear = start.Year;
            int endYear = end.Year;
            for (int year = startYear; year <= endYear; year++)
            {
                yield return Path.Combine(_dbFolder, $"歷史價格資料庫.{year}.db");
            }
        }

        private IEnumerable<string> EnumerateDbFilesForAllYears()
        {
            if (!Directory.Exists(_dbFolder))
            {
                yield break;
            }

            foreach (var file in Directory.GetFiles(_dbFolder, "歷史價格資料庫.*.db"))
            {
                yield return file;
            }
        }

        private static long ToUnixSeconds(DateTime time)
        {
            var offset = new DateTimeOffset(time, ExchangeOffset);
            return offset.ToUnixTimeSeconds();
        }

        private static DateTime FromUnixSeconds(long ts)
        {
            var offset = DateTimeOffset.FromUnixTimeSeconds(ts).ToOffset(ExchangeOffset);
            return DateTime.SpecifyKind(offset.DateTime, DateTimeKind.Unspecified);
        }
    }
}
