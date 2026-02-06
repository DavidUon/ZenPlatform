using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Forms = System.Windows.Forms;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Cursors = System.Windows.Input.Cursors;

namespace TaifexHisDbManager
{
    internal partial class DbValidationWindow : Window
    {
        internal enum ValidationMode
        {
            Import,
            DateRange
        }


        private readonly string _dbFolder;
        private readonly HashSet<int> _loadedYears = new();
        private readonly ValidationMode _mode;
        private readonly List<DateTime> _rangeClicks = new();
        private DateTime? _rangeStart;
        private DateTime? _rangeEnd;
        private readonly string _rangeSettingsPath = Path.Combine(AppContext.BaseDirectory, "date_range_settings.json");
        public DateTime? SelectedStartDateTime { get; private set; }
        public DateTime? SelectedEndDateTime { get; private set; }
        public int SelectedPreloadDays { get; private set; } = 1;
        public BacktestMode SelectedBacktestMode { get; private set; } = BacktestMode.Exact;
        public BacktestProduct SelectedBacktestProduct { get; private set; } = BacktestProduct.Tx;

        internal DbValidationWindow(string dbFolder)
            : this(dbFolder, ValidationMode.Import)
        {
        }

        internal DbValidationWindow(string dbFolder, ValidationMode mode)
        {
            InitializeComponent();
            _dbFolder = dbFolder;
            _mode = mode;
            BuildTabs();
            YearTabs.SelectionChanged += OnTabSelectionChanged;
            StartDatePicker.SelectedDateChanged += OnRangePickerChanged;
            EndDatePicker.SelectedDateChanged += OnRangePickerChanged;
            ApplyMode();
        }


        private void BuildTabs()
        {
            YearTabs.Items.Clear();

            if (string.IsNullOrWhiteSpace(_dbFolder) || !Directory.Exists(_dbFolder))
                return;

            var dbFiles = GetDbFiles(_dbFolder);
            foreach (var item in dbFiles)
            {
                var tab = new TabItem
                {
                    Header = item.Year.ToString(),
                    Tag = item
                };

                YearTabs.Items.Add(tab);
            }
        }

        private async void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (YearTabs.SelectedItem is not TabItem tab)
                return;

            if (tab.Tag is not DbFileInfo info)
                return;

            if (_loadedYears.Contains(info.Year))
                return;

            try
            {
                YearTabs.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;
                tab.Content = new TextBlock
                {
                    Text = "載入中...",
                    Margin = new Thickness(12),
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
                };

                var statusMap = await System.Threading.Tasks.Task.Run(() => LoadDateStatuses(info.Path));
                var grid = BuildMonthGrid(info.Year, statusMap);
                tab.Content = grid;
                _loadedYears.Add(info.Year);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                YearTabs.IsEnabled = true;
            }
        }

