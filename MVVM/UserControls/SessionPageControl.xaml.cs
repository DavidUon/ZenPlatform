using System;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ZenPlatform.Core;
using ZenPlatform.MVVM.RulePage;
using ZenPlatform.SessionManager;
using ZenPlatform.SessionManager.Backtest;
using ZenPlatform.Utility;

namespace ZenPlatform.MVVM.UserControls
{
    public partial class SessionPageControl : UserControl
    {
        private ZenPlatform.Core.Core? _core;
        private SessionManager.SessionManager? _manager;
        private bool _columnObserversAttached;
        private BacktestEngine? _backtestEngine;
        private CancellationTokenSource? _backtestCts;
        private Task? _backtestTask;
        private Stopwatch? _backtestStopwatch;
        private int _backtestStopOnce;
        private Action<int>? _backtestProgressHandler;
        private Action<bool>? _backtestManagerStoppedHandler;
        private long _backtestTotalCount;
        private DateTime? _backtestRangeStart;
        private DateTime? _backtestRangeEnd;
        private TaifexHisDbManager.BacktestMode _backtestLastMode;

        public SessionPageControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
            MouseLeave += OnMouseLeave;
        }
        private void OnStrategyToggleClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SessionPageViewModel page)
            {
                var manager = page.Manager;
                var owner = Window.GetWindow(this);
                var isRunning = manager.IsStrategyRunning;
                var message = isRunning ? "確認要結束策略嗎？" : "確認要開始策略嗎？";
                var title = isRunning ? "結束策略" : "開始策略";
                if (owner == null || ConfirmWindow.Show(owner, message, title))
                {
                    manager.ToggleStrategy();
                }
            }
        }

        private void OnSessionGridRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var row = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as System.Windows.DependencyObject);
            if (row != null)
            {
                row.IsSelected = true;
                SessionGrid.SelectedItem = row.Item;
                SessionGrid.Focus();
            }
        }

        private void OnSessionGridLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var row = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as System.Windows.DependencyObject);
            if (row == null)
            {
                SessionGrid.SelectedIndex = -1;
                SessionGrid.UnselectAll();
            }
        }

        private void OnSessionGridLostFocus(object sender, RoutedEventArgs e)
        {
            SessionGrid.SelectedIndex = -1;
            SessionGrid.UnselectAll();
        }

        private void OnSessionGridContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            if (SessionGrid.SelectedItem is not ZenPlatform.Strategy.Session session)
            {
                CloseSessionMenuItem.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            var hasPosition = session.Position != 0;
            CloseSessionMenuItem.Visibility = hasPosition ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            CloseSessionMenuItem.IsEnabled = hasPosition;
        }

        private void OnCloseSessionClick(object sender, RoutedEventArgs e)
        {
            if (SessionGrid.SelectedItem is ZenPlatform.Strategy.Session session)
            {
                session.CloseAll();
            }
        }

        private static T? FindAncestor<T>(System.Windows.DependencyObject? current) where T : System.Windows.DependencyObject
        {
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void OnCreateSessionClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            var owner = Window.GetWindow(this);
            var dialog = new SelectSideWindow
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var result = dialog.ShowDialog();
            if (result != true || dialog.SelectedIsBuy == null)
            {
                return;
            }

            var session = page.Manager.AddSession();
            if (session == null)
            {
                return;
            }

            session.Start(dialog.SelectedIsBuy.Value);
        }

        private void OnStrategySettingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            var owner = Window.GetWindow(this);
            var dialog = new StrategySettingsWindow(page.Manager.RuleSet)
            {
                Owner = owner
            };
            dialog.ShowDialog();
        }

        private void OnHistoryBacktestClick(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            if (owner == null)
            {
                return;
            }

            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            if (page.Manager.IsBacktestActive)
            {
                StopBacktest(true);
                return;
            }

            var dbManager = new TaifexHisDbManager.TaifexHisDbManager(owner);
            var info = dbManager.GetBackTestInfo();
            if (!info.Accepted || info.Start == null || info.End == null)
            {
                return;
            }

            if (_backtestTask is { IsCompleted: false })
            {
                StopBacktest(true);
                return;
            }

            if (_core == null)
            {
                ResolveCore();
            }
            if (_core == null)
            {
                MessageBoxWindow.Show(owner, "核心尚未就緒，無法開始回測。", "歷史回測");
                return;
            }

            int product = (int)info.Product;
            var dbFolder = Path.Combine(AppContext.BaseDirectory, "回測歷史資料庫");
            var dataSource = new HisDbDataSource(dbFolder);

            _backtestEngine?.Dispose();
            _backtestEngine = new BacktestEngine(page.Manager);
            _backtestEngine.UseBarCloseTimeSignal = info.Mode != TaifexHisDbManager.BacktestMode.Fast;
            _backtestEngine.Initialize();
            _core.LogCtrl.SetRenderSuspended(false);

            _backtestCts = new CancellationTokenSource();
            _backtestRangeStart = info.Start;
            _backtestRangeEnd = info.End;
            _backtestLastMode = info.Mode;
            page.Manager.StrategyStartTime = info.Start;
            if (!page.Manager.IsStrategyRunning)
            {
                page.Manager.ToggleStrategy();
            }
            page.Manager.Log.SetMaxLines(1000);
            page.Manager.Log.Clear();
            page.Manager.Log.UseSystemTime();
            page.Manager.Log.Add("==== 回測開始 ====");
            if (_core.LogCtrl.TryGetTargetScreenRect(out var logRect))
            {
                var title = string.IsNullOrWhiteSpace(page.Manager.DisplayName)
                    ? $"策略{page.Manager.Index + 1}回測中"
                    : $"{page.Manager.DisplayName}回測中";
                ConsoleLog.OpenAt(logRect, title);
            }
            _core.LogCtrl.SetConsoleOnly(true);
            _backtestStopwatch = Stopwatch.StartNew();
            _backtestStopOnce = 0;
            Action<string> log = text => Dispatcher.Invoke(() => page.Manager.Log.AddAt(DateTime.Now, text));
            Action<DateTime, string> logWithTime = (time, text) => Dispatcher.Invoke(() =>
            {
                page.Manager.Log.AddAt(time, text);
            });
            Action<string> setStatus = text => Dispatcher.Invoke(() => page.Manager.BacktestStatusText = text);
            _backtestProgressHandler = percent => Dispatcher.Invoke(() => page.Manager.BacktestProgressPercent = percent);
            _backtestEngine.ProgressChanged += _backtestProgressHandler;
            _backtestManagerStoppedHandler = wasCanceled =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StopBacktest(wasCanceled);
                }));
            page.Manager.BacktestStopped += _backtestManagerStoppedHandler;
            Action<long> setTotalCount = total => Interlocked.Exchange(ref _backtestTotalCount, total);
            _backtestTask = Task.Run(() => RunBacktest(_backtestEngine, dataSource, product, info.Start.Value, info.End.Value, info.PreloadDays, info.Mode, _core, log, logWithTime, setStatus, setTotalCount, _backtestCts.Token));
            _backtestTask.ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(new Action(() => StopBacktest(false)), System.Windows.Threading.DispatcherPriority.Send);
            }, TaskScheduler.Default);
        }

        private void OnRealTradeChecked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SessionPageViewModel page)
            {
                var manager = page.Manager;
                if (Window.GetWindow(this) is Window owner &&
                    owner.DataContext is ZenPlatform.Core.Core core &&
                    !core.IsBrokerConnected)
                {
                    MessageBoxWindow.Show(owner, "期貨商連線尚未建立，無法進行真實交易", "真實交易");
                    manager.IsRealTrade = false;
                    if (sender is RadioButton rb)
                    {
                        rb.IsChecked = false;
                    }
                    return;
                }

                manager.IsRealTrade = true;
            }
        }

        private void OnSimTradeChecked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SessionPageViewModel page)
            {
                page.IsRealTrade = false;
            }
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ResolveCore();
            EnforceBrokerState();
            ApplyColumnWidths();
            AttachColumnWidthObservers();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CaptureColumnWidths();
            StopBacktest();
            if (_core != null)
            {
                _core.PropertyChanged -= OnCorePropertyChanged;
                _core = null;
            }
        }

        private void StopBacktest(bool wasCanceled = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => StopBacktest(wasCanceled)), System.Windows.Threading.DispatcherPriority.Send);
                return;
            }
            try
            {
                if (Interlocked.Exchange(ref _backtestStopOnce, 1) == 1)
                {
                    return;
                }
                if (_backtestEngine != null)
                {
                    if (_backtestProgressHandler != null)
                    {
                        _backtestEngine.ProgressChanged -= _backtestProgressHandler;
                        _backtestProgressHandler = null;
                    }
                    if (_backtestManagerStoppedHandler != null)
                    {
                        _backtestEngine.Manager.BacktestStopped -= _backtestManagerStoppedHandler;
                        _backtestManagerStoppedHandler = null;
                    }
                }
                if (_backtestCts != null)
                {
                    _backtestCts.Cancel();
                    _backtestCts.Dispose();
                    _backtestCts = null;
                }

                if (_backtestEngine?.Manager.IsStrategyRunning == true)
                {
                    _backtestEngine.Manager.StopStrategySilently();
                }

                if (_backtestEngine != null)
                {
                    _backtestEngine.Manager.Log.UseSystemTime();
                    _backtestEngine.Manager.Log.SetMaxLines(5000);
                    if (_backtestStopwatch != null)
                    {
                        _backtestStopwatch.Stop();
                    }
                    var lastEntry = _backtestEngine.Manager.Log.Entries.Count > 0
                        ? _backtestEngine.Manager.Log.Entries[^1].Text
                        : string.Empty;
                    if (wasCanceled)
                    {
                        if (!string.Equals(lastEntry, "==== 回測中斷 ====", StringComparison.Ordinal))
                        {
                            _backtestEngine.Manager.Log.AddAt(DateTime.Now, "==== 回測中斷 ====");
                        }
                    }
                    else
                    {
                        var elapsed = _backtestStopwatch?.Elapsed ?? TimeSpan.Zero;
                        var modeText = _backtestLastMode == TaifexHisDbManager.BacktestMode.Fast
                            ? "快速回測"
                            : "精確回測";
                        _backtestEngine.Manager.Log.AddAt(DateTime.Now, "==== 回測結束 ====");
                        _backtestEngine.Manager.Log.AddAt(DateTime.Now, modeText);
                        _backtestEngine.Manager.Log.AddAt(DateTime.Now, $"用時 {elapsed:hh\\:mm\\:ss}");
                        if (_backtestRangeStart.HasValue && _backtestRangeEnd.HasValue && elapsed.TotalSeconds > 0)
                        {
                            var minutes = (_backtestRangeEnd.Value - _backtestRangeStart.Value).TotalMinutes;
                            if (minutes > 0)
                            {
                                _backtestEngine.Manager.Log.AddAt(DateTime.Now, $"回測總分鐘數: {minutes:0}");
                                var metric = minutes / 1440.0 / elapsed.TotalSeconds;
                                _backtestEngine.Manager.Log.AddAt(DateTime.Now, $"效能指標: {metric:0.00} (回測天/秒)");
                            }
                        }
                    }
                }
                _backtestEngine?.Dispose();
                _backtestEngine = null;
                _backtestTask = null;
                _backtestStopwatch = null;
                _backtestRangeStart = null;
                _backtestRangeEnd = null;
                _backtestLastMode = default;
                _core?.LogCtrl.SetConsoleOnly(false);
                ConsoleLog.Close();
            }
            catch (Exception)
            {
                throw;
            }
        }


        private static void RunBacktest(
            BacktestEngine engine,
            HisDbDataSource dataSource,
            int product,
            DateTime start,
            DateTime end,
            int preloadDays,
            TaifexHisDbManager.BacktestMode mode,
            Core.Core core,
            Action<string> log,
            Action<DateTime, string> logWithTime,
            Action<string> setStatus,
            Action<long> setTotalCount,
            CancellationToken token)
        {
            DateTime preloadStart = start;
            if (preloadDays > 0)
            {
                var found = dataSource.FindPreloadStartByTime(product, start, preloadDays);
                preloadStart = found ?? start.AddDays(-preloadDays);
            }
            engine.Manager.SuppressIndicatorLog = true;

            long totalMain;
            long totalPreload = 0;
            if (mode == TaifexHisDbManager.BacktestMode.Fast)
            {
                totalMain = dataSource.CountBars1m(product, start, end);
                if (preloadStart < start)
                {
                    totalPreload = dataSource.CountBars1m(product, preloadStart, start);
                }
                log($"回測資料筆數(1分K): {totalMain:N0}，前置: {totalPreload:N0}");
            }
            else
            {
                totalMain = dataSource.CountTicks(product, start, end, null);
                if (preloadStart < start)
                {
                    totalPreload = dataSource.CountTicks(product, preloadStart, start, null);
                }
                log($"回測資料筆數(tick): {totalMain:N0}，前置: {totalPreload:N0}");
            }
            setTotalCount(totalMain);
            engine.SetTotalUnits(totalMain);

            if (totalMain == 0)
            {
                if (mode == TaifexHisDbManager.BacktestMode.Fast)
                {
                    var range = dataSource.GetBarRange1m(product);
                    if (range.Min == null || range.Max == null)
                    {
                        log("回測檢查: 1分K 無任何資料。");
                    }
                    else
                    {
                        log($"回測檢查: 1分K 可用區間 {range.Min:yyyy/MM/dd HH:mm:ss} ~ {range.Max:yyyy/MM/dd HH:mm:ss}");
                    }
                }
                else
                {
                    var range = dataSource.GetTickRange(product, null);
                    if (range.Min == null || range.Max == null)
                    {
                        log("回測檢查: 該商品無 tick 資料。");
                    }
                    else
                    {
                        log($"回測檢查: 該商品 tick 可用區間 {range.Min:yyyy/MM/dd HH:mm:ss} ~ {range.Max:yyyy/MM/dd HH:mm:ss}");
                    }

                    var anyRange = dataSource.GetTickRange(product, null);
                    if (anyRange.Min == null || anyRange.Max == null)
                    {
                        log("回測檢查: 該商品完全沒有 tick 資料。");
                    }
                    else
                    {
                        log($"回測檢查: 該商品 tick 可用區間 {anyRange.Min:yyyy/MM/dd HH:mm:ss} ~ {anyRange.Max:yyyy/MM/dd HH:mm:ss}");
                    }
                }
            }

            if (preloadStart < start)
            {
                var preloadEnd = start.AddSeconds(-1);
                if (preloadEnd >= preloadStart)
                {
                    RunRange(engine, dataSource, product, preloadStart, preloadEnd, mode, core, log, logWithTime, setStatus, token, false);
                }
            }

            if (token.IsCancellationRequested)
            {
                engine.Manager.SuppressIndicatorLog = false;
                return;
            }

            engine.Manager.SuppressIndicatorLog = false;

            RunRange(engine, dataSource, product, start, end, mode, core, log, logWithTime, setStatus, token, true);

            engine.CompleteProduction();
            engine.WaitForDrainAsync(token).GetAwaiter().GetResult();
        }

        private static void RunRange(
            BacktestEngine engine,
            HisDbDataSource dataSource,
            int product,
            DateTime start,
            DateTime end,
            TaifexHisDbManager.BacktestMode mode,
            Core.Core core,
            Action<string> log,
            Action<DateTime, string> logWithTime,
            Action<string> setStatus,
            CancellationToken token,
            bool countProgress)
        {
            DateTime? lastSecondStamp = null;
            var productName = GetProductName(product);
            int year = 0;
            int month = 0;
            int? lastYearMonth = null;

            void EnqueueSecondIfNeeded(DateTime time)
            {
                var stamp = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second, time.Kind);
                if (lastSecondStamp == null || lastSecondStamp.Value != stamp)
                {
                    lastSecondStamp = stamp;
                    engine.EnqueueTime(time);
                }
            }

            void LogMonthIfChanged(DateTime time)
            {
                var ym = time.Year * 100 + time.Month;
                if (lastYearMonth == null || lastYearMonth.Value != ym)
                {
                    lastYearMonth = ym;
                    var text = $"回測進度: {time:yyyy/MM}";
                    log(text);
                    setStatus(text);
                }
            }

            void UpdateProgress()
            {
                if (countProgress)
                {
                    engine.EnqueueProgressUnit();
                }
            }

            void EnqueueTick(decimal price, int volume, DateTime time)
            {
                var volQuote = new QuoteUpdate(productName, QuoteField.Volume, year, month, volume.ToString(), false, time, QuoteSource.Network);
                engine.EnqueueTick(volQuote);
                var priceQuote = new QuoteUpdate(productName, QuoteField.Last, year, month, price.ToString(), false, time, QuoteSource.Network);
                engine.EnqueueTick(priceQuote);
            }

            if (mode == TaifexHisDbManager.BacktestMode.Fast)
            {
                foreach (var batch in dataSource.ReadBar1mBatches(product, start, end, 10000))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    engine.WaitForQueueBelow(50000, token);

                    foreach (var bar in batch)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        var baseTime = bar.EndTime;
                        LogMonthIfChanged(baseTime);
                        var oneMinBar = new KChartCore.FunctionKBar
                        {
                            StartTime = baseTime.AddMinutes(-1),
                            CloseTime = baseTime,
                            Open = bar.Open,
                            High = bar.High,
                            Low = bar.Low,
                            Close = bar.Close,
                            Volume = bar.Volume,
                            IsNullBar = false,
                            IsFloating = false,
                            ContainsMarketOpen = bar.Event == 1,
                            ContainsMarketClose = bar.Event == 2,
                            IsAlignmentBar = bar.Event == 1
                        };

                        EnqueueSecondIfNeeded(baseTime);
                        engine.EnqueueBar(oneMinBar);
                        UpdateProgress();
                    }
                }
                return;
            }

            foreach (var batch in dataSource.ReadTickBatches(product, start, end, 10000, null))
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                engine.WaitForQueueBelow(50000, token);

                foreach (var tick in batch)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    LogMonthIfChanged(tick.Time);
                    EnqueueSecondIfNeeded(tick.Time);
                    EnqueueTick(tick.Price, tick.Volume, tick.Time);
                    // 回測事件暫不輸出到 UI
                    UpdateProgress();
                }
            }
        }

        private static string GetEventText(int ev)
        {
            return ev switch
            {
                1 => "開盤",
                2 => "收盤",
                _ => $"事件{ev}"
            };
        }

        private static string GetProductName(int product)
        {
            return product switch
            {
                (int)TaifexHisDbManager.BacktestProduct.Tx => "大型台指",
                (int)TaifexHisDbManager.BacktestProduct.Mtx => "小型台指",
                (int)TaifexHisDbManager.BacktestProduct.Tmf => "微型台指",
                _ => "大型台指"
            };
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _manager = (DataContext as SessionPageViewModel)?.Manager;
            EnforceBrokerState();
            ApplyColumnWidths();
        }

        private void ResolveCore()
        {
            if (_core != null)
            {
                return;
            }

            if (Window.GetWindow(this) is Window owner && owner.DataContext is ZenPlatform.Core.Core core)
            {
                _core = core;
                _core.PropertyChanged += OnCorePropertyChanged;
            }
        }

        private void OnCorePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(ZenPlatform.Core.Core.IsBrokerConnected), System.StringComparison.Ordinal))
            {
                EnforceBrokerState();
            }
        }

        private void EnforceBrokerState()
        {
            if (_core != null && !_core.IsBrokerConnected && _manager != null)
            {
                _manager.IsRealTrade = false;
            }
        }

        private void OnMouseLeave(object sender, RoutedEventArgs e)
        {
            // SessionGrid.SelectedItem = null; // Removed this line to prevent selection from clearing on mouse leave
        }

        private void CaptureColumnWidths()
        {
            if (_manager == null)
            {
                return;
            }

            var columns = SessionGrid.Columns;
            if (columns.Count == 0)
            {
                return;
            }

            _manager.ColumnWidths.Clear();
            for (var i = 0; i < columns.Count; i++)
            {
                _manager.ColumnWidths.Add(columns[i].ActualWidth);
            }
        }

        private void AttachColumnWidthObservers()
        {
            if (_columnObserversAttached)
            {
                return;
            }

            var columns = SessionGrid.Columns;
            if (columns.Count == 0)
            {
                return;
            }

            _columnObserversAttached = true;
            foreach (var column in columns)
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
                descriptor?.AddValueChanged(column, OnColumnWidthChanged);
            }
        }

        private void OnColumnWidthChanged(object? sender, EventArgs e)
        {
            CaptureColumnWidths();
        }

        private void ApplyColumnWidths()
        {
            if (_manager == null)
            {
                return;
            }

            var widths = _manager.ColumnWidths;
            if (widths.Count == 0 || SessionGrid.Columns.Count == 0)
            {
                return;
            }

            var count = Math.Min(widths.Count, SessionGrid.Columns.Count);
            for (var i = 0; i < count; i++)
            {
                var width = widths[i];
                if (width > 0)
                {
                    SessionGrid.Columns[i].Width = new DataGridLength(width);
                }
            }
        }

    }
}
