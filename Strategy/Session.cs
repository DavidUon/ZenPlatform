using System;
using ZenPlatform.Strategy.ExitRules;

namespace ZenPlatform.Strategy
{
    public sealed class Session : RuleBase, System.ComponentModel.INotifyPropertyChanged
    {
        public int Id { get; set; }
        private DateTime _startTime;
        private int _position;
        private int _startPosition;
        private decimal _avgEntryPrice;
        private decimal _floatProfit;
        private decimal _realizedProfit;
        private int _tradeCount;
        private int _reverseCount;
        private bool _isFailed;
        private decimal _minTotalProfitSeen;
        private decimal _worstTotalProfit;
        private decimal _minTotalProfitForRiseExit = decimal.MaxValue;
        private bool _riseFromDrawdownArmed;
        private bool _coverLossArmed;
        private bool _totalProfitDropAfterTriggerArmed;
        private bool _profitMaExitArmed;
        private bool _lossMaExitArmed;
        private bool _hasLastPriceForSharedMaCross;
        private decimal _lastPriceForSharedMaCross;
        private bool _hasEntryRangeBoundSnapshot;
        private decimal _entryRangeBoundA;
        private decimal _entryRangeBoundV;
        internal const int AutoStopLossRulePeriodMinutes = 5;
        private const int AutoStopLossFallbackLookbackBars = 20;
        private const decimal AutoStopLossFloatProfitThreshold = 50m;
        private static readonly IExitRule[] _commonExitRules =
        {
            new CloseBeforeSessionEndExitRule(),
            new LongHolidayCloseExitRule(),
            new AutoStopLossExitRule()
        };
        private static readonly IExitRule[] _tickExitRules =
        {
            new AbsoluteStopLossExitRule(),
            new StopLossExitRule(),
            new LossRetraceMaExitRule(),
            new ProfitRetraceMaExitRule(),
            new CoverLossRecoveryExitRule(),
            new RiseFromWorstExitRule(),
            new TotalProfitDropAfterTriggerExitRule(),
            new TakeProfitExitRule()
        };
        internal DateTime? LastBacktestUiRefreshBucket { get; set; }
        private const decimal CoverLossExitTarget = 2m;

