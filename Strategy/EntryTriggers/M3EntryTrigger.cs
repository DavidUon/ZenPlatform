namespace ZenPlatform.Strategy.EntryTriggers
{
    internal sealed class M3EntryTrigger : IEntryTrigger
    {
        private readonly M1EntryTrigger _baseM1;

        public M3EntryTrigger(ZenPlatform.SessionManager.EntryTriggerRuntimeState state)
        {
            _baseM1 = new M1EntryTrigger(state);
        }

        public void Reset()
        {
            _baseM1.Reset();
        }

        public EntryTriggerSignal OnKBarCompleted(EntryKBarTriggerContext context)
        {
            var m1 = _baseM1.OnKBarCompleted(context);
            if (m1.IsBuy == 0)
            {
                return EntryTriggerSignal.None;
            }

            if (!context.HasCurrentPrice)
            {
                return EntryTriggerSignal.None;
            }

            // M3 extra gate: trigger price must be inside BBIBOLL channel.
            if (!context.HasBbiBoll || context.CurrentPrice < context.BbiDn || context.CurrentPrice > context.BbiUp)
            {
                return EntryTriggerSignal.None;
            }

            var direction = m1.IsBuy;
            if (context.HasA && context.CurrentPrice > context.A)
            {
                direction = 1;
            }
            else if (context.HasV && context.CurrentPrice < context.V)
            {
                direction = -1;
            }

            return new EntryTriggerSignal(direction, "M3", m1.Reason);
        }
    }
}
