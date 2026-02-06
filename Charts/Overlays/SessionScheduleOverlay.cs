using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    // 依據來源清單繪製 Session 時間範圍水平線；來源交由 MultiPaneChartView 綁定
    public class SessionScheduleOverlay : IOverlayIndicator
    {
        public string TagName => "SessionSchedule";

        private IList? _source; // IList of Utility.SessionScheduleLine
        public void SetSource(IList? source) { _source = source; }

        private List<GraphKBar> _bars = new();
        private int _visibleStart;
        private int _visibleCount;
        private double _spacing;

        public void OnDataChanged(List<GraphKBar> bars) => _bars = bars ?? new List<GraphKBar>();
        public void OnViewportChanged(int visibleStart, int visibleCount, double spacing)
        {
            _visibleStart = visibleStart;
            _visibleCount = visibleCount;
            _spacing = spacing;
        }
        public void OnCrosshairIndexChanged(int visibleIndex, bool isValid) { }

        public void Draw(Canvas layer, ChartPane pane)
        {
            if (_source == null || _visibleCount <= 0) return;
            var rect = pane.GetChartDrawRect();
            if (rect.Width <= 0 || rect.Height <= 0) return;

            foreach (var obj in _source)
            {
                if (obj is not Utility.SessionScheduleLine ln) continue;
                if (!ln.LineVisible) continue;

                // Y 位置：若超出可視價格範圍，將其夾在上下邊界，避免整條線看起來消失
                double yRaw = rect.Y + pane.PriceToY(ln.LinePrice);
                double y = Math.Max(rect.Y + 1, Math.Min(rect.Y + rect.Height - 1, yRaw));

                // X 位置：時間到座標
                double rawX1 = TimeToX(rect, pane, ln.LineStartTime);
                double rawX2 = ln.LineEndTime.HasValue ? TimeToX(rect, pane, ln.LineEndTime.Value) : rect.X + rect.Width;

                // 是否完全在右側（尚未開始且進行中）
                bool isFutureOngoing = !ln.LineEndTime.HasValue && rawX1 > rect.X + rect.Width;

                // clamp 後的可視線段
                double x1 = Math.Max(rect.X, Math.Min(rect.X + rect.Width, rawX1));
                double x2 = Math.Max(rect.X, Math.Min(rect.X + rect.Width, rawX2));

                var stroke = new SolidColorBrush(ln.LineColor);

                if (x2 > x1)
                {
                    // 正常情況：與視窗有交集，畫完整線段
                    var seg = new Line { X1 = x1, X2 = x2, Y1 = y, Y2 = y, Stroke = stroke, StrokeThickness = 1.2 };
                    layer.Children.Add(seg);

                    if (!string.IsNullOrWhiteSpace(ln.LineLabel))
                    {
                        var tb = new TextBlock { Text = ln.LineLabel, Foreground = Brushes.White, FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14), Margin = new System.Windows.Thickness(6, 2, 6, 2) };
                        tb.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                        var bd = new Border { Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), BorderBrush = stroke, BorderThickness = new System.Windows.Thickness(1), CornerRadius = new System.Windows.CornerRadius(3), Child = tb };
                        layer.Children.Add(bd);
                        const double labelWidth = 160; const double labelHeight = 24;
                        bd.Width = labelWidth; bd.Height = labelHeight;
                        double labelX = ln.LineEndTime.HasValue ? Math.Min(rect.X + rect.Width - labelWidth - 4, Math.Max(rect.X + 4, x2 - labelWidth - 8)) : rect.X + rect.Width - labelWidth - 4;
                        Canvas.SetLeft(bd, labelX); Canvas.SetTop(bd, y - labelHeight / 2.0);
                    }
                }
                else if (isFutureOngoing)
                {
                    // 邊界提示：任務未開始但完全在右側，於右邊框畫短線＋價籤
                    double hintX2 = rect.X + rect.Width - 2;
                    double hintX1 = hintX2 - 10; // 10px 短線
                    var seg = new Line { X1 = hintX1, X2 = hintX2, Y1 = y, Y2 = y, Stroke = stroke, StrokeThickness = 1.2 };
                    layer.Children.Add(seg);

                    if (!string.IsNullOrWhiteSpace(ln.LineLabel))
                    {
                        var tb = new TextBlock { Text = ln.LineLabel, Foreground = Brushes.White, FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14), Margin = new System.Windows.Thickness(6, 2, 6, 2) };
                        tb.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                        var bd = new Border { Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), BorderBrush = stroke, BorderThickness = new System.Windows.Thickness(1), CornerRadius = new System.Windows.CornerRadius(3), Child = tb };
                        layer.Children.Add(bd);
                        const double labelWidth = 160; const double labelHeight = 24;
                        bd.Width = labelWidth; bd.Height = labelHeight;
                        double labelX = rect.X + rect.Width - labelWidth - 4;
                        Canvas.SetLeft(bd, labelX); Canvas.SetTop(bd, y - labelHeight / 2.0);
                    }
                }
            }
        }

        public IEnumerable<PriceInfoPanel.InfoLine> GetInfoLines(int dataIndex, int prevDataIndex, int priceDecimals)
        {
            // 本 overlay 僅負責繪製時間段水平線，不提供資訊面板數據
            yield break;
        }

        private double TimeToX(System.Windows.Rect rect, ChartPane pane, DateTime t)
        {
            if (_bars == null || _bars.Count == 0) return rect.X;
            int idx = LowerBoundByTime(t);
            int visIdx = idx - _visibleStart;
            return rect.X + pane.XLeftByVisibleIndex(visIdx, _visibleCount, _spacing) + pane.HalfBarWidth();
        }

        private int LowerBoundByTime(DateTime t)
        {
            int lo = 0, hi = _bars.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_bars[mid].Time < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }
}