        public DateTime StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime == value) return;
                _startTime = value;
                OnPropertyChanged(nameof(StartTime));
            }
        }

        public override bool IsFinished
        {
            get => base.IsFinished;
            protected set
            {
                if (base.IsFinished == value) return;
                base.IsFinished = value;
                OnPropertyChanged(nameof(IsFinished));
            }
        }

        public bool IsFailed
        {
            get => _isFailed;
            private set
            {
                if (_isFailed == value) return;
                _isFailed = value;
                OnPropertyChanged(nameof(IsFailed));
            }
        }

        public int Position
        {
            get => _position;
            set
            {
                if (_position == value) return;
                var previous = _position;
                _position = value;
                HandlePositionChangedForAutoStop(previous, _position);
                OnPropertyChanged(nameof(Position));
                OnPropertyChanged(nameof(StopLossBaseline));
            }
        }

        public int StartPosition
        {
            get => _startPosition;
            set
            {
                if (_startPosition == value) return;
                _startPosition = value;
                OnPropertyChanged(nameof(StartPosition));
            }
        }

        public decimal AvgEntryPrice
        {
            get => _avgEntryPrice;
            set
            {
                if (_avgEntryPrice == value) return;
                _avgEntryPrice = value;
                OnPropertyChanged(nameof(AvgEntryPrice));
            }
        }

        public decimal FloatProfit
        {
            get => _floatProfit;
            set
            {
                if (_floatProfit == value) return;
                _floatProfit = value;
                OnPropertyChanged(nameof(FloatProfit));
                OnPropertyChanged(nameof(TotalProfit));
                TrackWorstTotalProfit();
            }
        }

        public decimal RealizedProfit
        {
            get => _realizedProfit;
            set
            {
                if (_realizedProfit == value) return;
                _realizedProfit = value;
                OnPropertyChanged(nameof(RealizedProfit));
                OnPropertyChanged(nameof(TotalProfit));
                TrackWorstTotalProfit();
            }
        }

        public int TradeCount
        {
            get => _tradeCount;
            set
            {
                if (_tradeCount == value) return;
                _tradeCount = value;
                OnPropertyChanged(nameof(TradeCount));
            }
        }

        public decimal? StopLossBaseline
        {
            get
            {
                if (!_hasEntryRangeBoundSnapshot)
                {
                    return null;
                }

                var position = PositionManager.TotalKou;
                if (position > 0)
                {
                    return _entryRangeBoundV > 0m ? _entryRangeBoundV : null;
                }

                if (position < 0)
                {
                    return _entryRangeBoundA > 0m ? _entryRangeBoundA : null;
                }

                return null;
            }
        }

        public int ReverseCount
        {
            get => _reverseCount;
            set
            {
                if (_reverseCount == value) return;
                _reverseCount = value;
                OnPropertyChanged(nameof(ReverseCount));
            }
        }

        public decimal TotalProfit => FloatProfit + RealizedProfit;
        public decimal MaxTotalLoss => _worstTotalProfit < 0m ? _worstTotalProfit : 0m;
        internal decimal WorstTotalProfit => _worstTotalProfit;

        public void Start(bool isBuy, string reason)
        {
            var managerTime = Manager?.CurrentTime;
            StartTime = managerTime.HasValue && managerTime.Value.Year >= 2000
                ? managerTime.Value
                : DateTime.Now;
            StartPosition = isBuy ? 1 : -1;
            LastBacktestUiRefreshBucket = null;
            ReverseCount = 0;
            _minTotalProfitSeen = 0m;
            _worstTotalProfit = 0m;
            _minTotalProfitForRiseExit = decimal.MaxValue;
            _riseFromDrawdownArmed = false;
            _coverLossArmed = false;
            _totalProfitDropAfterTriggerArmed = false;
            _profitMaExitArmed = false;
            _lossMaExitArmed = false;
            _hasLastPriceForSharedMaCross = false;
            _lastPriceForSharedMaCross = 0m;
            _hasEntryRangeBoundSnapshot = false;
            _entryRangeBoundA = 0m;
            _entryRangeBoundV = 0m;
            IsFailed = false;
            OnPropertyChanged(nameof(MaxTotalLoss));
            SetFinished(false);
            var qty = Manager?.RuleSet.OrderSize ?? 1;
            Trade(isBuy, qty, reason);
        }

        public void CloseAll()
        {
            ForcePosition(0, "使用者手動平倉", isFinishClose: true);
            SetFinished(true);
        }

        public void CloseAllAtPrice(decimal fillPrice, string reason = "使用者全部平倉")
        {
            if (PositionManager.TotalKou != 0)
            {
                // Netting close path: broker order has already been sent once at manager level.
                // Here we only settle each session's bookkeeping at the shared fill price.
                SettlePositionAtPrice(0, fillPrice, reason, isFinishClose: true);
            }
            SetFinished(true);
        }

        public bool ReverseNow()
        {
            if (IsFinished)
            {
                return false;
            }

            var currentPosition = PositionManager.TotalKou;
            if (currentPosition == 0)
            {
                return false;
            }

            var targetPosition = -currentPosition;
            var reason = targetPosition > 0 ? "使用者立即反手做多" : "使用者立即反手做空";
            ForcePosition(targetPosition, reason);
            return PositionManager.TotalKou == targetPosition;
        }

        public bool TrySetStopLossBaseline(decimal baseline, out string? error)
        {
            error = null;

            if (IsFinished || PositionManager.TotalKou == 0)
            {
                error = "目前任務沒有進行中的部位。";
                return false;
            }

            var manager = Manager;
            if (manager == null)
            {
                error = "找不到任務管理器。";
                return false;
            }

            if (manager.RuleSet.StopLossMode != StopLossMode.Auto)
            {
                error = "目前停損模式不是自動判定。";
                return false;
            }

            if (baseline <= 0m)
            {
                error = "停損基準線必須大於 0。";
                return false;
            }

            _hasEntryRangeBoundSnapshot = true;
            if (PositionManager.TotalKou > 0)
            {
                _entryRangeBoundV = baseline;
            }
            else
            {
                _entryRangeBoundA = baseline;
            }

            OnPropertyChanged(nameof(StopLossBaseline));
            PutStr($"停損基準線手動調整為{baseline:0.##}");
            return true;
        }

        internal void RestoreFinished(bool isFinished)
        {
            SetFinished(isFinished);
        }

        internal void RestoreFailed(bool isFailed)
        {
            IsFailed = isFailed;
        }

        internal void MarkAsFailed(string message)
        {
            if (IsFailed)
            {
                return;
            }

            IsFailed = true;
            Manager?.RaiseSessionTradeFailed(this, message ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(message))
            {
                PutStrColored(message, ZenPlatform.LogText.LogTxtColor.黃色);
            }
            SetFinished(true);
        }

        internal void GetAutoStopSnapshot(out bool hasSnapshot, out decimal a, out decimal v)
        {
            hasSnapshot = _hasEntryRangeBoundSnapshot;
            a = _entryRangeBoundA;
            v = _entryRangeBoundV;
        }

        internal void RestoreAutoStopSnapshot(bool hasSnapshot, decimal a, decimal v)
        {
            _hasEntryRangeBoundSnapshot = hasSnapshot;
            _entryRangeBoundA = a;
            _entryRangeBoundV = v;
            OnPropertyChanged(nameof(StopLossBaseline));
        }

        internal void RestoreWorstTotalProfit(decimal worstTotalProfit)
        {
            if (worstTotalProfit >= _worstTotalProfit)
            {
                return;
            }

            _worstTotalProfit = worstTotalProfit;
            OnPropertyChanged(nameof(MaxTotalLoss));
        }

        internal void ApplyRolloverSpread(decimal spread)
        {
            if (PositionManager.TotalKou == 0 || spread == 0m)
            {
                return;
            }

            var snapshot = PositionManager.ToSnapshot();
            snapshot.AvgEntryPrice += spread;
            PositionManager.FromSnapshot(snapshot);

            var manager = Manager;
            if (manager?.Bid.HasValue == true && manager.Ask.HasValue)
            {
                PositionManager.OnTick(manager.Bid.Value, manager.Ask.Value);
            }

            Position = PositionManager.TotalKou;
            AvgEntryPrice = PositionManager.AvgEntryPrice;
            FloatProfit = PositionManager.FloatProfit;
            RealizedProfit = PositionManager.PingProfit;
        }

        public override void OnTick()
        {
            base.OnTick();
            if (IsFinished)
            {
                return;
            }

            var manager = Manager;
            if (manager == null)
            {
                return;
            }

            if (TryExecuteExitRules(_commonExitRules, manager, ExitRuleContext.Tick()))
            {
                return;
            }

            if (PositionManager.TotalKou == 0)
            {
                return;
            }

            _ = TryExecuteExitRules(_tickExitRules, manager, ExitRuleContext.Tick());
        }

        public override void OnKBarCompleted(int period, KBar bar)
        {
            base.OnKBarCompleted(period, bar);
            if (IsFinished)
            {
                return;
            }

            var manager = Manager;
            if (manager == null)
            {
                return;
            }

            if (TryExecuteExitRules(_commonExitRules, manager, ExitRuleContext.KBarCompleted(period, bar)))
            {
                return;
            }

            if (PositionManager.TotalKou == 0)
            {
                return;
            }

        }

        private bool TryExecuteExitRules(IExitRule[] rules, ZenPlatform.SessionManager.SessionManager manager, ExitRuleContext context)
        {
            foreach (var rule in rules)
            {
                if (rule.TryExecute(this, manager, context))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool TryTakeProfitExit() => CheckTakeProfit();
        internal bool TryStopLossExit(ZenPlatform.SessionManager.SessionManager manager) => CheckStopLoss();
        internal bool TryAbsoluteStopLossExit() => CheckAbsoluteStopLoss();
        internal bool TryLossRetraceMaExit() => CheckLossRetraceMaExit();
        internal bool TryProfitRetraceMaExit() => CheckProfitRetraceMaExit();
        internal bool TryCoverLossRecoveryExit() => CheckCoverLossRecoveryExit();
        internal bool TryRiseFromWorstExit() => CheckRiseFromWorstExit();
        internal bool TryTotalProfitDropAfterTriggerExit() => CheckTotalProfitDropAfterTriggerExit();
        internal bool TryKBarLossRetraceMaExit(KBar bar, ZenPlatform.SessionManager.SessionManager manager) => CheckKBarLossRetraceMaExit(bar, manager);
        internal bool TryKBarProfitRetraceMaExit(KBar bar, ZenPlatform.SessionManager.SessionManager manager) => CheckKBarProfitRetraceMaExit(bar, manager);
        internal bool TryKBarRiskExit(KBar bar, ZenPlatform.SessionManager.SessionManager manager)
        {
            var beforePosition = PositionManager.TotalKou;
            var beforeFinished = IsFinished;
            CheckKBarRiskExit(bar);
            return IsFinished != beforeFinished || PositionManager.TotalKou != beforePosition;
        }

        private bool CheckTakeProfit()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            var totalTakeProfit = ruleSet.TakeProfitPoints;
            var floatTakeProfit = ruleSet.AutoTakeProfitPoints;
            if (totalTakeProfit <= 0 && floatTakeProfit <= 0)
            {
                return false;
            }

            var totalProfit = PositionManager.FloatProfit + PositionManager.PingProfit;
            if (totalTakeProfit > 0 && totalProfit >= totalTakeProfit)
            {
                ForcePosition(0, "停利點到平倉", isFinishClose: true);
                SetFinished(true);
                return true;
            }

            if (floatTakeProfit > 0 && PositionManager.FloatProfit >= floatTakeProfit)
            {
                ForcePosition(0, "固定浮動損益停利", isFinishClose: true);
                SetFinished(true);
                return true;
            }

            return false;
        }

        internal bool TryCloseBeforeSessionEndExit(ZenPlatform.SessionManager.SessionManager manager)
        {
            if (PositionManager.TotalKou == 0)
            {
                return false;
            }

            var now = manager.CurrentTime;
            if (now.Year < 2000)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (TradingTimeService.ShouldCloseBeforeDaySessionEnd(now, ruleSet))
            {
                ForcePosition(0, "早盤收盤前平倉", isFinishClose: true);
                SetFinished(true);
                return true;
            }

            if (TradingTimeService.ShouldCloseBeforeNightSessionEnd(now, ruleSet))
            {
                ForcePosition(0, "夜盤收盤前平倉", isFinishClose: true);
                SetFinished(true);
                return true;
            }

            return false;
        }

        internal bool TryCloseBeforeLongHolidayExit(ZenPlatform.SessionManager.SessionManager manager)
        {
            if (PositionManager.TotalKou == 0)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (!ruleSet.CloseBeforeLongHoliday)
            {
                return false;
            }

            var now = manager.CurrentTime;
            if (now.Year < 2000)
            {
                return false;
            }

            if (!TradingTimeService.ShouldCloseBeforeLongHoliday(now, ruleSet, minClosedDays: 2))
            {
                return false;
            }

            ForcePosition(0, "連續假日前平倉", isFinishClose: true);
            SetFinished(true);
            return true;
        }

        private bool CheckStopLoss()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            if (manager.RuleSet.StopLossMode == StopLossMode.Auto)
            {
                return CheckAutoStopLoss(manager);
            }

            var stopLoss = manager.RuleSet.StopLossPoints;
            if (stopLoss <= 0)
            {
                return false;
            }

            if (PositionManager.FloatProfit <= -stopLoss)
            {
                const string reason = "停損點到平倉";
                if (!TryReverseAfterStopLoss(reason))
                {
                    ForcePosition(0, reason, isFinishClose: true);
                    SetFinished(true);
                }
                return true;
            }

            return false;
        }

        private bool CheckAbsoluteStopLoss()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (!ruleSet.EnableAbsoluteStopLoss || ruleSet.AbsoluteStopLossPoints <= 0)
            {
                return false;
            }

            var threshold = -Math.Abs((decimal)ruleSet.AbsoluteStopLossPoints);
            var totalProfit = PositionManager.FloatProfit + PositionManager.PingProfit;
            if (totalProfit > threshold)
            {
                return false;
            }

            ForcePosition(0, "已達絕對停損", isFinishClose: true);
            SetFinished(true);
            return true;
        }

        private void CheckKBarRiskExit(KBar bar)
        {
            var manager = Manager;
            if (manager == null)
            {
                return;
            }

            if (CheckKBarAbsoluteStopLoss(bar, manager))
            {
                return;
            }

            if (manager.RuleSet.StopLossMode == StopLossMode.Auto)
            {
                return;
            }

            var ruleSet = manager.RuleSet;
            var totalTakeProfit = ruleSet.TakeProfitPoints;
            var floatTakeProfit = ruleSet.AutoTakeProfitPoints;
            var stopLoss = manager.RuleSet.StopLossPoints;
            var riseExitEnabled =
                ruleSet.ExitOnTotalProfitRise &&
                ruleSet.ExitOnTotalProfitRiseArmBelowPoints > 0 &&
                ruleSet.ExitOnTotalProfitRisePoints > 0;
            if (!riseExitEnabled)
            {
                _riseFromDrawdownArmed = false;
                _minTotalProfitForRiseExit = decimal.MaxValue;
            }
            var totalProfitDropEnabled =
                ruleSet.ExitOnTotalProfitDropAfterTrigger &&
                ruleSet.ExitOnTotalProfitDropTriggerPoints > 0 &&
                ruleSet.ExitOnTotalProfitDropExitPoints >= 0 &&
                ruleSet.ExitOnTotalProfitDropExitPoints < ruleSet.ExitOnTotalProfitDropTriggerPoints;
            if (totalTakeProfit <= 0 && floatTakeProfit <= 0 && stopLoss <= 0 && !riseExitEnabled && !totalProfitDropEnabled)
            {
                return;
            }

            var entry = PositionManager.AvgEntryPrice;
            var position = PositionManager.TotalKou;
            var requiredFloatForTotalTp = totalTakeProfit - PositionManager.PingProfit;
            if (position > 0)
            {
                // 任務總停利點數：已平倉損益 + 浮動損益 >= totalTakeProfit
                var totalTpPrice = requiredFloatForTotalTp <= 0m ? bar.Close : entry + requiredFloatForTotalTp;
                var floatTpPrice = entry + floatTakeProfit;
                var slPrice = entry - stopLoss;
                var coverLossArmThreshold = -Math.Abs((decimal)ruleSet.CoverLossTriggerPoints);
                var worstTotalInBar = PositionManager.PingProfit + (bar.Low - entry);
                var bestTotalInBar = PositionManager.PingProfit + (bar.High - entry);
                UpdateCoverLossArmed(worstTotalInBar, coverLossArmThreshold, ruleSet.CoverLossBeforeTakeProfit);
                var requiredFloatForCoverLossExit = CoverLossExitTarget - PositionManager.PingProfit;
                var coverLossExitPrice = requiredFloatForCoverLossExit <= 0m ? bar.Close : entry + requiredFloatForCoverLossExit;
                var hitCoverLossExit = _coverLossArmed && ruleSet.CoverLossBeforeTakeProfit &&
                                       (requiredFloatForCoverLossExit <= 0m || bar.High >= coverLossExitPrice);
                if (totalProfitDropEnabled && !_totalProfitDropAfterTriggerArmed && bestTotalInBar > ruleSet.ExitOnTotalProfitDropTriggerPoints)
                {
                    _totalProfitDropAfterTriggerArmed = true;
                    PutStr($"任務總損益超過{ruleSet.ExitOnTotalProfitDropTriggerPoints}點，若低於{ruleSet.ExitOnTotalProfitDropExitPoints}點將平倉...");
                }
                var requiredFloatForDropExit = ruleSet.ExitOnTotalProfitDropExitPoints - PositionManager.PingProfit;
                var dropExitPrice = requiredFloatForDropExit <= 0m ? bar.Close : entry + requiredFloatForDropExit;
                var hitTotalProfitDropExit = totalProfitDropEnabled && _totalProfitDropAfterTriggerArmed &&
                                             worstTotalInBar < ruleSet.ExitOnTotalProfitDropExitPoints;
                var riseArmThreshold = -Math.Abs((decimal)ruleSet.ExitOnTotalProfitRiseArmBelowPoints);
                if (riseExitEnabled && !_riseFromDrawdownArmed && worstTotalInBar <= riseArmThreshold)
                {
                    _riseFromDrawdownArmed = true;
                    _minTotalProfitForRiseExit = worstTotalInBar;
                    PutStr($"任務總損益低於-{ruleSet.ExitOnTotalProfitRiseArmBelowPoints}點，若拉回{ruleSet.ExitOnTotalProfitRisePoints}點將平倉...");
                }
                if (riseExitEnabled && _riseFromDrawdownArmed && worstTotalInBar < _minTotalProfitForRiseExit)
                {
                    _minTotalProfitForRiseExit = worstTotalInBar;
                }
                var requiredTotalForRiseExit = _minTotalProfitForRiseExit + ruleSet.ExitOnTotalProfitRisePoints;
                var requiredFloatForRiseExit = requiredTotalForRiseExit - PositionManager.PingProfit;
                var riseExitPrice = requiredFloatForRiseExit <= 0m ? bar.Close : entry + requiredFloatForRiseExit;
                var hitRiseExit = riseExitEnabled && _riseFromDrawdownArmed &&
                                  (requiredFloatForRiseExit <= 0m || bar.High >= riseExitPrice);
                var hitTotalTp = totalTakeProfit > 0 && (requiredFloatForTotalTp <= 0m || bar.High >= totalTpPrice);
                var hitFloatTp = floatTakeProfit > 0 && bar.High >= floatTpPrice;
                var hitTp = hitTotalTp || hitFloatTp;
                var hitSl = stopLoss > 0 && bar.Low <= slPrice;

                // Conservative rule for same bar dual hit: stop-loss first.
                if (hitSl)
                {
                    const string reason = "停損點到平倉";
                    if (!TryReverseAfterStopLoss(reason))
                    {
                        ForcePosition(0, reason, isFinishClose: true);
                        SetFinished(true);
                    }
                    return;
                }

                if (hitCoverLossExit)
                {
                    ForcePosition(0, "獲利補足平倉", isFinishClose: true);
                    SetFinished(true);
                    return;
                }

                if (hitRiseExit)
                {
                    ForcePosition(0, BuildRiseExitReason(ruleSet.ExitOnTotalProfitRiseArmBelowPoints, ruleSet.ExitOnTotalProfitRisePoints, _minTotalProfitForRiseExit), isFinishClose: true);
                    SetFinished(true);
                    return;
                }

                if (hitTotalProfitDropExit)
                {
                    ForcePosition(0, $"任務總損益低於{ruleSet.ExitOnTotalProfitDropExitPoints}點平倉", isFinishClose: true);
                    SetFinished(true);
                    return;
                }

                if (hitTp)
                {
                    var tpPrice = hitTotalTp && hitFloatTp ? Math.Min(totalTpPrice, floatTpPrice)
                        : hitTotalTp ? totalTpPrice
                        : floatTpPrice;
                    var reason = hitTotalTp && (!hitFloatTp || totalTpPrice <= floatTpPrice)
                        ? "停利點到平倉"
                        : "固定浮動損益停利";
                    ForcePosition(0, reason, isFinishClose: true);
                    SetFinished(true);
                }
                return;
            }

            if (position < 0)
            {
                // 任務總停利點數：已平倉損益 + 浮動損益 >= totalTakeProfit
                var totalTpPrice = requiredFloatForTotalTp <= 0m ? bar.Close : entry - requiredFloatForTotalTp;
                var floatTpPrice = entry - floatTakeProfit;
                var slPrice = entry + stopLoss;
                var coverLossArmThreshold = -Math.Abs((decimal)ruleSet.CoverLossTriggerPoints);
                var worstTotalInBar = PositionManager.PingProfit + (entry - bar.High);
                var bestTotalInBar = PositionManager.PingProfit + (entry - bar.Low);
                UpdateCoverLossArmed(worstTotalInBar, coverLossArmThreshold, ruleSet.CoverLossBeforeTakeProfit);
                var requiredFloatForCoverLossExit = CoverLossExitTarget - PositionManager.PingProfit;
                var coverLossExitPrice = requiredFloatForCoverLossExit <= 0m ? bar.Close : entry - requiredFloatForCoverLossExit;
                var hitCoverLossExit = _coverLossArmed && ruleSet.CoverLossBeforeTakeProfit &&
                                       (requiredFloatForCoverLossExit <= 0m || bar.Low <= coverLossExitPrice);
                if (totalProfitDropEnabled && !_totalProfitDropAfterTriggerArmed && bestTotalInBar > ruleSet.ExitOnTotalProfitDropTriggerPoints)
                {
                    _totalProfitDropAfterTriggerArmed = true;
                    PutStr($"任務總損益超過{ruleSet.ExitOnTotalProfitDropTriggerPoints}點，若低於{ruleSet.ExitOnTotalProfitDropExitPoints}點將平倉...");
                }
                var requiredFloatForDropExit = ruleSet.ExitOnTotalProfitDropExitPoints - PositionManager.PingProfit;
                var dropExitPrice = requiredFloatForDropExit <= 0m ? bar.Close : entry - requiredFloatForDropExit;
                var hitTotalProfitDropExit = totalProfitDropEnabled && _totalProfitDropAfterTriggerArmed &&
                                             worstTotalInBar < ruleSet.ExitOnTotalProfitDropExitPoints;
                var riseArmThreshold = -Math.Abs((decimal)ruleSet.ExitOnTotalProfitRiseArmBelowPoints);
                if (riseExitEnabled && !_riseFromDrawdownArmed && worstTotalInBar <= riseArmThreshold)
                {
                    _riseFromDrawdownArmed = true;
                    _minTotalProfitForRiseExit = worstTotalInBar;
                    PutStr($"任務總損益低於-{ruleSet.ExitOnTotalProfitRiseArmBelowPoints}點，若拉回{ruleSet.ExitOnTotalProfitRisePoints}點將平倉...");
                }
                if (riseExitEnabled && _riseFromDrawdownArmed && worstTotalInBar < _minTotalProfitForRiseExit)
                {
                    _minTotalProfitForRiseExit = worstTotalInBar;
                }
                var requiredTotalForRiseExit = _minTotalProfitForRiseExit + ruleSet.ExitOnTotalProfitRisePoints;
                var requiredFloatForRiseExit = requiredTotalForRiseExit - PositionManager.PingProfit;
                var riseExitPrice = requiredFloatForRiseExit <= 0m ? bar.Close : entry - requiredFloatForRiseExit;
                var hitRiseExit = riseExitEnabled && _riseFromDrawdownArmed &&
                                  (requiredFloatForRiseExit <= 0m || bar.Low <= riseExitPrice);
                var hitTotalTp = totalTakeProfit > 0 && (requiredFloatForTotalTp <= 0m || bar.Low <= totalTpPrice);
                var hitFloatTp = floatTakeProfit > 0 && bar.Low <= floatTpPrice;
                var hitTp = hitTotalTp || hitFloatTp;
                var hitSl = stopLoss > 0 && bar.High >= slPrice;

                // Conservative rule for same bar dual hit: stop-loss first.
                if (hitSl)
                {
                    const string reason = "停損點到平倉";
                    if (!TryReverseAfterStopLoss(reason))
                    {
                        ForcePosition(0, reason, isFinishClose: true);
                        SetFinished(true);
                    }
                    return;
                }

                if (hitCoverLossExit)
                {
                    ForcePosition(0, "獲利補足平倉", isFinishClose: true);
                    SetFinished(true);
                    return;
                }

                if (hitRiseExit)
                {
                    ForcePosition(0, BuildRiseExitReason(ruleSet.ExitOnTotalProfitRiseArmBelowPoints, ruleSet.ExitOnTotalProfitRisePoints, _minTotalProfitForRiseExit), isFinishClose: true);
                    SetFinished(true);
                    return;
                }

                if (hitTotalProfitDropExit)
                {
                    ForcePosition(0, $"任務總損益低於{ruleSet.ExitOnTotalProfitDropExitPoints}點平倉", isFinishClose: true);
                    SetFinished(true);
                    return;
                }

                if (hitTp)
                {
                    var tpPrice = hitTotalTp && hitFloatTp ? Math.Max(totalTpPrice, floatTpPrice)
                        : hitTotalTp ? totalTpPrice
                        : floatTpPrice;
                    var reason = hitTotalTp && (!hitFloatTp || totalTpPrice >= floatTpPrice)
                        ? "停利點到平倉"
                        : "固定浮動損益停利";
                    ForcePosition(0, reason, isFinishClose: true);
                    SetFinished(true);
                }
            }
        }

        private bool TryReverseAfterStopLoss(string stopLossReason, decimal? fillPrice = null)
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            if (!manager.RuleSet.ReverseAfterStopLoss)
            {
                return false;
            }

            var maxReverseCount = manager.RuleSet.MaxReverseCount;
            if (maxReverseCount <= 0)
            {
                return false;
            }

            if (ReverseCount >= maxReverseCount)
            {
                return false;
            }

            var currentPosition = PositionManager.TotalKou;
            if (currentPosition == 0)
            {
                return false;
            }

            var nextReverseCount = ReverseCount + 1;
            var targetPosition = -currentPosition;
            var directionText = targetPosition > 0 ? "多" : "空";
            var reasonPrefix = string.IsNullOrWhiteSpace(stopLossReason) ? "停損觸發" : stopLossReason;
            var reason = $"{reasonPrefix}，停損後反手做{directionText}({nextReverseCount}/{maxReverseCount})";

            if (fillPrice.HasValue)
            {
                ForcePositionAtPrice(targetPosition, fillPrice.Value, reason);
            }
            else
            {
                ForcePosition(targetPosition, reason);
            }

            if (PositionManager.TotalKou != targetPosition)
            {
                return false;
            }

            ReverseCount = nextReverseCount;
            return true;
        }

        private bool CheckAutoStopLoss(ZenPlatform.SessionManager.SessionManager manager)
        {
            // Auto stop-loss is evaluated on completed 5-minute KBar only.
            return false;
        }

        private bool CheckLossRetraceMaExit()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (!ruleSet.LossRetraceExitEnabled || ruleSet.LossRetraceTriggerPoints <= 0)
            {
                _lossMaExitArmed = false;
                return false;
            }

            var threshold = -Math.Abs((decimal)ruleSet.LossRetraceTriggerPoints);
            var totalProfit = PositionManager.FloatProfit + PositionManager.PingProfit;
            if (!_lossMaExitArmed && totalProfit <= threshold)
            {
                _lossMaExitArmed = true;
                PutStr($"損失超過{ruleSet.LossRetraceTriggerPoints}點，等待碰到均線平倉...");
            }

            if (!_lossMaExitArmed)
            {
                return false;
            }

            if (!TryGetSharedExitMa(out var ma))
            {
                return false;
            }

            var price = manager.CurPrice;
            if (!price.HasValue)
            {
                return false;
            }

            var hit = _hasLastPriceForSharedMaCross &&
                      HasCrossedPrice(_lastPriceForSharedMaCross, price.Value, ma);

            _lastPriceForSharedMaCross = price.Value;
            _hasLastPriceForSharedMaCross = true;
            if (!hit)
            {
                return false;
            }

            const string reason = "損失超過門檻碰均線平倉";
            if (!TryReverseAfterStopLoss(reason))
            {
                ForcePosition(0, reason, isFinishClose: true);
                SetFinished(true);
            }
            return true;
        }

        private bool CheckProfitRetraceMaExit()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (!ruleSet.ProfitRetraceExitEnabled || ruleSet.ProfitRetraceTriggerPoints <= 0)
            {
                _profitMaExitArmed = false;
                return false;
            }

            var totalProfit = PositionManager.FloatProfit + PositionManager.PingProfit;
            if (!_profitMaExitArmed && totalProfit >= ruleSet.ProfitRetraceTriggerPoints)
            {
                _profitMaExitArmed = true;
                PutStr($"獲利超過{ruleSet.ProfitRetraceTriggerPoints}點，等待碰到均線平倉...");
            }

            if (!_profitMaExitArmed)
            {
                return false;
            }

            if (!TryGetSharedExitMa(out var ma))
            {
                return false;
            }

            var price = manager.CurPrice;
            if (!price.HasValue)
            {
                return false;
            }

            var hit = _hasLastPriceForSharedMaCross &&
                      HasCrossedPrice(_lastPriceForSharedMaCross, price.Value, ma);

            _lastPriceForSharedMaCross = price.Value;
            _hasLastPriceForSharedMaCross = true;
            if (!hit)
            {
                return false;
            }

            ForcePosition(0, "獲利超過門檻碰均線平倉", isFinishClose: true);
            SetFinished(true);
            return true;
        }

        private bool CheckRiseFromWorstExit()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (!ruleSet.ExitOnTotalProfitRise ||
                ruleSet.ExitOnTotalProfitRiseArmBelowPoints <= 0 ||
                ruleSet.ExitOnTotalProfitRisePoints <= 0)
            {
                _riseFromDrawdownArmed = false;
                _minTotalProfitForRiseExit = decimal.MaxValue;
                return false;
            }

            var totalProfit = PositionManager.FloatProfit + PositionManager.PingProfit;
            var armThreshold = -Math.Abs((decimal)ruleSet.ExitOnTotalProfitRiseArmBelowPoints);
            if (!_riseFromDrawdownArmed && totalProfit <= armThreshold)
            {
                _riseFromDrawdownArmed = true;
                _minTotalProfitForRiseExit = totalProfit;
                PutStr($"任務總損益低於-{ruleSet.ExitOnTotalProfitRiseArmBelowPoints}點，若拉回{ruleSet.ExitOnTotalProfitRisePoints}點將平倉...");
                return false;
            }

            if (!_riseFromDrawdownArmed)
            {
                return false;
            }

            if (_minTotalProfitForRiseExit == decimal.MaxValue || totalProfit < _minTotalProfitForRiseExit)
            {
                _minTotalProfitForRiseExit = totalProfit;
            }

            var rise = totalProfit - _minTotalProfitForRiseExit;
            if (rise < ruleSet.ExitOnTotalProfitRisePoints)
            {
                return false;
            }

            ForcePosition(0, BuildRiseExitReason(ruleSet.ExitOnTotalProfitRiseArmBelowPoints, ruleSet.ExitOnTotalProfitRisePoints, _minTotalProfitForRiseExit), isFinishClose: true);
            SetFinished(true);
            return true;
        }

        private static string BuildRiseExitReason(int armBelowPoints, int risePoints, decimal minTotalProfit)
        {
            return $"任務總損益曾低於-{armBelowPoints}點，已拉回{risePoints}點(最低{minTotalProfit:0.##})";
        }

        private bool CheckTotalProfitDropAfterTriggerExit()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (!ruleSet.ExitOnTotalProfitDropAfterTrigger ||
                ruleSet.ExitOnTotalProfitDropTriggerPoints <= 0 ||
                ruleSet.ExitOnTotalProfitDropExitPoints < 0 ||
                ruleSet.ExitOnTotalProfitDropExitPoints >= ruleSet.ExitOnTotalProfitDropTriggerPoints)
            {
                _totalProfitDropAfterTriggerArmed = false;
                return false;
            }

            var totalProfit = PositionManager.FloatProfit + PositionManager.PingProfit;
            if (!_totalProfitDropAfterTriggerArmed && totalProfit > ruleSet.ExitOnTotalProfitDropTriggerPoints)
            {
                _totalProfitDropAfterTriggerArmed = true;
                PutStr($"任務總損益超過{ruleSet.ExitOnTotalProfitDropTriggerPoints}點，若低於{ruleSet.ExitOnTotalProfitDropExitPoints}點將平倉...");
                return false;
            }

            if (!_totalProfitDropAfterTriggerArmed || totalProfit >= ruleSet.ExitOnTotalProfitDropExitPoints)
            {
                return false;
            }

            ForcePosition(0, $"任務總損益低於{ruleSet.ExitOnTotalProfitDropExitPoints}點平倉", isFinishClose: true);
            SetFinished(true);
            return true;
        }

        private bool CheckCoverLossRecoveryExit()
        {
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            var ruleSet = manager.RuleSet;
            if (!ruleSet.CoverLossBeforeTakeProfit || ruleSet.CoverLossTriggerPoints <= 0)
            {
                return false;
            }

            var totalProfit = PositionManager.FloatProfit + PositionManager.PingProfit;
            var armThreshold = -Math.Abs((decimal)ruleSet.CoverLossTriggerPoints);
            UpdateCoverLossArmed(totalProfit, armThreshold, enabled: true);
            if (!_coverLossArmed)
            {
                return false;
            }

            if (totalProfit >= CoverLossExitTarget)
            {
                ForcePosition(0, "獲利補足平倉", isFinishClose: true);
                SetFinished(true);
                return true;
            }

            return false;
        }

        private void UpdateCoverLossArmed(decimal observedTotalProfit, decimal armThreshold, bool enabled)
        {
            if (!enabled)
            {
                _coverLossArmed = false;
                return;
            }

            if (observedTotalProfit < _minTotalProfitSeen)
            {
                _minTotalProfitSeen = observedTotalProfit;
            }

            if (!_coverLossArmed && _minTotalProfitSeen <= armThreshold)
            {
                _coverLossArmed = true;
                PutStr("損失已超過門檻，等待獲利補足後平倉...");
            }
        }

        private void TrackWorstTotalProfit()
        {
            var totalProfit = TotalProfit;
            if (totalProfit >= _worstTotalProfit)
            {
                return;
            }

            _worstTotalProfit = totalProfit;
            OnPropertyChanged(nameof(MaxTotalLoss));
        }

        internal bool TryAutoStopLossByFiveMinuteKBar(KBar bar, ZenPlatform.SessionManager.SessionManager manager)
        {
            if (!_hasEntryRangeBoundSnapshot)
            {
                return false;
            }

            if (PositionManager.FloatProfit >= AutoStopLossFloatProfitThreshold)
            {
                return false;
            }

            var position = PositionManager.TotalKou;
            if (position > 0)
            {
                if (_entryRangeBoundV > 0m && bar.High < _entryRangeBoundV)
                {
                    const string reason = "自動停損到平倉";
                    if (!TryReverseAfterStopLoss(reason))
                    {
                        ForcePosition(0, reason, isFinishClose: true);
                        SetFinished(true);
                    }
                    return true;
                }
                return false;
            }

            if (position < 0)
            {
                if (_entryRangeBoundA > 0m && bar.Low > _entryRangeBoundA)
                {
                    const string reason = "自動停損到平倉";
                    if (!TryReverseAfterStopLoss(reason))
                    {
                        ForcePosition(0, reason, isFinishClose: true);
                        SetFinished(true);
                    }
                    return true;
                }
            }

            return false;
        }

        private void HandlePositionChangedForAutoStop(int before, int after)
        {
            if (after == 0)
            {
                _hasEntryRangeBoundSnapshot = false;
                _entryRangeBoundA = 0m;
                _entryRangeBoundV = 0m;
                OnPropertyChanged(nameof(StopLossBaseline));
                return;
            }

            var entered = before == 0 && after != 0;
            var reversed = before != 0 && after != 0 && Math.Sign(before) != Math.Sign(after);
            if (!entered && !reversed)
            {
                return;
            }

            var manager = Manager;
            if (manager == null)
            {
                return;
            }

            decimal indicatorA = 0m;
            decimal indicatorV = 0m;
            var hasIndicatorRangeBound = false;
            lock (manager.IndicatorSync)
            {
                if (manager.Indicators != null && manager.Indicators.RANGEBOUND.HasValue)
                {
                    indicatorA = manager.Indicators.RANGEBOUND.A;
                    indicatorV = manager.Indicators.RANGEBOUND.V;
                    hasIndicatorRangeBound = true;
                }
            }

            _entryRangeBoundA = indicatorA;
            _entryRangeBoundV = indicatorV;

            if (after < 0)
            {
                var currentPrice = manager.CurPrice ?? PositionManager.AvgEntryPrice;
                if (_entryRangeBoundA <= 0m || _entryRangeBoundA < currentPrice)
                {
                    if (TryGetRecentFiveMinuteHigh(manager, AutoStopLossFallbackLookbackBars, out var fallbackA) &&
                        fallbackA > 0m)
                    {
                        _entryRangeBoundA = fallbackA;
                    }
                }

                // 空單停損基準線若仍低於現價，改為現價上方最近的 100 整點。
                if (_entryRangeBoundA > 0m && _entryRangeBoundA < currentPrice)
                {
                    _entryRangeBoundA = RoundUpToNextHundred(currentPrice);
                }
            }
            else if (after > 0)
            {
                var currentPrice = manager.CurPrice ?? PositionManager.AvgEntryPrice;
                if (_entryRangeBoundV <= 0m || _entryRangeBoundV > currentPrice)
                {
                    if (TryGetRecentFiveMinuteLow(manager, AutoStopLossFallbackLookbackBars, out var fallbackV) &&
                        fallbackV > 0m)
                    {
                        _entryRangeBoundV = fallbackV;
                    }
                }

                // 多單停損基準線若仍高於現價，改為現價下方最近的 100 整點。
                if (_entryRangeBoundV > currentPrice)
                {
                    _entryRangeBoundV = RoundDownToHundred(currentPrice);
                }
            }

            var baseline = after > 0 ? _entryRangeBoundV : _entryRangeBoundA;
            _hasEntryRangeBoundSnapshot = baseline > 0m || hasIndicatorRangeBound;
            OnPropertyChanged(nameof(StopLossBaseline));

            if (baseline > 0m)
            {
                PutStr($"停損基準線設定於{baseline:0.##}");
            }
        }

        private static decimal RoundUpToNextHundred(decimal price)
        {
            if (price <= 0m)
            {
                return 100m;
            }

            return decimal.Ceiling(price / 100m) * 100m;
        }

        private static decimal RoundDownToHundred(decimal price)
        {
            if (price <= 0m)
            {
                return 0m;
            }

            return decimal.Floor(price / 100m) * 100m;
        }

        private static bool TryGetRecentFiveMinuteHigh(ZenPlatform.SessionManager.SessionManager manager, int lookbackBars, out decimal high)
        {
            high = 0m;
            if (lookbackBars <= 0 || manager.FetchHistoryBars == null)
            {
                return false;
            }

            var bars = manager.FetchHistoryBars(AutoStopLossRulePeriodMinutes);
            if (bars == null || bars.Count == 0)
            {
                return false;
            }

            var count = 0;
            for (var i = bars.Count - 1; i >= 0 && count < lookbackBars; i--)
            {
                var bar = bars[i];
                if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
                {
                    continue;
                }

                if (count == 0 || bar.High > high)
                {
                    high = bar.High;
                }

                count++;
            }

            return count > 0;
        }

        private static bool TryGetRecentFiveMinuteLow(ZenPlatform.SessionManager.SessionManager manager, int lookbackBars, out decimal low)
        {
            low = 0m;
            if (lookbackBars <= 0 || manager.FetchHistoryBars == null)
            {
                return false;
            }

            var bars = manager.FetchHistoryBars(AutoStopLossRulePeriodMinutes);
            if (bars == null || bars.Count == 0)
            {
                return false;
            }

            var count = 0;
            for (var i = bars.Count - 1; i >= 0 && count < lookbackBars; i--)
            {
                var bar = bars[i];
                if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
                {
                    continue;
                }

                if (count == 0 || bar.Low < low)
                {
                    low = bar.Low;
                }

                count++;
            }

            return count > 0;
        }

        private bool CheckKBarLossRetraceMaExit(KBar bar, ZenPlatform.SessionManager.SessionManager manager)
        {
            var ruleSet = manager.RuleSet;
            if (!ruleSet.LossRetraceExitEnabled || ruleSet.LossRetraceTriggerPoints <= 0)
            {
                _lossMaExitArmed = false;
                return false;
            }

            if (!TryGetSharedExitMa(out var ma))
            {
                return false;
            }

            var totalLow = EstimateKBarTotalProfitLow(bar);
            var threshold = -Math.Abs((decimal)ruleSet.LossRetraceTriggerPoints);
            if (!_lossMaExitArmed && totalLow <= threshold)
            {
                _lossMaExitArmed = true;
                PutStr($"損失超過{ruleSet.LossRetraceTriggerPoints}點，等待碰到均線平倉...");
            }

            if (!_lossMaExitArmed)
            {
                return false;
            }

            if (bar.Low <= ma && bar.High >= ma)
            {
                const string reason = "損失超過門檻碰均線平倉";
                if (!TryReverseAfterStopLoss(reason))
                {
                    ForcePosition(0, reason, isFinishClose: true);
                    SetFinished(true);
                }
                return true;
            }

            return false;
        }

        private bool CheckKBarProfitRetraceMaExit(KBar bar, ZenPlatform.SessionManager.SessionManager manager)
        {
            var ruleSet = manager.RuleSet;
            if (!ruleSet.ProfitRetraceExitEnabled || ruleSet.ProfitRetraceTriggerPoints <= 0)
            {
                _profitMaExitArmed = false;
                return false;
            }

            if (!TryGetSharedExitMa(out var ma))
            {
                return false;
            }

            var totalHigh = EstimateKBarTotalProfitHigh(bar);
            if (!_profitMaExitArmed && totalHigh >= ruleSet.ProfitRetraceTriggerPoints)
            {
                _profitMaExitArmed = true;
                PutStr($"獲利超過{ruleSet.ProfitRetraceTriggerPoints}點，等待碰到均線平倉...");
            }

            if (!_profitMaExitArmed)
            {
                return false;
            }

            if (bar.Low <= ma && bar.High >= ma)
            {
                ForcePosition(0, "獲利超過門檻碰均線平倉", isFinishClose: true);
                SetFinished(true);
                return true;
            }

            return false;
        }

        private bool TryGetSharedExitMa(out decimal ma)
        {
            ma = 0m;
            var manager = Manager;
            if (manager == null)
            {
                return false;
            }

            lock (manager.IndicatorSync)
            {
                if (manager.Indicators == null || manager.Indicators.MA.Length < 2)
                {
                    return false;
                }

                var shared = manager.Indicators.MA[1];
                if (!shared.IsConfigured || !shared.HasValue)
                {
                    return false;
                }

                ma = shared.Value;
                return true;
            }
        }

        private bool HasCrossedPrice(decimal prev, decimal current, decimal ma)
        {
            return (prev < ma && current >= ma) || (prev > ma && current <= ma);
        }

        private decimal EstimateKBarTotalProfitLow(KBar bar)
        {
            var position = PositionManager.TotalKou;
            var entry = PositionManager.AvgEntryPrice;
            if (position > 0)
            {
                return PositionManager.PingProfit + (bar.Low - entry);
            }
            if (position < 0)
            {
                return PositionManager.PingProfit + (entry - bar.High);
            }
            return PositionManager.PingProfit;
        }

        private decimal EstimateKBarTotalProfitHigh(KBar bar)
        {
            var position = PositionManager.TotalKou;
            var entry = PositionManager.AvgEntryPrice;
            if (position > 0)
            {
                return PositionManager.PingProfit + (bar.High - entry);
            }
            if (position < 0)
            {
                return PositionManager.PingProfit + (entry - bar.Low);
            }
            return PositionManager.PingProfit;
        }

        private bool CheckKBarAbsoluteStopLoss(KBar bar, ZenPlatform.SessionManager.SessionManager manager)
        {
            var ruleSet = manager.RuleSet;
            if (!ruleSet.EnableAbsoluteStopLoss || ruleSet.AbsoluteStopLossPoints <= 0)
            {
                return false;
            }

            var entry = PositionManager.AvgEntryPrice;
            var position = PositionManager.TotalKou;
            if (position == 0)
            {
                return false;
            }

            var threshold = -Math.Abs((decimal)ruleSet.AbsoluteStopLossPoints);
            var requiredFloatLoss = threshold - PositionManager.PingProfit;
            if (requiredFloatLoss >= 0m)
            {
                return false;
            }

            if (position > 0)
            {
                var worstTotalInBar = PositionManager.PingProfit + (bar.Low - entry);
                if (worstTotalInBar > threshold)
                {
                    return false;
                }

                ForcePosition(0, "已達絕對停損", isFinishClose: true);
                SetFinished(true);
                return true;
            }

            var worstTotalForShort = PositionManager.PingProfit + (entry - bar.High);
            if (worstTotalForShort > threshold)
            {
                return false;
            }

            ForcePosition(0, "已達絕對停損", isFinishClose: true);
            SetFinished(true);
            return true;
        }

        public override void OnMinute()
        {
            // Default session has no per-minute behavior.
        }


        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            // Ensure PropertyChanged is invoked on the UI thread
            if (System.Windows.Application.Current != null && System.Windows.Application.Current.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
                });
            }
            else
            {
                // Fallback for cases where Dispatcher is not available (e.g., unit tests or shutdown)
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
