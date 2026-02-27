using System.Windows;

namespace Charts
{
    public enum IndicatorPanelType
    {
        Vol,
        Macd,
        Kd,
        Atr,
        Ha
    }

    public static class IndicatorPaneFactory
    {
        public static ChartPane Create(IndicatorPanelType type)
        {
            return type switch
            {
                IndicatorPanelType.Vol => new VolumePane(),
                IndicatorPanelType.Macd => new MacdPane(),
                IndicatorPanelType.Kd => new KdPane(),
                IndicatorPanelType.Atr => new AtrPane(),
                IndicatorPanelType.Ha => new HaPane(),
                _ => new VolumePane()
            };
        }
    }
}
