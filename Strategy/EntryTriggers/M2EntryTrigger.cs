using System;

namespace ZenPlatform.Strategy.EntryTriggers
{
    internal sealed class M2EntryTrigger : IEntryTrigger
    {
        private readonly ZenPlatform.SessionManager.EntryTriggerRuntimeState _state;
        private readonly Action<string>? _diagnostic;

        public M2EntryTrigger(ZenPlatform.SessionManager.EntryTriggerRuntimeState state, Action<string>? diagnostic = null)
        {
            _state = state ?? throw new System.ArgumentNullException(nameof(state));
            _diagnostic = diagnostic;
        }

        public void Reset()
        {
            _state.M2WaitLongAfterUpTouch = false;
            _state.M2WaitShortAfterDownTouch = false;
            _state.M2HasLastTickPrice = false;
            _state.M2LastTickPrice = 0m;
        }

        public EntryTriggerSignal OnTick(EntryTickTriggerContext context)
        {
            var preWaitLong = _state.M2WaitLongAfterUpTouch;
            var preWaitShort = _state.M2WaitShortAfterDownTouch;
            var hasPrev = _state.M2HasLastTickPrice;
            var prevPrice = _state.M2LastTickPrice;

            EntryTriggerSignal Finish(EntryTriggerSignal signal)
            {
                _state.M2LastTickPrice = context.Price;
                _state.M2HasLastTickPrice = true;
                return signal;
            }

            if (!context.HasBbiBoll)
            {
                _diagnostic?.Invoke($"M2 skip: no-bbiboll, preWaitLong={preWaitLong}, preWaitShort={preWaitShort}");
                return Finish(EntryTriggerSignal.None);
            }

            if (context.CurrentSide == EntryTrendSide.無)
            {
                _state.M2WaitLongAfterUpTouch = false;
                _state.M2WaitShortAfterDownTouch = false;
                _diagnostic?.Invoke($"M2 reset: side=無, preWaitLong={preWaitLong}, preWaitShort={preWaitShort}");
                return Finish(EntryTriggerSignal.None);
            }

            if (context.CurrentSide == EntryTrendSide.多)
            {
                // Direction filter: ignore bearish-side edge when current trend is long.
                _state.M2WaitShortAfterDownTouch = false;

                if (!_state.M2WaitLongAfterUpTouch)
                {
                    if (context.Price >= context.Up)
                    {
                        _state.M2WaitLongAfterUpTouch = true;
                        _diagnostic?.Invoke($"M2 arm-long: price={context.Price:0.##} >= up={context.Up:0.##}");
                    }

                    _diagnostic?.Invoke($"M2 no-fire long: price={context.Price:0.##}, mid={context.Mid:0.##}, waitLong={_state.M2WaitLongAfterUpTouch}, preWaitLong={preWaitLong}");
                    return Finish(EntryTriggerSignal.None);
                }

                // Cross-up only: previous tick below mid, current tick at/above mid.
                if (hasPrev && prevPrice < context.Mid && context.Price >= context.Mid)
                {
                    _state.M2WaitLongAfterUpTouch = false;
                    _diagnostic?.Invoke($"M2 fire-long: prev={prevPrice:0.##} < mid={context.Mid:0.##} && now={context.Price:0.##} >= mid, preWaitLong={preWaitLong}, postWaitLong={_state.M2WaitLongAfterUpTouch}");
                    return Finish(new EntryTriggerSignal(1, "M2", string.Empty));
                }

                _diagnostic?.Invoke($"M2 hold-long: prev={(hasPrev ? prevPrice.ToString("0.##") : "N/A")}, now={context.Price:0.##}, mid={context.Mid:0.##}, waitLong={_state.M2WaitLongAfterUpTouch}");
                return Finish(EntryTriggerSignal.None);
            }

            // CurrentSide == 空
            _state.M2WaitLongAfterUpTouch = false;

            if (!_state.M2WaitShortAfterDownTouch)
            {
                if (context.Price <= context.Dn)
                {
                    _state.M2WaitShortAfterDownTouch = true;
                    _diagnostic?.Invoke($"M2 arm-short: price={context.Price:0.##} <= dn={context.Dn:0.##}");
                }

                _diagnostic?.Invoke($"M2 no-fire short: price={context.Price:0.##}, mid={context.Mid:0.##}, waitShort={_state.M2WaitShortAfterDownTouch}, preWaitShort={preWaitShort}");
                return Finish(EntryTriggerSignal.None);
            }

            // Cross-down only: previous tick above mid, current tick at/below mid.
            if (hasPrev && prevPrice > context.Mid && context.Price <= context.Mid)
            {
                _state.M2WaitShortAfterDownTouch = false;
                _diagnostic?.Invoke($"M2 fire-short: prev={prevPrice:0.##} > mid={context.Mid:0.##} && now={context.Price:0.##} <= mid, preWaitShort={preWaitShort}, postWaitShort={_state.M2WaitShortAfterDownTouch}");
                return Finish(new EntryTriggerSignal(-1, "M2", string.Empty));
            }

            _diagnostic?.Invoke($"M2 hold-short: prev={(hasPrev ? prevPrice.ToString("0.##") : "N/A")}, now={context.Price:0.##}, mid={context.Mid:0.##}, waitShort={_state.M2WaitShortAfterDownTouch}");
            return Finish(EntryTriggerSignal.None);
        }
    }
}
