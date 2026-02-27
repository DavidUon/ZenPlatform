namespace ZenPlatform.Strategy.ExitRules
{
    internal sealed class AutoStopLossExitRule : IExitRule
    {
        public bool TryExecute(ZenPlatform.Strategy.Session session, ZenPlatform.SessionManager.SessionManager manager, ExitRuleContext context)
        {
            if (context.TriggerKind != ExitTriggerKind.KBarCompleted)
            {
                return false;
            }

            if (manager.RuleSet.StopLossMode != ZenPlatform.Strategy.StopLossMode.Auto ||
                context.Period != ZenPlatform.Strategy.Session.AutoStopLossRulePeriodMinutes ||
                context.Bar == null)
            {
                return false;
            }

            return session.TryAutoStopLossByFiveMinuteKBar(context.Bar, manager);
        }
    }
}
