using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    public class MaOverlay : IOverlayIndicator
    {
        public string TagName => "均線";

        private readonly string _maType; // "SMA" or "EMA"
        private int _period;
        public int Period => _period;
        public string MaType => _maType;
        private readonly SolidColorBrush _lineBrush;
        private readonly double _thickness;

        private List<GraphKBar> _bars = new();
        private double[] _ma = Array.Empty<double>();
        private int _visStart, _visCount; private double _spacing;

        public MaOverlay(int period, string maType, Color color, double thickness = 1.0)
        {
            _period = Math.Max(1, period);
            _maType = (maType?.ToUpperInvariant() == "EMA") ? "EMA" : "SMA";
            _lineBrush = new SolidColorBrush(color);
            _thickness = thickness;
        }

        public Color LineColor => _lineBrush.Color;

        public void OnDataChanged(List<GraphKBar> bars)
        {
            _bars = bars;
            if (!TryLoadFromIndicators())
            {
                Recalc();
            }
        }

        public void OnViewportChanged(int visibleStart, int visibleCount, double spacing)
        {
            _visStart = visibleStart; _visCount = visibleCount; _spacing = spacing;
        }

        public void OnCrosshairIndexChanged(int visibleIndex, bool isValid)
        {
            // no-op
        }

        public void Draw(Canvas layer, ChartPane pane)
        {
            if (_bars.Count == 0 || _visCount <= 0 || _ma.Length == 0) return;
            layer.Clip = new RectangleGeometry(pane.GetChartDrawRect());

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                int start = Math.Max(0, _visStart);
                int end = Math.Min(_bars.Count - 1, _visStart + _visCount - 1);
                for (int vis = 0; vis <= end - start; vis++)
                {
                    int idx = start + vis;
                    double xLeft = pane.XLeftByVisibleIndex(vis, _visCount, _spacing);
                    double xCenter = xLeft + pane.HalfBarWidth();
                    double y = pane.PriceToY((decimal)_ma[idx]);
                    Point p = new Point(pane.ChartTheme.ChartMargin.Left + xCenter, pane.ChartTheme.ChartMargin.Top + y);
                    if (!started) { g.BeginFigure(p, false, false); started = true; }
                    else g.LineTo(p, true, false);
                }
            }
            geo.Freeze();
            var path = new Path { Data = geo, Stroke = _lineBrush, StrokeThickness = _thickness };
            layer.Children.Add(path);
        }

        public IEnumerable<PriceInfoPanel.InfoLine> GetInfoLines(int dataIndex, int prevDataIndex, int priceDecimals)
        {
            var list = new List<PriceInfoPanel.InfoLine>();
            if (_ma.Length == 0) return list;
            int i = Math.Max(0, Math.Min(_ma.Length - 1, dataIndex));
            int p = Math.Max(0, Math.Min(_ma.Length - 1, prevDataIndex));
            int dir = _ma[i] > _ma[p] ? 1 : (_ma[i] < _ma[p] ? -1 : 0);
            string fmt = "F" + Math.Max(0, priceDecimals).ToString();
            list.Add(new PriceInfoPanel.InfoLine { Label = $"MA{_period}:", ValueText = _ma[i].ToString(fmt), ValueBrush = _lineBrush, ArrowDir = dir });
            return list;
        }

        private void Recalc()
        {
            int n = Math.Max(1, _period);
            int count = _bars.Count;
            _ma = new double[count];
            if (count == 0) return;
            string key = _maType == "EMA" ? $"EMA{_period}" : $"MA{_period}";

            if (_maType == "EMA")
            {
                double alpha = 2.0 / (n + 1);
                double ema = (double)_bars[0].Close;
                for (int i = 0; i < count; i++)
                {
                    double c = (double)_bars[i].Close;
                    ema = ema + alpha * (c - ema);
                    _ma[i] = ema;
                    _bars[i].Indicators[key] = (decimal)ema;
                }
            }
            else // SMA
            {
                Queue<double> win = new();
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    double c = (double)_bars[i].Close;
                    win.Enqueue(c); sum += c;
                    if (win.Count > n) sum -= win.Dequeue();
                    _ma[i] = sum / win.Count;
                    _bars[i].Indicators[key] = (decimal)_ma[i];
                }
            }
        }

        private bool TryLoadFromIndicators()
        {
            if (_bars.Count == 0) return false;
            string key = _maType == "EMA" ? $"EMA{_period}" : $"MA{_period}";
            _ma = new double[_bars.Count];
            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i].Indicators == null || !_bars[i].Indicators.TryGetValue(key, out var v))
                {
                    return false;
                }
                _ma[i] = (double)v;
            }
            return true;
        }
    }
}
