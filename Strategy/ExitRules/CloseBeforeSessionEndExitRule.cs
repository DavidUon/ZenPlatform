namespace ZenPlatform.Strategy.ExitRules
{
    internal sealed class CloseBeforeSessionEndExitRule : IExitRule
    {
        public bool TryExecute(ZenPlatform.Strategy.Session session, ZenPlatform.SessionManager.SessionManager manager, ExitRuleContext context)
        {
            return session.TryCloseBeforeSessionEndExit(manager);
        }
    }
}
