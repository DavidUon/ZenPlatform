namespace ZenPlatform.Strategy
{
    public abstract class RuleBase
    {
        public ZenPlatform.SessionManager.SessionManager? Manager { get; set; }
        public PositionManager PositionManager { get; } = new();
        private bool _hasLoggedFirstTick;
        protected bool _isFinished;

        public virtual bool IsFinished
        {
            get => _isFinished;
            protected set => _isFinished = value;
        }

        public virtual void OnTick()
        {
            if (IsFinished)
            {
                return;
            }
            UpdateFloatingProfit();
        }

        public virtual void OnMinute()
        {
            if (IsFinished)
            {
                return;
            }
        }

        public virtual void OnKBarCompleted(int period, KBar bar)
        {
            if (IsFinished)
            {
                return;
            }
        }

        protected DateTime GetCurrentTime()
        {
            if (Manager == null || Manager.CurrentTime.Year < 2000)
            {
                return DateTime.Now;
            }

            return Manager.CurrentTime;
        }

        protected bool CanCreateNewSession(
            DateTime now,
            bool isBuy,
            out string reason,
            bool applySameDirectionBlock = true,
            bool applyCreateSessionWindow = true)
        {
            if (Manager == null)
            {
                reason = "找不到策略管理器";
                return false;
            }

            var maxSessions = Manager.RuleSet.MaxSessionCount;
            if (maxSessions > 0)
            {
                var activeCount = 0;
                foreach (var session in Manager.Sessions)
                {
                    if (!session.IsFinished)
                    {
                        activeCount++;
                    }
                }

                if (activeCount >= maxSessions)
                {
                    var sideText = isBuy ? "多單" : "空單";
                    reason = $"超過任務數量上限，{sideText}任務取消";
                    return false;
                }
            }

            var blockMinutes = Manager.RuleSet.SameDirectionBlockMinutes;
            var blockRange = Manager.RuleSet.SameDirectionBlockRange;
            if (applySameDirectionBlock && blockMinutes > 0)
            {
                var nowPrice = Manager.CurPrice;
                var blockState = Manager.RuntimeState.SameDirectionBlock;
                if (nowPrice.HasValue &&
                    blockState.LastAllowedEntryTime.HasValue &&
                    blockState.LastAllowedEntrySide == (isBuy ? 1 : -1) &&
                    blockState.LastAllowedEntryPrice > 0m)
                {
                    var elapsed = now - blockState.LastAllowedEntryTime.Value;
                    if (elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromMinutes(blockMinutes))
                    {
                        var priceDelta = Math.Abs(nowPrice.Value - blockState.LastAllowedEntryPrice);
                        if (priceDelta <= blockRange)
                        {
                            var sideText = isBuy ? "多單" : "空單";
                            reason = $"{blockMinutes}分鐘內{blockRange}點範圍內，取消{sideText}任務";
                            return false;
                        }
                    }
                }
            }

            var time = now.TimeOfDay;
            var inDay = TradingTimeService.IsInDaySession(time);
            var inNight = TradingTimeService.IsInNightSession(time);

            if (inDay)
            {
                if (!applyCreateSessionWindow || TradingTimeService.IsInCreateSessionWindow(now, Manager.RuleSet))
                {
                    reason = string.Empty;
                    return true;
                }
                var sideText = isBuy ? "多單" : "空單";
                reason = $"非建立任務時間範圍，{sideText}任務取消";
                return false;
            }

            if (inNight)
            {
                if (!applyCreateSessionWindow || TradingTimeService.IsInCreateSessionWindow(now, Manager.RuleSet))
                {
                    reason = string.Empty;
                    return true;
                }
                var sideText = isBuy ? "多單" : "空單";
                reason = $"非建立任務時間範圍，{sideText}任務取消";
                return false;
            }

            var defaultSideText = isBuy ? "多單" : "空單";
            reason = $"非建立任務時間範圍，{defaultSideText}任務取消";
            return false;
        }

        protected void SetFinished(bool isFinished)
        {
            if (IsFinished == isFinished)
            {
                return;
            }

            IsFinished = isFinished;
        }

        protected void UpdateFloatingProfit()
        {
            if (Manager == null)
            {
                return;
            }

            var bid = Manager.Bid;
            var ask = Manager.Ask;
            if (!bid.HasValue || !ask.HasValue)
            {
                return;
            }

            // 多單用買進價(Bid)做價差，空單用賣出價(Ask)做價差
            PositionManager.OnTick(bid.Value, ask.Value);

            if (this is Session session)
            {
                if (Manager.IsBacktestActive && !ShouldRefreshBacktestUi(session))
                {
                    return;
                }

                session.Position = PositionManager.TotalKou;
                session.AvgEntryPrice = PositionManager.AvgEntryPrice;
                session.FloatProfit = PositionManager.FloatProfit;
                session.RealizedProfit = PositionManager.PingProfit;
            }

            if (!_hasLoggedFirstTick)
            {
                _hasLoggedFirstTick = true;
            }
        }

        private bool ShouldRefreshBacktestUi(Session session)
        {
            if (Manager == null)
            {
                return true;
            }

            var now = Manager.CurrentTime;
            if (now.Year < 2000)
            {
                return true;
            }

            var bucketMinute = 0;
            var bucket = new DateTime(now.Year, now.Month, now.Day, now.Hour, bucketMinute, 0, DateTimeKind.Unspecified);
            if (session.LastBacktestUiRefreshBucket.HasValue && session.LastBacktestUiRefreshBucket.Value == bucket)
            {
                return false;
            }

            session.LastBacktestUiRefreshBucket = bucket;
            return true;
        }

        public void PutStr(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string prefix = string.Empty;
            if (this is Session session)
            {
                prefix = $"[{session.Id}] ";
            }
            else if (Manager != null)
            {
                prefix = $"[{Manager.Index + 1}] ";
            }
            Manager?.Log.Add(prefix + text);
        }

        protected void PutStrColored(string text, ZenPlatform.LogText.LogTxtColor color)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string prefix = string.Empty;
            if (this is Session session)
            {
                prefix = $"[{session.Id}] ";
            }
            else if (Manager != null)
            {
                prefix = $"[{Manager.Index + 1}] ";
            }
            Manager?.Log.Add(prefix + text, prefix + text, color);
        }

        public void PutStrAt(System.DateTime time, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string prefix = string.Empty;
            if (this is Session session)
            {
                prefix = $"[{session.Id}] ";
            }
            else if (Manager != null)
            {
                prefix = $"[{Manager.Index + 1}] ";
            }
            Manager?.Log.AddAt(time, prefix + text);
        }

        public decimal? Trade(bool isBuy, int qty, string reason, bool isFinishClose = false)
        {
            if (qty <= 0)
            {
                return null;
            }

            try
            {
                var formattedReason = FormatOrderReason(reason, isBuy);
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    if (IsEntryReason(reason))
                    {
                        PutStrColored(formattedReason, isBuy ? ZenPlatform.LogText.LogTxtColor.紅色 : ZenPlatform.LogText.LogTxtColor.綠色);
                    }
                    else
                    {
                        PutStr(formattedReason);
                    }
                }
                var price = Manager?.SendOrder(isBuy, qty);
                if (price.HasValue)
                {
                    var beforePosition = PositionManager.TotalKou;
                    var realizedDelta = PositionManager.AddPosition(isBuy, qty, price.Value);
                    var afterPosition = PositionManager.TotalKou;
                    var sideText = isBuy ? "多單" : "空單";
                    PutStr($"{sideText}{qty}口成交，均價 {price.Value:0.0}");
                    if (realizedDelta != 0)
                    {
                        PutStrColored($"本次交易獲利 {realizedDelta:0.0} 點", ZenPlatform.LogText.LogTxtColor.黃色);
                    }
                    AppendOrderMark(isBuy, qty, price.Value, formattedReason, beforePosition, afterPosition, isFinishClose);
                    if (this is Session session)
                    {
                        session.Position = PositionManager.TotalKou;
                        session.AvgEntryPrice = PositionManager.AvgEntryPrice;
                        session.FloatProfit = PositionManager.FloatProfit;
                        session.RealizedProfit = PositionManager.PingProfit;
                        session.TradeCount += qty;
                    }
                }
                else
                {
                    MarkSessionFailed("任務執行中發生交易失敗，此任務停止運作");
                }
                return price;
            }
            catch (System.InvalidOperationException ex)
            {
                var text = $"下單失敗: {ex.Message}";
                Manager?.Log.Add(text, text, ZenPlatform.LogText.LogTxtColor.黃色);
                MarkSessionFailed("任務執行中發生交易失敗，此任務停止運作");
                return null;
            }
        }

        protected decimal? TradeAtPrice(bool isBuy, int qty, decimal fillPrice, string reason, bool isFinishClose = false)
        {
            if (qty <= 0)
            {
                return null;
            }

            try
            {
                var formattedReason = FormatOrderReason(reason, isBuy);
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    if (IsEntryReason(reason))
                    {
                        PutStrColored(formattedReason, isBuy ? ZenPlatform.LogText.LogTxtColor.紅色 : ZenPlatform.LogText.LogTxtColor.綠色);
                    }
                    else
                    {
                        PutStr(formattedReason);
                    }
                }

                var beforePosition = PositionManager.TotalKou;
                var realizedDelta = PositionManager.AddPosition(isBuy, qty, fillPrice);
                var afterPosition = PositionManager.TotalKou;
                var sideText = isBuy ? "多單" : "空單";
                PutStr($"{sideText}{qty}口成交，均價 {fillPrice:0.0}");
                if (realizedDelta != 0)
                {
                    PutStrColored($"本次交易獲利 {realizedDelta:0.0} 點", ZenPlatform.LogText.LogTxtColor.黃色);
                }
                AppendOrderMark(isBuy, qty, fillPrice, formattedReason, beforePosition, afterPosition, isFinishClose);
                if (this is Session session)
                {
                    session.Position = PositionManager.TotalKou;
                    session.AvgEntryPrice = PositionManager.AvgEntryPrice;
                    session.FloatProfit = PositionManager.FloatProfit;
                    session.RealizedProfit = PositionManager.PingProfit;
                    session.TradeCount += qty;
                }

                return fillPrice;
            }
            catch (System.InvalidOperationException ex)
            {
                var text = $"下單失敗: {ex.Message}";
                Manager?.Log.Add(text, text, ZenPlatform.LogText.LogTxtColor.黃色);
                MarkSessionFailed("任務執行中發生交易失敗，此任務停止運作");
                return null;
            }
        }

        private void MarkSessionFailed(string message)
        {
            if (this is Session session)
            {
                session.MarkAsFailed(message);
            }
        }

        public void ForcePosition(int targetPosition, string reason, bool isFinishClose = false)
        {
            var current = PositionManager.TotalKou;
            var delta = targetPosition - current;
            if (delta == 0)
            {
                return;
            }

            var isBuy = delta > 0;
            var qty = Math.Abs(delta);
            Trade(isBuy, qty, reason, isFinishClose);
        }

        protected void ForcePositionAtPrice(int targetPosition, decimal fillPrice, string reason, bool isFinishClose = false)
        {
            // fillPrice path is for deterministic backtest fills.
            // In live/sim runtime (non-backtest), always route through Trade()
            // so order is sent to TradeCtrl/Broker and filled by actual quote/report.
            if (Manager != null && !Manager.IsBacktestActive)
            {
                ForcePosition(targetPosition, reason, isFinishClose);
                return;
            }

            var current = PositionManager.TotalKou;
            var delta = targetPosition - current;
            if (delta == 0)
            {
                return;
            }

            var isBuy = delta > 0;
            var qty = Math.Abs(delta);
            TradeAtPrice(isBuy, qty, fillPrice, reason, isFinishClose);
        }

        // Settlement-only path:
        // use provided fill price to update session accounting/log without sending broker order.
        // Intended for netting workflows where the real order has already been sent once centrally.
        protected void SettlePositionAtPrice(int targetPosition, decimal fillPrice, string reason, bool isFinishClose = false)
        {
            var current = PositionManager.TotalKou;
            var delta = targetPosition - current;
            if (delta == 0)
            {
                return;
            }

            var isBuy = delta > 0;
            var qty = Math.Abs(delta);
            TradeAtPrice(isBuy, qty, fillPrice, reason, isFinishClose);
        }

        private void AppendOrderMark(bool isBuy, int qty, decimal price, string reason, int beforePosition, int afterPosition, bool isFinishClose)
        {
            var manager = Manager;
            var recorder = manager?.BacktestRecorder;
            if (recorder == null || !manager!.IsBacktestActive)
            {
                return;
            }

            var sessionId = this is Session s ? s.Id : 0;
            var signedQty = isBuy ? qty : -qty;
            var eventType = ResolveOrderEventType(beforePosition, afterPosition, signedQty, isFinishClose);
            recorder.AppendOrderMark(GetCurrentTime(), sessionId, eventType, isBuy ? 1 : -1, qty, price, reason);
        }

        private static string ResolveOrderEventType(int beforePosition, int afterPosition, int signedQty, bool isFinishClose)
        {
            if (beforePosition == 0 && afterPosition != 0)
            {
                return "Open";
            }

            if (afterPosition == 0)
            {
                return isFinishClose ? "CloseFinish" : "Close";
            }

            if (Math.Sign(beforePosition) != 0 && Math.Sign(beforePosition) != Math.Sign(afterPosition))
            {
                return "Reverse";
            }

            if (Math.Abs(afterPosition) > Math.Abs(beforePosition))
            {
                return "Add";
            }

            if (Math.Abs(afterPosition) < Math.Abs(beforePosition))
            {
                return "Reduce";
            }

            return signedQty == 0 ? "General" : "Trade";
        }

        private static string FormatOrderReason(string reason, bool isBuy)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return string.Empty;
            }

            var text = reason.Trim();
            if (text.Contains("送出多單委託", StringComparison.Ordinal) ||
                text.Contains("送出空單委託", StringComparison.Ordinal))
            {
                return text;
            }

            var sideText = isBuy ? "多單" : "空單";
            return $"{text}，送出{sideText}委託...";
        }

        private static bool IsEntryReason(string reason)
        {
            return reason.Contains("建立", StringComparison.Ordinal) ||
                   reason.Contains("進場條件", StringComparison.Ordinal);
        }
    }
}
