using System;

namespace ZenPlatform.Core
{
    public enum CoreQueueType
    {
        TimeSignal,
        PriceUpdate
    }

    public sealed class CoreQueueItem
    {
        private CoreQueueItem(CoreQueueType type, DateTime time, QuoteUpdate? quote)
        {
            Type = type;
            Time = time;
            Quote = quote;
        }

        public CoreQueueType Type { get; }
        public DateTime Time { get; }
        public QuoteUpdate? Quote { get; }

        public static CoreQueueItem FromTime(DateTime time)
        {
            return new CoreQueueItem(CoreQueueType.TimeSignal, time, null);
        }

        public static CoreQueueItem FromQuote(QuoteUpdate quote)
        {
            return new CoreQueueItem(CoreQueueType.PriceUpdate, quote.Time, quote);
        }
    }
}
