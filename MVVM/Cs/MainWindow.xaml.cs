using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.Json;
using Charts;
using KChartCore;
using Utility;
using ZenPlatform.Core;
using ZenPlatform.Core.AutoUpdate;
using ZenPlatform.DdePrice;
using ZenPlatform.MVVM.Cs;

namespace ZenPlatform
{
	/// <summary>
	/// MainWindow.xaml 的互動邏輯
	/// </summary>
		public partial class MainWindow : Window
		{
			private readonly ZenPlatform.Core.Core _core = new ZenPlatform.Core.Core();
		private bool _didRender;
		private bool _startupCompleted;
		private bool _duplicateCheckInFlight;
		private bool _duplicateCheckCompleted;
		private bool _forceClose;
		private int _viewPeriod = 5;
		private DateTime? _lastClockStamp;
		private string _lastChartContractKey = string.Empty;
		private ZenPlatform.SessionManager.SessionManager? _selectedManager;
		private TextBlock? _topBidText;
		private TextBlock? _topAskText;
		private TextBlock? _topLastText;
		private TextBlock? _topVolText;
		private TextBlock? _topChgText;
		private TimeframeBar? _timeframeBar;
		private readonly ChartQuoteViewModel _chartQuoteVm = new();
		private ToolTip? _brokerStatusToolTip;
		private ToolTip? _userStatusToolTip;
		private int? _pendingSessionIndex;
			private const string LayoutConfigFileName = "user.config";
			private const int AutoSaveIntervalMinutes = 10;
			private const double TaskSplitterRowHeight = 5d;
			private readonly DispatcherTimer _autoSaveTimer;
			private readonly DispatcherTimer _autoUpdateTimer;
		private readonly AutoUpdateService _autoUpdateService;
		private bool _installerStartedOnClose;
		private ZenPlatform.SessionManager.SessionManager? _editingLogHeaderManager;

