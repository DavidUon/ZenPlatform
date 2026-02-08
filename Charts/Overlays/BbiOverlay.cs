using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    public class BbiOverlay : IOverlayIndicator
    {
        public string TagName => "BBI";

        private readonly int[] _periods;
        private readonly SolidColorBrush _lineBrush;
        private readonly double _thickness;

        private List<GraphKBar> _bars = new();
        private double[] _bbi = Array.Empty<double>();
        private int _visStart, _visCount; private double _spacing;

        public BbiOverlay() : this(new[] { 5, 10, 30, 60 }, Color.FromRgb(0xE9, 0x1E, 0x1F), 1.0) { }

        public BbiOverlay(IEnumerable<int> periods, Color color, double thickness = 1.0)
        {
            _periods = (periods ?? Array.Empty<int>()).Select(p => Math.Max(1, p)).Distinct().OrderBy(p => p).ToArray();
            if (_periods.Length == 0) _periods = new[] { 3, 6, 12, 24 };
            _lineBrush = new SolidColorBrush(color);
            _thickness = thickness;
        }

        public IReadOnlyList<int> Periods => _periods;
        public Color LineColor => _lineBrush.Color;

        public int[] GetParameters() => _periods.ToArray();

        public void OnDataChanged(List<GraphKBar> bars)
        {
            _bars = bars;
            Recalc();
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
            if (_bars.Count == 0 || _visCount <= 0 || _bbi.Length == 0) return;
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
                    double y = pane.PriceToY((decimal)_bbi[idx]);
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
            if (_bbi.Length == 0) return list;
            int i = Math.Max(0, Math.Min(_bbi.Length - 1, dataIndex));
            int p = Math.Max(0, Math.Min(_bbi.Length - 1, prevDataIndex));
            int dir = _bbi[i] > _bbi[p] ? 1 : (_bbi[i] < _bbi[p] ? -1 : 0);
            string fmt = "F" + Math.Max(0, priceDecimals).ToString();
            list.Add(new PriceInfoPanel.InfoLine { Label = "BBI:", ValueText = _bbi[i].ToString(fmt), ValueBrush = _lineBrush, ArrowDir = dir });
            return list;
        }

        private void Recalc()
        {
            int count = _bars.Count;
            _bbi = new double[count];
            if (count == 0) return;

            int pcount = _periods.Length;
            var wins = new Queue<double>[pcount];
            var sums = new double[pcount];
            for (int i = 0; i < pcount; i++) wins[i] = new Queue<double>();

            for (int i = 0; i < count; i++)
            {
                double c = (double)_bars[i].Close;
                double avg = 0;
                for (int pi = 0; pi < pcount; pi++)
                {
                    int n = _periods[pi];
                    var win = wins[pi];
                    win.Enqueue(c); sums[pi] += c;
                    if (win.Count > n) sums[pi] -= win.Dequeue();
                    double ma = sums[pi] / win.Count;
                    avg += ma;
                }
                _bbi[i] = avg / pcount;
                _bars[i].Indicators["BBI"] = (decimal)_bbi[i];
            }
        }
    }
}
