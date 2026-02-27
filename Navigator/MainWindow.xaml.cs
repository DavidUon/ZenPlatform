using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Charts;
using Microsoft.Data.Sqlite;

namespace Navigator
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly string _dbDir;
		private readonly string _tempMetaPath;
		private List<ChartKBar> _currentBars = new();

		public MainWindow()
		{
			InitializeComponent();
			SourceInitialized += OnSourceInitialized;
			Closing += OnClosing;
			LoadButton.Click += (_, __) => LoadSelectedMonth();
			ExportMetaButton.Click += (_, __) => ExportMetadata();
			ImportMetaButton.Click += (_, __) => ImportMetadata();

			_dbDir = Path.Combine(AppContext.BaseDirectory, "回測歷史資料庫");
			_tempMetaPath = Path.Combine(AppContext.BaseDirectory, "temp.znvdb");
			if (!Directory.Exists(_dbDir))
			{
				var fallback = @"D:\Project\ZenPlatform\bin\Debug\net8.0-windows\回測歷史資料庫";
				if (Directory.Exists(fallback))
				{
					_dbDir = fallback;
				}
			}

			PopulateYearMonth();
		}

		private void OnSourceInitialized(object? sender, System.EventArgs e)
		{
			WindowStateStore.Apply(this);
			ApplyToolbarState();
			LoadSelectedMonth();
		}

		private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
		{
			try { ChartView.SaveConfig(); } catch { }
			WindowStateStore.Save(this);
		}

		private void PopulateYearMonth()
		{
			var years = new List<int>();
			if (Directory.Exists(_dbDir))
			{
				var files = Directory.GetFiles(_dbDir, "歷史價格資料庫.*.db");
				foreach (var file in files)
				{
					var name = Path.GetFileName(file);
					var parts = name.Split('.');
					if (parts.Length >= 2 && int.TryParse(parts[1], out var year))
					{
						years.Add(year);
					}
				}
			}

			years = years.Distinct().OrderBy(y => y).ToList();
			if (years.Count == 0)
			{
				years.Add(DateTime.Now.Year);
			}

			YearCombo.ItemsSource = years;
			YearCombo.SelectedIndex = years.Count - 1;
			MonthCombo.ItemsSource = Enumerable.Range(1, 12).ToList();
			MonthCombo.SelectedIndex = DateTime.Now.Month - 1;
			PeriodCombo.ItemsSource = new List<int> { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60, 90, 120 };
			PeriodCombo.SelectedItem = 5;
		}

		private void ApplyToolbarState()
		{
			var data = WindowStateStore.Load();
			if (data == null)
			{
				return;
			}

			if (data.SelectedYear.HasValue && YearCombo.Items.Contains(data.SelectedYear.Value))
			{
				YearCombo.SelectedItem = data.SelectedYear.Value;
			}

			if (data.SelectedMonth.HasValue && MonthCombo.Items.Contains(data.SelectedMonth.Value))
			{
				MonthCombo.SelectedItem = data.SelectedMonth.Value;
			}

			if (data.SelectedPeriod.HasValue && PeriodCombo.Items.Contains(data.SelectedPeriod.Value))
			{
				PeriodCombo.SelectedItem = data.SelectedPeriod.Value;
			}
		}

		private void LoadSelectedMonth()
		{
			if (YearCombo.SelectedItem is not int year || MonthCombo.SelectedItem is not int month)
			{
				return;
			}
			var period = PeriodCombo.SelectedItem is int p ? p : 1;

			var dbPath = Path.Combine(_dbDir, $"歷史價格資料庫.{year}.db");
			if (!File.Exists(dbPath))
			{
				MessageBox.Show(this, $"找不到資料庫檔案: {dbPath}", "Navigator");
				return;
			}

			var start = new DateTime(year, month, 1, 0, 0, 0);
			var end = start.AddMonths(1);
			var startTs = new DateTimeOffset(start).ToUnixTimeSeconds();
			var endTs = new DateTimeOffset(end).ToUnixTimeSeconds();

			var bars = new List<ChartKBar>();
			using (var conn = new SqliteConnection($"Data Source={dbPath}"))
			{
				conn.Open();
				using var cmd = conn.CreateCommand();
				cmd.CommandText = @"
SELECT ts, open, high, low, close, volume
FROM bars_1m
WHERE product = 1 AND ts >= $start AND ts < $end
ORDER BY ts";
				cmd.Parameters.AddWithValue("$start", startTs);
				cmd.Parameters.AddWithValue("$end", endTs);
				using var reader = cmd.ExecuteReader();
				while (reader.Read())
				{
					var ts = reader.GetInt64(0);
					var time = DateTimeOffset.FromUnixTimeSeconds(ts).ToOffset(TimeSpan.FromHours(8)).DateTime;
					var bar = new ChartKBar
					{
						Time = time,
						Open = Convert.ToDecimal(reader.GetDouble(1)),
						High = Convert.ToDecimal(reader.GetDouble(2)),
						Low = Convert.ToDecimal(reader.GetDouble(3)),
						Close = Convert.ToDecimal(reader.GetDouble(4)),
						Volume = reader.GetInt32(5)
					};
					bars.Add(bar);
				}
			}

			if (period > 1)
			{
				bars = AggregateBars(bars, period);
			}

			ApplyIndicatorsFromConfig(bars);
			WriteTempMeta(bars);
			var (loadedBars, _, _) = ImportFromMetaDb(_tempMetaPath);
			_currentBars = loadedBars;
			ChartView.LoadHistory(loadedBars);
		}

		private static List<ChartKBar> AggregateBars(List<ChartKBar> bars, int periodMinutes)
		{
			if (periodMinutes <= 1 || bars.Count == 0)
			{
				return bars;
			}

			var grouped = bars
				.OrderBy(b => b.Time)
				.GroupBy(b =>
				{
					var t = b.Time;
					var bucketMinute = (t.Minute / periodMinutes) * periodMinutes;
					return new DateTime(t.Year, t.Month, t.Day, t.Hour, bucketMinute, 0);
				});

			var result = new List<ChartKBar>();
			foreach (var g in grouped)
			{
				var list = g.ToList();
				var first = list[0];
				var last = list[^1];
				var barTime = g.Key.AddMinutes(periodMinutes);
				result.Add(new ChartKBar
				{
					Time = barTime,
					Open = first.Open,
					High = list.Max(b => b.High),
					Low = list.Min(b => b.Low),
					Close = last.Close,
					Volume = list.Sum(b => b.Volume)
				});
			}

			return result;
		}

		internal int? GetSelectedYear() => YearCombo.SelectedItem as int?;
		internal int? GetSelectedMonth() => MonthCombo.SelectedItem as int?;
		internal int? GetSelectedPeriod() => PeriodCombo.SelectedItem as int?;

		private void ExportMetadata()
		{
			var dialog = new MetaFileDialog(
				MetaFileDialog.DialogMode.Export,
				Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				$"meta{DateTime.Now:yyyyMMdd-HHmm}.znvdb");
			if (dialog.ShowDialog() != true)
			{
				return;
			}

			try
			{
				if (_currentBars.Count == 0)
				{
					MessageBox.Show(this, "目前沒有可匯出的 K 棒資料", "Navigator");
					return;
				}

				ChartView.SaveConfig();
				var configPath = Path.Combine(AppContext.BaseDirectory, "charts_config.json");
				var configJson = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";

				ExportToMetaDb(dialog.FullPath, configJson, _currentBars);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, $"匯出失敗: {ex.Message}", "Navigator");
			}
		}

		private void ImportMetadata()
		{
			var dialog = new MetaFileDialog(
				MetaFileDialog.DialogMode.Import,
				Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				"");
			if (dialog.ShowDialog() != true)
			{
				return;
			}

			try
			{
				var (bars, configJson, meta) = ImportFromMetaDb(dialog.FullPath);
				if (bars.Count == 0)
				{
					MessageBox.Show(this, "中繼檔沒有 K 棒資料", "Navigator");
					return;
				}

				_currentBars = bars;
				ChartView.LoadHistory(bars);

				if (!string.IsNullOrWhiteSpace(configJson))
				{
					var target = Path.Combine(AppContext.BaseDirectory, "charts_config.json");
					File.WriteAllText(target, configJson);
					ChartView.LoadConfig();
				}

				if (meta.TryGetValue("year", out var yearText) && int.TryParse(yearText, out var year))
				{
					if (YearCombo.Items.Contains(year))
					{
						YearCombo.SelectedItem = year;
					}
				}
				if (meta.TryGetValue("month", out var monthText) && int.TryParse(monthText, out var month))
				{
					if (MonthCombo.Items.Contains(month))
					{
						MonthCombo.SelectedItem = month;
					}
				}
				if (meta.TryGetValue("period", out var periodText) && int.TryParse(periodText, out var period))
				{
					if (PeriodCombo.Items.Contains(period))
					{
						PeriodCombo.SelectedItem = period;
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, $"匯入失敗: {ex.Message}", "Navigator");
			}
		}

		private void ExportToMetaDb(string path, string configJson, List<ChartKBar> bars)
		{
			using var conn = new SqliteConnection($"Data Source={path}");
			conn.Open();
			using var tx = conn.BeginTransaction();

			var create = conn.CreateCommand();
			create.CommandText = @"
DROP TABLE IF EXISTS meta;
DROP TABLE IF EXISTS bars;
DROP TABLE IF EXISTS indicators;
DROP TABLE IF EXISTS annotations;
CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE bars (ts INTEGER NOT NULL, open REAL NOT NULL, high REAL NOT NULL, low REAL NOT NULL, close REAL NOT NULL, volume INTEGER NOT NULL);
CREATE TABLE indicators (ts INTEGER NOT NULL, name TEXT NOT NULL, value REAL NOT NULL);
CREATE TABLE annotations (ts INTEGER NOT NULL, type TEXT NOT NULL, text TEXT, color TEXT);";
			create.ExecuteNonQuery();

			var meta = conn.CreateCommand();
			meta.CommandText = "INSERT INTO meta(key, value) VALUES ($k, $v)";
			meta.Parameters.Add("$k", SqliteType.Text);
			meta.Parameters.Add("$v", SqliteType.Text);
			void InsertMeta(string key, string value)
			{
				meta.Parameters["$k"].Value = key;
				meta.Parameters["$v"].Value = value;
				meta.ExecuteNonQuery();
			}

			InsertMeta("version", "1");
			if (GetSelectedYear() is int y) InsertMeta("year", y.ToString());
			if (GetSelectedMonth() is int m) InsertMeta("month", m.ToString());
			if (GetSelectedPeriod() is int p) InsertMeta("period", p.ToString());
			InsertMeta("chart_config_json", configJson ?? "{}");

			var insert = conn.CreateCommand();
			insert.CommandText = "INSERT INTO bars(ts, open, high, low, close, volume) VALUES ($ts,$o,$h,$l,$c,$v)";
			insert.Parameters.Add("$ts", SqliteType.Integer);
			insert.Parameters.Add("$o", SqliteType.Real);
			insert.Parameters.Add("$h", SqliteType.Real);
			insert.Parameters.Add("$l", SqliteType.Real);
			insert.Parameters.Add("$c", SqliteType.Real);
			insert.Parameters.Add("$v", SqliteType.Integer);

			foreach (var b in bars)
			{
				insert.Parameters["$ts"].Value = new DateTimeOffset(b.Time).ToUnixTimeSeconds();
				insert.Parameters["$o"].Value = (double)b.Open;
				insert.Parameters["$h"].Value = (double)b.High;
				insert.Parameters["$l"].Value = (double)b.Low;
				insert.Parameters["$c"].Value = (double)b.Close;
				insert.Parameters["$v"].Value = b.Volume;
				insert.ExecuteNonQuery();
			}

			if (bars.Count > 0)
			{
				var insInd = conn.CreateCommand();
				insInd.CommandText = "INSERT INTO indicators(ts, name, value) VALUES ($ts,$name,$value)";
				insInd.Parameters.Add("$ts", SqliteType.Integer);
				insInd.Parameters.Add("$name", SqliteType.Text);
				insInd.Parameters.Add("$value", SqliteType.Real);

				foreach (var b in bars)
				{
					if (b.Indicators == null || b.Indicators.Count == 0) continue;
					var ts = new DateTimeOffset(b.Time).ToUnixTimeSeconds();
					foreach (var kv in b.Indicators)
					{
						insInd.Parameters["$ts"].Value = ts;
						insInd.Parameters["$name"].Value = kv.Key;
						insInd.Parameters["$value"].Value = (double)kv.Value;
						insInd.ExecuteNonQuery();
					}
				}
			}

			tx.Commit();
		}

		private static (List<ChartKBar> bars, string? configJson, Dictionary<string, string> meta) ImportFromMetaDb(string path)
		{
			var bars = new List<ChartKBar>();
			var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			string? configJson = null;

			using var conn = new SqliteConnection($"Data Source={path}");
			conn.Open();

			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandText = "SELECT key, value FROM meta";
				using var reader = cmd.ExecuteReader();
				while (reader.Read())
				{
					var key = reader.GetString(0);
					var value = reader.IsDBNull(1) ? "" : reader.GetString(1);
					meta[key] = value;
				}
			}

			if (meta.TryGetValue("chart_config_json", out var cfg))
			{
				configJson = cfg;
			}

			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandText = "SELECT ts, open, high, low, close, volume FROM bars ORDER BY ts";
				using var reader = cmd.ExecuteReader();
				while (reader.Read())
				{
					var ts = reader.GetInt64(0);
					var time = DateTimeOffset.FromUnixTimeSeconds(ts).ToOffset(TimeSpan.FromHours(8)).DateTime;
					bars.Add(new ChartKBar
					{
						Time = time,
						Open = Convert.ToDecimal(reader.GetDouble(1)),
						High = Convert.ToDecimal(reader.GetDouble(2)),
						Low = Convert.ToDecimal(reader.GetDouble(3)),
						Close = Convert.ToDecimal(reader.GetDouble(4)),
						Volume = reader.GetInt32(5)
					});
				}
			}

			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandText = "SELECT ts, name, value FROM indicators ORDER BY ts";
				using var reader = cmd.ExecuteReader();
				var map = bars.ToDictionary(b => new DateTimeOffset(b.Time).ToUnixTimeSeconds(), b => b);
				while (reader.Read())
				{
					var ts = reader.GetInt64(0);
					var name = reader.GetString(1);
					var value = reader.GetDouble(2);
					if (map.TryGetValue(ts, out var bar))
					{
						bar.Indicators[name] = (decimal)value;
					}
				}
			}

			return (bars, configJson, meta);
		}

		private void WriteTempMeta(List<ChartKBar> bars)
		{
			ChartView.SaveConfig();
			var configPath = Path.Combine(AppContext.BaseDirectory, "charts_config.json");
			var configJson = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
			try
			{
				ExportToMetaDb(_tempMetaPath, configJson, bars);
			}
			catch (IOException ex)
			{
				MessageBox.Show(this, $"temp.znvdb 正在被占用，無法更新：{ex.Message}", "Navigator");
			}
		}

		private void ApplyIndicatorsFromConfig(List<ChartKBar> bars)
		{
			if (bars.Count == 0) return;
			var configPath = Path.Combine(AppContext.BaseDirectory, "charts_config.json");
			if (!File.Exists(configPath)) return;

			ChartViewConfig? cfg;
			try
			{
				cfg = JsonSerializer.Deserialize<ChartViewConfig>(File.ReadAllText(configPath));
			}
			catch
			{
				return;
			}
			if (cfg == null) return;

			if (cfg.Overlays != null)
			{
				foreach (var ov in cfg.Overlays)
				{
					if (ov.Type == "MA")
					{
						ComputeMa(bars, ov.Period, ov.MaType);
					}
					else if (ov.Type == "BBI")
					{
						var periods = ParsePeriods(ov.BbiPeriodsCsv);
						ComputeBbi(bars, periods);
					}
					else if (ov.Type == "BOLL")
					{
						ComputeBoll(bars, ov.Period, ov.K);
					}
				}
			}

			if (cfg.Indicators != null)
			{
				foreach (var ind in cfg.Indicators)
				{
					if (ind.Type == "KD")
					{
						ComputeKd(bars, ind.Period, ind.SmoothK, ind.SmoothD);
					}
					else if (ind.Type == "MACD")
					{
						ComputeMacd(bars, ind.EMA1, ind.EMA2, ind.Day);
					}
				}
			}
		}

		private static int[] ParsePeriods(string? csv)
		{
			if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
			return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
				.Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
				.Where(v => v > 0)
				.Distinct()
				.OrderBy(v => v)
				.ToArray();
		}

		private static void ComputeMa(List<ChartKBar> bars, int period, string? maType)
		{
			if (bars.Count == 0) return;
			int n = Math.Max(1, period);
			string key = (maType?.ToUpperInvariant() == "EMA") ? $"EMA{n}" : $"MA{n}";

			if (maType?.ToUpperInvariant() == "EMA")
			{
				decimal alpha = 2m / (n + 1);
				decimal ema = bars[0].Close;
				for (int i = 0; i < bars.Count; i++)
				{
					var c = bars[i].Close;
					ema = ema + alpha * (c - ema);
					bars[i].Indicators[key] = ema;
				}
				return;
			}

			var win = new Queue<decimal>();
			decimal sum = 0;
			for (int i = 0; i < bars.Count; i++)
			{
				var c = bars[i].Close;
				win.Enqueue(c); sum += c;
				if (win.Count > n) sum -= win.Dequeue();
				bars[i].Indicators[key] = sum / win.Count;
			}
		}

		private static void ComputeBbi(List<ChartKBar> bars, int[] periods)
		{
			if (bars.Count == 0) return;
			if (periods.Length == 0) periods = new[] { 5, 10, 30, 60 };
			var wins = new Queue<decimal>[periods.Length];
			var sums = new decimal[periods.Length];
			for (int i = 0; i < periods.Length; i++) wins[i] = new Queue<decimal>();

			for (int i = 0; i < bars.Count; i++)
			{
				var c = bars[i].Close;
				decimal avg = 0;
				for (int pi = 0; pi < periods.Length; pi++)
				{
					int n = Math.Max(1, periods[pi]);
					var win = wins[pi];
					win.Enqueue(c); sums[pi] += c;
					if (win.Count > n) sums[pi] -= win.Dequeue();
					avg += sums[pi] / win.Count;
				}
				bars[i].Indicators["BBI"] = avg / periods.Length;
			}
		}

		private static void ComputeBoll(List<ChartKBar> bars, int period, double k)
		{
			if (bars.Count == 0) return;
			int n = Math.Max(1, period);
			var win = new Queue<decimal>();
			for (int i = 0; i < bars.Count; i++)
			{
				var c = bars[i].Close;
				win.Enqueue(c);
				if (win.Count > n) win.Dequeue();
				decimal sum = 0;
				foreach (var v in win) sum += v;
				decimal ma = sum / win.Count;
				decimal var = 0;
				foreach (var v in win) { var d = v - ma; var += d * d; }
				decimal std = (decimal)Math.Sqrt((double)(var / win.Count));
				bars[i].Indicators["BOLL_MID"] = ma;
				bars[i].Indicators["BOLL_UP"] = ma + (decimal)k * std;
				bars[i].Indicators["BOLL_DN"] = ma - (decimal)k * std;
			}
		}

		private static void ComputeKd(List<ChartKBar> bars, int period, int smoothK, int smoothD)
		{
			if (bars.Count == 0) return;
			int n = Math.Max(1, period);
			decimal prevK = 50m;
			decimal prevD = 50m;
			var window = new Queue<ChartKBar>();

			for (int i = 0; i < bars.Count; i++)
			{
				var bar = bars[i];
				window.Enqueue(bar);
				if (window.Count > n) window.Dequeue();

				decimal highestHigh = window.Max(b => b.High);
				decimal lowestLow = window.Min(b => b.Low);
				decimal rsv = 0m;
				if (highestHigh != lowestLow)
				{
					rsv = (bar.Close - lowestLow) / (highestHigh - lowestLow) * 100m;
				}

				decimal kVal = (2m * prevK + rsv) / 3m;
				decimal dVal = (2m * prevD + kVal) / 3m;
				prevK = kVal;
				prevD = dVal;

				bar.Indicators["KD_K"] = kVal;
				bar.Indicators["KD_D"] = dVal;
			}
		}

		private static void ComputeMacd(List<ChartKBar> bars, int fast, int slow, int signal)
		{
			if (bars.Count == 0) return;
			int f = Math.Max(1, fast);
			int s = Math.Max(f + 1, slow);
			int sig = Math.Max(1, signal);

			decimal alphaFast = 2m / (f + 1);
			decimal alphaSlow = 2m / (s + 1);
			decimal alphaSignal = 2m / (sig + 1);

			decimal emaFast = bars[0].Close;
			decimal emaSlow = bars[0].Close;
			decimal dif = 0m;
			decimal dea = 0m;

			for (int i = 0; i < bars.Count; i++)
			{
				var c = bars[i].Close;
				emaFast = emaFast + alphaFast * (c - emaFast);
				emaSlow = emaSlow + alphaSlow * (c - emaSlow);
				dif = emaFast - emaSlow;
				dea = dea + alphaSignal * (dif - dea);
				bars[i].Indicators["MACD_DIF"] = dif;
				bars[i].Indicators["MACD_DEA"] = dea;
				bars[i].Indicators["MACD_HIST"] = dif - dea;
			}
		}
	}
}
