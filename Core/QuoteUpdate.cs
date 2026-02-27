using System;

namespace ZenPlatform.Core
{
    public enum QuoteField
    {
        Unknown,
        Bid,
        Ask,
        Last,
        Volume,
        Change,
        ChangePercent,
        Time,
        DaysToExpiry
    }

    public enum QuoteSource
    {
        Dde,
        Network,
        Backtest
    }

    public sealed class QuoteUpdate
    {
        public QuoteUpdate(
            string product,
            QuoteField field,
            int year,
            int month,
            string value,
            bool isRequest,
            DateTime time,
            QuoteSource source)
        {
            Product = product ?? "";
            Field = field;
            Year = year;
            Month = month;
            Value = value ?? "";
            IsRequest = isRequest;
            Time = time;
            Source = source;
        }

        public string Product { get; }
        public QuoteField Field { get; }
        public int Year { get; }
        public int Month { get; }
        public string Value { get; }
        public bool IsRequest { get; }
        public DateTime Time { get; }
        public QuoteSource Source { get; }
    }

    public static class QuoteFieldMapper
    {
        public static QuoteField FromChineseName(string name)
        {
            return name switch
            {
                "買價" => QuoteField.Bid,
                "賣價" => QuoteField.Ask,
                "成交價" => QuoteField.Last,
                "成交量" => QuoteField.Volume,
                "漲跌" => QuoteField.Change,
                "漲跌幅" => QuoteField.ChangePercent,
                "時間" => QuoteField.Time,
                "距到期日" => QuoteField.DaysToExpiry,
                _ => QuoteField.Unknown
            };
        }
    }
}
