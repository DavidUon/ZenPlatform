using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public class ChartStyle
    {
        // Colors
        public Brush BackgroundColor { get; set; } = Brushes.Transparent;
        public Brush XAxisTextColor { get; set; } = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        public Brush YAxisTextColor { get; set; } = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        public Brush BullishColor { get; set; } = new SolidColorBrush(Color.FromRgb(255, 76, 76));
        public Brush BearishColor { get; set; } = new SolidColorBrush(Color.FromRgb(40, 240, 80));
        public Brush CrosshairColor { get; set; } = new SolidColorBrush(Color.FromRgb(200, 200, 0));
        public Brush GridLineColor { get; set; } = new SolidColorBrush(Color.FromRgb(50, 50, 50));
        public Brush AxisLineColor { get; set; } = Brushes.White;
        public Brush AlignmentLineColor { get; set; } = new SolidColorBrush(Color.FromRgb(125, 125, 125));

        // Fonts
        public double AxisFontSize { get; set; } = 14;

        // Layout & Spacing
        public double LeftPanelWidth { get; set; } = 120; // 統一左側資訊區寬度，確保主/副圖對齊
        public double YAxisWidth { get; set; } = 60;
        public double XAxisHeight { get; set; } = 33;
        public Thickness ChartMargin { get; set; } = new Thickness(10, 0, 30, 0); // Left, Top, Right, Bottom

        // Bar Style
        public double DefaultBarSpacing { get; set; } = 13;
        public double DefaultBarWidth { get; set; } = 11;

        // High/Low Labels
        public Brush HighPriceLabelColor { get; set; } = new SolidColorBrush(Color.FromRgb(255, 76, 76));
        public Brush LowPriceLabelColor { get; set; } = new SolidColorBrush(Color.FromRgb(40, 240, 80));
        public double HighLowFontSize { get; set; } = ChartFontManager.GetFontSize("ChartFontSizeMd", 16);

        // Axis density
        // 目標每兩條Y軸水平線之間的像素距離，數字越大格線越稀疏
        public double YAxisTargetPixelsPerStep { get; set; } = 120;
        public int YAxisMinTickCount { get; set; } = 5;   // 最少顯示線數
        public int YAxisMaxTickCount { get; set; } = 12;  // 最多顯示線數（避免太密）

        // X 軸密度設定
        // 時間標籤之間的最小像素距離（避免文字重疊）
        public double XAxisMinLabelSpacing { get; set; } = 80;
        // 垂直格線之間的最小像素距離（用來決定 30/60/120 分鐘刻度）
        public double XAxisMinGridSpacing { get; set; } = 50;
    }
}
