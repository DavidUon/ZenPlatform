using System;

namespace ZenPlatform.Indicators
{
    public readonly record struct KBar(
        DateTime Time,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        int Volume);
}
