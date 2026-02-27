namespace ZenPlatform.Strategy.ExitRules
{
    internal sealed class KBarLossRetraceMaExitRule : IExitRule
    {
        public bool TryExecute(ZenPlatform.Strategy.Session session, ZenPlatform.SessionManager.SessionManager manager, ExitRuleContext context)
        {
            if (context.TriggerKind != ExitTriggerKind.KBarCompleted ||
                context.Period != 1 ||
                context.Bar == null)
            {
                return false;
            }

            return session.TryKBarLossRetraceMaExit(context.Bar, manager);
        }
    }
}
