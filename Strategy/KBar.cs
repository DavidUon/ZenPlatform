namespace ZenPlatform.Strategy
{
    public sealed class KBar
    {
        public KBar(decimal open, decimal high, decimal low, decimal close, int volume)
        {
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
        }

        public decimal Open { get; }
        public decimal High { get; }
        public decimal Low { get; }
        public decimal Close { get; }
        public int Volume { get; }
    }
}
