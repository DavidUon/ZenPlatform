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
					var time = DateTimeOffset.FromUnixTimeSeconds(ts).DateTime;
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
			_currentBars = bars;
			ChartView.LoadHistory(bars);
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
			if (File.Exists(path))
			{
				File.Delete(path);
			}

			using var conn = new SqliteConnection($"Data Source={path}");
			conn.Open();
			using var tx = conn.BeginTransaction();

			var create = conn.CreateCommand();
			create.CommandText = @"
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
					var time = DateTimeOffset.FromUnixTimeSeconds(ts).DateTime;
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

			return (bars, configJson, meta);
		}
	}
}