        private static List<DbFileInfo> GetDbFiles(string dbFolder)
        {
            var files = new List<DbFileInfo>();
            var regex = new Regex(@"歷史價格資料庫\.(\d{4})\.db$", RegexOptions.IgnoreCase);

            foreach (string path in Directory.GetFiles(dbFolder, "歷史價格資料庫.*.db", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(path);
                var match = regex.Match(fileName);
                if (!match.Success)
                    continue;

                if (int.TryParse(match.Groups[1].Value, out int year))
                    files.Add(new DbFileInfo(year, path));
            }

            return files
                .GroupBy(f => f.Year)
                .Select(g => g.First())
                .OrderBy(f => f.Year)
                .ToList();
        }

        private void ApplyMode()
        {
            if (_mode == ValidationMode.DateRange)
            {
                Title = "選擇歷史資料區間";
                ImportModePanel.Visibility = Visibility.Collapsed;
                RangeModePanel.Visibility = Visibility.Visible;
                LoadRangeSettings();
                SelectYearTabForRangeStart();
            }
            else
            {
                Title = "歷史資料庫維護";
                ImportModePanel.Visibility = Visibility.Visible;
                RangeModePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshTabs()
        {
            _loadedYears.Clear();
            BuildTabs();
        }

        private void SelectYearTabForRangeStart()
        {
            var date = StartDatePicker.SelectedDate ?? EndDatePicker.SelectedDate;
            if (date == null)
                return;

            string yearText = date.Value.Year.ToString();
            foreach (var item in YearTabs.Items)
            {
                if (item is TabItem tab && tab.Header?.ToString() == yearText)
                {
                    YearTabs.SelectedItem = tab;
                    break;
                }
            }
        }

        private void OnStartBacktest(object sender, RoutedEventArgs e)
        {
            SaveRangeSettings();
            SelectedStartDateTime = CombineDateTime(StartDatePicker.SelectedDate, StartTimeCombo.Text);
            SelectedEndDateTime = CombineDateTime(EndDatePicker.SelectedDate, EndTimeCombo.Text);
            SelectedPreloadDays = ParsePreloadDays();
            SelectedBacktestMode = GetSelectedBacktestMode();
            SelectedBacktestProduct = GetSelectedBacktestProduct();
            DialogResult = true;
            Close();
        }

        private void OnCancelBacktest(object sender, RoutedEventArgs e)
        {
            SelectedStartDateTime = CombineDateTime(StartDatePicker.SelectedDate, StartTimeCombo.Text);
            SelectedEndDateTime = CombineDateTime(EndDatePicker.SelectedDate, EndTimeCombo.Text);
            SelectedPreloadDays = ParsePreloadDays();
            SelectedBacktestMode = GetSelectedBacktestMode();
            SelectedBacktestProduct = GetSelectedBacktestProduct();
            DialogResult = false;
            Close();
        }

        

        private DataGrid BuildMonthGrid(int year, Dictionary<DateTime, DateStatus> statusMap)
        {
            var table = new DataTable();
            table.Columns.Add("月份");

            string[] dayNames = { "一", "二", "三", "四", "五", "六", "日" };
            for (int week = 1; week <= 6; week++)
            {
                foreach (string day in dayNames)
                {
                    table.Columns.Add($"{day}{week}");
                }
            }

            for (int month = 1; month <= 12; month++)
            {
                var row = table.NewRow();
                row[0] = month;
                FillMonthRow(year, month, row);
                table.Rows.Add(row);
            }

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                ItemsSource = table.DefaultView,
                ColumnWidth = DataGridLength.Auto,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                RowBackground = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
                Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                EnableRowVirtualization = false,
                EnableColumnVirtualization = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                RowHeaderWidth = 0
            };
            grid.SelectionChanged += (_, _) =>
            {
                grid.SelectedItem = null;
                grid.SelectedCells.Clear();
            };
            grid.PreviewMouseLeftButtonUp += OnGridDateClick;
            grid.Tag = year;

            var monthColumn = new DataGridTextColumn
            {
                Header = "月份",
                Binding = new Binding("月份")
            };
            grid.Columns.Add(monthColumn);

            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            cellStyle.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Center));
            grid.CellStyle = cellStyle;

            var converter = new DateStatusToBrushConverter(statusMap, year);
            var rangeConverter = new DateRangeToBrushConverter(() => (_rangeStart, _rangeEnd), year);

            for (int week = 1; week <= 6; week++)
            {
                foreach (string day in dayNames)
                {
                    string columnName = $"{day}{week}";
                    var textColumn = new DataGridTextColumn
                    {
                        Header = day,
                        Binding = new Binding(columnName)
                    };

                    var elementStyle = new Style(typeof(TextBlock));
                    elementStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
                    elementStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
                    elementStyle.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 0, 6, 0)));

