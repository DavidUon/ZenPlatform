using System;

namespace ZenPlatform.Strategy
{
    public interface ISession
    {
        int Id { get; set; }
        DateTime StartTime { get; set; }
        bool IsFinished { get; set; }
        int Position { get; set; }
        int StartPosition { get; set; }
        decimal AvgEntryPrice { get; set; }
        decimal FloatProfit { get; set; }
        decimal RealizedProfit { get; set; }
        int TradeCount { get; set; }
        decimal TotalProfit { get; }
        void Start(SessionSide side);
    }
}
