using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
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
        private BacktestRecorder? _backtestRecorder;
        private string? _backtestTempDbPath;
        private Action<ZenPlatform.LogText.LogEntry>? _backtestLogEntryAddedHandler;
        private bool _isBacktestLogCaptureAttached;
        private System.Windows.Threading.DispatcherTimer? _backtestConsoleFollowTimer;
        private INotifyCollectionChanged? _sessionsCollectionChangedSource;
        private ScrollViewer? _sessionGridScrollViewer;
        private const string BacktestTempFileName = "BackTestingTemp.btdb";
        private static readonly string BacktestSaveSettingsPath = Path.Combine(AppContext.BaseDirectory, "backtest_save_settings.json");
        private const string ViewerOpenDbPipeName = "BackTestReviewer.OpenDb";
        private const string ViewerPipeCmdActivate = "ACTIVATE";
        private const string ViewerPipeCmdOpenPrefix = "OPEN|";

        public SessionPageControl()
        {
            InitializeComponent();
#if !DEBUG
            RolloverNowButton.Visibility = Visibility.Collapsed;
#endif
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
            MouseLeave += OnMouseLeave;
        }
        private void OnStrategyToggleClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_core?.IsProgramStopped == true)
            {
                var ownerWhenStopped = Window.GetWindow(this);
                if (ownerWhenStopped != null)
                {
                    MessageBoxWindow.Show(ownerWhenStopped, "目前授權已停用，無法啟動策略。", "授權狀態");
                }
                return;
            }

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
                ReverseSessionMenuItem.Visibility = System.Windows.Visibility.Collapsed;
                EditStopLossBaselineMenuItem.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            var hasPosition = session.Position != 0;
            var canOperate = hasPosition && !session.IsFinished;
            var canEditStopLossBaseline = canOperate && page.Manager.RuleSet.StopLossMode == ZenPlatform.Strategy.StopLossMode.Auto;
            EditStopLossBaselineMenuItem.Visibility = canEditStopLossBaseline ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            EditStopLossBaselineMenuItem.IsEnabled = canEditStopLossBaseline;
            ReverseSessionMenuItem.Visibility = canOperate ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ReverseSessionMenuItem.IsEnabled = canOperate;
            CloseSessionMenuItem.Visibility = hasPosition ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            CloseSessionMenuItem.IsEnabled = hasPosition;
        }

        private void OnEditStopLossBaselineClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            if (SessionGrid.SelectedItem is not ZenPlatform.Strategy.Session session)
            {
                return;
            }

            if (session.IsFinished || session.Position == 0)
            {
                return;
            }

            if (page.Manager.RuleSet.StopLossMode != ZenPlatform.Strategy.StopLossMode.Auto)
            {
                var ownerForMode = Window.GetWindow(this);
                if (ownerForMode != null)
                {
                    MessageBoxWindow.Show(ownerForMode, "目前停損模式不是自動判定，無法調整停損基準線。", "停損基準線");
                }
                return;
            }

            var owner = Window.GetWindow(this);
            var currentPrice = page.Manager.CurPrice;
            var baseline = session.StopLossBaseline;
            var dialog = new EditStopLossBaselineWindow(session.Id, baseline, currentPrice)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() != true || dialog.NewBaseline == null)
            {
                return;
            }

            var newBaseline = dialog.NewBaseline.Value;
            var mayImmediateStop = false;
            if (currentPrice.HasValue && session.FloatProfit < 50m)
            {
                if (session.Position > 0)
                {
                    mayImmediateStop = currentPrice.Value <= newBaseline;
                }
                else if (session.Position < 0)
                {
                    mayImmediateStop = currentPrice.Value >= newBaseline;
                }
            }

            if (mayImmediateStop && owner != null)
            {
                var message = $"目前市價 {currentPrice:0.##}，調整後可能立刻觸發停損平倉。\n是否仍要套用新的停損基準線 {newBaseline:0.##}？";
                if (!ConfirmWindow.Show(owner, message, "停損基準線警告"))
                {
                    return;
                }
            }

            if (!session.TrySetStopLossBaseline(newBaseline, out var error))
            {
                if (owner != null)
                {
                    MessageBoxWindow.Show(owner, error ?? "停損基準線更新失敗。", "停損基準線");
                }
            }
        }

        private void OnReverseSessionClick(object sender, RoutedEventArgs e)
        {
            if (SessionGrid.SelectedItem is ZenPlatform.Strategy.Session session)
            {
                var owner = Window.GetWindow(this);
                if (owner != null && !ConfirmWindow.Show(owner, "確認要立即反手嗎？", "立即反手"))
                {
                    return;
                }

                session.ReverseNow();
            }
        }

        private void OnCloseSessionClick(object sender, RoutedEventArgs e)
        {
            if (SessionGrid.SelectedItem is ZenPlatform.Strategy.Session session)
            {
                session.CloseAll();
            }
        }

        private void OnCloseAllSessionsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            var owner = Window.GetWindow(this);
            if (owner != null && !ConfirmWindow.Show(owner, "確認要全部平倉嗎？", "全部平倉"))
            {
                return;
            }

            page.Manager.CloseAllSessionsByNetting();
        }

        private async void OnRolloverNowClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            var owner = Window.GetWindow(this);
            if (owner != null && !ConfirmWindow.Show(owner, "確認要立即執行換月嗎？", "立刻換月"))
            {
                return;
            }

            var ok = false;
            try
            {
                ok = await Task.Run(() => page.Manager.TriggerRolloverNow());
            }
            catch
            {
                ok = false;
            }

            if (!ok && owner != null)
            {
                MessageBoxWindow.Show(owner, "立刻換月未執行（可能無持倉、設定未啟用、已執行過或資料不足）。", "立刻換月");
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

            var reason = dialog.SelectedIsBuy.Value ? "使用者建立多單任務" : "使用者建立空單任務";
            page.Manager.Entry.RequestNewSession(dialog.SelectedIsBuy.Value, reason);
        }

        private void OnStrategySettingsClick(object sender, RoutedEventArgs e)
        {
            OpenStrategySettingsDialog();
        }

        private void OnTrendCardMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenStrategySettingsDialog();
            e.Handled = true;
        }

        private void OpenStrategySettingsDialog()
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            var owner = Window.GetWindow(this);
            var dialog = new StrategySettingsWindow(page.Manager.RuleSet, page.Manager.IsStrategyRunning)
            {
                Owner = owner
            };
            var accepted = dialog.ShowDialog() == true;
            if (accepted && page.Manager.IsStrategyRunning)
            {
                // Apply updated user-defined trend settings immediately while strategy is running.
                page.Manager.ResetCurrentTrendSide();
                page.Manager.Entry.ResetTrendState();
                page.Manager.RebuildIndicators(force: true);
                page.Manager.InitializeTrendSideFromLatestBar();
            }
            page.RefreshRuleSetStatus();
            SessionGrid.Items.Refresh();
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
            var dbFolder = TaifexHisDbManager.MagistockStoragePaths.MagistockLibPath;
            Directory.CreateDirectory(dbFolder);
            var dataSource = new HisDbDataSource(dbFolder);
            var tempDbPath = Path.Combine(AppContext.BaseDirectory, BacktestTempFileName);
            SafeDeleteFile(tempDbPath);
            _backtestTempDbPath = tempDbPath;
            _backtestRecorder = new BacktestRecorder(tempDbPath);
            _backtestRecorder.BeginRun(
                info.Start.Value,
                product,
                ToBacktestModeText(info.Precision),
                page.Manager.DisplayName,
                BuildBacktestParamsJson(page.Manager.RuleSet, info.PreloadDays, info.Start.Value, info.End.Value),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString());
            _backtestRecorder.AppendEvent(DateTime.Now, "Info", "BacktestStart", "回測開始", page.Manager.Index);
            AttachBacktestLogCapture(page.Manager, _backtestRecorder);

            _backtestEngine?.Dispose();
            _backtestEngine = new BacktestEngine(page.Manager);
            _backtestEngine.Recorder = _backtestRecorder;
            _backtestEngine.TickMinDiffToProcess = ToBacktestTickMinDiff(info.Precision);
            // Keep CurrentTime aligned by using bar close time for every completed bar.
            _backtestEngine.UseBarCloseTimeSignal = true;
            _backtestEngine.Initialize();

            _backtestCts = new CancellationTokenSource();
            _backtestRangeStart = info.Start;
            _backtestRangeEnd = info.End;
            page.Manager.BacktestRecorder = _backtestRecorder;
            page.Manager.StrategyStartTime = info.Start;
            if (!page.Manager.IsStrategyRunning)
            {
                page.Manager.ToggleStrategy();
            }
            else
            {
                // Backtest start should always reset trend state and indicator registration
                // even when strategy is already running.
                page.Manager.ResetCurrentTrendSide();
                page.Manager.Entry.ResetTrendState();
                page.Manager.RebuildIndicators(force: true);
            }
            page.Manager.Log.SetMaxLines(1000);
            page.Manager.Log.Clear();
            page.Manager.Log.UseSystemTime();
            page.Manager.Log.Add("==== 回測開始 ====");
            _core.LogCtrl.SetConsoleOnly(false);
            _core.LogCtrl.SetRenderSuspended(true);
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
            var recorder = _backtestRecorder;
            _backtestTask = Task.Run(() => RunBacktest(_backtestEngine, recorder, dataSource, product, info.Start.Value, info.End.Value, info.PreloadDays, _core, log, logWithTime, setStatus, setTotalCount, _backtestCts.Token));
            _backtestTask.ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(new Action(() => StopBacktest(false)), System.Windows.Threading.DispatcherPriority.Send);
            }, TaskScheduler.Default);
        }

        private void OnOpenBacktestViewerClick(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            try
            {
                if (TryNotifyRunningViewerActivate())
                {
                    return;
                }

                var viewerExe = ResolveBacktestViewerExePath();
                if (!File.Exists(viewerExe))
                {
                    if (owner != null)
                    {
                        MessageBox.Show(owner, $"找不到回測瀏覽器：\n{viewerExe}", "回測瀏覽器", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"找不到回測瀏覽器：\n{viewerExe}", "回測瀏覽器", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = viewerExe,
                    WorkingDirectory = Path.GetDirectoryName(viewerExe) ?? AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                if (owner != null)
                {
                    MessageBox.Show(owner, $"啟動回測瀏覽器失敗：{ex.Message}", "回測瀏覽器", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"啟動回測瀏覽器失敗：{ex.Message}", "回測瀏覽器", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void OnRealTradeChecked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SessionPageViewModel page)
            {
                var manager = page.Manager;
                manager.IsRealTrade = true;
            }
        }

        private void OnRealTradePreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is not SessionPageViewModel page)
            {
                return;
            }

            var owner = Window.GetWindow(this);
            if (owner?.DataContext is ZenPlatform.Core.Core core && !core.IsBrokerConnected)
            {
                MessageBoxWindow.ShowWithExtra(owner,
                    "期貨商連線尚未建立，無法進行真實交易",
                    "真實交易",
                    "確定",
                    "我要開戶",
                    () =>
                    {
                        var window = new OpenAccountWindow
                        {
                            Owner = owner,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };
                        window.ShowDialog();
                    });
                e.Handled = true;
                return;
            }

            if (owner?.DataContext is ZenPlatform.Core.Core authCore)
            {
                if (!authCore.ClientCtrl.IsRealUserLogin)
                {
                    MessageBoxWindow.Show(owner, "請先登入 Magistock 帳號後再切換真實交易。", "真實交易");
                    e.Handled = true;
                    return;
                }

                if (!TryHasEnoughRealTradePermission(authCore, page.Manager, out var error))
                {
                    MessageBoxWindow.Show(owner, error, "真實交易");
                    e.Handled = true;
                    return;
                }
            }
        }

        private static bool TryHasEnoughRealTradePermission(ZenPlatform.Core.Core core, SessionManager.SessionManager targetManager, out string message)
        {
            message = string.Empty;
            ZClient.PermissionData? permission = null;
            var permissions = core.ClientCtrl.LastUserData?.Permissions;
            if (permissions != null)
            {
                foreach (var item in permissions)
                {
                    if (string.Equals(item.ProgramName, core.ProgramName, StringComparison.OrdinalIgnoreCase))
                    {
                        permission = item;
                        break;
                    }
                }
            }

            if (permission == null)
            {
                message = "查無授權資料，請重新登入 Magistock 後再試。";
                return false;
            }

            if (permission.UnlimitedPermission)
            {
                return true;
            }

            var totalPermission = permission.PermissionCount;
            if (totalPermission <= 0)
            {
                message = "目前權限不足，無法切換至真實交易。";
                return false;
            }

            var weight = GetContractPermissionWeight(core.CurContract);
            var needed = targetManager.RuleSet.OrderSize * targetManager.RuleSet.MaxSessionCount * weight;
            var used = 0;

            foreach (var manager in core.SessionManagers)
            {
                if (ReferenceEquals(manager, targetManager))
                {
                    continue;
                }

                if (!manager.IsRealTrade)
                {
                    continue;
                }

                used += manager.RuleSet.OrderSize * manager.RuleSet.MaxSessionCount * weight;
            }

            var available = totalPermission - used;
            if (available >= needed)
            {
                return true;
            }

            message = $"權限不足：可用 {available}，需要 {needed}（口數*最大任務數*商品權重）。";
            return false;
        }

        private static int GetContractPermissionWeight(string? contract)
        {
            return contract switch
            {
                "大型台指" => 20,
                "小型台指" => 5,
                _ => 1
            };
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
            AttachSessionsCollectionChanged();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CaptureColumnWidths();
            StopBacktest();
            StopBacktestConsoleFollow();
            DetachSessionsCollectionChanged();
            if (DataContext is SessionPageViewModel page)
            {
                DetachBacktestLogCapture(page.Manager);
            }
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
                    DetachBacktestLogCapture(_backtestEngine.Manager);
                    _backtestEngine.Recorder = null;
                    _backtestEngine.Manager.BacktestRecorder = null;
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
                        _backtestEngine.Manager.Log.AddAt(DateTime.Now, "==== 回測結束 ====");
                        _backtestEngine.Manager.Log.AddAt(DateTime.Now, "回測");
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
                _core?.LogCtrl.SetConsoleOnly(false);
                _core?.LogCtrl.SetRenderSuspended(false);
                StopBacktestConsoleFollow();
                ConsoleLog.Close();
                _backtestEngine?.Dispose();
                FinalizeBacktestTempFile(wasCanceled);
                _backtestEngine = null;
                _backtestTask = null;
                _backtestStopwatch = null;
                _backtestRangeStart = null;
                _backtestRangeEnd = null;
            }
            catch (Exception)
            {
                throw;
            }
        }


        private static void RunBacktest(
            BacktestEngine engine,
            BacktestRecorder? recorder,
            HisDbDataSource dataSource,
            int product,
            DateTime start,
            DateTime end,
            int preloadDays,
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

            long totalMain = dataSource.CountTicks(product, start, end, null);
            long totalPreload = 0;
            if (preloadStart < start)
            {
                totalPreload = dataSource.CountTicks(product, preloadStart, start, null);
            }
            log($"回測資料筆數(tick): {totalMain:N0}，前置: {totalPreload:N0}");
            setTotalCount(totalMain);
            engine.SetTotalUnits(totalMain);

            if (totalMain == 0)
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
            }

            if (preloadStart < start)
            {
                var preloadEnd = start.AddSeconds(-1);
                if (preloadEnd >= preloadStart)
                {
                    RunRange(engine, recorder, dataSource, product, preloadStart, preloadEnd, core, log, logWithTime, setStatus, token, false);
                }
            }

            if (token.IsCancellationRequested)
            {
                engine.Manager.SuppressIndicatorLog = false;
                return;
            }

            engine.Manager.SuppressIndicatorLog = false;

            RunRange(engine, recorder, dataSource, product, start, end, core, log, logWithTime, setStatus, token, true);

            engine.CompleteProduction();
            engine.WaitForDrainAsync(token).GetAwaiter().GetResult();
        }

        private static void RunRange(
            BacktestEngine engine,
            BacktestRecorder? recorder,
            HisDbDataSource dataSource,
            int product,
            DateTime start,
            DateTime end,
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
                var volQuote = new QuoteUpdate(productName, QuoteField.Volume, year, month, volume.ToString(), false, time, QuoteSource.Backtest);
                engine.EnqueueTick(volQuote);
                var priceQuote = new QuoteUpdate(productName, QuoteField.Last, year, month, price.ToString(), false, time, QuoteSource.Backtest);
                engine.EnqueueTick(priceQuote);
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
            AttachSessionsCollectionChanged();
        }

        private void AttachSessionsCollectionChanged()
        {
            var source = (DataContext as SessionPageViewModel)?.Sessions as INotifyCollectionChanged;
            if (ReferenceEquals(_sessionsCollectionChangedSource, source))
            {
                return;
            }

            DetachSessionsCollectionChanged();
            if (source == null)
            {
                return;
            }

            _sessionsCollectionChangedSource = source;
            _sessionsCollectionChangedSource.CollectionChanged += OnSessionsCollectionChanged;
        }

        private void DetachSessionsCollectionChanged()
        {
            if (_sessionsCollectionChangedSource == null)
            {
                return;
            }

            _sessionsCollectionChangedSource.CollectionChanged -= OnSessionsCollectionChanged;
            _sessionsCollectionChangedSource = null;
        }

        private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null || e.NewItems.Count == 0)
            {
                return;
            }

            if (e.NewStartingIndex != 0)
            {
                return;
            }

            var insertedCount = e.NewItems.Count;
            Dispatcher.BeginInvoke(new Action(() => PreserveSessionGridOffsetOnTopInsert(insertedCount)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void PreserveSessionGridOffsetOnTopInsert(int insertedCount)
        {
            if (insertedCount <= 0)
            {
                return;
            }

            var scrollViewer = GetSessionGridScrollViewer();
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            // If user is already at top, keep the default behavior (show newest rows immediately).
            if (scrollViewer.VerticalOffset <= 0.5)
            {
                return;
            }

            if (ScrollViewer.GetCanContentScroll(SessionGrid))
            {
                var targetOffset = Math.Min(scrollViewer.VerticalOffset + insertedCount, scrollViewer.ScrollableHeight);
                scrollViewer.ScrollToVerticalOffset(targetOffset);
                return;
            }

            var rowHeight = SessionGrid.RowHeight > 0 ? SessionGrid.RowHeight : 24d;
            var pixelOffset = insertedCount * rowHeight;
            var targetPixelOffset = Math.Min(scrollViewer.VerticalOffset + pixelOffset, scrollViewer.ScrollableHeight);
            scrollViewer.ScrollToVerticalOffset(targetPixelOffset);
        }

        private ScrollViewer? GetSessionGridScrollViewer()
        {
            _sessionGridScrollViewer ??= FindVisualChild<ScrollViewer>(SessionGrid);
            return _sessionGridScrollViewer;
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                {
                    return typed;
                }

                var found = FindVisualChild<T>(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
            if (_core == null || _manager == null)
            {
                return;
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

        private void FinalizeBacktestTempFile(bool wasCanceled)
        {
            var recorder = _backtestRecorder;
            var tempPath = _backtestTempDbPath;
            _backtestRecorder = null;
            _backtestTempDbPath = null;
            var manager = _backtestEngine?.Manager ?? (DataContext as SessionPageViewModel)?.Manager;
            if (manager != null)
            {
                manager.BacktestRecorder = null;
            }

            if (recorder != null)
            {
                try
                {
                    FlushBacktestArtifacts(recorder);
                    var status = wasCanceled ? "Canceled" : "Completed";
                    recorder.EndRun(DateTime.Now, status, BuildBacktestSummaryJson(wasCanceled));
                }
                catch
                {
                    // Best-effort finalization.
                }
                finally
                {
                    recorder.Dispose();
                }
            }

            if (string.IsNullOrWhiteSpace(tempPath))
            {
                return;
            }

            if (wasCanceled)
            {
                SafeDeleteFile(tempPath);
                return;
            }

            PromptSaveBacktestDb(tempPath);
        }

        private void PromptSaveBacktestDb(string tempPath)
        {
            if (!File.Exists(tempPath))
            {
                return;
            }

            var owner = Window.GetWindow(this);
            var defaultName = $"Backtest_{DateTime.Now:yyyyMMdd_HHmmss}.btdb";
            var initialDir = LoadLastBacktestSaveFolder();
            var saveWindow = new BacktestSaveWindow(initialDir, defaultName)
            {
                Owner = owner,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
            };
            if (saveWindow.ShowDialog() != true)
            {
                SafeDeleteFile(tempPath);
                TryActivateOwnerWindow(owner);
                return;
            }

            var targetFolder = saveWindow.FolderPath;
            var targetPath = saveWindow.FullPath;
            try
            {
                Directory.CreateDirectory(targetFolder);
                File.Copy(tempPath, targetPath, overwrite: true);
                SaveLastBacktestSaveFolder(targetFolder);
                SafeDeleteFile(tempPath);
                LaunchBacktestViewer(targetPath, owner);
            }
            catch (Exception ex)
            {
                if (owner != null)
                {
                    MessageBox.Show(owner, $"儲存失敗：{ex.Message}\n目標檔：{targetPath}\n暫存檔仍保留於：{tempPath}", "回測結果", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TryActivateOwnerWindow(owner);
                }
                else
                {
                    MessageBox.Show($"儲存失敗：{ex.Message}\n目標檔：{targetPath}\n暫存檔仍保留於：{tempPath}", "回測結果", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void TryActivateOwnerWindow(Window? owner)
        {
            if (owner == null)
            {
                return;
            }

            try
            {
                if (owner.WindowState == WindowState.Minimized)
                {
                    owner.WindowState = WindowState.Normal;
                }

                owner.Activate();
                owner.Focus();
            }
            catch
            {
                // ignore focus restore exceptions
            }
        }

        private void LaunchBacktestViewer(string dbPath, Window? owner)
        {
            try
            {
                if (TryNotifyRunningViewerOpenDb(dbPath))
                {
                    return;
                }

                var viewerExe = ResolveBacktestViewerExePath();
                if (!File.Exists(viewerExe))
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = viewerExe,
                    Arguments = $"\"{dbPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(viewerExe) ?? AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                if (owner != null)
                {
                    MessageBox.Show(owner, $"回測檔已儲存，但啟動回測瀏覽器失敗：{ex.Message}", "回測結果", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"回測檔已儲存，但啟動回測瀏覽器失敗：{ex.Message}", "回測結果", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static bool TryNotifyRunningViewerOpenDb(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            {
                return false;
            }

            return TrySendViewerPipeMessage($"{ViewerPipeCmdOpenPrefix}{dbPath}");
        }

        private static bool TryNotifyRunningViewerActivate()
        {
            return TrySendViewerPipeMessage(ViewerPipeCmdActivate);
        }

        private static string ResolveBacktestViewerExePath()
        {
            var baseDir = AppContext.BaseDirectory;
            var parentInfo = Directory.GetParent(baseDir);
            var parentDir = parentInfo?.FullName;
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                // Primary layout: <InstallRoot>\TxNo2\TxNo2.exe + <InstallRoot>\BackTestReviewer\BackTestReviewer.exe
                var siblingPath = Path.Combine(parentDir, "BackTestReviewer", "BackTestReviewer.exe");
                if (File.Exists(siblingPath))
                {
                    return siblingPath;
                }

                var grandParentDir = parentInfo?.Parent?.FullName;
                if (!string.IsNullOrWhiteSpace(grandParentDir))
                {
                    var siblingFromGrandParentPath = Path.Combine(grandParentDir, "BackTestReviewer", "BackTestReviewer.exe");
                    if (File.Exists(siblingFromGrandParentPath))
                    {
                        return siblingFromGrandParentPath;
                    }
                }
            }

            // Local subfolder fallback.
            var localSubfolderPath = Path.Combine(baseDir, "BackTestReviewer", "BackTestReviewer.exe");
            if (File.Exists(localSubfolderPath))
            {
                return localSubfolderPath;
            }

            // Same-folder fallback.
            return Path.Combine(baseDir, "BackTestReviewer.exe");
        }

        private static bool TrySendViewerPipeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            try
            {
                using var client = new NamedPipeClientStream(".", ViewerOpenDbPipeName, PipeDirection.Out, PipeOptions.None);
                client.Connect(250);
                if (!client.IsConnected)
                {
                    return false;
                }

                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(message);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildBacktestParamsJson(ZenPlatform.Strategy.RuleSet ruleSet, int preloadDays, DateTime start, DateTime end)
        {
            var payload = new
            {
                ruleSet.OrderSize,
                ruleSet.KbarPeriod,
                ruleSet.TakeProfitPoints,
                TakeProfitMode = ToTakeProfitModeDisplayText(ruleSet.TakeProfitMode),
                ruleSet.AutoTakeProfitPoints,
                ruleSet.StopLossPoints,
                ruleSet.EnableAbsoluteStopLoss,
                ruleSet.AbsoluteStopLossPoints,
                ruleSet.LossRetraceExitEnabled,
                ruleSet.LossRetraceTriggerPoints,
                ruleSet.LossRetracePercent,
                StopLossMode = ToStopLossModeDisplayText(ruleSet.StopLossMode),
                TrendMode = ToTrendModeDisplayText(ruleSet.TrendMode),
                ruleSet.TrendMaPeriod,
                TrendForceSide = ToTrendForceSideDisplayText(ruleSet.TrendForceSide),
                ruleSet.SameDirectionBlockMinutes,
                ruleSet.SameDirectionBlockRange,
                ruleSet.DaySessionStart,
                ruleSet.DaySessionEnd,
                ruleSet.NightSessionStart,
                ruleSet.NightSessionEnd,
                ruleSet.MaxReverseCount,
                ruleSet.MaxSessionCount,
                ruleSet.ReverseAfterStopLoss,
                ruleSet.CoverLossBeforeTakeProfit,
                ruleSet.CoverLossTriggerPoints,
                ruleSet.ExitOnTotalProfitRise,
                ruleSet.ExitOnTotalProfitRiseArmBelowPoints,
                ruleSet.ExitOnTotalProfitRisePoints,
                ruleSet.ExitOnTotalProfitDropAfterTrigger,
                ruleSet.ExitOnTotalProfitDropTriggerPoints,
                ruleSet.ExitOnTotalProfitDropExitPoints,
                ruleSet.ProfitRetraceExitEnabled,
                ruleSet.ProfitRetraceTriggerPoints,
                ruleSet.ProfitRetracePercent,
                ruleSet.AutoRolloverWhenHolding,
                ruleSet.AutoRolloverTime,
                ruleSet.CloseBeforeDaySessionEnd,
                ruleSet.CloseBeforeNightSessionEnd,
                ruleSet.DayCloseBeforeTime,
                ruleSet.NightCloseBeforeTime,
                ruleSet.CloseBeforeLongHoliday,
                ruleSet.CloseBeforeLongHolidayTime,
                PreloadDays = preloadDays,
                Start = start,
                End = end
            };
            return JsonSerializer.Serialize(payload);
        }

        private static string ToBacktestModeText(TaifexHisDbManager.BacktestPrecision precision)
        {
            return precision switch
            {
                TaifexHisDbManager.BacktestPrecision.Fast => "Fast",
                TaifexHisDbManager.BacktestPrecision.UltraFast => "UltraFast",
                _ => "Exact"
            };
        }

        private static decimal ToBacktestTickMinDiff(TaifexHisDbManager.BacktestPrecision precision)
        {
            return precision switch
            {
                TaifexHisDbManager.BacktestPrecision.Fast => 3m,
                TaifexHisDbManager.BacktestPrecision.UltraFast => 6m,
                _ => 1m
            };
        }

        private static string ToTakeProfitModeDisplayText(ZenPlatform.Strategy.TakeProfitMode mode)
        {
            return mode switch
            {
                ZenPlatform.Strategy.TakeProfitMode.AutoAfterN => "固定浮動損益停利",
                _ => "固定停利點數"
            };
        }

        private static string ToTrendModeDisplayText(ZenPlatform.Strategy.TrendMode mode)
        {
            return mode switch
            {
                ZenPlatform.Strategy.TrendMode.Auto => "系統自動判定",
                ZenPlatform.Strategy.TrendMode.None => "無方向判定",
                ZenPlatform.Strategy.TrendMode.MovingAverage => "均線判定",
                ZenPlatform.Strategy.TrendMode.Force => "強制判定",
                _ => mode.ToString()
            };
        }

        private static string ToStopLossModeDisplayText(ZenPlatform.Strategy.StopLossMode mode)
        {
            return mode switch
            {
                ZenPlatform.Strategy.StopLossMode.Auto => "自動判定",
                _ => "固定停損點數"
            };
        }

        private static string ToTrendForceSideDisplayText(ZenPlatform.Strategy.TrendForceSide side)
        {
            return side switch
            {
                ZenPlatform.Strategy.TrendForceSide.多 => "強制多方",
                ZenPlatform.Strategy.TrendForceSide.空 => "強制空方",
                _ => "無"
            };
        }

        private string BuildBacktestSummaryJson(bool wasCanceled)
        {
            var elapsed = _backtestStopwatch?.Elapsed ?? TimeSpan.Zero;
            var manager = _backtestEngine?.Manager ?? (DataContext as SessionPageViewModel)?.Manager;
            decimal totalFloat = 0;
            decimal totalRealized = 0;
            int totalTradeCount = 0;
            int activeSessions = 0;

            if (manager != null)
            {
                foreach (var session in manager.Sessions)
                {
                    totalFloat += session.FloatProfit;
                    totalRealized += session.RealizedProfit;
                    totalTradeCount += session.TradeCount;
                    if (!session.IsFinished)
                    {
                        activeSessions++;
                    }
                }
            }

            var summary = new
            {
                WasCanceled = wasCanceled,
                Mode = "Exact",
                RangeStart = _backtestRangeStart,
                RangeEnd = _backtestRangeEnd,
                ElapsedSeconds = elapsed.TotalSeconds,
                TotalCount = _backtestTotalCount,
                ActiveSessions = activeSessions,
                TotalFloatProfit = totalFloat,
                TotalRealizedProfit = totalRealized,
                TotalTradeCount = totalTradeCount,
                TotalProfit = totalFloat + totalRealized
            };
            return JsonSerializer.Serialize(summary);
        }

        private void FlushBacktestArtifacts(BacktestRecorder recorder)
        {
            var manager = _backtestEngine?.Manager ?? (DataContext as SessionPageViewModel)?.Manager;
            if (manager == null)
            {
                return;
            }

            foreach (var session in manager.Sessions)
            {
                recorder.AppendSession(
                    session.Id,
                    session.StartTime,
                    session.IsFinished ? manager.CurrentTime : null,
                    session.StartPosition,
                    session.IsFinished,
                    session.RealizedProfit,
                    session.TradeCount,
                    session.MaxTotalLoss);
            }
        }

        private void AttachBacktestLogCapture(SessionManager.SessionManager manager, BacktestRecorder recorder)
        {
            DetachBacktestLogCapture(manager);

            _backtestLogEntryAddedHandler = entry =>
            {
                var (message, sessionId) = ClassifyLogEntryWithoutValidation(entry.Text);
                recorder.AppendLog(entry.Time, message, sessionId);
            };

            manager.Log.EntryAdded += _backtestLogEntryAddedHandler;
            _isBacktestLogCaptureAttached = true;
        }

        private void DetachBacktestLogCapture(SessionManager.SessionManager manager)
        {
            if (!_isBacktestLogCaptureAttached || _backtestLogEntryAddedHandler == null)
            {
                return;
            }

            manager.Log.EntryAdded -= _backtestLogEntryAddedHandler;
            _backtestLogEntryAddedHandler = null;
            _isBacktestLogCaptureAttached = false;
        }

        private static (string Message, int? SessionId) ClassifyLogEntryWithoutValidation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return (string.Empty, null);
            }

            if (text.Length >= 3 && text[0] == '[')
            {
                var rightBracket = text.IndexOf(']');
                if (rightBracket > 1)
                {
                    var idText = text.Substring(1, rightBracket - 1);
                    if (int.TryParse(idText, out var id))
                    {
                        var message = text.Substring(rightBracket + 1).TrimStart();
                        return (message, id);
                    }
                }
            }

            return (text, null);
        }

        private static void SafeDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private void StartBacktestConsoleFollow()
        {
            StopBacktestConsoleFollow();

            _backtestConsoleFollowTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _backtestConsoleFollowTimer.Tick += OnBacktestConsoleFollowTick;
            _backtestConsoleFollowTimer.Start();
        }

        private void StopBacktestConsoleFollow()
        {
            if (_backtestConsoleFollowTimer == null)
            {
                return;
            }

            _backtestConsoleFollowTimer.Stop();
            _backtestConsoleFollowTimer.Tick -= OnBacktestConsoleFollowTick;
            _backtestConsoleFollowTimer = null;
        }

        private void OnBacktestConsoleFollowTick(object? sender, EventArgs e)
        {
            if (_core == null)
            {
                return;
            }

            if (_core.LogCtrl.TryGetTargetScreenRect(out var rect))
            {
                ConsoleLog.UpdateBounds(rect);
            }
        }

        private static string LoadLastBacktestSaveFolder()
        {
            var fallback = TaifexHisDbManager.MagistockStoragePaths.BacktestReportFolder;
            try
            {
                Directory.CreateDirectory(fallback);
                if (!File.Exists(BacktestSaveSettingsPath))
                {
                    return fallback;
                }

                var json = File.ReadAllText(BacktestSaveSettingsPath);
                var settings = JsonSerializer.Deserialize<BacktestSaveSettings>(json);
                if (!string.IsNullOrWhiteSpace(settings?.LastSaveFolder) && Directory.Exists(settings.LastSaveFolder))
                {
                    return settings.LastSaveFolder;
                }
            }
            catch
            {
                // Ignore broken settings; fallback to default folder.
            }

            return fallback;
        }

        private static void SaveLastBacktestSaveFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                var settings = new BacktestSaveSettings
                {
                    LastSaveFolder = folder
                };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(BacktestSaveSettingsPath, json);
            }
            catch
            {
                // Best effort only.
            }
        }

        private sealed class BacktestSaveSettings
        {
            public string? LastSaveFolder { get; set; }
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
