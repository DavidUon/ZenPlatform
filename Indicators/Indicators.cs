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
            BBIBOLL = new BbiBollIndicator();
            MACDTRIGGER = new MacdTriggerIndicator();
            RANGEBOUND = new RangeBoundIndicator();
        }

        public MovingAverage[] MA { get; }
        public KdjIndicator KD { get; }
        public MacdIndicator MACD { get; }
        public BbiBollIndicator BBIBOLL { get; }
        public MacdTriggerIndicator MACDTRIGGER { get; }
        public RangeBoundIndicator RANGEBOUND { get; }
        public MovingAverage[] ma => MA;
        public KdjIndicator kd => KD;
        public MacdIndicator macd => MACD;
        public BbiBollIndicator bbiboll => BBIBOLL;
        public MacdTriggerIndicator macdtrigger => MACDTRIGGER;
        public RangeBoundIndicator rangebound => RANGEBOUND;

        public void Reset()
        {
            foreach (var ma in MA)
            {
                ma.Reset();
            }
            KD.Reset();
            MACD.Reset();
            BBIBOLL.Reset();
            MACDTRIGGER.Reset();
            RANGEBOUND.Reset();
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

        public void UpdateMacdTrigger(KBar bar)
        {
            UpdateMacdTrigger(bar.Close);
        }

        public void UpdateMacdTrigger(decimal close)
        {
            MACDTRIGGER.Update(close);
        }

        public void UpdateBbiBoll(KBar bar)
        {
            UpdateBbiBoll(bar.Close);
        }

        public void UpdateBbiBoll(decimal close)
        {
            BBIBOLL.Update(close);
        }

        public void UpdateRangeBound(KBar bar)
        {
            UpdateRangeBound(bar.High, bar.Low, bar.Close);
        }

        public void UpdateRangeBound(decimal high, decimal low, decimal close)
        {
            RANGEBOUND.Update(high, low, close);
        }
    }
}
