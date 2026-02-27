using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            Entry = new SessionEntry(this);
        }

        public int Index { get; }
        public SessionEntry Entry { get; }
        public bool AcceptSecondTicks { get; set; } = true;
        public bool AcceptPriceTicks { get; set; } = true;
        public decimal BacktestTickMinDiffToProcess { get; set; } = 0m;
        public bool EnableParallelBacktestTickDispatch { get; set; } = false;
        public int ParallelBacktestTickMaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);
        public int ParallelBacktestTickMinSessionCount { get; set; } = 8;
        public List<double> ColumnWidths { get; } = new();
        public RuleSet RuleSet { get; } = new();
        public SessionManagerRuntimeState RuntimeState { get; } = new();
        public IndicatorsNamespace.Indicators? Indicators { get; private set; }
        public Func<int, List<KChartCore.FunctionKBar>>? FetchHistoryBars { get; set; }
        public Action<int>? RegisterKBarPeriod { get; set; }
        public Action<int>? UnregisterKBarPeriod { get; set; }
        public Func<bool>? IsHistoryReady { get; set; }
        public Func<(string Product, int Year, int Month)>? GetCurrentContract { get; set; }
        public Func<string, int, int, bool, TimeSpan, decimal?>? ResolveContractPrice { get; set; }
        public Action<string, int, int>? SwitchContract { get; set; }
        public DateTime? LastIndicatorBarCloseTime { get; internal set; }
        public bool SuppressIndicatorLog { get; set; }
        internal ZenPlatform.SessionManager.Backtest.BacktestRecorder? BacktestRecorder { get; set; }
        private EntryTrendSide _currnetSide = EntryTrendSide.無;
        public EntryTrendSide CurrnetSide
        {
            get => _currnetSide;
            private set => SetCurrentTrendSide(value);
        }

        internal void SetCurrentTrendSide(EntryTrendSide side)
        {
            if (_currnetSide == side) return;
            _currnetSide = side;
            OnPropertyChanged(nameof(CurrnetSide));
        }

        internal void ResetCurrentTrendSide()
        {
            SetCurrentTrendSide(EntryTrendSide.無);
        }
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
        private bool _backtestPriceReady;
        private decimal? _lastBacktestProcessedPrice;
        private bool _pendingLogReset;
        private int _sessionIdSeed;
        private DateTime? _lastMinuteStamp;
        private readonly List<Session> _activeSessions = new();
        private readonly object _rebuildLock = new();
        private readonly HashSet<int> _kbarPeriods = new();
        private const string IndicatorPendingText = "(計算中....)";
        private const int BbiBollPeriodMinutes = 10;
        private const int MacdTriggerPeriodMinutes = 5;

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
        public event System.Action<Session, string>? SessionTradeFailed;

        public decimal? SendOrder(bool isBuy, int qty)
        {
            if (qty <= 0)
            {
                return null;
            }

            if (IsBacktestActive && !IsRealTrade)
            {
                if (!_backtestPriceReady)
                {
                    throw new System.InvalidOperationException("回測價格未就緒，無法成交。");
                }

                var backtestFill = CurPrice ?? (isBuy ? (Ask ?? Bid) : (Bid ?? Ask));
                if (!backtestFill.HasValue)
                {
                    throw new System.InvalidOperationException("回測價格未就緒，無法成交。");
                }

                return backtestFill.Value;
            }

            return _tradeCtrl.SendOrder(isBuy, qty, IsRealTrade);
        }

        internal decimal? SendOrderForContract(bool isBuy, int qty, string product, int year, int month, decimal? overrideSimFillPrice = null)
        {
            if (qty <= 0)
            {
                return null;
            }

            if (IsBacktestActive && !IsRealTrade)
            {
                return SendOrder(isBuy, qty);
            }

            return _tradeCtrl.SendOrder(
                isBuy,
                qty,
                IsRealTrade,
                overrideProduct: product,
                overrideYear: year,
                overrideMonth: month,
                overrideSimFillPrice: overrideSimFillPrice);
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
                RuntimeState = RuntimeState,
                RuleSet = new RuleSet
                {
                    OrderSize = RuleSet.OrderSize,
                    KbarPeriod = RuleSet.KbarPeriod,
                    TakeProfitPoints = RuleSet.TakeProfitPoints,
                    TakeProfitMode = RuleSet.TakeProfitMode,
                    AutoTakeProfitPoints = RuleSet.AutoTakeProfitPoints,
                    StopLossPoints = RuleSet.StopLossPoints,
                    StopLossMode = RuleSet.StopLossMode,
                    EnableAbsoluteStopLoss = RuleSet.EnableAbsoluteStopLoss,
                    AbsoluteStopLossPoints = RuleSet.AbsoluteStopLossPoints,
                    LossRetraceExitEnabled = RuleSet.LossRetraceExitEnabled,
                    LossRetraceTriggerPoints = RuleSet.LossRetraceTriggerPoints,
                    LossRetracePercent = RuleSet.LossRetracePercent,
                    TrendMode = RuleSet.TrendMode,
                    TrendMaPeriod = RuleSet.TrendMaPeriod,
                    TrendForceSide = RuleSet.TrendForceSide,
                    SameDirectionBlockMinutes = RuleSet.SameDirectionBlockMinutes,
                    SameDirectionBlockRange = RuleSet.SameDirectionBlockRange,
                    DaySessionStart = RuleSet.DaySessionStart,
                    DaySessionEnd = RuleSet.DaySessionEnd,
                    NightSessionStart = RuleSet.NightSessionStart,
                    NightSessionEnd = RuleSet.NightSessionEnd,
                    MaxReverseCount = RuleSet.MaxReverseCount,
                    MaxSessionCount = RuleSet.MaxSessionCount,
                    ReverseAfterStopLoss = RuleSet.ReverseAfterStopLoss,
                    CoverLossBeforeTakeProfit = RuleSet.CoverLossBeforeTakeProfit,
                    CoverLossTriggerPoints = RuleSet.CoverLossTriggerPoints,
                    ExitOnTotalProfitRise = RuleSet.ExitOnTotalProfitRise,
                    ExitOnTotalProfitRiseArmBelowPoints = RuleSet.ExitOnTotalProfitRiseArmBelowPoints,
                    ExitOnTotalProfitRisePoints = RuleSet.ExitOnTotalProfitRisePoints,
                    ExitOnTotalProfitDropAfterTrigger = RuleSet.ExitOnTotalProfitDropAfterTrigger,
                    ExitOnTotalProfitDropTriggerPoints = RuleSet.ExitOnTotalProfitDropTriggerPoints,
                    ExitOnTotalProfitDropExitPoints = RuleSet.ExitOnTotalProfitDropExitPoints,
                    ProfitRetraceExitEnabled = RuleSet.ProfitRetraceExitEnabled,
                    ProfitRetraceTriggerPoints = RuleSet.ProfitRetraceTriggerPoints,
                    ProfitRetracePercent = RuleSet.ProfitRetracePercent,
                    AutoRolloverWhenHolding = RuleSet.AutoRolloverWhenHolding,
                    AutoRolloverTime = RuleSet.AutoRolloverTime,
                    CloseBeforeDaySessionEnd = RuleSet.CloseBeforeDaySessionEnd,
                    CloseBeforeNightSessionEnd = RuleSet.CloseBeforeNightSessionEnd,
                    DayCloseBeforeTime = RuleSet.DayCloseBeforeTime,
                    NightCloseBeforeTime = RuleSet.NightCloseBeforeTime,
                    CloseBeforeLongHoliday = RuleSet.CloseBeforeLongHoliday,
                    CloseBeforeLongHolidayTime = RuleSet.CloseBeforeLongHolidayTime
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
            RuleSet.TakeProfitPoints = state.RuleSet?.TakeProfitPoints ?? RuleSet.TakeProfitPoints;
            RuleSet.TakeProfitMode = state.RuleSet?.TakeProfitMode ?? RuleSet.TakeProfitMode;
            RuleSet.AutoTakeProfitPoints = state.RuleSet?.AutoTakeProfitPoints ?? RuleSet.AutoTakeProfitPoints;
            RuleSet.StopLossPoints = state.RuleSet?.StopLossPoints ?? RuleSet.StopLossPoints;
            RuleSet.StopLossMode = state.RuleSet?.StopLossMode ?? RuleSet.StopLossMode;
            RuleSet.EnableAbsoluteStopLoss = state.RuleSet?.EnableAbsoluteStopLoss ?? RuleSet.EnableAbsoluteStopLoss;
            RuleSet.AbsoluteStopLossPoints = state.RuleSet?.AbsoluteStopLossPoints ?? RuleSet.AbsoluteStopLossPoints;
            RuleSet.LossRetraceExitEnabled = state.RuleSet?.LossRetraceExitEnabled ?? RuleSet.LossRetraceExitEnabled;
            RuleSet.LossRetraceTriggerPoints = state.RuleSet?.LossRetraceTriggerPoints ?? RuleSet.LossRetraceTriggerPoints;
            RuleSet.LossRetracePercent = state.RuleSet?.LossRetracePercent ?? RuleSet.LossRetracePercent;
            RuleSet.TrendMode = state.RuleSet?.TrendMode ?? RuleSet.TrendMode;
            RuleSet.TrendMaPeriod = state.RuleSet?.TrendMaPeriod ?? RuleSet.TrendMaPeriod;
            RuleSet.TrendForceSide = state.RuleSet?.TrendForceSide ?? RuleSet.TrendForceSide;
            RuleSet.SameDirectionBlockMinutes = state.RuleSet?.SameDirectionBlockMinutes ?? RuleSet.SameDirectionBlockMinutes;
            RuleSet.SameDirectionBlockRange = state.RuleSet?.SameDirectionBlockRange ?? RuleSet.SameDirectionBlockRange;
            RuleSet.DaySessionStart = state.RuleSet?.DaySessionStart ?? RuleSet.DaySessionStart;
            RuleSet.DaySessionEnd = state.RuleSet?.DaySessionEnd ?? RuleSet.DaySessionEnd;
            RuleSet.NightSessionStart = state.RuleSet?.NightSessionStart ?? RuleSet.NightSessionStart;
            RuleSet.NightSessionEnd = state.RuleSet?.NightSessionEnd ?? RuleSet.NightSessionEnd;
            RuleSet.MaxReverseCount = state.RuleSet?.MaxReverseCount ?? RuleSet.MaxReverseCount;
            RuleSet.MaxSessionCount = state.RuleSet?.MaxSessionCount ?? RuleSet.MaxSessionCount;
            RuleSet.ReverseAfterStopLoss = state.RuleSet?.ReverseAfterStopLoss ?? RuleSet.ReverseAfterStopLoss;
            RuleSet.CoverLossBeforeTakeProfit = state.RuleSet?.CoverLossBeforeTakeProfit ?? RuleSet.CoverLossBeforeTakeProfit;
            RuleSet.CoverLossTriggerPoints = state.RuleSet?.CoverLossTriggerPoints ?? RuleSet.CoverLossTriggerPoints;
            RuleSet.ExitOnTotalProfitRise = state.RuleSet?.ExitOnTotalProfitRise ?? RuleSet.ExitOnTotalProfitRise;
            RuleSet.ExitOnTotalProfitRiseArmBelowPoints = state.RuleSet?.ExitOnTotalProfitRiseArmBelowPoints ?? RuleSet.ExitOnTotalProfitRiseArmBelowPoints;
            RuleSet.ExitOnTotalProfitRisePoints = state.RuleSet?.ExitOnTotalProfitRisePoints ?? RuleSet.ExitOnTotalProfitRisePoints;
            RuleSet.ExitOnTotalProfitDropAfterTrigger = state.RuleSet?.ExitOnTotalProfitDropAfterTrigger ?? RuleSet.ExitOnTotalProfitDropAfterTrigger;
            RuleSet.ExitOnTotalProfitDropTriggerPoints = state.RuleSet?.ExitOnTotalProfitDropTriggerPoints ?? RuleSet.ExitOnTotalProfitDropTriggerPoints;
            RuleSet.ExitOnTotalProfitDropExitPoints = state.RuleSet?.ExitOnTotalProfitDropExitPoints ?? RuleSet.ExitOnTotalProfitDropExitPoints;
            RuleSet.ProfitRetraceExitEnabled = state.RuleSet?.ProfitRetraceExitEnabled ?? RuleSet.ProfitRetraceExitEnabled;
            RuleSet.ProfitRetraceTriggerPoints = state.RuleSet?.ProfitRetraceTriggerPoints ?? RuleSet.ProfitRetraceTriggerPoints;
            RuleSet.ProfitRetracePercent = state.RuleSet?.ProfitRetracePercent ?? RuleSet.ProfitRetracePercent;
            RuleSet.AutoRolloverWhenHolding = state.RuleSet?.AutoRolloverWhenHolding ?? RuleSet.AutoRolloverWhenHolding;
            RuleSet.AutoRolloverTime = state.RuleSet?.AutoRolloverTime ?? RuleSet.AutoRolloverTime;
            RuleSet.CloseBeforeDaySessionEnd =
                state.RuleSet?.CloseBeforeDaySessionEnd ??
                state.RuleSet?.CloseBeforeSessionEnd ??
                RuleSet.CloseBeforeDaySessionEnd;
            RuleSet.CloseBeforeNightSessionEnd =
                state.RuleSet?.CloseBeforeNightSessionEnd ??
                state.RuleSet?.CloseBeforeSessionEnd ??
                RuleSet.CloseBeforeNightSessionEnd;
            RuleSet.DayCloseBeforeTime = state.RuleSet?.DayCloseBeforeTime ?? RuleSet.DayCloseBeforeTime;
            RuleSet.NightCloseBeforeTime = state.RuleSet?.NightCloseBeforeTime ?? RuleSet.NightCloseBeforeTime;
            RuleSet.CloseBeforeLongHoliday = state.RuleSet?.CloseBeforeLongHoliday ?? RuleSet.CloseBeforeLongHoliday;
            RuleSet.CloseBeforeLongHolidayTime = state.RuleSet?.CloseBeforeLongHolidayTime ?? RuleSet.CloseBeforeLongHolidayTime;
            RuntimeState.CopyFrom(state.RuntimeState);
            _isRealTrade = state.IsRealTrade;
            _isStrategyRunning = state.IsStrategyRunning;
            OnPropertyChanged(nameof(IsRealTrade));
            OnPropertyChanged(nameof(IsStrategyRunning));
            OnPropertyChanged(nameof(StrategyButtonText));

            Sessions.Clear();
            _activeSessions.Clear();
            var maxId = 0;
            foreach (var sessionState in state.Sessions)
            {
                var session = sessionState.ToSession();
                session.Manager = this;
                Sessions.Add(session);
                TrackSessionIfActive(session);
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
            if (!_isStrategyRunning)
            {
                Log.Clear();
            }
        }

        public Session? AddSession()
        {
            var session = new Session
            {
                Id = ++_sessionIdSeed,
                Manager = this
            };

            Sessions.Insert(0, session);
            TrackSessionIfActive(session);
            return session;
        }

        public Session? CreateNewSession(bool isBuy, string reason)
        {
            return Entry.RequestNewSession(isBuy, reason, isAutoTrigger: false);
        }

        public bool CloseAllSessionsByNetting()
        {
            CompactActiveSessions();
            var activeSessions = _activeSessions.ToList();
            if (activeSessions.Count == 0)
            {
                return true;
            }

            Log.Add("使用者按下全部平倉。");

            var netPosition = 0;
            foreach (var session in activeSessions)
            {
                netPosition += session.PositionManager.TotalKou;
            }

            decimal? sharedFillPrice = null;
            if (netPosition != 0)
            {
                var isBuy = netPosition < 0;
                var qty = System.Math.Abs(netPosition);
                sharedFillPrice = SendOrder(isBuy, qty);
                if (!sharedFillPrice.HasValue)
                {
                    Log.Add("全部平倉失敗：淨額對沖單未成交。", "全部平倉失敗：淨額對沖單未成交。", ZenPlatform.LogText.LogTxtColor.黃色);
                    return false;
                }

                var sideText = isBuy ? "多單" : "空單";
                Log.Add($"淨額對沖成交：{sideText}{qty}口，成交價 {sharedFillPrice.Value:0.0}");
            }
            else
            {
                sharedFillPrice = CurPrice ?? Ask ?? Bid;
                if (!sharedFillPrice.HasValue)
                {
                    Log.Add("全部平倉失敗：無可用價格可結束任務。", "全部平倉失敗：無可用價格可結束任務。", ZenPlatform.LogText.LogTxtColor.黃色);
                    return false;
                }

                Log.Add($"淨額為 0，使用目前價格 {sharedFillPrice.Value:0.0} 進行任務結算。");
            }

            foreach (var session in activeSessions)
            {
                session.CloseAllAtPrice(sharedFillPrice.Value);
            }
            return true;
        }

        internal Session? CreateNewSessionCore(bool isBuy, string reason, bool shouldRecordAllowedEntry = true)
        {
            if (!IsStrategyRunning)
            {
                return null;
            }

            var app = System.Windows.Application.Current;
            var dispatcher = app?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                Session? created = null;
                dispatcher.Invoke(() =>
                {
                    created = CreateNewSessionCore(isBuy, reason, shouldRecordAllowedEntry);
                });
                return created;
            }

            var session = AddSession();
            if (session == null)
            {
                return null;
            }

            var sideText = isBuy ? "多單" : "空單";
            var reasonText = string.IsNullOrWhiteSpace(reason) ? "無" : reason.Trim();
            var createText = $"[{session.Id}]建立{sideText}新任務({reasonText})";
            Log.Add(
                createText,
                createText,
                isBuy ? ZenPlatform.LogText.LogTxtColor.紅色 : ZenPlatform.LogText.LogTxtColor.綠色);

            session.Start(isBuy, reason);
            if (shouldRecordAllowedEntry && session.AvgEntryPrice > 0m)
            {
                RecordAllowedEntry(isBuy, session.AvgEntryPrice, CurrentTime.Year >= 2000 ? CurrentTime : DateTime.Now);
            }
            return session;
        }

        public void OnTick(QuoteUpdate quote)
        {
            if (quote == null)
            {
                throw new System.ArgumentNullException(nameof(quote));
            }

            var isLastUpdate = quote.Field == QuoteField.Last;

            if (IsBacktestActive)
            {
                if (quote.Source != QuoteSource.Backtest)
                {
                    // Backtest isolation: ignore live quote sources.
                    return;
                }

                switch (quote.Field)
                {
                    case QuoteField.Last:
                        if (decimal.TryParse(quote.Value, out var last))
                        {
                            CurPrice = last;
                            Bid = last;
                            Ask = last;
                            _backtestPriceReady = true;
                        }
                        break;
                    case QuoteField.Volume:
                        if (int.TryParse(quote.Value, out var volume))
                        {
                            Volume = volume;
                        }
                        break;
                }
            }
            else
            {
                // Live/sim source of truth.
                _tradeCtrl.OnQuote(quote);
                Bid = _tradeCtrl.Bid;
                Ask = _tradeCtrl.Ask;
                CurPrice = _tradeCtrl.Last;
                Volume = _tradeCtrl.Volume;
            }

            LastQuoteTime = quote.Time;

            if (IsBacktestActive && !isLastUpdate)
            {
                // In backtest, evaluate strategy only on consolidated Last price updates.
                return;
            }

            if (IsBacktestActive && isLastUpdate && CurPrice.HasValue && BacktestTickMinDiffToProcess > 0m)
            {
                if (_lastBacktestProcessedPrice.HasValue)
                {
                    var diff = Math.Abs(CurPrice.Value - _lastBacktestProcessedPrice.Value);
                    if (diff < BacktestTickMinDiffToProcess)
                    {
                        return;
                    }
                }

                _lastBacktestProcessedPrice = CurPrice.Value;
            }

            Entry.OnTick();

            if (IsStrategyRunning && (!IsBacktestActive || !StrategyStartTime.HasValue || CurrentTime >= StrategyStartTime.Value))
            {
                var signal = Entry.GetEntrySignalOnTick();
                if (signal.IsBuy != 0)
                {
                    var triggerId = string.IsNullOrWhiteSpace(signal.TriggerId) ? "M?" : signal.TriggerId;
                    Entry.RequestNewSession(signal.IsBuy > 0, signal.Reason, isAutoTrigger: true, triggerId: triggerId);
                }
            }

            if (Sessions.Count == 0)
            {
                return;
            }

            if (IsBacktestActive && StrategyStartTime.HasValue && CurrentTime < StrategyStartTime.Value)
            {
                return;
            }

            CompactActiveSessions();
            if (_activeSessions.Count == 0)
            {
                return;
            }

            var snapshot = new List<Session>(_activeSessions);
            DispatchSessionsOnTick(snapshot);
            CompactActiveSessions();
        }

        public void RaiseRenameClicked()
        {
            LogRequested?.Invoke($"{DisplayName} 按下按鈕");
        }

        internal void PrepareBacktestQuoteIsolation()
        {
            Bid = null;
            Ask = null;
            CurPrice = null;
            Volume = null;
            LastQuoteTime = null;
            _backtestPriceReady = false;
            _lastBacktestProcessedPrice = null;
        }

        internal void ClearBacktestQuoteIsolation()
        {
            _backtestPriceReady = false;
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
            Entry.OnMinute();
            TryAutoRolloverAtSettlement(time);
            if (Sessions.Count == 0)
            {
                return;
            }

            if (IsBacktestActive && StrategyStartTime.HasValue && time < StrategyStartTime.Value)
            {
                return;
            }

            CompactActiveSessions();
            if (_activeSessions.Count == 0)
            {
                return;
            }

            var snapshot = new List<Session>(_activeSessions);
            foreach (var session in snapshot)
            {
                session.OnMinute();
            }
            CompactActiveSessions();
        }

        public bool TriggerRolloverNow()
        {
            var now = CurrentTime.Year >= 2000 ? CurrentTime : DateTime.Now;
            return TryAutoRolloverAtSettlement(now, ignoreSchedule: true, manualTrigger: true);
        }

        private bool TryAutoRolloverAtSettlement(DateTime now, bool ignoreSchedule = false, bool manualTrigger = false)
        {
            if (!IsStrategyRunning || IsBacktestActive)
            {
                return false;
            }

            if (!manualTrigger && !RuleSet.AutoRolloverWhenHolding)
            {
                return false;
            }

            if (!ignoreSchedule && (now.Year < 2000 || !IsThirdWednesday(now.Date)))
            {
                return false;
            }

            if (!ignoreSchedule && now.TimeOfDay < RuleSet.AutoRolloverTime)
            {
                return false;
            }

            if (!ignoreSchedule &&
                RuntimeState.AutoRollover.LastExecutedDate.HasValue &&
                RuntimeState.AutoRollover.LastExecutedDate.Value.Date == now.Date)
            {
                return false;
            }

            CompactActiveSessions();
            var active = _activeSessions.Where(s => s.PositionManager.TotalKou != 0).ToList();
            if (active.Count == 0)
            {
                MarkAutoRolloverExecuted(now, ignoreSchedule);
                return false;
            }

            var contract = GetCurrentContract?.Invoke();
            if (contract == null || string.IsNullOrWhiteSpace(contract.Value.Product) || contract.Value.Year <= 0 || contract.Value.Month <= 0)
            {
                return false;
            }

            var currentProduct = contract.Value.Product;
            var currentYear = contract.Value.Year;
            var currentMonth = contract.Value.Month;
            var nextContract = new DateTime(currentYear, currentMonth, 1).AddMonths(1);
            var nextYear = nextContract.Year;
            var nextMonth = nextContract.Month;

            var netPosition = active.Sum(s => s.PositionManager.TotalKou);
            var timeout = TimeSpan.FromSeconds(10);

            decimal? closeFill = null;
            decimal? openFill = null;
            decimal spread;

            if (IsRealTrade && netPosition != 0)
            {
                var closeIsBuy = netPosition < 0;
                var qty = Math.Abs(netPosition);
                closeFill = SendOrderForContract(closeIsBuy, qty, currentProduct, currentYear, currentMonth);
                if (!closeFill.HasValue)
                {
                    Log.Add("結算日自動換月失敗：本月淨額平倉未成交。", "結算日自動換月失敗：本月淨額平倉未成交。", ZenPlatform.LogText.LogTxtColor.黃色);
                    MarkAutoRolloverExecuted(now, ignoreSchedule);
                    return false;
                }

                var openIsBuy = netPosition > 0;
                openFill = SendOrderForContract(openIsBuy, qty, currentProduct, nextYear, nextMonth);
                if (!openFill.HasValue)
                {
                    Log.Add("結算日自動換月失敗：次月淨額開倉未成交。", "結算日自動換月失敗：次月淨額開倉未成交。", ZenPlatform.LogText.LogTxtColor.黃色);
                    MarkAutoRolloverExecuted(now, ignoreSchedule);
                    return false;
                }

                spread = openFill.Value - closeFill.Value;
            }
            else
            {
                var currentSell = ResolveContractPrice?.Invoke(currentProduct, currentYear, currentMonth, false, timeout);
                var currentBuy = ResolveContractPrice?.Invoke(currentProduct, currentYear, currentMonth, true, timeout);
                var nextSell = ResolveContractPrice?.Invoke(currentProduct, nextYear, nextMonth, false, timeout);
                var nextBuy = ResolveContractPrice?.Invoke(currentProduct, nextYear, nextMonth, true, timeout);

                if (!nextSell.HasValue || !nextBuy.HasValue)
                {
                    foreach (var session in active)
                    {
                        session.ForcePosition(0, "無法取得次月報價，任務平倉", isFinishClose: true);
                        session.RestoreFinished(true);
                    }
                    MarkAutoRolloverExecuted(now, ignoreSchedule);
                    return false;
                }

                if (netPosition > 0)
                {
                    if (!currentSell.HasValue)
                    {
                        currentSell = CurPrice ?? Bid ?? Ask;
                    }
                    closeFill = currentSell;
                    openFill = nextBuy;
                }
                else if (netPosition < 0)
                {
                    if (!currentBuy.HasValue)
                    {
                        currentBuy = CurPrice ?? Ask ?? Bid;
                    }
                    closeFill = currentBuy;
                    openFill = nextSell;
                }
                else
                {
                    var currentMid = BuildMid(currentSell, currentBuy) ?? CurPrice ?? Bid ?? Ask;
                    var nextMid = BuildMid(nextSell, nextBuy);
                    if (!currentMid.HasValue || !nextMid.HasValue)
                    {
                        foreach (var session in active)
                        {
                            session.ForcePosition(0, "無法取得次月報價，任務平倉", isFinishClose: true);
                            session.RestoreFinished(true);
                        }
                        MarkAutoRolloverExecuted(now, ignoreSchedule);
                        return false;
                    }

                    closeFill = currentMid;
                    openFill = nextMid;
                }

                if (!closeFill.HasValue || !openFill.HasValue)
                {
                    foreach (var session in active)
                    {
                        session.ForcePosition(0, "無法取得次月報價，任務平倉", isFinishClose: true);
                        session.RestoreFinished(true);
                    }
                    MarkAutoRolloverExecuted(now, ignoreSchedule);
                    return false;
                }

                spread = openFill.Value - closeFill.Value;
            }

            foreach (var session in active)
            {
                session.ApplyRolloverSpread(spread);
            }

            SwitchContract?.Invoke(currentProduct, nextYear, nextMonth);
            Log.Add($"結算日自動換月完成：{currentYear}/{currentMonth:00} -> {nextYear}/{nextMonth:00}，價差 {spread:0.##}");
            MarkAutoRolloverExecuted(now, ignoreSchedule);
            return true;
        }

        private void MarkAutoRolloverExecuted(DateTime now, bool ignoreSchedule)
        {
            if (ignoreSchedule)
            {
                return;
            }

            RuntimeState.AutoRollover.LastExecutedDate = now.Date;
        }

        private static decimal? BuildMid(decimal? sell, decimal? buy)
        {
            if (sell.HasValue && buy.HasValue)
            {
                return (sell.Value + buy.Value) / 2m;
            }

            return sell ?? buy;
        }

        private static bool IsThirdWednesday(DateTime date)
        {
            if (date.DayOfWeek != DayOfWeek.Wednesday)
            {
                return false;
            }

            var first = new DateTime(date.Year, date.Month, 1);
            var offset = ((int)DayOfWeek.Wednesday - (int)first.DayOfWeek + 7) % 7;
            var firstWed = first.AddDays(offset);
            var thirdWed = firstWed.AddDays(14);
            return date.Date == thirdWed.Date;
        }

        public void OnKBarCompleted(int period, ZenPlatform.Strategy.KBar bar)
        {
            if (!IsKBarPeriodRegistered(period))
            {
                return;
            }

            Entry.OnKBarCompleted(period, bar);

            if (IsStrategyRunning && (!IsBacktestActive || !StrategyStartTime.HasValue || CurrentTime >= StrategyStartTime.Value))
            {
                var signal = Entry.GetEntrySignalOnKBarCompleted(period, bar);
                if (signal.IsBuy != 0)
                {
                    var triggerId = string.IsNullOrWhiteSpace(signal.TriggerId) ? "M?" : signal.TriggerId;
                    Entry.RequestNewSession(signal.IsBuy > 0, signal.Reason, isAutoTrigger: true, triggerId: triggerId);
                }
            }

            if (Sessions.Count == 0)
            {
                return;
            }

            CompactActiveSessions();
            if (_activeSessions.Count == 0)
            {
                return;
            }

            var snapshot = new List<Session>(_activeSessions);
            foreach (var session in snapshot)
            {
                session.OnKBarCompleted(period, bar);
            }
            CompactActiveSessions();
        }

        public string StrategyButtonText => IsStrategyRunning ? "策略結束" : "策略開始";

        public void ToggleStrategy()
        {
            IsStrategyRunning = !IsStrategyRunning;
            if (IsStrategyRunning)
            {
                AcceptPriceTicks = true;
                AcceptSecondTicks = true;
                IsRealTrade = false;
                Sessions.Clear();
                _activeSessions.Clear();
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
            RuntimeState.ResetForStrategyStart();
            ResetCurrentTrendSide();
            Entry.ResetTrendState();
            RegisterStrategyKBarPeriod(RuleSet.KbarPeriod);
            RegisterStrategyKBarPeriod(BbiBollPeriodMinutes);
            RegisterStrategyKBarPeriod(MacdTriggerPeriodMinutes);
            RebuildIndicators(force: true);
            InitializeTrendSideFromLatestBar();
        }

        private void TrackSessionIfActive(Session session)
        {
            if (session.IsFinished)
            {
                return;
            }

            _activeSessions.Add(session);
        }

        private void CompactActiveSessions()
        {
            for (var i = _activeSessions.Count - 1; i >= 0; i--)
            {
                if (_activeSessions[i].IsFinished)
                {
                    _activeSessions.RemoveAt(i);
                }
            }
        }

        private void DispatchSessionsOnTick(List<Session> snapshot)
        {
            if (!IsBacktestActive ||
                !EnableParallelBacktestTickDispatch ||
                snapshot.Count < Math.Max(2, ParallelBacktestTickMinSessionCount))
            {
                foreach (var session in snapshot)
                {
                    session.OnTick();
                }
                return;
            }

            var degree = ParallelBacktestTickMaxDegreeOfParallelism;
            if (degree <= 0)
            {
                degree = Math.Max(1, Environment.ProcessorCount);
            }

            degree = Math.Min(degree, snapshot.Count);
            if (degree <= 1)
            {
                foreach (var session in snapshot)
                {
                    session.OnTick();
                }
                return;
            }

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = degree
            };

            Parallel.ForEach(snapshot, options, session => session.OnTick());
        }

        internal void RecordAllowedEntry(bool isBuy, decimal price, DateTime time)
        {
            var blockState = RuntimeState.SameDirectionBlock;
            blockState.LastAllowedEntryTime = time;
            blockState.LastAllowedEntrySide = isBuy ? 1 : -1;
            blockState.LastAllowedEntryPrice = price;
        }

        internal void InitializeTrendSideFromLatestBar()
        {
            if (RuleSet.TrendMode == TrendMode.None)
            {
                ResetCurrentTrendSide();
                return;
            }

            if (RuleSet.TrendMode == TrendMode.Force)
            {
                var forced = RuleSet.TrendForceSide switch
                {
                    TrendForceSide.多 => EntryTrendSide.多,
                    TrendForceSide.空 => EntryTrendSide.空,
                    _ => EntryTrendSide.無
                };
                SetCurrentTrendSide(forced);
                return;
            }

            var bars = FetchHistoryBars?.Invoke(RuleSet.KbarPeriod);
            if (bars == null || bars.Count == 0)
            {
                return;
            }

            // Replay all completed bars from old to new so trend side can be
            // established during warm-up and then carried forward.
            foreach (var raw in bars.OrderBy(b => b.CloseTime))
            {
                if (raw.IsNullBar || raw.IsFloating || raw.IsAlignmentBar)
                {
                    continue;
                }

                var bar = new KBar(raw.Open, raw.High, raw.Low, raw.Close, raw.Volume);
                var side = Entry.GetCurrentTrendSide(bar);
                SetCurrentTrendSide(side);
            }
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
                    Indicators.MA[0].SetParameter(RuleSet.TrendMaPeriod);
                    var sharedExitMaPeriod = System.Math.Max(RuleSet.ProfitRetracePercent, RuleSet.LossRetracePercent);
                    if (sharedExitMaPeriod > 0)
                    {
                        // Shared MA for profit/loss retrace exits.
                        Indicators.MA[1].SetParameter(sharedExitMaPeriod);
                        Indicators.MA[2].SetParameter(sharedExitMaPeriod);
                    }
                    Indicators.MACD.SetParameter(12, 26, 9);
                    LastIndicatorBarCloseTime = null;
                    if (IsStrategyRunning)
                    {
                        RegisterStrategyKBarPeriod(RuleSet.KbarPeriod);
                        RegisterStrategyKBarPeriod(BbiBollPeriodMinutes);
                        RegisterStrategyKBarPeriod(MacdTriggerPeriodMinutes);
                    }

                    var seenCloseTimes = new HashSet<DateTime>();
                    foreach (var bar in bars.OrderBy(b => b.CloseTime))
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
                        LastIndicatorBarCloseTime = closeStamp;
                    }

                    // BbiBoll 固定使用 10 分 K（獨立於主策略週期）
                    var bbiBollBars = FetchHistoryBars(BbiBollPeriodMinutes);
                    if (bbiBollBars != null && bbiBollBars.Count > 0)
                    {
                        var seenBbiBollCloseTimes = new HashSet<DateTime>();
                        foreach (var bar in bbiBollBars.OrderBy(b => b.CloseTime))
                        {
                            if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
                            {
                                continue;
                            }

                            var closeStamp = new DateTime(
                                bar.CloseTime.Year,
                                bar.CloseTime.Month,
                                bar.CloseTime.Day,
                                bar.CloseTime.Hour,
                                bar.CloseTime.Minute,
                                0,
                                DateTimeKind.Unspecified);
                            if (!seenBbiBollCloseTimes.Add(closeStamp))
                            {
                                continue;
                            }

                            Indicators.UpdateBbiBoll(new IndicatorsNamespace.KBar(
                                bar.StartTime,
                                bar.Open,
                                bar.High,
                                bar.Low,
                                bar.Close,
                                bar.Volume));
                        }
                    }

                    // MacdTrigger 固定使用 5 分 K（獨立於主策略週期）
                    var macdTriggerBars = FetchHistoryBars(MacdTriggerPeriodMinutes);
                    if (macdTriggerBars != null && macdTriggerBars.Count > 0)
                    {
                        var seenMacdTriggerCloseTimes = new HashSet<DateTime>();
                        foreach (var bar in macdTriggerBars.OrderBy(b => b.CloseTime))
                        {
                            if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
                            {
                                continue;
                            }

                            var closeStamp = new DateTime(
                                bar.CloseTime.Year,
                                bar.CloseTime.Month,
                                bar.CloseTime.Day,
                                bar.CloseTime.Hour,
                                bar.CloseTime.Minute,
                                0,
                                DateTimeKind.Unspecified);
                            if (!seenMacdTriggerCloseTimes.Add(closeStamp))
                            {
                                continue;
                            }

                            Indicators.UpdateMacdTrigger(new IndicatorsNamespace.KBar(
                                bar.StartTime,
                                bar.Open,
                                bar.High,
                                bar.Low,
                                bar.Close,
                                bar.Volume));
                            Indicators.UpdateRangeBound(new IndicatorsNamespace.KBar(
                                bar.StartTime,
                                bar.Open,
                                bar.High,
                                bar.Low,
                                bar.Close,
                                bar.Volume));
                        }
                    }
                    IndicatorsReadyForLog = true;
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

        internal void RaiseSessionTradeFailed(Session session, string detail)
        {
            SessionTradeFailed?.Invoke(session, detail);
        }
    }

}
