namespace ZenPlatform.Strategy.ExitRules
{
    internal enum ExitTriggerKind
    {
        Tick = 0,
        KBarCompleted = 1
    }

    internal readonly record struct ExitRuleContext(
        ExitTriggerKind TriggerKind,
        int Period,
        ZenPlatform.Strategy.KBar? Bar)
    {
        public static ExitRuleContext Tick() => new(ExitTriggerKind.Tick, 0, null);

        public static ExitRuleContext KBarCompleted(int period, ZenPlatform.Strategy.KBar bar) => new(ExitTriggerKind.KBarCompleted, period, bar);
    }
}
