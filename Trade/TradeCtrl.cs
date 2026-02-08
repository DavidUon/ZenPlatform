using System;
using ZenPlatform.Core;

namespace ZenPlatform.Trade
{
    public sealed class TradeCtrl
    {
        private readonly object _sync = new();

        public DateTime? LastQuoteTime { get; private set; }
        public decimal? Bid { get; private set; }
        public decimal? Ask { get; private set; }
        public decimal? Last { get; private set; }
        public int? Volume { get; private set; }
        public string Product { get; private set; } = "";
        public int Year { get; private set; }
        public int Month { get; private set; }
        public QuoteSource? Source { get; private set; }
        public DateTime? LastOrderTime { get; private set; }
        public int? LastOrderIsBuy { get; private set; }
        public int? LastOrderQty { get; private set; }
        public string LastOrderReason { get; private set; } = "";
        public bool? LastOrderIsRealTrade { get; private set; }

        public void OnQuote(QuoteUpdate quote)
        {
            if (quote == null)
            {
                throw new ArgumentNullException(nameof(quote));
            }

            lock (_sync)
            {
                Product = quote.Product;
                Year = quote.Year;
                Month = quote.Month;
                LastQuoteTime = quote.Time;
                Source = quote.Source;

                switch (quote.Field)
                {
                    case QuoteField.Bid:
                        if (decimal.TryParse(quote.Value, out var bid))
                        {
                            Bid = bid;
                        }
                        break;
                    case QuoteField.Ask:
                        if (decimal.TryParse(quote.Value, out var ask))
                        {
                            Ask = ask;
                        }
                        break;
                    case QuoteField.Last:
                        if (decimal.TryParse(quote.Value, out var last))
                        {
                            Last = last;
                        }
                        break;
                    case QuoteField.Volume:
                        if (int.TryParse(quote.Value, out var volume))
                        {
                            Volume = volume;
                        }
                        break;
                }
            }
        }

        public decimal? SendOrder(bool isBuy, int qty, bool isRealTrade)
        {
            lock (_sync)
            {
                LastOrderTime = DateTime.Now;
                LastOrderIsBuy = isBuy ? 1 : 0;
                LastOrderQty = qty;
                LastOrderReason = string.Empty;
                LastOrderIsRealTrade = isRealTrade;

                decimal? fillPrice = null;
                if (isBuy)
                {
                    fillPrice = Ask ?? Last ?? Bid;
                }
                else
                {
                    fillPrice = Bid ?? Last ?? Ask;
                }

                if (!fillPrice.HasValue)
                {
                    throw new InvalidOperationException("無可用模擬價格");
                }

                return fillPrice;
            }
        }
    }
}
