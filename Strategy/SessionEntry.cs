using System;
using ZenPlatform.Debug;
using ZenPlatform.Strategy.EntryTriggers;

namespace ZenPlatform.Strategy
{
    public enum EntryTrendSide
    {
        無 = 0,
        多 = 1,
        空 = -1
    }

    public sealed class SessionEntry : RuleBase
    {
        private const string TrendDebugSession = "EntryTrend";
        private const int BbiBollPeriodMinutes = 10;
        private const int MacdTriggerPeriodMinutes = 5;
        private readonly ZenPlatform.SessionManager.SessionManager _manager;
        private readonly EntryTriggerRouter _triggerRouter;
        private bool _hasLoggedInitialSide;
        private int _debugBarCounter;
        private EntryTriggerSignal? _pendingEntrySignal;
        private EntryTriggerSignal? _pendingTickEntrySignal;

        public SessionEntry(ZenPlatform.SessionManager.SessionManager manager)
        {
            _manager = manager ?? throw new System.ArgumentNullException(nameof(manager));
            _triggerRouter = new EntryTriggerRouter(manager.RuntimeState.EntryTrigger, TraceM2Diagnostic);
            Manager = manager;
        }

        private void TraceM2Diagnostic(string text)
        {
            var recorder = _manager.BacktestRecorder;
            if (recorder == null || !recorder.EnableDevDiagnostics)
            {
                return;
            }

            var now = _manager.CurrentTime.Year >= 2000 ? _manager.CurrentTime : DateTime.Now;
            recorder.AppendTriggerDiagnostic(
                now,
                "M2",
                "Tick",
                (int)_manager.CurrnetSide,
                0,
                "trace",
                text);
        }

        public override void OnTick()
        {
            if (!CanEvaluateEntrySignalsNow())
            {
                _pendingTickEntrySignal = null;
                return;
            }

            var signal = CheckCreateNewSessionOnTick();
            if (signal.IsBuy != 0)
            {
                _pendingTickEntrySignal = signal;
            }
        }

        public override void OnMinute()
        {
        }

        public override void OnKBarCompleted(int period, KBar bar)
        {
            if (period == BbiBollPeriodMinutes)
            {
                FeedBbiBollFromBar(bar);
            }

            if (period == MacdTriggerPeriodMinutes)
            {
                FeedMacdTriggerFromBar(bar);
            }

            if (period == _manager.RuleSet.KbarPeriod)
            {
                FeedIndicatorsFromBar(bar);
            }

            if (!CanEvaluateEntrySignalsNow())
            {
                _pendingEntrySignal = null;
                return;
            }

            var signal = CheckCreateNewSession(period, bar);
            if (signal.IsBuy != 0)
            {
                _pendingEntrySignal = signal;
            }
        }

        private EntryTriggerSignal CheckCreateNewSession(int period, KBar bar)
        {
            var eval = BuildEntryEvaluation(period, bar);
            if (eval.TrendMode == TrendMode.None)
            {
                return _triggerRouter.OnKBarCompleted(
                    eval.TrendMode,
                    new EntryKBarTriggerContext(
                        eval.Period,
                        eval.HasBbiBoll,
                        eval.BbiUp,
                        eval.BbiDown,
                        eval.IsFullyAboveUp,
                        eval.IsFullyBelowDown,
                        eval.MacdTriggerState,
                        eval.HasCurrentPrice,
                        eval.CurrentPrice,
                        eval.HasA,
                        eval.A,
                        eval.HasV,
                        eval.V));
            }

            SelectSignalForTrendMode(eval, bar);
            var signal = _triggerRouter.OnKBarCompleted(
                eval.TrendMode,
                new EntryKBarTriggerContext(
                    eval.Period,
                    eval.HasBbiBoll,
                    eval.BbiUp,
                    eval.BbiDown,
                    eval.IsFullyAboveUp,
                    eval.IsFullyBelowDown,
                    eval.MacdTriggerState,
                    eval.HasCurrentPrice,
                    eval.CurrentPrice,
                    eval.HasA,
                    eval.A,
                    eval.HasV,
                    eval.V));
            return FilterByCurrentTrend(eval.TrendMode, signal);
        }

