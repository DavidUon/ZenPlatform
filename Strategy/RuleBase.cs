namespace ZenPlatform.Strategy
{
    public abstract class RuleBase
    {
        public ZenPlatform.SessionManager.SessionManager? Manager { get; set; }
        public PositionManager PositionManager { get; } = new();
        private bool _hasLoggedFirstTick;
        private bool _hasLoggedMissingQuote;
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

        public virtual void OnKBarCompleted(int period, KChartCore.FunctionKBar bar)
        {
            if (IsFinished)
            {
                return;
            }
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
                if (!_hasLoggedMissingQuote)
                {
                    Manager.Log.Add("Tick收到但缺少 Bid/Ask，無法計算浮動損益");
                    _hasLoggedMissingQuote = true;
                }
                return;
            }

            // 多單用買進價(Bid)做價差，空單用賣出價(Ask)做價差
            PositionManager.OnTick(bid.Value, ask.Value);

            if (this is Session session)
            {
                session.Position = PositionManager.TotalKou;
                session.AvgEntryPrice = PositionManager.AvgEntryPrice;
                session.FloatProfit = PositionManager.FloatProfit;
                session.RealizedProfit = PositionManager.PingProfit;
            }

            if (!_hasLoggedFirstTick)
            {
                Manager.Log.Add($"Tick更新 Bid={bid:0.0} Ask={ask:0.0} 浮動損益={PositionManager.FloatProfit:0.0}");
                _hasLoggedFirstTick = true;
            }
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

        public decimal? Trade(bool isBuy, int qty, string reason)
        {
            if (qty <= 0)
            {
                return null;
            }

            try
            {
                var price = Manager?.SendOrder(isBuy, qty);
                if (price.HasValue)
                {
                    var realizedDelta = PositionManager.AddPosition(isBuy, qty, price.Value);
                    if (this is Session session)
                    {
                        session.Position = PositionManager.TotalKou;
                        session.AvgEntryPrice = PositionManager.AvgEntryPrice;
                        session.FloatProfit = PositionManager.FloatProfit;
                        session.RealizedProfit = PositionManager.PingProfit;
                        session.TradeCount += qty;
                    }
                    var sideText = isBuy ? "多單" : "空單";
                    PutStr($"{sideText}{qty}口成交，均價 {price.Value:0.0}");
                    if (realizedDelta != 0)
                    {
                        PutStr($"本次交易獲利 {realizedDelta:0.0} 點");
                    }
                }
                return price;
            }
            catch (System.InvalidOperationException ex)
            {
                var text = $"下單失敗: {ex.Message}";
                Manager?.Log.Add(text, text, ZenPlatform.LogText.LogTxtColor.黃色);
                return null;
            }
        }
    }
}
