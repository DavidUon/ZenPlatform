using System.Windows;

namespace Charts
{
    public enum IndicatorPanelType
    {
        Vol,
        Macd,
        Kd
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
                _ => new VolumePane()
            };
        }
    }
}