        private EntryEvaluation BuildEntryEvaluation(int period, KBar bar)
        {
            var trendMode = _manager.RuleSet.TrendMode;
            var triggerPeriod = GetSignalTriggerPeriod(trendMode);
            var nextSide = GetCurrentTrendSide(bar);
            var maText = "N/A";
            var hasBbiBoll = false;
            var bbiUp = 0m;
            var bbiDn = 0m;
            var macdTriggerState = 0;
            var hasA = false;
            var hasV = false;
            var a = 0m;
            var v = 0m;
            var hasCurrentPrice = _manager.CurPrice.HasValue;
            var currentPrice = _manager.CurPrice ?? 0m;

            lock (_manager.IndicatorSync)
            {
                var indicators = _manager.Indicators;
                if (indicators != null)
                {
                    if (indicators.MA.Length > 0 && indicators.MA[0].IsConfigured && indicators.MA[0].HasValue)
                    {
                        maText = indicators.MA[0].Value.ToString("0.##");
                    }

                    if (indicators.BBIBOLL.IsConfigured && indicators.BBIBOLL.HasValue)
                    {
                        hasBbiBoll = true;
                        bbiUp = indicators.BBIBOLL.Up;
                        bbiDn = indicators.BBIBOLL.Dn;
                    }

                    if (indicators.MACDTRIGGER.HasValue)
                    {
                        macdTriggerState = indicators.MACDTRIGGER.TriggerState;
                    }

                    if (indicators.RANGEBOUND.HasValue)
                    {
                        a = indicators.RANGEBOUND.A;
                        v = indicators.RANGEBOUND.V;
                        hasA = a > 0m;
                        hasV = v > 0m;
                    }
                }
            }

            return new EntryEvaluation(
                period,
                triggerPeriod,
                trendMode,
                nextSide,
                maText,
                hasBbiBoll,
                bbiUp,
                bbiDn,
                macdTriggerState,
                bar.Low > bbiUp,
                bar.High < bbiDn,
                hasCurrentPrice,
                currentPrice,
                hasA,
                a,
                hasV,
                v);
        }

        private EntryTriggerSignal SelectSignalForTrendMode(EntryEvaluation eval, KBar bar)
        {
            if (eval.Period != eval.TriggerPeriod)
            {
                return EntryTriggerSignal.None;
            }

            _debugBarCounter++;
            if (_debugBarCounter % 100 == 0)
            {
                DebugBus.Send(
                    $"count={_debugBarCounter} period={eval.Period} H={bar.High:0.##} L={bar.Low:0.##} MA={eval.MaText} side={eval.NextSide}",
                    TrendDebugSession,
                    DebugMessageType.Trace);
            }

            if (!_hasLoggedInitialSide)
            {
                _hasLoggedInitialSide = true;
                _manager.SetCurrentTrendSide(eval.NextSide);
                return EntryTriggerSignal.None;
            }

            if (_manager.CurrnetSide == eval.NextSide)
            {
                return EntryTriggerSignal.None;
            }

            var previousSide = _manager.CurrnetSide;
            _manager.SetCurrentTrendSide(eval.NextSide);
            if (previousSide != EntryTrendSide.無 &&
                eval.NextSide != EntryTrendSide.無)
            {
                var modeText = eval.TrendMode switch
                {
                    TrendMode.Auto => "自動偵測",
                    TrendMode.MovingAverage => "均線判定",
                    TrendMode.Force => "強制判定",
                    _ => "方向判定"
                };
                AddLogSafe(eval.NextSide == EntryTrendSide.多
                    ? $"目前為多方趨勢({modeText})"
                    : $"目前為空方趨勢({modeText})");
            }
            return EntryTriggerSignal.None;
        }

