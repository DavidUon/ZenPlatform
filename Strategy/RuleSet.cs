namespace ZenPlatform.Strategy
{
    public enum TrendMode
    {
        Auto = 0,
        None = 1,
        MovingAverage = 2,
        Force = 3
    }

    public enum TrendForceSide
    {
        無 = 0,
        多 = 1,
        空 = 2
    }

    public enum TakeProfitMode
    {
        FixedPoints = 0,
        AutoAfterN = 1
    }

    public enum StopLossMode
    {
        FixedPoints = 0,
        Auto = 1
    }

    public sealed class RuleSet
    {
        public int OrderSize { get; set; } = 1;
        public int KbarPeriod { get; set; } = 5;
        public int TakeProfitPoints { get; set; } = 300;
        public TakeProfitMode TakeProfitMode { get; set; } = TakeProfitMode.FixedPoints;
        public int AutoTakeProfitPoints { get; set; } = 0;
        public int StopLossPoints { get; set; } = 100;
        public StopLossMode StopLossMode { get; set; } = StopLossMode.Auto;
        public bool EnableAbsoluteStopLoss { get; set; } = false;
        public int AbsoluteStopLossPoints { get; set; } = 300;
        public bool LossRetraceExitEnabled { get; set; } = false;
        public int LossRetraceTriggerPoints { get; set; } = 300;
        public int LossRetracePercent { get; set; } = 50;
        public TrendMode TrendMode { get; set; } = TrendMode.None;
        public int TrendMaPeriod { get; set; } = 144;
        public TrendForceSide TrendForceSide { get; set; } = TrendForceSide.無;
        public int SameDirectionBlockMinutes { get; set; } = 300;
        public int SameDirectionBlockRange { get; set; } = 100;
        public System.TimeSpan DaySessionStart { get; set; } = new System.TimeSpan(8, 45, 0);
        public System.TimeSpan DaySessionEnd { get; set; } = new System.TimeSpan(13, 0, 0);
        public System.TimeSpan NightSessionStart { get; set; } = new System.TimeSpan(15, 0, 0);
        public System.TimeSpan NightSessionEnd { get; set; } = new System.TimeSpan(2, 0, 0);
        public int MaxReverseCount { get; set; } = 20;
        public int MaxSessionCount { get; set; } = 5;
        public bool ReverseAfterStopLoss { get; set; } = true;
        public bool CoverLossBeforeTakeProfit { get; set; } = false;
        public int CoverLossTriggerPoints { get; set; } = 150;
        public bool ExitOnTotalProfitRise { get; set; } = false;
        public int ExitOnTotalProfitRiseArmBelowPoints { get; set; } = 100;
        public int ExitOnTotalProfitRisePoints { get; set; } = 500;
        public bool ExitOnTotalProfitDropAfterTrigger { get; set; } = false;
        public int ExitOnTotalProfitDropTriggerPoints { get; set; } = 500;
        public int ExitOnTotalProfitDropExitPoints { get; set; } = 100;
        public bool ProfitRetraceExitEnabled { get; set; } = false;
        public int ProfitRetraceTriggerPoints { get; set; } = 300;
        public int ProfitRetracePercent { get; set; } = 50;
        public bool AutoRolloverWhenHolding { get; set; } = false;
        public System.TimeSpan AutoRolloverTime { get; set; } = new System.TimeSpan(13, 15, 0);
        public bool CloseBeforeDaySessionEnd { get; set; } = false;
        public bool CloseBeforeNightSessionEnd { get; set; } = false;
        // Backward-compatible aggregate flag for legacy settings payloads.
        public bool CloseBeforeSessionEnd
        {
            get => CloseBeforeDaySessionEnd || CloseBeforeNightSessionEnd;
            set
            {
                CloseBeforeDaySessionEnd = value;
                CloseBeforeNightSessionEnd = value;
            }
        }
        public System.TimeSpan DayCloseBeforeTime { get; set; } = new System.TimeSpan(13, 40, 0);
        public System.TimeSpan NightCloseBeforeTime { get; set; } = new System.TimeSpan(4, 50, 0);
        public bool CloseBeforeLongHoliday { get; set; } = false;
        public System.TimeSpan CloseBeforeLongHolidayTime { get; set; } = new System.TimeSpan(4, 50, 0);
    }
}
