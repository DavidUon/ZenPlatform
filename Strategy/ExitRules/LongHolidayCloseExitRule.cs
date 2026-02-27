namespace ZenPlatform.Strategy.ExitRules
{
    internal sealed class LongHolidayCloseExitRule : IExitRule
    {
        public bool TryExecute(ZenPlatform.Strategy.Session session, ZenPlatform.SessionManager.SessionManager manager, ExitRuleContext context)
        {
            return session.TryCloseBeforeLongHolidayExit(manager);
        }
    }
}
