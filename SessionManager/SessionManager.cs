using System.Collections.Generic;
using ZenPlatform.Core;
using ZenPlatform.Trade;
using IndicatorsNamespace = ZenPlatform.Indicators;
using ZenPlatform.Strategy;

namespace ZenPlatform.SessionManager
{
    public sealed class SessionManager : System.ComponentModel.INotifyPropertyChanged
    {
        public SessionManager(int index, TradeCtrl tradeCtrl)
        {
            Index = index;
            _tradeCtrl = tradeCtrl ?? throw new System.ArgumentNullException(nameof(tradeCtrl));
            Log = new ZenPlatform.LogText.LogModel();
            Sessions = new System.Collections.ObjectModel.ObservableCollection<Session>();
            IsRealTrade = false;
        }

        public int Index { get; }
        public bool AcceptSecondTicks { get; set; } = true;
        public bool AcceptPriceTicks { get; set; } = true;
        public List<double> ColumnWidths { get; } = new();
        public RuleSet RuleSet { get; } = new();
        public IndicatorsNamespace.Indicators? Indicators { get; private set; }
        public Func<int, List<KChartCore.FunctionKBar>>? FetchHistoryBars { get; set; }
        public Action<int>? RegisterKBarPeriod { get; set; }
        public Action<int>? UnregisterKBarPeriod { get; set; }
        public Func<bool>? IsHistoryReady { get; set; }
        public DateTime? LastIndicatorBarCloseTime { get; internal set; }
        public bool SuppressIndicatorLog { get; set; }
        private bool _isBacktestActive;
        public bool IsBacktestActive
        {
            get => _isBacktestActive;
            internal set
            {
                if (_isBacktestActive == value) return;
                _isBacktestActive = value;
                OnPropertyChanged(nameof(IsBacktestActive));
            }
        }
        internal object IndicatorSync { get; } = new();
        public bool IndicatorsReadyForLog { get; private set; }
        private string _name = "";
        private string _backtestStatusText = "回測中";
        private double _backtestProgressPercent;

