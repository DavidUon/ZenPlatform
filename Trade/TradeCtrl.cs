using System;
using Brokers;
using ZenPlatform.Core;

namespace ZenPlatform.Trade
{
    public sealed class TradeCtrl
    {
        private readonly object _sync = new();
        private IBroker? _broker;

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

        public void BindBroker(IBroker broker)
        {
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        }

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

        public decimal? SendOrder(
            bool isBuy,
            int qty,
            bool isRealTrade,
            string? overrideProduct = null,
            int? overrideYear = null,
            int? overrideMonth = null,
            decimal? overrideSimFillPrice = null)
        {
            string product;
            int year;
            int month;
            decimal? last;
            decimal? bid;
            decimal? ask;

            lock (_sync)
            {
                LastOrderTime = DateTime.Now;
                LastOrderIsBuy = isBuy ? 1 : 0;
                LastOrderQty = qty;
                LastOrderReason = string.Empty;
                LastOrderIsRealTrade = isRealTrade;
                product = Product;
                year = Year;
                month = Month;
                last = Last;
                bid = Bid;
                ask = Ask;
            }

            if (!string.IsNullOrWhiteSpace(overrideProduct))
            {
                product = overrideProduct!;
            }

            if (overrideYear.HasValue && overrideYear.Value > 0)
            {
                year = overrideYear.Value;
            }

            if (overrideMonth.HasValue && overrideMonth.Value > 0)
            {
                month = overrideMonth.Value;
            }

            if (isRealTrade)
            {
                var broker = _broker ?? throw new InvalidOperationException("尚未綁定券商下單通道。");
                if (!TryMapContract(product, out var contractName))
                {
                    throw new InvalidOperationException($"不支援的商品：{product}");
                }

                if (year <= 0 || month <= 0)
                {
                    throw new InvalidOperationException("合約年月尚未就緒，無法真實下單。");
                }

                var marketPrice = last ?? (isBuy ? (ask ?? bid) : (bid ?? ask));
                if (!marketPrice.HasValue || marketPrice.Value <= 0)
                {
                    throw new InvalidOperationException("無可用市價，無法真實下單。");
                }

                // Real trade uses limit order.
                // User rule:
                // - long entry: current market + 50
                // - short entry: current market - 50
                var limitPrice = isBuy
                    ? marketPrice.Value + 50m
                    : marketPrice.Value - 50m;

                try
                {
                    var form = new OrderForm(
                        contractName,
                        year,
                        month,
                        isBuy,
                        qty,
                        IsMarketOrder: false,
                        IsDayTrading: false,
                        LimitPrice: limitPrice);
                    var fill = broker.SendOrderAsync(form, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    return fill;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"真實下單失敗：{ex.Message}", ex);
                }
            }

            lock (_sync)
            {
                if (overrideSimFillPrice.HasValue && overrideSimFillPrice.Value > 0m)
                {
                    return overrideSimFillPrice.Value;
                }

                decimal? fillPrice = null;
                // Backtest/sim: prioritize unified transaction price to avoid stale bid/ask fills.
                fillPrice = Last ?? (isBuy ? (Ask ?? Bid) : (Bid ?? Ask));

                if (!fillPrice.HasValue)
                {
                    throw new InvalidOperationException("無可用模擬價格");
                }

                return fillPrice;
            }
        }

        private static bool TryMapContract(string product, out ContractName contractName)
        {
            switch (product)
            {
                case "大型台指":
                    contractName = ContractName.大型台指;
                    return true;
                case "小型台指":
                    contractName = ContractName.小型台指;
                    return true;
                case "微型台指":
                    contractName = ContractName.微型台指;
                    return true;
                default:
                    contractName = ContractName.大型台指;
                    return false;
            }
        }
    }
}
