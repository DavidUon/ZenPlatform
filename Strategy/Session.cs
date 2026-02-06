using System;

namespace ZenPlatform.Strategy
{
    public sealed class Session : ISession
    {
        public int Id { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsFinished { get; set; }
        public int Position { get; set; }
        public int StartPosition { get; set; }
        public decimal AvgEntryPrice { get; set; }
        public decimal FloatProfit { get; set; }
        public decimal RealizedProfit { get; set; }
        public int TradeCount { get; set; }
        public decimal TotalProfit => FloatProfit + RealizedProfit;

        public void Start(SessionSide side)
        {
            StartTime = DateTime.Now;
            StartPosition = side == SessionSide.Long ? 1 : -1;
            Position = StartPosition;
            IsFinished = false;
        }
    }
}
