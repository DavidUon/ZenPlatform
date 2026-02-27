namespace ZenPlatform.Strategy.ExitRules
{
    internal interface IExitRule
    {
        bool TryExecute(ZenPlatform.Strategy.Session session, ZenPlatform.SessionManager.SessionManager manager, ExitRuleContext context);
    }
}
