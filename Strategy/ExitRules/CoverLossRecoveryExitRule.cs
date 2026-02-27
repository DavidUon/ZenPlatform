namespace ZenPlatform.Strategy.ExitRules
{
    internal sealed class CoverLossRecoveryExitRule : IExitRule
    {
        public bool TryExecute(ZenPlatform.Strategy.Session session, ZenPlatform.SessionManager.SessionManager manager, ExitRuleContext context)
        {
            if (context.TriggerKind != ExitTriggerKind.Tick)
            {
                return false;
            }

            return session.TryCoverLossRecoveryExit();
        }
    }
}