			public MainWindow()
			{
				InitializeComponent();
				SizeChanged += (_, __) => ClampTaskListRowMaxHeight();
				LeftPaneGrid.SizeChanged += (_, __) => ClampTaskListRowMaxHeight();
				DataContext = _core;
			_core.ClientCtrl.TargetUrl = UserInfoCtrl.DefaultTargetUrl;
			_autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes)
			};
			_autoSaveTimer.Tick += OnAutoSaveTimerTick;
			_autoUpdateService = new AutoUpdateService(_core.BuildSerial);
			_autoUpdateService.StateChanged += OnAutoUpdateStateChanged;
			_autoUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromMinutes(10)
			};
			_autoUpdateTimer.Tick += async (_, _) => await CheckUpdateInBackgroundAsync();
			ApplyAppState(AppStateStore.Load());
			_core.TimeCtrl.TimeTick += OnTimeTick;
			_core.ChartBridge.PriceUpdated += OnChartPriceUpdated;
			_core.ChartBridge.TickUpdated += OnChartTickUpdated;
			_core.ChartBridge.KBarCompleted += OnKBarCompleted;
			_core.HistoryLoaded += OnHistoryLoaded;
			_core.ClientCtrl.PurchaseReminderNeeded += OnPurchaseReminderNeeded;
			_core.ClientCtrl.UserAccountInfoFetched += OnUserAccountInfoFetched;
			_core.ChartBridge.RegisterPeriod(_viewPeriod);
			UpdateChartContract();
			BuildChartTopRightPanel();
			MainChartView.OnContractChgReq += OnChartContractChangeRequested;
			SourceInitialized += MainWindow_SourceInitialized;
			Loaded += MainWindow_Loaded;
			ContentRendered += MainWindow_ContentRendered;
			Closing += MainWindow_Closing;
		}


		private void MainWindow_SourceInitialized(object? sender, EventArgs e)
		{
			if (TryLoadLayoutConfig(out var cfg))
			{
				ApplyLayoutConfig(cfg);
			}
		}

			private void MainWindow_Loaded(object sender, RoutedEventArgs e)
			{
				_core.LogCtrl.SetTarget(LogOutputBox);
				_core.LogCtrl.SetModel(_core.MainLog);
				RefreshUpdateBadge();
				ClampTaskListRowMaxHeight();
				if (!_autoSaveTimer.IsEnabled)
				{
					_autoSaveTimer.Start();
			}
			if (!_autoUpdateTimer.IsEnabled)
			{
				_autoUpdateTimer.Start();
			}
		}

		private void MainWindow_ContentRendered(object? sender, EventArgs e)
		{
			if (_didRender)
			{
				return;
			}

			_didRender = true;
			if (_core.SessionPages.Length > 0)
			{
				SessionTabs.SelectedIndex = ResolveSessionIndex();
				var page = _core.SessionPages[0];
				if (SessionTabs.SelectedItem is ZenPlatform.MVVM.RulePage.SessionPageViewModel selectedPage)
				{
					page = selectedPage;
				}
				_core.LogCtrl.SetModel(page.Log);
				UpdateLogHeaderText(page.Manager);
				AttachSelectedManager(page.Manager);
			}
			_ = Dispatcher.InvokeAsync(async () =>
			{
				await _core.StartupAsync();
				_startupCompleted = true;
				await TryCheckDuplicateLoginAsync();
				await CheckUpdateInBackgroundAsync();
			}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}

		private void OnSessionTabChanged(object sender, SelectionChangedEventArgs e)
		{
			if (SessionTabs.SelectedItem is ZenPlatform.MVVM.RulePage.SessionPageViewModel page)
			{
				_core.LogCtrl.SetModel(page.Log);
				UpdateLogHeaderText(page.Manager);
				AttachSelectedManager(page.Manager);
			}
		}

		private void OnLogHeaderClick(object sender, MouseButtonEventArgs e)
		{
			if (_selectedManager == null)
			{
				return;
			}

			_editingLogHeaderManager = _selectedManager;
			LogHeaderEditBox.Text = _selectedManager.DisplayName;
			LogHeaderText.Visibility = Visibility.Collapsed;
			LogHeaderEditBox.Visibility = Visibility.Visible;
			LogHeaderEditBox.Focus();
			LogHeaderEditBox.SelectAll();
		}


		private void OnLogHeaderEditLostFocus(object sender, RoutedEventArgs e)
		{
			CommitLogHeaderEdit();
		}

		private void OnLogHeaderEditKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter || e.Key == Key.Escape)
			{
				CommitLogHeaderEdit();
				e.Handled = true;
			}
		}

		private void CommitLogHeaderEdit()
		{
			if (LogHeaderEditBox.Visibility != Visibility.Visible)
			{
				return;
			}

			var manager = _editingLogHeaderManager ?? _selectedManager;
			if (manager == null)
			{
				return;
			}

			var input = LogHeaderEditBox.Text?.Trim() ?? string.Empty;
			var defaultName = $"策略{manager.Index + 1}";
			manager.Name = string.Equals(input, defaultName, StringComparison.Ordinal) ? string.Empty : input;

			if (_selectedManager != null)
			{
				UpdateLogHeaderText(_selectedManager);
			}

			_editingLogHeaderManager = null;
			LogHeaderEditBox.Visibility = Visibility.Collapsed;
			LogHeaderText.Visibility = Visibility.Visible;
		}

		private void AttachSelectedManager(ZenPlatform.SessionManager.SessionManager manager)
		{
			if (_selectedManager != null)
			{
				_selectedManager.PropertyChanged -= OnSelectedManagerPropertyChanged;
			}

			_selectedManager = manager;
			_selectedManager.PropertyChanged += OnSelectedManagerPropertyChanged;
			UpdateLogHeaderStatus(_selectedManager);
			UpdateLogBorder(_selectedManager);
			UpdateLogHeaderText(_selectedManager);
		}

		private void OnSelectedManagerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (sender is ZenPlatform.SessionManager.SessionManager manager &&
				(e.PropertyName == nameof(ZenPlatform.SessionManager.SessionManager.IsStrategyRunning) ||
				 e.PropertyName == nameof(ZenPlatform.SessionManager.SessionManager.IsRealTrade) ||
				 e.PropertyName == nameof(ZenPlatform.SessionManager.SessionManager.IsBacktestActive) ||
				 e.PropertyName == nameof(ZenPlatform.SessionManager.SessionManager.BacktestStatusText) ||
				 e.PropertyName == nameof(ZenPlatform.SessionManager.SessionManager.BacktestProgressPercent)))
			{
				UpdateLogHeaderStatus(manager);
				UpdateLogBorder(manager);
				UpdateLogHeaderText(manager);
			}
		}

		private void UpdateLogHeaderStatus(ZenPlatform.SessionManager.SessionManager manager)
		{
			if (manager.IsBacktestActive)
			{
				策略名稱區.Background = (System.Windows.Media.Brush)FindResource("BrushTabBacktest");
				return;
			}

			if (manager.IsStrategyRunning)
			{
				策略名稱區.Background = manager.IsRealTrade
					? (System.Windows.Media.Brush)FindResource("BrushMarketClosed")
					: new SolidColorBrush(Color.FromRgb(0, 100, 0));
				return;
			}

			策略名稱區.Background = Brushes.Transparent;
		}

		private void UpdateLogBorder(ZenPlatform.SessionManager.SessionManager manager)
		{
			if (LogOutputBox == null)
			{
				return;
			}

			var brushKey = "BrushBorder";
			if (manager.IsBacktestActive)
			{
				brushKey = "BrushTabBacktest";
			}
			else if (manager.IsStrategyRunning)
			{
				if (manager.IsRealTrade)
				{
					brushKey = "BrushTabRealTrade";
				}
				else
				{
					LogOutputBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 0));
					LogOutputBox.BorderThickness = new Thickness(1);
					return;
				}
			}

			LogOutputBox.BorderBrush = (System.Windows.Media.Brush)FindResource(brushKey);
			LogOutputBox.BorderThickness = new Thickness(1);
		}

		private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
		{
			if (!_forceClose && !ConfirmWindow.Show(this, "確認要關閉程式嗎？", "結束系統"))
			{
				e.Cancel = true;
				return;
			}

			if (!_forceClose && _autoUpdateService.HasPendingUpdate)
			{
				MessageBoxWindow.Show(this, "關閉後即將啟動新版安裝程序", "有可用更新");
			}

			_autoSaveTimer.Stop();
			_autoUpdateTimer.Stop();
			foreach (var manager in _core.SessionManagers)
			{
				if (manager.IsStrategyRunning)
				{
					manager.Log.Add("程式關閉");
				}
			}
			PersistRuntimeSnapshot();
			_core.TimeCtrl.TimeTick -= OnTimeTick;
			_core.ChartBridge.PriceUpdated -= OnChartPriceUpdated;
			_core.ChartBridge.TickUpdated -= OnChartTickUpdated;
			_core.ChartBridge.KBarCompleted -= OnKBarCompleted;
			_core.HistoryLoaded -= OnHistoryLoaded;
			_core.ClientCtrl.PurchaseReminderNeeded -= OnPurchaseReminderNeeded;
			_core.ClientCtrl.UserAccountInfoFetched -= OnUserAccountInfoFetched;
			try
			{
				MainChartView.SaveConfig();
			}
			catch
			{
				// Ignore chart config persistence failures on shutdown.
			}
			try
			{
				_core.ClientCtrl.Disconnect().GetAwaiter().GetResult();
			}
			catch
			{
				// Ignore disconnect failures on shutdown.
			}
			try
			{
				if (!_installerStartedOnClose)
				{
					_installerStartedOnClose = _autoUpdateService.TryLaunchPendingInstaller();
				}
			}
			catch
			{
				// Ignore update launch failures on shutdown.
			}
			_autoUpdateService.StateChanged -= OnAutoUpdateStateChanged;
			_autoUpdateService.Dispose();
		}

		private void OnAutoSaveTimerTick(object? sender, EventArgs e)
		{
			PersistRuntimeSnapshot();
		}

		private async Task CheckUpdateInBackgroundAsync()
		{
			try
			{
				await _autoUpdateService.CheckAndDownloadAsync();
				RefreshUpdateBadge();
			}
			catch
			{
				// ignore background update failures
			}
		}

		private void OnAutoUpdateStateChanged()
		{
			Dispatcher.Invoke(RefreshUpdateBadge);
		}

		private void RefreshUpdateBadge()
		{
			var hasUpdate = _autoUpdateService.HasPendingUpdate;
			UpdateStatusText.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
			UpdateStatusDivider.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
		}

		private void PersistRuntimeSnapshot()
		{
			try
			{
				ZenPlatform.SessionManager.SessionStateStore.SaveAll(_core.SessionManagers);
				SaveAppState();

				var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

				try
				{
					SaveLayoutConfig(bounds);
				}
				catch
				{
					// Ignore layout persistence failures.
				}

			}
			catch
			{
				// Ignore periodic persistence failures.
			}
		}

		private sealed class LayoutConfig
		{
			public double WindowLeft { get; set; }
			public double WindowTop { get; set; }
			public double WindowWidth { get; set; }
			public double WindowHeight { get; set; }
			public int WindowState { get; set; }
			public double RightPanelWidth { get; set; }
			public double TaskListHeight { get; set; }
		}

		private static string GetLayoutConfigPath()
			=> System.IO.Path.Combine(AppContext.BaseDirectory, LayoutConfigFileName);

		private bool TryLoadLayoutConfig(out LayoutConfig cfg)
		{
			cfg = new LayoutConfig();
			try
			{
				var path = GetLayoutConfigPath();
				if (!File.Exists(path)) return false;
				var json = File.ReadAllText(path);
				var loaded = JsonSerializer.Deserialize<LayoutConfig>(json);
				if (loaded == null) return false;
				cfg = loaded;
				return true;
			}
			catch
			{
				return false;
			}
		}

			private void ApplyLayoutConfig(LayoutConfig cfg)
			{
			if (cfg.WindowWidth > 0 && cfg.WindowHeight > 0)
			{
				Width = cfg.WindowWidth;
				Height = cfg.WindowHeight;
			}

			if (!double.IsNaN(cfg.WindowLeft) && !double.IsNaN(cfg.WindowTop))
			{
				Left = cfg.WindowLeft;
				Top = cfg.WindowTop;
			}

			if (cfg.RightPanelWidth > 0)
			{
				RightPanelColumn.Width = new GridLength(cfg.RightPanelWidth);
			}

				if (cfg.TaskListHeight > 0)
				{
					TaskListRow.Height = new GridLength(cfg.TaskListHeight);
				}

				ClampTaskListRowMaxHeight();

				var savedState = (WindowState)cfg.WindowState;
				WindowState = savedState == WindowState.Minimized ? WindowState.Normal : savedState;
			}

			private void ClampTaskListRowMaxHeight()
			{
				var available = LeftPaneGrid.ActualHeight;
				if (available <= 0)
				{
					return;
				}

				var maxAllowed = available - ChartRow.MinHeight - TaskSplitterRowHeight;
				if (double.IsNaN(maxAllowed) || double.IsInfinity(maxAllowed))
				{
					return;
				}

				var clampedMax = Math.Max(TaskListRow.MinHeight, maxAllowed);
				TaskListRow.MaxHeight = clampedMax;

				if (!TaskListRow.Height.IsAbsolute)
				{
					return;
				}

				var current = TaskListRow.Height.Value;
				if (current > clampedMax)
				{
					TaskListRow.Height = new GridLength(clampedMax);
				}
			}

		private void SaveLayoutConfig(Rect bounds)
		{
			var cfg = new LayoutConfig
			{
				WindowLeft = bounds.Left,
				WindowTop = bounds.Top,
				WindowWidth = bounds.Width,
				WindowHeight = bounds.Height,
				WindowState = (int)WindowState,
				RightPanelWidth = RightPanelColumn.ActualWidth,
				TaskListHeight = TaskListRow.ActualHeight
			};
			var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(GetLayoutConfigPath(), json);
		}


		private int ResolveSessionIndex()
		{
			if (_pendingSessionIndex.HasValue &&
				_pendingSessionIndex.Value >= 0 &&
				_pendingSessionIndex.Value < SessionTabs.Items.Count)
			{
				return _pendingSessionIndex.Value;
			}

			return 0;
		}

		private void ApplyAppState(AppState? state)
		{
			if (state == null)
			{
				return;
			}

			if (!string.IsNullOrWhiteSpace(state.TargetUrl))
			{
				// 連線目標由 UserInfoCtrl.DefaultTargetUrl 統一管理，不使用 app_state 覆寫。
			}

			if (!string.IsNullOrWhiteSpace(state.CurContract) &&
				state.ContractYear > 0 &&
				state.ContractMonth > 0)
			{
				_core.CurContract = state.CurContract;
				_core.ContractYear = state.ContractYear;
				_core.ContractMonth = state.ContractMonth;
			}

			if (state.ViewPeriod > 0 && state.ViewPeriod <= 120)
			{
				_viewPeriod = state.ViewPeriod;
			}

			if (state.PriceMode.HasValue &&
				Enum.IsDefined(typeof(PriceManager.PriceMode), state.PriceMode.Value))
			{
				_core.PriceManager.SetMode((PriceManager.PriceMode)state.PriceMode.Value);
			}

			_pendingSessionIndex = state.SelectedSessionIndex;
		}

		private void SaveAppState()
		{
			var state = new AppState
			{
				CurContract = _core.CurContract,
				ContractYear = _core.ContractYear,
				ContractMonth = _core.ContractMonth,
				ViewPeriod = _viewPeriod,
				SelectedSessionIndex = SessionTabs.SelectedIndex,
				PriceMode = (int)_core.PriceManager.Mode
			};

			AppStateStore.Save(state);
		}

		private void OnUserAccountInfoFetched(UserAccountInfo info)
		{
			_ = TryCheckDuplicateLoginAsync();
		}

		private async Task TryCheckDuplicateLoginAsync()
		{
			if (_duplicateCheckCompleted || _duplicateCheckInFlight)
			{
				return;
			}

			if (!_didRender || !_startupCompleted)
			{
				return;
			}

			if (_core.ClientCtrl.IsGuest || !_core.ClientCtrl.IsLoggedIn)
			{
				return;
			}

			var data = _core.ClientCtrl.LastUserData;
			if (data?.Permissions == null || data.Permissions.Count == 0)
			{
				return;
			}

			var permission = data.Permissions.FirstOrDefault(p =>
				string.Equals(p.ProgramName, _core.ProgramName, StringComparison.OrdinalIgnoreCase));
			if (permission == null || string.IsNullOrWhiteSpace(permission.Account))
			{
				return;
			}

			if (permission.AllowConcurrent)
			{
				_duplicateCheckCompleted = true;
				return;
			}

			_duplicateCheckInFlight = true;
			try
			{
				var duplicated = await _core.ClientCtrl.CheckDuplicateConnection(permission.Account, permission.ProgramName);
				if (duplicated)
				{
					await Dispatcher.InvokeAsync(() =>
					{
						MessageBoxWindow.Show(this, $"您的{permission.ProgramName}已在其他地方開啟", "重複登入", "關閉程式");
						_forceClose = true;
						Close();
					});
				}
				_duplicateCheckCompleted = true;
			}
			finally
			{
				_duplicateCheckInFlight = false;
			}
		}

		private void OnChartPriceUpdated(PriceType type, string value)
		{
			Dispatcher.BeginInvoke(() => MainChartView.SetPrice(type, value));
			Dispatcher.BeginInvoke(() =>
			{
				var display = NormalizeTopValue(value);

				switch (type)
				{
					case PriceType.買價:
						_chartQuoteVm.BidText = $"買進:{display}";
						_chartQuoteVm.BidBrush = Brushes.White;
						break;
					case PriceType.賣價:
						_chartQuoteVm.AskText = $"賣出:{display}";
						_chartQuoteVm.AskBrush = Brushes.White;
						break;
					case PriceType.成交價:
						_chartQuoteVm.LastText = $"成交:{display}";
						_chartQuoteVm.LastBrush = Brushes.Yellow;
						break;
					case PriceType.成交量:
						_chartQuoteVm.VolumeText = $"量:{display}";
						_chartQuoteVm.VolumeBrush = Brushes.White;
						break;
					case PriceType.漲跌:
						if (decimal.TryParse(display, out var change))
						{
							if (change > 0)
							{
								_chartQuoteVm.ChangeText = $"漲跌:▲ {Math.Abs(change):0}";
								_chartQuoteVm.ChangeBrush = Brushes.Red;
							}
							else if (change < 0)
							{
								_chartQuoteVm.ChangeText = $"漲跌:▼ {Math.Abs(change):0}";
								_chartQuoteVm.ChangeBrush = Brushes.LimeGreen;
							}
							else
							{
								_chartQuoteVm.ChangeText = $"漲跌:{display}";
								_chartQuoteVm.ChangeBrush = Brushes.White;
							}
						}
						else
						{
							_chartQuoteVm.ChangeText = $"漲跌:{display}";
							_chartQuoteVm.ChangeBrush = Brushes.White;
						}
						break;
				}
			});
		}

		private void OnChartTickUpdated(decimal price, int volume)
		{
			Dispatcher.BeginInvoke(() => MainChartView.AddTick(price, volume));
		}

		private void OnKBarCompleted(int period, FunctionKBar bar)
		{
			if (period != _viewPeriod)
			{
				return;
			}

			Dispatcher.BeginInvoke(() => MainChartView.AddBar(bar));
		}

		private void OnHistoryLoaded(List<FunctionKBar> bars)
		{
			var history = _viewPeriod == 1 ? bars : _core.ChartBridge.GetHistoryList(_viewPeriod);
			Dispatcher.BeginInvoke(() => MainChartView.AddBarList(history));
			if (_selectedManager != null)
			{
				Dispatcher.BeginInvoke(() => UpdateLogHeaderText(_selectedManager));
			}
		}

		private void UpdateLogHeaderText(ZenPlatform.SessionManager.SessionManager manager)
		{
			LogHeaderText.Text = manager.DisplayName;
			var showPending = manager.IsStrategyRunning && manager.IsHistoryReady != null && !manager.IsHistoryReady();
			LogHeaderSubText.Text = showPending ? "(計算中...)" : "";

			if (manager.IsBacktestActive)
			{
				var status = string.IsNullOrWhiteSpace(manager.BacktestStatusText)
					? "回測中"
					: manager.BacktestStatusText;
				LogHeaderStatusText.Text = $"{status} ({manager.BacktestProgressPercent:0}%)";
				LogHeaderStatusText.Visibility = Visibility.Visible;
				LogHeaderStatusBorder.Visibility = Visibility.Visible;
				LogHeaderStatusRow.Height = new GridLength(22);
				LogHeaderProgressBar.Value = manager.BacktestProgressPercent;
			}
			else
			{
				LogHeaderStatusText.Text = string.Empty;
				LogHeaderStatusText.Visibility = Visibility.Collapsed;
				LogHeaderStatusBorder.Visibility = Visibility.Collapsed;
				LogHeaderStatusRow.Height = new GridLength(0);
				LogHeaderProgressBar.Value = 0;
			}
		}

		private void OnTimeTick(object? sender, DateTime time)
		{
			Dispatcher.Invoke(() =>
			{
				UpdateStatusBar(time);
			});
		}

		private void UpdateStatusBar(DateTime time)
		{
			UpdateMagistockStatus();
			UpdateBrokerStatus();
			UpdateUserStatus();
			UpdatePriceSourceStatus();
			RefreshChartContractIfChanged();
			var exchangeTime = _core.TimeCtrl.ExchangeTime;
			var stamp = new DateTime(exchangeTime.Year, exchangeTime.Month, exchangeTime.Day, exchangeTime.Hour, exchangeTime.Minute, exchangeTime.Second, exchangeTime.Kind);
			if (_lastClockStamp == null || _lastClockStamp.Value != stamp)
			{
				_lastClockStamp = stamp;
				var timeText = exchangeTime.ToString("ddd HH:mm:ss");
				ClockText.Text = timeText;
			}
		}

		private void RefreshChartContractIfChanged()
		{
			var key = $"{_core.CurContract}|{_core.ContractYear:D4}|{_core.ContractMonth:D2}";
			if (string.Equals(_lastChartContractKey, key, StringComparison.Ordinal))
			{
				return;
			}

			_lastChartContractKey = key;
			UpdateChartContract();
		}

		private void UpdateChartContract()
		{
			var contract = new Contracts
			{
				Name = _core.CurContract,
				Year = _core.ContractYear,
				Month = _core.ContractMonth
			};
			MainChartView.SetContract(contract);
		}

		private void OnChartContractChangeRequested()
		{
			if (_core.SessionManagers.Any(m => m.IsStrategyRunning))
			{
				MessageBoxWindow.Show(this, "策略執行中不可切換交易合約。", "交易合約");
				return;
			}

			var dialog = new DdeSubscribeWindow
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};
			if (dialog.ShowDialog() == true)
			{
				_core.SetContract(dialog.SelectedProduct, dialog.SelectedYear, dialog.SelectedMonth);
				UpdateChartContract();
			}
		}

		private void BuildChartTopRightPanel()
		{
			var panel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Left,
				Margin = new Thickness(6, 0, 0, 0)
			};

			_timeframeBar = new TimeframeBar();
			_timeframeBar.SetTimeframe(_viewPeriod);
			_timeframeBar.OnTimeframeChange += (s, v) => _ = ChangeTimeframeAsync(v);
			panel.Children.Add(_timeframeBar);

			var quotePanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(10, 0, 0, 0)
			};

			_topBidText = CreateTopQuoteText("買進:---");
			_topAskText = CreateTopQuoteText("賣出:---");
			_topLastText = CreateTopQuoteText("成交:---");
			_topVolText = CreateTopQuoteText("量:---");
			_topChgText = CreateTopQuoteText("漲跌:---");
			quotePanel.DataContext = _chartQuoteVm;
			BindTopQuote(_topBidText, nameof(ChartQuoteViewModel.BidText), nameof(ChartQuoteViewModel.BidBrush));
			BindTopQuote(_topAskText, nameof(ChartQuoteViewModel.AskText), nameof(ChartQuoteViewModel.AskBrush));
			BindTopQuote(_topLastText, nameof(ChartQuoteViewModel.LastText), nameof(ChartQuoteViewModel.LastBrush));
			BindTopQuote(_topVolText, nameof(ChartQuoteViewModel.VolumeText), nameof(ChartQuoteViewModel.VolumeBrush));
			BindTopQuote(_topChgText, nameof(ChartQuoteViewModel.ChangeText), nameof(ChartQuoteViewModel.ChangeBrush));
			quotePanel.Children.Add(_topBidText);
			quotePanel.Children.Add(_topAskText);
			quotePanel.Children.Add(_topLastText);
			quotePanel.Children.Add(_topVolText);
			quotePanel.Children.Add(_topChgText);

			panel.Children.Add(quotePanel);
			MainChartView.SetMainTopRightContent(panel);
		}

		private async Task ChangeTimeframeAsync(int period)
		{
			if (period < 1 || period > 120)
			{
				return;
			}

			if (period == _viewPeriod)
			{
				return;
			}

			var oldPeriod = _viewPeriod;
			_viewPeriod = period;
			_core.ChartBridge.RegisterPeriod(period);
			if (oldPeriod != 1 && oldPeriod != period)
			{
				_core.ChartBridge.UnregisterPeriod(oldPeriod);
			}

			var history = _core.ChartBridge.GetHistoryList(period);
			await Dispatcher.InvokeAsync(() => MainChartView.AddBarList(history));
		}

		private static TextBlock CreateTopQuoteText(string text)
		{
			return new TextBlock
			{
				Text = text,
				Foreground = Brushes.White,
				FontSize = Charts.ChartFontManager.GetFontSize("ChartFontSizeMd", 15),
				Margin = new Thickness(8, 0, 0, 0)
			};
		}

		private static void BindTopQuote(TextBlock target, string textPath, string brushPath)
		{
			target.SetBinding(TextBlock.TextProperty, new Binding(textPath));
			target.SetBinding(TextBlock.ForegroundProperty, new Binding(brushPath));
		}

		private static string NormalizeTopValue(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return value;
			}

			var trimmed = value.Trim();
			var dotIndex = trimmed.IndexOf('.');
			if (dotIndex >= 0)
			{
				return trimmed.Substring(0, dotIndex);
			}

			var commaIndex = trimmed.IndexOf(',');
			if (commaIndex >= 0)
			{
				return trimmed.Substring(0, commaIndex);
			}

			return trimmed;
		}

		private void UpdateMagistockStatus()
		{
			if (_core.ClientCtrl.IsLoggedIn)
			{
				MagistockStatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushMarketOpen");
				return;
			}

			MagistockStatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushStatusClosed");
		}

		private void UpdateBrokerStatus()
		{
			if (_brokerStatusToolTip == null)
			{
				_brokerStatusToolTip = new ToolTip
				{
					Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
					Foreground = Brushes.White,
					BorderBrush = (Brush)FindResource("BrushBorder"),
					BorderThickness = new Thickness(1),
					Padding = new Thickness(8, 4, 8, 4)
				};
				BrokerStatusText.ToolTip = _brokerStatusToolTip;
			}

			if (_core.IsBrokerConnected)
			{
				var data = _core.ClientCtrl.LastUserData;
				var user = data?.User;
				var name = user?.Name ?? "---";
				var id = user?.Id ?? "---";
				var permission = data?.Permissions?.FirstOrDefault(p =>
					string.Equals(p.ProgramName, _core.ProgramName, StringComparison.OrdinalIgnoreCase));
				var account = permission?.Account ?? "---";
				_brokerStatusToolTip.Content = $"{name} {id} {account} 連線成功";
				return;
			}

			_brokerStatusToolTip.Content = "尚未連線期貨商，無法下單";
		}

		private void UpdateUserStatus()
		{
			if (_userStatusToolTip == null)
			{
				_userStatusToolTip = new ToolTip
				{
					Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
					Foreground = Brushes.White,
					BorderBrush = (Brush)FindResource("BrushBorder"),
					BorderThickness = new Thickness(1),
					Padding = new Thickness(8, 4, 8, 4)
				};
				UserStatusText.ToolTip = _userStatusToolTip;
			}

			if (_core.ClientCtrl.IsRealUserLogin)
			{
				UserStatusText.Text = "登入成功";
				UserStatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushMarketOpen");
				UserStatusText.ToolTip = null;
				return;
			}

			UserStatusText.Text = "未登入";
			UserStatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushStatusClosed");
			UserStatusText.ToolTip = _userStatusToolTip;
			_userStatusToolTip.Content = "登入Magistock帳號";
		}

		private void UpdatePriceSourceStatus()
		{
			var currentSource = _core.PriceManager.CurrentSource;
			DdePriceStatusText.Text = currentSource == Core.PriceManager.PriceSource.Dde ? "▶DDE報價" : "DDE報價";
			NetPriceStatusText.Text = currentSource == Core.PriceManager.PriceSource.Network ? "▶網路報價" : "網路報價";

			var ddeAvailable = _core.IsDdeConnected;
			var netAvailable = _core.ClientCtrl.IsLoggedIn;
			DdePriceStatusText.Foreground = ddeAvailable
				? (System.Windows.Media.Brush)FindResource("BrushMarketOpen")
				: (System.Windows.Media.Brush)FindResource("BrushStatusClosed");
			NetPriceStatusText.Foreground = netAvailable
				? (System.Windows.Media.Brush)FindResource("BrushMarketOpen")
				: (System.Windows.Media.Brush)FindResource("BrushStatusClosed");
			NetPriceStatusText.Visibility = Visibility.Visible;
		}

		private async void OnUserLoginClick(object sender, MouseButtonEventArgs e)
		{
			var versionText = $"{_core.Version1} {_core.Version2}".Trim();
			if (_core.ClientCtrl.IsUserLogin)
			{
				var data = _core.ClientCtrl.LastUserData;
				var permission = data?.Permissions?.FirstOrDefault(p =>
					string.Equals(p.ProgramName, _core.ProgramName, StringComparison.OrdinalIgnoreCase));

				var account = permission?.Account ?? "---";
				var branch = permission?.BranchName ?? "---";
				var accountText = account == "---" ? account : $"({branch}){account}";
				var password = permission?.BrokerPassword ?? "---";
				var permissionText = permission == null
					? "尚未購買"
					: permission.UnlimitedPermission ? "無口數限制" : $"{permission.PermissionCount} 口微型台指";
				var canChangePassword = permission != null && (permission.UnlimitedPermission || permission.PermissionCount > 0);
				var expireText = permission == null
					? "---"
					: permission.UnlimitedExpiry ? "永久" : (string.IsNullOrWhiteSpace(permission.ProgramExpireAt) ? "---" : permission.ProgramExpireAt);

				var user = data?.User;
				var userName = user?.Name ?? "";
				var userId = user?.Id ?? "";
				var message = $"身分證字號：{userId}\n期貨帳號：{accountText}\n{_core.ProgramName} 權限：{permissionText}\n到期日：{expireText}";
				if (InfoWindow.Show(this, "使用者資訊", userName, message, canChangePassword, true, out var logoutRequested, out var modifyRequested, out var changePasswordRequested))
				{
					if (logoutRequested)
					{
						await _core.ClientCtrl.LogoutToGuestAsync(_core.ProgramName, versionText);
						return;
					}

					if (changePasswordRequested)
					{
						var passwordWindow = new ChangeBrokerPasswordWindow(_core.ClientCtrl, userId, permission)
						{
							Owner = this,
							WindowStartupLocation = WindowStartupLocation.CenterOwner
						};
						passwordWindow.ShowDialog();
						return;
					}

					if (modifyRequested)
					{
						var editWindow = new EditProfileWindow(_core.ClientCtrl, userId, permission, user)
						{
							Owner = this,
							WindowStartupLocation = WindowStartupLocation.CenterOwner
						};
						editWindow.ShowDialog();
					}
				}
				return;
			}

			var loginWindow = new LoginWindow(_core.ClientCtrl, _core.ProgramName, versionText)
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};

			var result = loginWindow.ShowDialog();
			if (result != true)
			{
				return;
			}

			var ok = await _core.ClientCtrl.LoginUserAsync(loginWindow.UserId, loginWindow.Password, _core.ProgramName, versionText);
			if (!ok)
			{
				MessageBoxWindow.Show(this, "登入失敗，請確認帳號密碼或網路狀態。", "登入失敗");
			}
		}

		private void OnHolidayScheduleClick(object sender, MouseButtonEventArgs e)
		{
			var path = TaifexHisDbManager.MagistockStoragePaths.TradingCalendarPath;
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = path,
					UseShellExecute = true
				});
			}
			catch
			{
				// Ignore editor launch failures.
			}
		}

		private void OnImportHistoryClick(object sender, RoutedEventArgs e)
		{
			var dialog = new Microsoft.Win32.OpenFileDialog
			{
				Title = "選擇檔案",
				Filter = "一分鐘歷史 (*.csv)|*.csv|所有檔案 (*.*)|*.*"
			};

			if (dialog.ShowDialog(this) != true)
			{
				return;
			}

			if (!TryMatchImportContract(dialog.FileName, _core.CurContract, _core.ContractYear, _core.ContractMonth, out var mismatchReason))
			{
				MessageBoxWindow.Show(this, mismatchReason ?? "匯入的合約與目前選擇不一致。", "匯入歷史");
				return;
			}

			var history = _core.ChartBridge.ImportHistory(dialog.FileName, _core.TimeCtrl.ExchangeTime);
			if (history.Count > 0)
			{
				var viewHistory = _viewPeriod == 1 ? history : _core.ChartBridge.GetHistoryList(_viewPeriod);
				Dispatcher.BeginInvoke(() => MainChartView.AddBarList(viewHistory));
			}
			foreach (var manager in _core.SessionManagers)
			{
				if (manager.IsStrategyRunning)
				{
					manager.RebuildIndicators();
				}
			}

			if (_core.Client.QueueKBarImportFromFile(dialog.FileName, out _))
			{
				_ = _core.Client.UploadQueuedKBarImportAsync();
			}
		}

		private static bool TryMatchImportContract(string path, string product, int year, int month, out string? error)
		{
			error = null;
			try
			{
				if (!System.IO.File.Exists(path))
				{
					error = "找不到匯入檔案。";
					return false;
				}

				var header = ReadHeaderLine(path);
				if (string.IsNullOrWhiteSpace(header))
				{
					error = "匯入檔案格式錯誤。";
					return false;
				}

				if (!TryParseContractHeader(header, out var contractKey))
				{
					error = "無法解析匯入檔案的合約資訊。";
					return false;
				}

				var expectedKey = $"{product}_{year:D4}_{month:D2}";
				if (!string.Equals(contractKey, expectedKey, StringComparison.Ordinal))
				{
					error = $"匯入合約({contractKey})與目前合約({expectedKey})不一致。";
					return false;
				}

				return true;
			}
			catch
			{
				error = "匯入檔案讀取失敗。";
				return false;
			}
		}

		private static string? ReadHeaderLine(string path)
		{
			string? header = null;
			try
			{
				using var reader = new System.IO.StreamReader(path, new System.Text.UTF8Encoding(false, true), true);
				header = reader.ReadLine();
				if (!string.IsNullOrWhiteSpace(header) && TryParseContractHeader(header, out _))
				{
					return header;
				}
			}
			catch
			{
				// fall back to Big5
			}

			using (var reader = new System.IO.StreamReader(path, System.Text.Encoding.GetEncoding(950), true))
			{
				return reader.ReadLine();
			}
		}

		private static bool TryParseContractHeader(string header, out string contractKey)
		{
			contractKey = "";
			var match = System.Text.RegularExpressions.Regex.Match(header, "\\((?<code>[A-Z0-9]+)\\)");
			if (!match.Success)
			{
				return false;
			}

			var code = match.Groups["code"].Value;
			var prefix = code.Substring(0, 3).ToUpperInvariant();
			var name = prefix switch
			{
				"WTX" => "大型台指",
				"WMT" => "小型台指",
				"WTM" => "微型台指",
				_ => ""
			};
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}

			var ymMatch = System.Text.RegularExpressions.Regex.Match(header, "(?<ym>\\d{4})");
			if (ymMatch.Success)
			{
				var ym = ymMatch.Groups["ym"].Value;
				if (ym.Length == 4 &&
					int.TryParse(ym[..2], out var yy) &&
					int.TryParse(ym[2..], out var mm))
				{
					var year = 2000 + yy;
					contractKey = $"{name}_{year:D4}_{mm:D2}";
					return true;
				}
				return false;
			}

			if (code.Length < 5)
			{
				return false;
			}

			var monthCode = code[3];
			var month = DdeItemCatalog.GetMonthFromCode(monthCode);
			if (month <= 0)
			{
				return false;
			}

			var yearDigit = code[^1];
			if (!char.IsDigit(yearDigit))
			{
				return false;
			}

			var decade = DateTime.Now.Year / 10 * 10;
			var yearValue = decade + (yearDigit - '0');
			if (yearValue < DateTime.Now.Year - 1)
			{
				yearValue += 10;
			}

			contractKey = $"{name}_{yearValue:D4}_{month:D2}";
			return true;
		}

		private void ChartContextMenu_Opened(object sender, RoutedEventArgs e)
		{
			var pane = MainChartView.GetMainPricePane();
			ChartMenuCrosshair.IsChecked = pane.IsCrosshairVisible;
			ChartMenuTooltip.IsChecked = pane.IsTooltipVisible;
			ChartMenuTooltip.IsEnabled = pane.IsCrosshairVisible;
		}

		private void OnChartIndicatorClick(object sender, RoutedEventArgs e)
		{
			MainChartView.ShowAppearLayerWindow(this);
		}

		private void OnChartCrosshairToggle(object sender, RoutedEventArgs e)
		{
			var isChecked = ChartMenuCrosshair.IsChecked;
			MainChartView.SetCrosshairVisible(isChecked);

			if (!isChecked)
			{
				ChartMenuTooltip.IsChecked = false;
				ChartMenuTooltip.IsEnabled = false;
				MainChartView.SetTooltipVisible(false);
				return;
			}

			ChartMenuTooltip.IsEnabled = true;
			if (!ChartMenuTooltip.IsChecked)
			{
				ChartMenuTooltip.IsChecked = true;
				MainChartView.SetTooltipVisible(true);
			}
		}

		private void OnChartTooltipToggle(object sender, RoutedEventArgs e)
		{
			if (!ChartMenuCrosshair.IsChecked)
			{
				ChartMenuTooltip.IsChecked = false;
				ChartMenuTooltip.IsEnabled = false;
				MainChartView.SetTooltipVisible(false);
				return;
			}

			MainChartView.SetTooltipVisible(ChartMenuTooltip.IsChecked);
		}

		private void OnManageHistoryClick(object sender, MouseButtonEventArgs e)
		{
			try
			{
				var manager = new TaifexHisDbManager.TaifexHisDbManager(this);
				manager.ImportDialog();
			}
			catch
			{
				MessageBoxWindow.Show(this, "無法開啟歷史資料位置。", "管理歷史資料");
			}
		}

		private void OnSupportClick(object sender, MouseButtonEventArgs e)
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "https://www.magistock.com/custom.html",
					UseShellExecute = true
				});
			}
			catch
			{
				MessageBoxWindow.Show(this, "無法開啟客服頁面。", "聯絡客服");
			}
		}

		private void OnUpdateStatusClick(object sender, MouseButtonEventArgs e)
		{
			MessageBoxWindow.Show(this, "更新安裝程式將在您關閉程式後進行更新", "有可用更新");
		}

		private void OnPurchaseReminderNeeded(string message)
		{
			Dispatcher.Invoke(() =>
			{
				PurchaseReminderWindow.Show(this, message, _core.ClientCtrl);
			});
		}
	}
}
