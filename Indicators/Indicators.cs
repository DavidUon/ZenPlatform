namespace ZenPlatform.Indicators
{
    public sealed class Indicators
    {
        public Indicators()
        {
            MA = new[]
            {
                new MovingAverage(),
                new MovingAverage(),
                new MovingAverage()
            };
            KD = new KdjIndicator();
            MACD = new MacdIndicator();
        }

        public MovingAverage[] MA { get; }
        public KdjIndicator KD { get; }
        public MacdIndicator MACD { get; }
        public MovingAverage[] ma => MA;
        public KdjIndicator kd => KD;
        public MacdIndicator macd => MACD;

        public void Reset()
        {
            foreach (var ma in MA)
            {
                ma.Reset();
            }
            KD.Reset();
            MACD.Reset();
        }

        public void Update(KBar bar)
        {
            Update(bar.High, bar.Low, bar.Close);
        }

        public void Update(decimal high, decimal low, decimal close)
        {
            foreach (var ma in MA)
            {
                ma.Update(close);
            }
            KD.Update(high, low, close);
            MACD.Update(close);
        }
    }
}
