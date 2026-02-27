namespace ZenPlatform.Strategy.EntryTriggers
{
    public readonly record struct EntryTriggerSignal(int IsBuy, string TriggerId, string Reason)
    {
        public static readonly EntryTriggerSignal None = new(0, string.Empty, string.Empty);
    }
}
