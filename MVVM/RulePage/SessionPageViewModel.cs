using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using Brokers;
using ZenPlatform.Core;
using ZenPlatform.LogText;
using ZenPlatform.SessionManager;
using ZenPlatform.Strategy;

namespace ZenPlatform.MVVM.RulePage
{
    public sealed class SessionPageViewModel : INotifyPropertyChanged
    {
        private readonly SessionManager.SessionManager _manager;
        private readonly IBroker _broker;
        private readonly UserInfoCtrl _userInfoCtrl;
        private string _summaryPositionText = "無";
        private decimal _summaryFloatProfit;
        private decimal _summaryRealizedProfit;
        private int _summaryTradeCount;
        private decimal _summaryTotalProfit;
        private readonly DispatcherTimer _summaryTimer;
        private bool _summaryDirty;

        public SessionPageViewModel(SessionManager.SessionManager manager, IBroker broker, UserInfoCtrl userInfoCtrl)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
            _userInfoCtrl = userInfoCtrl ?? throw new ArgumentNullException(nameof(userInfoCtrl));

            _manager.PropertyChanged += OnManagerPropertyChanged;
            QueryMarginCommand = new AsyncRelayCommand(QueryMarginAsync);
            CheckCertCommand = new AsyncRelayCommand(CheckCertAsync);
            Sessions.CollectionChanged += OnSessionsChanged;
            foreach (var session in Sessions)
            {
                session.PropertyChanged += OnSessionPropertyChanged;
            }
            _summaryTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _summaryTimer.Tick += OnSummaryTimerTick;
            RebuildSummary();
        }

        public SessionManager.SessionManager Manager => _manager;
        public int Index => _manager.Index;
        public string DisplayName => _manager.DisplayName;
        public bool IsStrategyRunning => _manager.IsStrategyRunning;
        public bool IsBacktestActive => _manager.IsBacktestActive;
        public string BacktestButtonText => _manager.IsBacktestActive ? "停止回測" : "歷史回測";
        public bool IsBacktestButtonEnabled => _manager.IsBacktestActive || !_manager.IsStrategyRunning;
        public bool IsRealTrade
        {
            get => _manager.IsRealTrade;
            set
            {
                if (_manager.IsRealTrade == value)
                {
                    return;
                }

                _manager.IsRealTrade = value;
                OnPropertyChanged(nameof(IsRealTrade));
            }
        }

        public string StrategyButtonText => _manager.StrategyButtonText;
        public ObservableCollection<Session> Sessions => _manager.Sessions;
        public LogModel Log => _manager.Log;
        public string SummaryPositionText
        {
            get => _summaryPositionText;
            private set
            {
                if (string.Equals(_summaryPositionText, value, StringComparison.Ordinal))
                {
                    return;
                }
                _summaryPositionText = value;
                OnPropertyChanged(nameof(SummaryPositionText));
            }
        }

        public decimal SummaryFloatProfit
        {
            get => _summaryFloatProfit;
            private set
            {
                if (_summaryFloatProfit == value) return;
                _summaryFloatProfit = value;
                OnPropertyChanged(nameof(SummaryFloatProfit));
            }
        }

        public decimal SummaryRealizedProfit
        {
            get => _summaryRealizedProfit;
            private set
            {
                if (_summaryRealizedProfit == value) return;
                _summaryRealizedProfit = value;
                OnPropertyChanged(nameof(SummaryRealizedProfit));
            }
        }

        public int SummaryTradeCount
        {
            get => _summaryTradeCount;
            private set
            {
                if (_summaryTradeCount == value) return;
                _summaryTradeCount = value;
                OnPropertyChanged(nameof(SummaryTradeCount));
            }
        }

        public decimal SummaryTotalProfit
        {
            get => _summaryTotalProfit;
            private set
            {
                if (_summaryTotalProfit == value) return;
                _summaryTotalProfit = value;
                OnPropertyChanged(nameof(SummaryTotalProfit));
            }
        }

        public ICommand QueryMarginCommand { get; }
        public ICommand CheckCertCommand { get; }
        public string TrendModeText => _manager.RuleSet.TrendMode switch
        {
            TrendMode.Auto => "系統自動判定",
            TrendMode.None => "無方向判定",
            TrendMode.MovingAverage => $"均線判定 (MA{Math.Max(1, _manager.RuleSet.TrendMaPeriod)})",
            TrendMode.Force => "強制判定",
            _ => "未設定"
        };

        public StopLossMode CurrentStopLossMode => _manager.RuleSet.StopLossMode;

