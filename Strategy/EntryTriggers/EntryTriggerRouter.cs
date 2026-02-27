namespace ZenPlatform.Strategy.EntryTriggers
{
    internal sealed class EntryTriggerRouter
    {
        private readonly M1EntryTrigger _m1;
        private readonly M2EntryTrigger _m2;
        private readonly M3EntryTrigger _m3;

        public EntryTriggerRouter(ZenPlatform.SessionManager.EntryTriggerRuntimeState state, System.Action<string>? m2Diagnostic = null)
        {
            _m1 = new M1EntryTrigger(state);
            _m2 = new M2EntryTrigger(state, m2Diagnostic);
            _m3 = new M3EntryTrigger(state);
        }

        public void Reset()
        {
            _m1.Reset();
            _m2.Reset();
            _m3.Reset();
        }

        public EntryTriggerSignal OnKBarCompleted(TrendMode trendMode, EntryKBarTriggerContext context)
        {
            return trendMode == TrendMode.None
                ? _m1.OnKBarCompleted(context)
                : _m3.OnKBarCompleted(context);
        }

        public EntryTriggerSignal OnTick(TrendMode trendMode, EntryTickTriggerContext context)
        {
            return trendMode == TrendMode.None
                ? EntryTriggerSignal.None
                : _m2.OnTick(context);
        }
    }
}