        private void FeedIndicatorsFromBar(KBar bar)
        {
            if (_manager.Indicators == null)
            {
                _manager.RebuildIndicators(force: true);
            }

            lock (_manager.IndicatorSync)
            {
                if (_manager.Indicators == null)
                {
                    return;
                }

                var now = _manager.CurrentTime;
                var closeStamp = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Unspecified);
                if (_manager.LastIndicatorBarCloseTime.HasValue && closeStamp <= _manager.LastIndicatorBarCloseTime.Value)
                {
                    return;
                }

                _manager.Indicators.Update(new ZenPlatform.Indicators.KBar(
                    _manager.CurrentTime,
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume));
                _manager.LastIndicatorBarCloseTime = closeStamp;

                var recorder = _manager.BacktestRecorder;
                if (recorder != null &&
                    recorder.EnableDevDiagnostics &&
                    _manager.Indicators.MA.Length > 1 &&
                    _manager.Indicators.MA[1].IsConfigured &&
                    _manager.Indicators.MA[1].HasValue)
                {
                    var logTime = _manager.CurrentTime.Year >= 2000 ? _manager.CurrentTime : DateTime.Now;
                    recorder.AppendIndicatorValue(logTime, _manager.RuleSet.KbarPeriod, "EXIT_MA_SHARED", _manager.Indicators.MA[1].Value);
                }
            }
        }

        private void FeedMacdTriggerFromBar(KBar bar)
        {
            if (_manager.Indicators == null)
            {
                _manager.RebuildIndicators(force: true);
            }

            lock (_manager.IndicatorSync)
            {
                if (_manager.Indicators == null)
                {
                    return;
                }

                _manager.Indicators.UpdateMacdTrigger(new ZenPlatform.Indicators.KBar(
                    _manager.CurrentTime,
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume));
                _manager.Indicators.UpdateRangeBound(new ZenPlatform.Indicators.KBar(
                    _manager.CurrentTime,
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume));

                var recorder = _manager.BacktestRecorder;
                if (recorder != null && recorder.EnableDevDiagnostics && _manager.Indicators.RANGEBOUND.HasValue)
                {
                    var now = _manager.CurrentTime.Year >= 2000 ? _manager.CurrentTime : DateTime.Now;
                    recorder.AppendIndicatorValue(now, MacdTriggerPeriodMinutes, "RANGEBOUND_A", _manager.Indicators.RANGEBOUND.A);
                    recorder.AppendIndicatorValue(now, MacdTriggerPeriodMinutes, "RANGEBOUND_V", _manager.Indicators.RANGEBOUND.V);
                }
            }
        }

        private void FeedBbiBollFromBar(KBar bar)
        {
            if (_manager.Indicators == null)
            {
                _manager.RebuildIndicators(force: true);
            }

            lock (_manager.IndicatorSync)
            {
                if (_manager.Indicators == null)
                {
                    return;
                }

                _manager.Indicators.UpdateBbiBoll(new ZenPlatform.Indicators.KBar(
                    _manager.CurrentTime,
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume));

                var recorder = _manager.BacktestRecorder;
                if (recorder != null && recorder.EnableDevDiagnostics && _manager.Indicators.BBIBOLL.HasValue)
                {
                    var now = _manager.CurrentTime.Year >= 2000 ? _manager.CurrentTime : DateTime.Now;
                    recorder.AppendIndicatorValue(now, BbiBollPeriodMinutes, "BBIBOLL_MID", _manager.Indicators.BBIBOLL.Mid);
                    recorder.AppendIndicatorValue(now, BbiBollPeriodMinutes, "BBIBOLL_UP", _manager.Indicators.BBIBOLL.Up);
                    recorder.AppendIndicatorValue(now, BbiBollPeriodMinutes, "BBIBOLL_DN", _manager.Indicators.BBIBOLL.Dn);
                }
            }
        }

        internal void ResetTrendState()
        {
            _hasLoggedInitialSide = false;
            _debugBarCounter = 0;
            _pendingEntrySignal = null;
            _pendingTickEntrySignal = null;
            _triggerRouter.Reset();
        }

