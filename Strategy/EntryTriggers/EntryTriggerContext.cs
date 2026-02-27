namespace ZenPlatform.Strategy.EntryTriggers
{
    internal readonly record struct EntryKBarTriggerContext(
        int Period,
        bool HasBbiBoll,
        decimal BbiUp,
        decimal BbiDn,
        bool IsFullyAboveUp,
        bool IsFullyBelowDown,
        int MacdTriggerState,
        bool HasCurrentPrice,
        decimal CurrentPrice,
        bool HasA,
        decimal A,
        bool HasV,
        decimal V);

    internal readonly record struct EntryTickTriggerContext(
        EntryTrendSide CurrentSide,
        decimal Price,
        bool HasBbiBoll,
        decimal Mid,
        decimal Up,
        decimal Dn);
}
