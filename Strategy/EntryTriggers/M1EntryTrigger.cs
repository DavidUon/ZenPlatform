namespace ZenPlatform.Strategy.EntryTriggers
{
    internal sealed class M1EntryTrigger : IEntryTrigger
    {
        private const int BbiBollPeriodMinutes = 10;
        private const int MacdTriggerPeriodMinutes = 5;
        private readonly ZenPlatform.SessionManager.EntryTriggerRuntimeState _state;

        public M1EntryTrigger(ZenPlatform.SessionManager.EntryTriggerRuntimeState state)
        {
            _state = state ?? throw new System.ArgumentNullException(nameof(state));
        }

        public void Reset()
        {
            _state.M1WaitShortAfterUpBreak = false;
            _state.M1WaitLongAfterDownBreak = false;
        }

        public EntryTriggerSignal OnKBarCompleted(EntryKBarTriggerContext context)
        {
            if (context.Period == BbiBollPeriodMinutes)
            {
                if (context.HasBbiBoll)
                {
                    if (context.IsFullyAboveUp)
                    {
                        _state.M1WaitShortAfterUpBreak = true;
                        _state.M1WaitLongAfterDownBreak = false;
                    }
                    else if (context.IsFullyBelowDown)
                    {
                        _state.M1WaitLongAfterDownBreak = true;
                        _state.M1WaitShortAfterUpBreak = false;
                    }
                }

                return EntryTriggerSignal.None;
            }

            if (context.Period != MacdTriggerPeriodMinutes)
            {
                return EntryTriggerSignal.None;
            }

            // MacdTrigger: 1=空觸發, -1=多觸發
            if (_state.M1WaitShortAfterUpBreak && context.MacdTriggerState == 1)
            {
                _state.M1WaitShortAfterUpBreak = false;
                _state.M1WaitLongAfterDownBreak = false;
                return new EntryTriggerSignal(-1, "M1", string.Empty);
            }

            if (_state.M1WaitLongAfterDownBreak && context.MacdTriggerState == -1)
            {
                _state.M1WaitShortAfterUpBreak = false;
                _state.M1WaitLongAfterDownBreak = false;
                return new EntryTriggerSignal(1, "M1", string.Empty);
            }

            return EntryTriggerSignal.None;
        }
    }
}