        public string Name
        {
            get => _name;
            set
            {
                var next = value ?? "";
                if (_name == next) return;
                _name = next;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"策略{Index + 1}" : Name;
        public string BacktestStatusText
        {
            get => _backtestStatusText;
            internal set
            {
                var next = value ?? string.Empty;
                if (_backtestStatusText == next) return;
                _backtestStatusText = next;
                OnPropertyChanged(nameof(BacktestStatusText));
            }
        }

        public double BacktestProgressPercent
        {
            get => _backtestProgressPercent;
            internal set
            {
                if (System.Math.Abs(_backtestProgressPercent - value) < 0.0001) return;
                _backtestProgressPercent = value;
                OnPropertyChanged(nameof(BacktestProgressPercent));
            }
        }
        public ZenPlatform.LogText.LogModel Log { get; }
        public System.Collections.ObjectModel.ObservableCollection<Session> Sessions { get; }
        public DateTime CurrentTime { get; private set; }
        public DateTime? StrategyStartTime { get; set; }
        private bool _isStrategyRunning;
        private bool _isRealTrade;
        private readonly TradeCtrl _tradeCtrl;
        public decimal? Bid { get; private set; }
        public decimal? Ask { get; private set; }
        public decimal? CurPrice { get; private set; }
        public int? Volume { get; private set; }
        public DateTime? LastQuoteTime { get; private set; }
        private bool _pendingLogReset;
        private int _sessionIdSeed;
        private DateTime? _lastMinuteStamp;
        private readonly object _rebuildLock = new();
        private readonly HashSet<int> _kbarPeriods = new();
        private const string IndicatorPendingText = "(計算中....)";

        public bool IsStrategyRunning
        {
            get => _isStrategyRunning;
            private set
            {
                if (_isStrategyRunning == value) return;
                _isStrategyRunning = value;
                OnPropertyChanged(nameof(IsStrategyRunning));
            }
        }

        public bool IsRealTrade
        {
            get => _isRealTrade;
            set
            {
                if (_isRealTrade == value) return;
                _isRealTrade = value;
                OnPropertyChanged(nameof(IsRealTrade));
            }
        }

        public event System.Action<string>? LogRequested;
        public event System.Action<bool>? BacktestStopped;

        public decimal? SendOrder(bool isBuy, int qty)
        {
            if (qty <= 0)
            {
                return null;
            }

            return _tradeCtrl.SendOrder(isBuy, qty, IsRealTrade);
        }

        public SessionManagerState ExportState()
        {
            var state = new SessionManagerState
            {
                Name = Name,
                IsStrategyRunning = IsStrategyRunning,
                IsRealTrade = IsRealTrade,
                AcceptSecondTicks = AcceptSecondTicks,
                AcceptPriceTicks = AcceptPriceTicks,
                ColumnWidths = new List<double>(ColumnWidths),
                RuleSet = new RuleSet
                {
                    OrderSize = RuleSet.OrderSize,
                    KbarPeriod = RuleSet.KbarPeriod,
                    KPeriod = RuleSet.KPeriod,
                    DPeriod = RuleSet.DPeriod,
                    RsvPeriod = RuleSet.RsvPeriod,
                    TakeProfitPoints = RuleSet.TakeProfitPoints,
                    MaxReverseCount = RuleSet.MaxReverseCount,
                    MaxSessionCount = RuleSet.MaxSessionCount
                }
            };

            foreach (var session in Sessions)
            {
                state.Sessions.Add(SessionState.FromSession(session));
            }

            foreach (var entry in Log.Entries)
            {
                state.LogEntries.Add(new LogEntryState
                {
                    Time = entry.Time,
                    Text = entry.Text,
                    HighlightText = entry.HighlightText,
                    HighlightColor = entry.HighlightColor
                });
            }

            return state;
        }

        public void ImportState(SessionManagerState state)
        {
            Name = state.Name ?? string.Empty;
            AcceptSecondTicks = state.AcceptSecondTicks;
            AcceptPriceTicks = state.AcceptPriceTicks;
            ColumnWidths.Clear();
            if (state.ColumnWidths != null)
            {
                ColumnWidths.AddRange(state.ColumnWidths);
            }
            RuleSet.OrderSize = state.RuleSet?.OrderSize ?? RuleSet.OrderSize;
            RuleSet.KbarPeriod = state.RuleSet?.KbarPeriod ?? RuleSet.KbarPeriod;
            RuleSet.KPeriod = state.RuleSet?.KPeriod ?? RuleSet.KPeriod;
            RuleSet.DPeriod = state.RuleSet?.DPeriod ?? RuleSet.DPeriod;
            RuleSet.RsvPeriod = state.RuleSet?.RsvPeriod ?? RuleSet.RsvPeriod;
            RuleSet.TakeProfitPoints = state.RuleSet?.TakeProfitPoints ?? RuleSet.TakeProfitPoints;
            RuleSet.MaxReverseCount = state.RuleSet?.MaxReverseCount ?? RuleSet.MaxReverseCount;
            RuleSet.MaxSessionCount = state.RuleSet?.MaxSessionCount ?? RuleSet.MaxSessionCount;
            _isRealTrade = state.IsRealTrade;
            _isStrategyRunning = state.IsStrategyRunning;
            OnPropertyChanged(nameof(IsRealTrade));
            OnPropertyChanged(nameof(IsStrategyRunning));
            OnPropertyChanged(nameof(StrategyButtonText));

            Sessions.Clear();
            var maxId = 0;
            foreach (var sessionState in state.Sessions)
            {
                var session = sessionState.ToSession();
                session.Manager = this;
                Sessions.Add(session);
                if (session.Id > maxId)
                {
                    maxId = session.Id;
                }
            }

            _sessionIdSeed = maxId;
            var entries = new List<ZenPlatform.LogText.LogEntry>(state.LogEntries.Count);
            foreach (var entry in state.LogEntries)
            {
                entries.Add(new ZenPlatform.LogText.LogEntry(entry.Time, entry.Text, entry.HighlightText, entry.HighlightColor));
            }
            Log.LoadEntries(entries);
        }

        public Session? AddSession()
        {
            var session = new Session
            {
                Id = ++_sessionIdSeed,
                Manager = this
            };

            Sessions.Add(session);
            return session;
        }

        public void OnTick(QuoteUpdate quote)
        {
            if (quote == null)
            {
                throw new System.ArgumentNullException(nameof(quote));
            }

            switch (quote.Field)
            {
                case QuoteField.Bid:
                    if (decimal.TryParse(quote.Value, out var bid))
                    {
                        Bid = bid;
                    }
                    break;
                case QuoteField.Ask:
                    if (decimal.TryParse(quote.Value, out var ask))
                    {
                        Ask = ask;
                    }
                    break;
                case QuoteField.Last:
                    if (decimal.TryParse(quote.Value, out var last))
                    {
                        CurPrice = last;
                    }
                    break;
                case QuoteField.Volume:
                    if (int.TryParse(quote.Value, out var volume))
                    {
                        Volume = volume;
                    }
                    break;
            }

            LastQuoteTime = quote.Time;

            if (Sessions.Count == 0)
            {
                return;
            }

            if (IsBacktestActive && StrategyStartTime.HasValue && CurrentTime < StrategyStartTime.Value)
            {
                return;
            }

            var snapshot = new List<Session>(Sessions);
            foreach (var session in snapshot)
            {
                session.OnTick();
            }
        }

        public void RaiseRenameClicked()
        {
            LogRequested?.Invoke($"{DisplayName} 按下按鈕");
        }

        public void OnSecond(DateTime time)
        {
            CurrentTime = time;
            Log.SetCurrentTime(time);
            var minuteStamp = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
            if (_lastMinuteStamp == null || _lastMinuteStamp.Value != minuteStamp)
            {
                _lastMinuteStamp = minuteStamp;
                OnMinute(minuteStamp);
            }
            if (_pendingLogReset)
            {
                ResetLogForStrategyStart();
            }
        }

        public void OnMinute(DateTime time)
        {
            Log.SetCurrentTime(time);
            Log.Add($"[M{Index + 1}] OnMinute {time:MM/dd HH:mm:ss}");
            if (Sessions.Count == 0)
            {
                return;
            }

            if (IsBacktestActive && StrategyStartTime.HasValue && time < StrategyStartTime.Value)
            {
                return;
            }

            var snapshot = new List<Session>(Sessions);
            foreach (var session in snapshot)
            {
                session.OnMinute();
            }
        }

        public void OnKBarCompleted(int period, KChartCore.FunctionKBar bar)
        {
            if (!IsKBarPeriodRegistered(period) || Sessions.Count == 0)
            {
                return;
            }

            var snapshot = new List<Session>(Sessions);
            foreach (var session in snapshot)
            {
                session.OnKBarCompleted(period, bar);
            }
        }

        public string StrategyButtonText => IsStrategyRunning ? "策略結束" : "策略開始";

        public void ToggleStrategy()
        {
            IsStrategyRunning = !IsStrategyRunning;
            if (IsStrategyRunning)
            {
                IsRealTrade = false;
                Sessions.Clear();
                _sessionIdSeed = 0;
                ResetLogForStrategyStart();
                RuleStartInit();
            }
            else
            {
                UnregisterAllStrategyKBarPeriods();
                Log.Add("==== 策略結束 ====");
            }
            OnPropertyChanged(nameof(StrategyButtonText));
        }

        internal void StopStrategySilently()
        {
            if (!IsStrategyRunning)
            {
                return;
            }

            IsStrategyRunning = false;
            UnregisterAllStrategyKBarPeriods();
            OnPropertyChanged(nameof(StrategyButtonText));
        }

        private void RuleStartInit()
        {
            RegisterStrategyKBarPeriod(RuleSet.KbarPeriod);
            RebuildIndicators(force: true);
        }

        public void RebuildIndicators(bool force = false)
        {
            lock (_rebuildLock)
            {
                if (FetchHistoryBars == null)
                {
                    return;
                }
                if (IsHistoryReady != null && !IsHistoryReady())
                {
                    AddPendingIndicatorLog();
                    return;
                }

                var bars = FetchHistoryBars(RuleSet.KbarPeriod);
                if (bars == null || bars.Count == 0)
                {
                    AddPendingIndicatorLog();
                    return;
                }

                lock (IndicatorSync)
                {
                    if (!force)
                    {
                        var latestCloseStamp = GetLatestCloseStamp(bars);
                        if (latestCloseStamp.HasValue && Indicators != null && LastIndicatorBarCloseTime == latestCloseStamp)
                        {
                            return;
                        }
                    }
                    // no-op

                    Indicators = new IndicatorsNamespace.Indicators();
                    Indicators.MA[0].SetParameter(RuleSet.KbarPeriod);
                    Indicators.KD.SetParameter(RuleSet.KPeriod, RuleSet.DPeriod, RuleSet.RsvPeriod);
                    Indicators.MACD.SetParameter(12, 26, 9);
                    LastIndicatorBarCloseTime = null;
                    if (IsStrategyRunning)
                    {
                        RegisterStrategyKBarPeriod(RuleSet.KbarPeriod);
                    }

                    var seenCloseTimes = new HashSet<DateTime>();
                    var recentLogs = new Queue<string>(10);
                    foreach (var bar in bars)
                    {
                        if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
                        {
                            continue;
                        }

                        var closeStamp = new System.DateTime(bar.CloseTime.Year, bar.CloseTime.Month, bar.CloseTime.Day,
                            bar.CloseTime.Hour, bar.CloseTime.Minute, 0, System.DateTimeKind.Unspecified);
                        if (!seenCloseTimes.Add(closeStamp))
                        {
                            continue;
                        }

                        Indicators.Update(new IndicatorsNamespace.KBar(bar.StartTime, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
                        if (Indicators.KD.HasValue)
                        {
                            var text = $"K={Indicators.KD.K:0.00} D={Indicators.KD.D:0.00} ({closeStamp:MM/dd HH:mm:ss})";
                            if (recentLogs.Count == 10)
                            {
                                recentLogs.Dequeue();
                            }
                            recentLogs.Enqueue(text);
                        }
                        LastIndicatorBarCloseTime = closeStamp;
                    }
                    IndicatorsReadyForLog = true;
                    if (recentLogs.Count > 0)
                    {
                        foreach (var line in recentLogs)
                        {
                        }
                    }
                }
            }
        }

        public string BuildIndicatorSnapshot()
        {
            if (Indicators == null)
            {
                return "指標未初始化";
            }

            var lines = new List<string>();
            var kd = Indicators.KD;
            if (kd.IsConfigured)
            {
                lines.Add(kd.HasValue ? $"KD K={kd.K:0.00} D={kd.D:0.00}" : "KD=計算中");
            }

            var maParts = new List<string>();
            for (var i = 0; i < Indicators.MA.Length; i++)
            {
                var ma = Indicators.MA[i];
                if (!ma.IsConfigured)
                {
                    continue;
                }
                maParts.Add(ma.HasValue ? $"MA{ma.Period}={ma.Value:0.00}" : $"MA{ma.Period}=計算中");
            }
            if (maParts.Count > 0)
            {
                lines.Add(string.Join(" ", maParts));
            }

            var macd = Indicators.MACD;
            if (macd.IsConfigured)
            {
                if (macd.HasValue)
                {
                    var v = macd.GetValue(0);
                    lines.Add($"MACD DIF={v.Dif:0.00} DEA={v.Dea:0.00} MACD={v.Macd:0.00}");
                }
                else
                {
                    lines.Add("MACD=計算中");
                }
            }

            return lines.Count == 0 ? "指標未設定" : string.Join("\n", lines);
        }

        private static System.DateTime? GetLatestCloseStamp(List<KChartCore.FunctionKBar> bars)
        {
            for (var i = bars.Count - 1; i >= 0; i--)
            {
                var bar = bars[i];
                if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
                {
                    continue;
                }

                var closeTime = bar.CloseTime;
                return new System.DateTime(closeTime.Year, closeTime.Month, closeTime.Day,
                    closeTime.Hour, closeTime.Minute, 0, System.DateTimeKind.Unspecified);
            }

            return null;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        private void ResetLogForStrategyStart()
        {
            Log.Clear();
            Log.Add("==== 策略開始 ====");
            _pendingLogReset = false;
            IndicatorsReadyForLog = false;
        }

        private void AddPendingIndicatorLog()
        {
            if (Log.Entries.Count > 0 && Log.Entries[^1].Text == IndicatorPendingText)
            {
                return;
            }
            Log.Add(IndicatorPendingText);
        }

        public void RegisterStrategyKBarPeriod(int period)
        {
            if (period <= 0)
            {
                return;
            }

            if (_kbarPeriods.Add(period))
            {
                if (IsStrategyRunning || IsBacktestActive)
                {
                    RegisterKBarPeriod?.Invoke(period);
                }
            }
        }

        public void UnregisterAllStrategyKBarPeriods()
        {
            if (_kbarPeriods.Count == 0)
            {
                return;
            }

            foreach (var period in _kbarPeriods)
            {
                UnregisterKBarPeriod?.Invoke(period);
            }
            _kbarPeriods.Clear();
        }

        public bool IsKBarPeriodRegistered(int period) => _kbarPeriods.Contains(period);

        internal void RaiseBacktestStopped(bool canceled)
        {
            BacktestStopped?.Invoke(canceled);
        }

    }

}
