using System.Collections.Generic;

namespace Charts
{
    public class ChartViewConfig
    {
        public int PriceDecimals { get; set; } = 0;
        public List<double> RowHeights { get; set; } = new();
        public List<OverlayConfig> Overlays { get; set; } = new();
        public List<IndicatorConfig> Indicators { get; set; } = new();

        // 十字線和查價視窗顯示設定
        public bool CrosshairVisible { get; set; } = true;
        public bool TooltipVisible { get; set; } = true;
    }

    public class OverlayConfig
    {
        public string Type { get; set; } = string.Empty; // "MA" / "BOLL" / "BBI"
        // MA / BBI
        public int Period { get; set; }
        public string MaType { get; set; } = "MA"; // MA or EMA
        public string? ColorHex { get; set; }
        // BBI
        public string? BbiPeriodsCsv { get; set; }
        // BOLL
        public double K { get; set; }
        public string? FillHex { get; set; }
        public string? MidColorHex { get; set; }
        public string? EdgeColorHex { get; set; }
        public double Opacity { get; set; }
    }

    public class IndicatorConfig
    {
        public string Type { get; set; } = string.Empty; // VOL, KD, MACD
        // KD
        public int Period { get; set; }
        public int SmoothK { get; set; }
        public int SmoothD { get; set; }
        public string? KColorHex { get; set; }
        public string? DColorHex { get; set; }
        // MACD
        public int EMA1 { get; set; }
        public int EMA2 { get; set; }
        public int Day { get; set; }
        public string? DifColorHex { get; set; }
        public string? DeaColorHex { get; set; }
    }
}

