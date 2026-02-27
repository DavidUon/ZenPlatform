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
        public SessionManagerRuntimeState RuntimeState { get; set; } = new();
        public RuleSet RuleSet { get; set; } = new();
        public List<SessionState> Sessions { get; set; } = new();
        public List<LogEntryState> LogEntries { get; set; } = new();
    }

    public sealed class SessionState
    {
        public int Id { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsFinished { get; set; }
        public bool IsFailed { get; set; }
        public int Position { get; set; }
        public int StartPosition { get; set; }
        public decimal AvgEntryPrice { get; set; }
        public decimal FloatProfit { get; set; }
        public decimal RealizedProfit { get; set; }
        public decimal WorstTotalProfit { get; set; }
        public int TradeCount { get; set; }
        public int ReverseCount { get; set; }
        public bool HasAutoStopSnapshot { get; set; }
        public decimal EntryRangeBoundA { get; set; }
        public decimal EntryRangeBoundV { get; set; }

        public static SessionState FromSession(Session session)
        {
            session.GetAutoStopSnapshot(out var hasSnapshot, out var entryA, out var entryV);
            return new SessionState
            {
                Id = session.Id,
                StartTime = session.StartTime,
                IsFinished = session.IsFinished,
                IsFailed = session.IsFailed,
                Position = session.Position,
                StartPosition = session.StartPosition,
                AvgEntryPrice = session.AvgEntryPrice,
                FloatProfit = session.FloatProfit,
                RealizedProfit = session.RealizedProfit,
                WorstTotalProfit = session.WorstTotalProfit,
                TradeCount = session.TradeCount,
                ReverseCount = session.ReverseCount,
                HasAutoStopSnapshot = hasSnapshot,
                EntryRangeBoundA = entryA,
                EntryRangeBoundV = entryV
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
                TradeCount = TradeCount,
                ReverseCount = ReverseCount
            };
            session.PositionManager.FromSnapshot(new ZenPlatform.Strategy.PositionSnapshot
            {
                TotalKou = Position,
                AvgEntryPrice = AvgEntryPrice,
                PingProfit = RealizedProfit,
                FloatProfit = FloatProfit
            });
            session.RestoreAutoStopSnapshot(HasAutoStopSnapshot, EntryRangeBoundA, EntryRangeBoundV);
            session.RestoreWorstTotalProfit(WorstTotalProfit);
            session.RestoreFailed(IsFailed);
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
