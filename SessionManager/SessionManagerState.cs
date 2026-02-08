using System;
using System.Collections.Generic;
using ZenPlatform.Strategy;

namespace ZenPlatform.SessionManager
{
    public sealed class SessionManagerState
    {
        public string? Name { get; set; }
        public bool IsStrategyRunning { get; set; }
        public bool IsRealTrade { get; set; }
        public bool AcceptSecondTicks { get; set; }
        public bool AcceptPriceTicks { get; set; }
        public List<double> ColumnWidths { get; set; } = new();
        public RuleSet RuleSet { get; set; } = new();
        public List<SessionState> Sessions { get; set; } = new();
        public List<LogEntryState> LogEntries { get; set; } = new();
    }

    public sealed class SessionState
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

        public static SessionState FromSession(Session session)
        {
            return new SessionState
            {
                Id = session.Id,
                StartTime = session.StartTime,
                IsFinished = session.IsFinished,
                Position = session.Position,
                StartPosition = session.StartPosition,
                AvgEntryPrice = session.AvgEntryPrice,
                FloatProfit = session.FloatProfit,
                RealizedProfit = session.RealizedProfit,
                TradeCount = session.TradeCount
            };
        }

        public Session ToSession()
        {
            var session = new Session
            {
                Id = Id,
                StartTime = StartTime,
                Position = Position,
                StartPosition = StartPosition,
                AvgEntryPrice = AvgEntryPrice,
                FloatProfit = FloatProfit,
                RealizedProfit = RealizedProfit,
                TradeCount = TradeCount
            };
            session.PositionManager.FromSnapshot(new ZenPlatform.Strategy.PositionSnapshot
            {
                TotalKou = Position,
                AvgEntryPrice = AvgEntryPrice,
                PingProfit = RealizedProfit,
                FloatProfit = FloatProfit
            });
            session.RestoreFinished(IsFinished);
            return session;
        }
    }

    public sealed class LogEntryState
    {
        public DateTime Time { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? HighlightText { get; set; }
        public ZenPlatform.LogText.LogTxtColor HighlightColor { get; set; }
    }
}