        public EntryTrendSide GetCurrentTrendSide(KBar bar)
        {
            var ruleSet = _manager.RuleSet;
            switch (ruleSet.TrendMode)
            {
                case TrendMode.None:
                    return EntryTrendSide.無;
                case TrendMode.Force:
                    return ruleSet.TrendForceSide switch
                    {
                        TrendForceSide.多 => EntryTrendSide.多,
                        TrendForceSide.空 => EntryTrendSide.空,
                        _ => EntryTrendSide.無
                    };
                case TrendMode.MovingAverage:
                    return ResolveByMovingAverage(bar);
                case TrendMode.Auto:
                    return ResolveByBbiBoll(bar);
                default:
                    return EntryTrendSide.無;
            }
        }

        private EntryTrendSide ResolveByMovingAverage(KBar bar)
        {
            lock (_manager.IndicatorSync)
            {
                var indicators = _manager.Indicators;
                if (indicators == null || indicators.MA.Length == 0)
                {
                    return EntryTrendSide.無;
                }

                var ma = indicators.MA[0];
                if (!ma.IsConfigured || !ma.HasValue)
                {
                    return EntryTrendSide.無;
                }

                if (bar.Low > ma.Value)
                {
                    return EntryTrendSide.多;
                }

                if (bar.High < ma.Value)
                {
                    return EntryTrendSide.空;
                }
            }

            // Bar overlaps MA: keep previous side instead of returning neutral.
            return _manager.CurrnetSide;
        }

        private EntryTrendSide ResolveByBbiBoll(KBar bar)
        {
            lock (_manager.IndicatorSync)
            {
                var indicators = _manager.Indicators;
                if (indicators == null || !indicators.BBIBOLL.IsConfigured || !indicators.BBIBOLL.HasValue)
                {
                    return _manager.CurrnetSide;
                }

                if (bar.Low >= indicators.BBIBOLL.Up)
                {
                    return EntryTrendSide.多;
                }

                if (bar.High <= indicators.BBIBOLL.Dn)
                {
                    return EntryTrendSide.空;
                }
            }

            // Otherwise keep previous side.
            return _manager.CurrnetSide;
        }

        public bool CanCreateNewSession(bool isBuy, out string reason)
        {
            return base.CanCreateNewSession(
                GetCurrentTime(),
                isBuy,
                out reason,
                applySameDirectionBlock: false,
                applyCreateSessionWindow: false);
        }

        public EntryTriggerSignal GetEntrySignalOnKBarCompleted(int period, KBar bar)
        {
            if (_pendingEntrySignal == null)
            {
                return EntryTriggerSignal.None;
            }

            var signal = _pendingEntrySignal.Value;
            _pendingEntrySignal = null;
            return signal;
        }

        public EntryTriggerSignal GetEntrySignalOnTick()
        {
            if (_pendingTickEntrySignal == null)
            {
                return EntryTriggerSignal.None;
            }

            var signal = _pendingTickEntrySignal.Value;
            _pendingTickEntrySignal = null;
            return signal;
        }

        private int GetSignalTriggerPeriod(TrendMode trendMode)
        {
            return trendMode switch
            {
                TrendMode.Auto => 10,
                TrendMode.None => MacdTriggerPeriodMinutes,
                _ => _manager.RuleSet.KbarPeriod
            };
        }

