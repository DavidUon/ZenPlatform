using System;

namespace ZenPlatform.Core
{
    // Health-only guard: tracks quote freshness and price-flow activity.
    public sealed class QuoteFlowGuard
    {
        private string? _lastBid;
        private string? _lastAsk;
        private string? _lastLast;
        private bool _hasBid;
        private bool _hasAsk;
        private bool _hasLast;

        public DateTime? LastChangeAt { get; private set; }
        public DateTime? LastDdeAt { get; private set; }
        public DateTime? LastNetworkAt { get; private set; }

        public bool HasRequiredFields => _hasBid && _hasAsk && _hasLast;

        public bool TrackQuote(QuoteUpdate quote, DateTime now)
        {
            if (quote.Source == QuoteSource.Dde)
            {
                LastDdeAt = now;
            }
            else if (quote.Source == QuoteSource.Network)
            {
                LastNetworkAt = now;
            }

            return quote.Field switch
            {
                QuoteField.Bid => UpdateField(ref _lastBid, ref _hasBid, quote.Value, now),
                QuoteField.Ask => UpdateField(ref _lastAsk, ref _hasAsk, quote.Value, now),
                QuoteField.Last => UpdateField(ref _lastLast, ref _hasLast, quote.Value, now),
                _ => false
            };
        }

        public bool IsStale(DateTime now, TimeSpan threshold)
        {
            if (!HasRequiredFields || !LastChangeAt.HasValue)
            {
                return false;
            }

            return (now - LastChangeAt.Value) >= threshold;
        }

        public void MarkSyntheticChange(DateTime now)
        {
            LastChangeAt = now;
        }

        private bool UpdateField(ref string? lastValue, ref bool hasValue, string value, DateTime now)
        {
            if (hasValue && string.Equals(lastValue, value, StringComparison.Ordinal))
            {
                return false;
            }

            lastValue = value;
            hasValue = true;
            LastChangeAt = now;
            return true;
        }
    }
}
