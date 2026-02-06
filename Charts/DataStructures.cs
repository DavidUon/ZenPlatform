using System.Windows.Media;

namespace Charts
{
    /// <summary>
    /// 作為圖表對外接收資料的公開標準格式
    /// </summary>
    public class ChartKBar
    {
        public DateTime Time { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public int Volume { get; set; }

        /// <summary>
        /// 是否為對齊線K棒 (例如，交易日的開盤第一根)，會觸發繪製特殊的垂直格線
        /// </summary>
        public bool IsAlignmentBar { get; set; } = false;

        /// <summary>
        /// 是否為浮動K棒 (尚未收盤的最新一根K棒)
        /// </summary>
        public bool IsFloating { get; set; } = false;
    }

    // Data structures for the tooltips
    public struct OhlcData
    {
        public string Open { get; set; }
        public string High { get; set; }
        public string Low { get; set; }
        public string Close { get; set; }
    }

    public struct MaValue
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public SolidColorBrush Color { get; set; }
    }

    [Flags]
    public enum KBarTag
    {
        none = 0,
        xAxisMarkSplit = 1 << 0,
    }

    // 內部繪圖用的K棒結構，它會從公開的 ChartKBar 轉換而來
    public class GraphKBar
    {
        public DateTime Time { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public int Volume { get; set; }
        public KBarTag Tag { get; set; }
        public bool IsNullBar { get; set; } // For floating bar initialization

        public GraphKBar() { IsNullBar = true; }

        public GraphKBar(ChartKBar kBar)
        {
            Time = kBar.Time;
            Open = kBar.Open;
            High = kBar.High;
            Low = kBar.Low;
            Close = kBar.Close;
            Volume = kBar.Volume;
            Tag = kBar.IsAlignmentBar ? KBarTag.xAxisMarkSplit : KBarTag.none;
            IsNullBar = false;
        }
    }
}