        private EntryTriggerSignal CheckCreateNewSessionOnTick()
        {
            var trendMode = _manager.RuleSet.TrendMode;
            if (!_manager.CurPrice.HasValue)
            {
                return EntryTriggerSignal.None;
            }

            var hasBbiBoll = false;
            var mid = 0m;
            var up = 0m;
            var dn = 0m;
            lock (_manager.IndicatorSync)
            {
                var indicators = _manager.Indicators;
                if (indicators != null && indicators.BBIBOLL.IsConfigured && indicators.BBIBOLL.HasValue)
                {
                    hasBbiBoll = true;
                    mid = indicators.BBIBOLL.Mid;
                    up = indicators.BBIBOLL.Up;
                    dn = indicators.BBIBOLL.Dn;
                }
            }

            var signal = _triggerRouter.OnTick(
                trendMode,
                new EntryTickTriggerContext(
                    _manager.CurrnetSide,
                    _manager.CurPrice.Value,
                    hasBbiBoll,
                    mid,
                    up,
                    dn));
            signal = FilterByCurrentTrend(trendMode, signal);

            if (signal.IsBuy != 0)
            {
                var recorder = _manager.BacktestRecorder;
                if (recorder != null && recorder.EnableDevDiagnostics)
                {
                    var now = _manager.CurrentTime.Year >= 2000 ? _manager.CurrentTime : DateTime.Now;
                    var state = _manager.RuntimeState.EntryTrigger;
                    recorder.AppendTriggerDiagnostic(
                        now,
                        "M2",
                        "Tick",
                        (int)_manager.CurrnetSide,
                        signal.IsBuy,
                        "fire",
                        $"price={_manager.CurPrice.Value:0.##}, up={up:0.##}, mid={mid:0.##}, dn={dn:0.##}, waitLong={state.M2WaitLongAfterUpTouch}, waitShort={state.M2WaitShortAfterDownTouch}");
                }
            }

            return signal;
        }

        private EntryTriggerSignal FilterByCurrentTrend(TrendMode trendMode, EntryTriggerSignal signal)
        {
            if (signal.IsBuy == 0 || trendMode == TrendMode.None)
            {
                return signal;
            }

            var side = _manager.CurrnetSide;
            if (side == EntryTrendSide.無)
            {
                return EntryTriggerSignal.None;
            }

            var signalSide = signal.IsBuy > 0 ? EntryTrendSide.多 : EntryTrendSide.空;
            if (signalSide != side)
            {
                return EntryTriggerSignal.None;
            }

            return signal;
        }

        private readonly record struct EntryEvaluation(
            int Period,
            int TriggerPeriod,
            TrendMode TrendMode,
            EntryTrendSide NextSide,
            string MaText,
            bool HasBbiBoll,
            decimal BbiUp,
            decimal BbiDown,
            int MacdTriggerState,
            bool IsFullyAboveUp,
            bool IsFullyBelowDown,
            bool HasCurrentPrice,
            decimal CurrentPrice,
            bool HasA,
            decimal A,
            bool HasV,
            decimal V);

        public Session? RequestNewSession(bool isBuy, string reason, bool isAutoTrigger = false, string? triggerId = null)
        {
            if (!_manager.IsStrategyRunning)
            {
                return null;
            }

            if (!base.CanCreateNewSession(
                GetCurrentTime(),
                isBuy,
                out var blockReason,
                applySameDirectionBlock: isAutoTrigger,
                applyCreateSessionWindow: isAutoTrigger))
            {
                if (!isAutoTrigger)
                {
                    AddLogSafe(blockReason);
                }
                return null;
            }

            string createReason = reason;
            if (isAutoTrigger && !string.IsNullOrWhiteSpace(triggerId))
            {
                var triggerText = isBuy
                    ? $"偵測到多單進場條件({triggerId})"
                    : $"偵測到空單進場條件({triggerId})";
                AddLogSafe(triggerText);
                if (string.IsNullOrWhiteSpace(createReason))
                {
                    createReason = triggerText;
                }
            }

            return _manager.CreateNewSessionCore(isBuy, createReason, shouldRecordAllowedEntry: isAutoTrigger);
        }

        private void AddLogSafe(string text)
        {
            var app = System.Windows.Application.Current;
            var dispatcher = app?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => _manager.Log.Add(text)));
                return;
            }

            _manager.Log.Add(text);
        }

        private bool CanEvaluateEntrySignalsNow()
        {
            if (!_manager.IsStrategyRunning)
            {
                return false;
            }

            if (_manager.IsBacktestActive &&
                _manager.StrategyStartTime.HasValue &&
                _manager.CurrentTime < _manager.StrategyStartTime.Value)
            {
                return false;
            }

            return true;
        }
    }
}
