using System;

namespace ZenPlatform.Strategy
{
    public sealed class SessionEntry : ISessionEntry
    {
        public ISession? CreateSession(int id)
        {
            return new Session
            {
                Id = id,
                IsFinished = false,
                Position = 0,
                StartPosition = 0,
                AvgEntryPrice = 0,
                FloatProfit = 0,
                RealizedProfit = 0,
                TradeCount = 0
            };
        }
    }
}