        public string TrendStatusText
        {
            get
            {
                return _manager.RuleSet.TrendMode switch
                {
                    TrendMode.None => "無視多空方向",
                    TrendMode.Force => _manager.RuleSet.TrendForceSide switch
                    {
                        TrendForceSide.多 => "自訂多方",
                        TrendForceSide.空 => "自訂空方",
                        _ => "---"
                    },
                    TrendMode.Auto or TrendMode.MovingAverage => _manager.CurrnetSide switch
                    {
                        EntryTrendSide.多 => "目前偏多",
                        EntryTrendSide.空 => "目前偏空",
                        _ => "---"
                    },
                    _ => "---"
                };
            }
        }

        public Brush TrendStatusBrush => _manager.RuleSet.TrendMode switch
        {
            TrendMode.None => new SolidColorBrush(Color.FromRgb(210, 210, 210)),
            TrendMode.Force => _manager.RuleSet.TrendForceSide switch
            {
                TrendForceSide.多 => new SolidColorBrush(Color.FromRgb(255, 120, 120)),
                TrendForceSide.空 => new SolidColorBrush(Color.FromRgb(120, 255, 140)),
                _ => new SolidColorBrush(Color.FromRgb(210, 210, 210))
            },
            TrendMode.Auto or TrendMode.MovingAverage => _manager.CurrnetSide switch
            {
                EntryTrendSide.多 => new SolidColorBrush(Color.FromRgb(255, 120, 120)),
                EntryTrendSide.空 => new SolidColorBrush(Color.FromRgb(120, 255, 140)),
                _ => new SolidColorBrush(Color.FromRgb(210, 210, 210))
            },
            _ => new SolidColorBrush(Color.FromRgb(210, 210, 210))
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(SessionManager.SessionManager.Name), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(SessionManager.SessionManager.DisplayName), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(DisplayName));
            }

            if (string.Equals(e.PropertyName, nameof(SessionManager.SessionManager.IsStrategyRunning), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(IsStrategyRunning));
                OnPropertyChanged(nameof(StrategyButtonText));
                OnPropertyChanged(nameof(IsBacktestButtonEnabled));
                RefreshTrendStatus();
            }

            if (string.Equals(e.PropertyName, nameof(SessionManager.SessionManager.IsRealTrade), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(IsRealTrade));
            }

            if (string.Equals(e.PropertyName, nameof(SessionManager.SessionManager.IsBacktestActive), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(IsBacktestActive));
                OnPropertyChanged(nameof(BacktestButtonText));
                OnPropertyChanged(nameof(IsBacktestButtonEnabled));
            }

            if (string.Equals(e.PropertyName, nameof(SessionManager.SessionManager.CurrnetSide), StringComparison.Ordinal))
            {
                RefreshTrendStatus();
            }
        }

        public void RefreshTrendStatus()
        {
            OnPropertyChanged(nameof(TrendModeText));
            OnPropertyChanged(nameof(TrendStatusText));
            OnPropertyChanged(nameof(TrendStatusBrush));
        }

        public void RefreshRuleSetStatus()
        {
            OnPropertyChanged(nameof(CurrentStopLossMode));
            RefreshTrendStatus();
        }

        private void OnSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is Session session)
                    {
                        session.PropertyChanged -= OnSessionPropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is Session session)
                    {
                        session.PropertyChanged += OnSessionPropertyChanged;
                    }
                }
            }

            RebuildSummary();
        }

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Session.Position):
                case nameof(Session.FloatProfit):
                case nameof(Session.RealizedProfit):
                case nameof(Session.TradeCount):
                    MarkSummaryDirty();
                    break;
            }
        }

        private void MarkSummaryDirty()
        {
            _summaryDirty = true;
            if (!_summaryTimer.IsEnabled)
            {
                _summaryTimer.Start();
            }
        }

        private void OnSummaryTimerTick(object? sender, EventArgs e)
        {
            if (!_summaryDirty)
            {
                _summaryTimer.Stop();
                return;
            }

            _summaryDirty = false;
            RebuildSummary();
        }

        private void RebuildSummary()
        {
            var totalPosition = 0;
            decimal totalFloat = 0;
            decimal totalRealized = 0;
            var totalTrades = 0;

            foreach (var session in Sessions)
            {
                totalPosition += session.Position;
                totalFloat += session.FloatProfit;
                totalRealized += session.RealizedProfit;
                totalTrades += session.TradeCount;
            }

            SummaryPositionText = totalPosition switch
            {
                > 0 => $"多單 {totalPosition} 口",
                < 0 => $"空單 {Math.Abs(totalPosition)} 口",
                _ => "無"
            };
            SummaryFloatProfit = totalFloat;
            SummaryRealizedProfit = totalRealized;
            SummaryTradeCount = totalTrades;
            SummaryTotalProfit = totalFloat + totalRealized;
        }

        private async Task QueryMarginAsync()
        {
            try
            {
                _manager.Log.Add("保證金查詢中...");
                var result = await Task.Run(QueryMarginLines).ConfigureAwait(true);
                foreach (var line in result.Lines)
                {
                    _manager.Log.Add(line);
                }

                if (result.TotalEquity.HasValue)
                {
                    await _userInfoCtrl.SendMarginBalanceAsync(result.TotalEquity.Value).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                _manager.Log.Add($"保證金查詢例外: {ex.Message}");
            }
        }

        private (List<string> Lines, double? TotalEquity) QueryMarginLines()
        {
            var lines = new List<string>();
            double totalEquity = 0;
            var hasEquity = false;
            if (!_broker.IsDomesticConnected && !_broker.IsForeignConnected)
            {
                lines.Add("保證金查詢失敗: 期貨商未連線");
                return (lines, null);
            }

            if (_broker.IsDomesticConnected)
            {
                var available = _broker.QueryDomesticAvailableMargin(out var availableCode, out var availableMessage);
                var total = _broker.QueryDomesticTotalEquity(out var totalCode, out var totalMessage);
                if (string.Equals(availableCode, "000", StringComparison.Ordinal) &&
                    string.Equals(totalCode, "000", StringComparison.Ordinal))
                {
                    lines.Add($"國內保證金 可動用:{available:N0} 總權益:{total:N0}");
                    totalEquity += Convert.ToDouble(total);
                    hasEquity = true;
                }
                else
                {
                    var message = string.IsNullOrWhiteSpace(availableMessage) ? totalMessage : availableMessage;
                    lines.Add($"國內保證金查詢失敗: {availableCode}/{totalCode} {message}".Trim());
                }
            }

            if (_broker.IsForeignConnected)
            {
                var available = _broker.QueryForeignAvailableMargin(out var availableCode, out var availableMessage);
                var total = _broker.QueryForeignTotalEquity(out var totalCode, out var totalMessage);
                if (string.Equals(availableCode, "000", StringComparison.Ordinal) &&
                    string.Equals(totalCode, "000", StringComparison.Ordinal))
                {
                    lines.Add($"國外保證金 可動用:{available:N0} 總權益:{total:N0}");
                    totalEquity += Convert.ToDouble(total);
                    hasEquity = true;
                }
                else
                {
                    var message = string.IsNullOrWhiteSpace(availableMessage) ? totalMessage : availableMessage;
                    lines.Add($"國外保證金查詢失敗: {availableCode}/{totalCode} {message}".Trim());
                }
            }

            if (lines.Count == 0)
            {
                lines.Add("保證金查詢無資料");
            }

            return (lines, hasEquity ? totalEquity : null);
        }

        private async Task CheckCertAsync()
        {
            try
            {
                var lines = await Task.Run(QueryCertLines).ConfigureAwait(true);
                foreach (var line in lines)
                {
                    _manager.Log.Add(line);
                }
            }
            catch (Exception ex)
            {
                _manager.Log.Add($"檢查憑證例外: {ex.Message}");
            }
        }

        private List<string> QueryCertLines()
        {
            var lines = new List<string>();
            var loginId = _userInfoCtrl.LoginId;
            if (string.IsNullOrWhiteSpace(loginId))
            {
                lines.Add("檢查憑證失敗: 無登入帳號");
                return lines;
            }

            var code = _broker.GetCertStatus(loginId, out var startDate, out var expireDate, out var message);
            if (string.Equals(code, "000", StringComparison.Ordinal))
            {
                var startText = NormalizeDateOnly(startDate);
                var expireText = NormalizeDateOnly(expireDate);
                lines.Add($"憑證狀態:正常 ({startText}~{expireText})");
            }
            else
            {
                var note = string.IsNullOrWhiteSpace(message) ? string.Empty : $" {message}";
                lines.Add($"憑證查詢失敗: {code}{note}");
            }

            return lines;
        }

        private static string NormalizeDateOnly(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (DateTime.TryParse(value, out var parsed))
            {
                return parsed.ToString("yyyy/MM/dd");
            }

            var trimmed = value.Trim();
            var spaceIndex = trimmed.IndexOf(' ');
            if (spaceIndex > 0)
            {
                trimmed = trimmed[..spaceIndex];
            }

            return trimmed.Length >= 10 ? trimmed[..10] : trimmed;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
