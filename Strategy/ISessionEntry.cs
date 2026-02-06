using System;

namespace ZenPlatform.Strategy
{
    public interface ISessionEntry
    {
        ISession? CreateSession(int id);
    }
}
