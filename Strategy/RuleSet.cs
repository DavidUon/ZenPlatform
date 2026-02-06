namespace ZenPlatform.Strategy
{
    public sealed class RuleSet
    {
        public int OrderSize { get; set; } = 1;
        public int KbarPeriod { get; set; } = 5;
        public int KPeriod { get; set; } = 3;
        public int DPeriod { get; set; } = 3;
        public int RsvPeriod { get; set; } = 9;
        public int TakeProfitPoints { get; set; } = 50;
        public int MaxReverseCount { get; set; } = 2;
        public int MaxSessionCount { get; set; } = 10;
    }
}