                    var multi = new MultiBinding { Converter = converter };
                    multi.Bindings.Add(new Binding(columnName));
                    multi.Bindings.Add(new Binding("月份"));
                    elementStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, multi));

                    textColumn.ElementStyle = elementStyle;

                    var dayCellStyle = new Style(typeof(DataGridCell));
                    dayCellStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
                    dayCellStyle.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Center));
                    dayCellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
                    var rangeBinding = new MultiBinding { Converter = rangeConverter, ConverterParameter = day };
                    rangeBinding.Bindings.Add(new Binding(columnName));
                    rangeBinding.Bindings.Add(new Binding("月份"));
                    dayCellStyle.Setters.Add(new Setter(BackgroundProperty, rangeBinding));
                    textColumn.CellStyle = dayCellStyle;

                    grid.Columns.Add(textColumn);
                }
            }

            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 45, 45))));
            headerStyle.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(Color.FromRgb(230, 230, 230))));
            headerStyle.Setters.Add(new Setter(BorderBrushProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
            headerStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            grid.ColumnHeaderStyle = headerStyle;

            var rowHeaderStyle = new Style(typeof(DataGridRowHeader));
            rowHeaderStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            rowHeaderStyle.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Center));
            grid.RowHeaderStyle = rowHeaderStyle;

            return grid;
        }

        private void OnImportOfficialHistory(object sender, RoutedEventArgs e)
        {
            string dbOutputFolder = Path.Combine(AppContext.BaseDirectory, "回測歷史資料庫");

            var window = new ImportRunWindow(dbOutputFolder)
            {
                Owner = this
            };
            window.ShowDialog();
            RefreshTabs();
        }

        private void OnImportHelpClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var message =
                "先將下載好的 台灣期貨交易所成交簡檔 放入同一個資料夾中，並於程式中選擇該資料夾。\n\n" +
                "按下【轉換】後，系統將會把此資料夾內的所有檔案匯入至系統資料庫中自動管理。\n\n" +
                "匯入完成後，處理完成的檔案會自動移動至來源資料夾中的 「已匯入」 子資料夾做分類，\n" +
                "您可以移動或刪除這些已經匯入的資料，系統只靠轉換過的資料進行運作。\n\n" +
                "支援的來源檔案格式包含：\n\n" +
                "。原始下載的 ZIP 壓縮檔\n" +
                "。已解壓縮的 CSV 檔案\n\n" +
                "注意事項：\n" +
                "每年的歷史資料檔案大小約為 2GB，請確保硬碟空間充足，以避免轉換失敗。";
            var window = new ImportHelpWindow(message)
            {
                Owner = this
            };
            window.ShowDialog();
        }


        private static void FillMonthRow(int year, int month, DataRow row)
        {
            var firstDay = new DateTime(year, month, 1);
            int daysInMonth = DateTime.DaysInMonth(year, month);

            int startOffset = ((int)firstDay.DayOfWeek + 6) % 7; // Monday=0 ... Sunday=6

            for (int day = 1; day <= daysInMonth; day++)
            {
                int index = startOffset + (day - 1);
                int week = index / 7;
                if (week >= 6)
                    break;

                int dayOfWeek = index % 7;
                int columnIndex = 1 + week * 7 + dayOfWeek;
                row[columnIndex] = day;
            }
        }

        private void OnGridDateClick(object sender, MouseButtonEventArgs e)
        {
            if (_mode != ValidationMode.DateRange)
                return;

            if (sender is not DataGrid grid)
                return;

            var dep = e.OriginalSource as DependencyObject;
            var cell = FindVisualParent<DataGridCell>(dep);
            if (cell == null)
                return;

            var row = FindVisualParent<DataGridRow>(cell);
            if (row == null)
                return;

            if (cell.Column is not DataGridBoundColumn boundColumn)
                return;

            if (boundColumn.Binding is not Binding binding || binding.Path == null)
                return;

            string columnName = binding.Path.Path;
            if (columnName == "月份")
                return;

            if (row.Item is not DataRowView rowView)
                return;

            if (rowView.Row[columnName] is DBNull)
                return;

            if (!int.TryParse(rowView.Row[columnName]?.ToString(), out int day))
                return;

            if (!int.TryParse(rowView.Row["月份"]?.ToString(), out int month))
                return;

            if (grid.Tag is not int year)
                return;

            DateTime date;
            try
            {
                date = new DateTime(year, month, day);
            }
            catch
            {
                return;
            }

            _rangeClicks.Add(date);
            if (_rangeClicks.Count > 2)
                _rangeClicks.RemoveAt(0);

            if (_rangeClicks.Count == 1)
            {
                StartDatePicker.SelectedDate = _rangeClicks[0];
                EndDatePicker.SelectedDate = null;
                UpdateRangeFromPickers();
                RefreshMonthGrids();
                return;
            }

            var start = _rangeClicks[0] <= _rangeClicks[1] ? _rangeClicks[0] : _rangeClicks[1];
            var end = _rangeClicks[0] <= _rangeClicks[1] ? _rangeClicks[1] : _rangeClicks[0];
            StartDatePicker.SelectedDate = start;
            EndDatePicker.SelectedDate = end;
            UpdateRangeFromPickers();
            RefreshMonthGrids();
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T typed)
                    return typed;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void OnRangePickerChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_mode != ValidationMode.DateRange)
                return;

            UpdateRangeFromPickers();
            RefreshMonthGrids();
        }

        private void LoadRangeSettings()
        {
            try
            {
                if (!File.Exists(_rangeSettingsPath))
                    return;

                string json = File.ReadAllText(_rangeSettingsPath);
                var settings = JsonSerializer.Deserialize<DateRangeSettings>(json);
                if (settings == null)
                    return;

                if (DateTime.TryParse(settings.StartDate, out var startDate))
                    StartDatePicker.SelectedDate = startDate;
                if (DateTime.TryParse(settings.EndDate, out var endDate))
                    EndDatePicker.SelectedDate = endDate;

                if (!string.IsNullOrWhiteSpace(settings.StartTime))
                    ApplyTimeCombo(StartTimeCombo, settings.StartTime);
                if (!string.IsNullOrWhiteSpace(settings.EndTime))
                    ApplyTimeCombo(EndTimeCombo, settings.EndTime);

                if (settings.PreloadDays > 0)
                    PreloadDaysTextBox.Text = settings.PreloadDays.ToString();

                ApplyBacktestMode(settings.BacktestMode);
                ApplyBacktestProduct(settings.BacktestProduct);
                UpdateRangeFromPickers();
                RefreshMonthGrids();
            }
            catch
            {
                // Ignore load failures.
            }
        }

        private void SaveRangeSettings()
        {
            try
            {
                int preloadDays = ParsePreloadDays();

                var settings = new DateRangeSettings
                {
                    StartDate = StartDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                    EndDate = EndDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                    StartTime = StartTimeCombo.Text?.Trim() ?? string.Empty,
                    EndTime = EndTimeCombo.Text?.Trim() ?? string.Empty,
                    PreloadDays = preloadDays,
                    BacktestMode = GetSelectedBacktestMode() == BacktestMode.Fast ? "Fast" : "Exact",
                    BacktestProduct = GetSelectedBacktestProduct().ToString()
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_rangeSettingsPath, json);
            }
            catch
            {
                // Ignore save failures.
            }
        }

        private sealed class DateRangeSettings
        {
            public string StartDate { get; set; } = string.Empty;
            public string EndDate { get; set; } = string.Empty;
            public string StartTime { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
            public int PreloadDays { get; set; } = 1;
            public string BacktestMode { get; set; } = "Exact";
            public string BacktestProduct { get; set; } = nameof(global::TaifexHisDbManager.BacktestProduct.Tx);
        }

        private void ApplyBacktestMode(string? mode)
        {
            if (string.Equals(mode, "Fast", System.StringComparison.OrdinalIgnoreCase))
            {
                if (FastBacktestRadio != null)
                    FastBacktestRadio.IsChecked = true;
                if (ExactBacktestRadio != null)
                    ExactBacktestRadio.IsChecked = false;
                return;
            }

            if (ExactBacktestRadio != null)
                ExactBacktestRadio.IsChecked = true;
            if (FastBacktestRadio != null)
                FastBacktestRadio.IsChecked = false;
        }

        private BacktestMode GetSelectedBacktestMode()
        {
            if (FastBacktestRadio != null && FastBacktestRadio.IsChecked == true)
                return BacktestMode.Fast;
            return BacktestMode.Exact;
        }

        private void ApplyBacktestProduct(string? product)
        {
            if (BacktestProductCombo == null)
            {
                return;
            }

            if (string.Equals(product, nameof(BacktestProduct.Mtx), System.StringComparison.OrdinalIgnoreCase))
            {
                BacktestProductCombo.SelectedIndex = 1;
                return;
            }

            if (string.Equals(product, nameof(BacktestProduct.Tmf), System.StringComparison.OrdinalIgnoreCase))
            {
                BacktestProductCombo.SelectedIndex = 2;
                return;
            }

            BacktestProductCombo.SelectedIndex = 0;
        }

        private BacktestProduct GetSelectedBacktestProduct()
        {
            if (BacktestProductCombo?.SelectedItem is ComboBoxItem item &&
                item.Tag != null &&
                int.TryParse(item.Tag.ToString(), out var value) &&
                System.Enum.IsDefined(typeof(BacktestProduct), value))
            {
                return (BacktestProduct)value;
            }

            return BacktestProduct.Tx;
        }

        private static DateTime? CombineDateTime(DateTime? date, string? timeText)
        {
            if (date == null)
                return null;

            if (string.IsNullOrWhiteSpace(timeText))
                return date.Value.Date;

            if (TimeSpan.TryParse(timeText, out var time))
                return date.Value.Date.Add(time);

            return date.Value.Date;
        }

        private static void ApplyTimeCombo(System.Windows.Controls.ComboBox combo, string timeText)
        {
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cb && string.Equals(cb.Content?.ToString(), timeText, System.StringComparison.Ordinal))
                {
                    combo.SelectedItem = cb;
                    return;
                }
            }
        }

        private int ParsePreloadDays()
        {
            int preloadDays = 1;
            _ = int.TryParse(PreloadDaysTextBox.Text?.Trim(), out preloadDays);
            if (preloadDays < 0)
                preloadDays = 0;
            return preloadDays;
        }

        private void UpdateRangeFromPickers()
        {
            var start = StartDatePicker.SelectedDate;
            var end = EndDatePicker.SelectedDate;

            if (start == null && end == null)
            {
                _rangeStart = null;
                _rangeEnd = null;
                return;
            }

            if (start != null && end == null)
            {
                _rangeStart = start.Value;
                _rangeEnd = start.Value;
                return;
            }

            if (start == null && end != null)
            {
                _rangeStart = end.Value;
                _rangeEnd = end.Value;
                return;
            }

            if (start <= end)
            {
                _rangeStart = start;
                _rangeEnd = end;
            }
            else
            {
                _rangeStart = end;
                _rangeEnd = start;
            }
        }

        private void RefreshMonthGrids()
        {
            foreach (var item in YearTabs.Items)
            {
                if (item is TabItem tab && tab.Content is DataGrid grid)
                {
                    grid.Items.Refresh();
                }
            }
        }

        private static Dictionary<DateTime, DateStatus> LoadDateStatuses(string dbPath)
        {
            var map = new Dictionary<DateTime, DateStatus>();
            if (!File.Exists(dbPath))
                return map;

            long fileSize = new FileInfo(dbPath).Length;
            string cachePath = dbPath + ".status.json";
            var cached = TryLoadCache(cachePath, fileSize);
            if (cached is not null)
                return cached;

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT date(datetime(ts, 'unixepoch', '+8 hours')) AS d,
       max(CASE WHEN strftime('%H:%M:%S', datetime(ts, 'unixepoch', '+8 hours')) = '08:45:00' THEN 1 ELSE 0 END) AS has_open,
       max(CASE WHEN strftime('%H:%M:%S', datetime(ts, 'unixepoch', '+8 hours')) = '13:45:00' THEN 1 ELSE 0 END) AS has_day_close,
       max(CASE WHEN strftime('%H:%M:%S', datetime(ts, 'unixepoch', '+8 hours')) = '05:00:00' THEN 1 ELSE 0 END) AS has_night_close
FROM ticks
GROUP BY d;
";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string dateStr = reader.GetString(0);
                long hasOpen = reader.GetInt64(1);
                long hasDayClose = reader.GetInt64(2);
                long hasNightClose = reader.GetInt64(3);

                if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
                    continue;

                if (hasOpen == 1 && hasDayClose == 1)
                {
                    map[date] = DateStatus.FullDay;
                }
                else if (hasNightClose == 1 && hasOpen == 0 && hasDayClose == 0)
                {
                    map[date] = DateStatus.NightOnly;
                }
            }

            TrySaveCache(cachePath, fileSize, map);
            return map;
        }

        private static long ToUnixSeconds(DateTime time)
        {
            var offset = new DateTimeOffset(time, TimeSpan.FromHours(8));
            return offset.ToUnixTimeSeconds();
        }

        private sealed record DbFileInfo(int Year, string Path);

        private sealed class StatusCache
        {
            public long FileSize { get; set; }
            public Dictionary<string, int> Status { get; set; } = new();
        }

        private static Dictionary<DateTime, DateStatus>? TryLoadCache(string cachePath, long fileSize)
        {
            if (!File.Exists(cachePath))
                return null;

            try
            {
                string json = File.ReadAllText(cachePath);
                var cache = JsonSerializer.Deserialize<StatusCache>(json);
                if (cache == null || cache.FileSize != fileSize)
                    return null;

                var map = new Dictionary<DateTime, DateStatus>();
                foreach (var kvp in cache.Status)
                {
                    if (DateTime.TryParseExact(kvp.Key, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
                        map[date] = (DateStatus)kvp.Value;
                }
                return map;
            }
            catch
            {
                return null;
            }
        }

        private static void TrySaveCache(string cachePath, long fileSize, Dictionary<DateTime, DateStatus> map)
        {
            try
            {
                var cache = new StatusCache
                {
                    FileSize = fileSize,
                    Status = map.ToDictionary(k => k.Key.ToString("yyyy-MM-dd"), v => (int)v.Value)
                };
                string json = JsonSerializer.Serialize(cache);
                File.WriteAllText(cachePath, json);
            }
            catch
            {
                // Ignore cache write failures.
            }
        }
    }

    public enum DateStatus
    {
        None = 0,
        FullDay = 1,
        NightOnly = 2
    }

    internal sealed class DateStatusToBrushConverter : IMultiValueConverter
    {
        private readonly Dictionary<DateTime, DateStatus> _statusMap;
        private readonly int _year;
        private readonly Brush _defaultBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        private readonly Brush _greenBrush = new SolidColorBrush(Color.FromRgb(0, 255, 0));
        private readonly Brush _yellowBrush = new SolidColorBrush(Color.FromRgb(255, 255, 0));

        public DateStatusToBrushConverter(Dictionary<DateTime, DateStatus> statusMap, int year)
        {
            _statusMap = statusMap;
            _year = year;
        }

        public object Convert(object[] values, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length < 2)
                return _defaultBrush;

            if (!int.TryParse(values[0]?.ToString(), out int day))
                return _defaultBrush;

            if (!int.TryParse(values[1]?.ToString(), out int month))
                return _defaultBrush;

            var date = new DateTime(_year, month, day);
            if (_statusMap.TryGetValue(date, out var status))
            {
                return status switch
                {
                    DateStatus.FullDay => _greenBrush,
                    DateStatus.NightOnly => _yellowBrush,
                    _ => _defaultBrush
                };
            }

            return _defaultBrush;
        }

        public object[] ConvertBack(object value, System.Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
        {
            return new object[] { Binding.DoNothing, Binding.DoNothing };
    }

}

    internal sealed class DateRangeToBrushConverter : IMultiValueConverter
    {
        private readonly Func<(DateTime? Start, DateTime? End)> _rangeProvider;
        private readonly int _year;
        private readonly Brush _rangeBrush = new SolidColorBrush(Color.FromRgb(54, 78, 114));
        private readonly Brush _weekendBrush = new SolidColorBrush(Color.FromRgb(30, 0, 0));

        public DateRangeToBrushConverter(Func<(DateTime? Start, DateTime? End)> rangeProvider, int year)
        {
            _rangeProvider = rangeProvider;
            _year = year;
        }

        public object Convert(object[] values, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length < 2)
                return DependencyProperty.UnsetValue;

            if (!int.TryParse(values[0]?.ToString(), out int day))
                return DependencyProperty.UnsetValue;

            if (!int.TryParse(values[1]?.ToString(), out int month))
                return DependencyProperty.UnsetValue;

            DateTime? start;
            DateTime? end;
            (start, end) = _rangeProvider();

            DateTime date;
            try
            {
                date = new DateTime(_year, month, day);
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }

            if (start != null && end != null && date >= start.Value && date <= end.Value)
                return _rangeBrush;

            var dayName = parameter as string;
            if (dayName == "六" || dayName == "日")
                return _weekendBrush;

            return DependencyProperty.UnsetValue;
        }

        public object[] ConvertBack(object value, System.Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
        {
            return new object[] { Binding.DoNothing, Binding.DoNothing };
        }
    }

    // Weekend background is handled per-column in BuildMonthGrid.

}
