using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    public class SarOverlay : IOverlayIndicator
    {
        public string TagName => "SAR";
        private const string KeySar = "SAR";
        private const string KeyMetaStep = "SAR_META_STEP";
        private const string KeyMetaMax = "SAR_META_MAX";

        private readonly SolidColorBrush _dotBrush;
        private readonly double _dotSize;
        private decimal _step;
        private decimal _max;

        private List<GraphKBar> _bars = new();
        private decimal?[] _sar = Array.Empty<decimal?>();
        private int _visStart, _visCount; private double _spacing;

        public SarOverlay() : this(0.02m, 0.2m, Color.FromRgb(0x00, 0xC8, 0xFF)) { }

        public SarOverlay(decimal step, decimal max, Color color, double dotSize = 3.0)
        {
            _step = Math.Max(0.0001m, step);
            _max = Math.Max(_step, max);
            _dotBrush = new SolidColorBrush(color);
            _dotSize = Math.Max(2.0, dotSize);
        }

        public decimal Step => _step;
        public decimal Max => _max;
        public Color DotColor => _dotBrush.Color;

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
            if (_bars.Count == 0 || _visCount <= 0 || _sar.Length == 0) return;
            layer.Clip = new RectangleGeometry(pane.GetChartDrawRect());

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                int start = Math.Max(0, _visStart);
                int end = Math.Min(_bars.Count - 1, _visStart + _visCount - 1);
                double half = _dotSize / 2.0;
                for (int vis = 0; vis <= end - start; vis++)
                {
                    int idx = start + vis;
                    var v = _sar[idx];
                    if (!v.HasValue) continue;
                    double xLeft = pane.XLeftByVisibleIndex(vis, _visCount, _spacing);
                    double xCenter = xLeft + pane.HalfBarWidth();
                    double y = pane.PriceToY(v.Value);
                    Point p1 = new Point(pane.ChartTheme.ChartMargin.Left + xCenter - half, pane.ChartTheme.ChartMargin.Top + y - half);
                    Point p2 = new Point(pane.ChartTheme.ChartMargin.Left + xCenter + half, pane.ChartTheme.ChartMargin.Top + y - half);
                    Point p3 = new Point(pane.ChartTheme.ChartMargin.Left + xCenter + half, pane.ChartTheme.ChartMargin.Top + y + half);
                    Point p4 = new Point(pane.ChartTheme.ChartMargin.Left + xCenter - half, pane.ChartTheme.ChartMargin.Top + y + half);
                    g.BeginFigure(p1, true, true);
                    g.LineTo(p2, true, false);
                    g.LineTo(p3, true, false);
                    g.LineTo(p4, true, false);
                }
            }
            geo.Freeze();
            var path = new Path { Data = geo, Fill = _dotBrush };
            layer.Children.Add(path);
        }

        public IEnumerable<PriceInfoPanel.InfoLine> GetInfoLines(int dataIndex, int prevDataIndex, int priceDecimals)
        {
            var list = new List<PriceInfoPanel.InfoLine>();
            if (_sar.Length == 0) return list;
            int i = Math.Max(0, Math.Min(_sar.Length - 1, dataIndex));
            int p = Math.Max(0, Math.Min(_sar.Length - 1, prevDataIndex));
            var currentSar = _sar[i];
            if (!currentSar.HasValue) return list;
            var currentValue = currentSar.Value;
            int dir = 0;
            var prevSar = _sar[p];
            if (prevSar.HasValue)
                dir = currentValue > prevSar.Value ? 1 : (currentValue < prevSar.Value ? -1 : 0);
            string fmt = "F" + Math.Max(0, priceDecimals).ToString();
            list.Add(new PriceInfoPanel.InfoLine { Label = "SAR:", ValueText = currentValue.ToString(fmt), ValueBrush = _dotBrush, ArrowDir = dir });
            return list;
        }

        private void Recalc()
        {
            int count = _bars.Count;
            _sar = new decimal?[count];
            if (count == 0) return;

            if (count == 1)
            {
                _sar[0] = _bars[0].Low;
                _bars[0].Indicators[KeySar] = _sar[0]!.Value;
                _bars[0].Indicators[KeyMetaStep] = _step;
                _bars[0].Indicators[KeyMetaMax] = _max;
                return;
            }

            bool up = _bars[1].Close >= _bars[0].Close;
            decimal sar = up ? _bars[0].Low : _bars[0].High;
            decimal ep = up ? Math.Max(_bars[0].High, _bars[1].High) : Math.Min(_bars[0].Low, _bars[1].Low);
            decimal af = _step;

            _sar[0] = sar;
            _sar[1] = sar;
            _bars[0].Indicators[KeySar] = sar;
            _bars[0].Indicators[KeyMetaStep] = _step;
            _bars[0].Indicators[KeyMetaMax] = _max;
            _bars[1].Indicators[KeySar] = sar;
            _bars[1].Indicators[KeyMetaStep] = _step;
            _bars[1].Indicators[KeyMetaMax] = _max;

            for (int i = 2; i < count; i++)
            {
                sar = sar + af * (ep - sar);

                if (up)
                {
                    decimal low1 = _bars[i - 1].Low;
                    decimal low2 = _bars[i - 2].Low;
                    if (sar > low1) sar = low1;
                    if (sar > low2) sar = low2;

                    if (_bars[i].Low < sar)
                    {
                        up = false;
                        sar = ep;
                        ep = _bars[i].Low;
                        af = _step;
                    }
                    else
                    {
                        if (_bars[i].High > ep)
                        {
                            ep = _bars[i].High;
                            af = Math.Min(_max, af + _step);
                        }
                    }
                }
                else
                {
                    decimal high1 = _bars[i - 1].High;
                    decimal high2 = _bars[i - 2].High;
                    if (sar < high1) sar = high1;
                    if (sar < high2) sar = high2;

                    if (_bars[i].High > sar)
                    {
                        up = true;
                        sar = ep;
                        ep = _bars[i].High;
                        af = _step;
                    }
                    else
                    {
                        if (_bars[i].Low < ep)
                        {
                            ep = _bars[i].Low;
                            af = Math.Min(_max, af + _step);
                        }
                    }
                }

                _sar[i] = sar;
                _bars[i].Indicators[KeySar] = sar;
                _bars[i].Indicators[KeyMetaStep] = _step;
                _bars[i].Indicators[KeyMetaMax] = _max;
            }
        }

        private bool TryLoadFromIndicators()
        {
            if (_bars.Count == 0) return false;
            _sar = new decimal?[_bars.Count];
            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i].Indicators == null ||
                    !_bars[i].Indicators.TryGetValue(KeyMetaStep, out var step) ||
                    !_bars[i].Indicators.TryGetValue(KeyMetaMax, out var max) ||
                    step != _step ||
                    max != _max ||
                    !_bars[i].Indicators.TryGetValue(KeySar, out var v))
                {
                    return false;
                }
                _sar[i] = v;
            }
            return true;
        }
    }
}
