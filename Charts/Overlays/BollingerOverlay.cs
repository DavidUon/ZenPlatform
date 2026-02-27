using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;

namespace Charts
{
    public class BollingerOverlay : IOverlayIndicator
    {
        public string TagName => "布林通道";
        private const string KeyMid = "BOLL_MID";
        private const string KeyUp = "BOLL_UP";
        private const string KeyDn = "BOLL_DN";
        private const string KeyMetaPeriod = "BOLL_META_PERIOD";
        private const string KeyMetaK = "BOLL_META_K";

        private int _period = 20;
        private double _k = 2.0;
        private List<GraphKBar> _bars = new();

        // 計算結果
        private double[] _mid = Array.Empty<double>();
        private double[] _up = Array.Empty<double>();
        private double[] _dn = Array.Empty<double>();

        // 可視範圍
        private int _visStart, _visCount;
        private double _spacing;

        // 樣式
        private Brush _midBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0xD4, 0xD5));
        private Brush _bandBrush = new SolidColorBrush(Color.FromArgb(15, 0xB7, 0xB8, 0xB7));
        private Brush _edgeBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xB4));
        private Color _fillColor = Color.FromRgb(0xB7, 0xB8, 0xB7);
        private double _fillOpacity = 0.059597315436241624; // 0..1

        public BollingerOverlay() { }
        public BollingerOverlay(int period, double k) { _period = Math.Max(1, period); _k = k; }
        public BollingerOverlay(int period, double k, Color fillColor, double opacity)
        {
            _period = Math.Max(1, period); _k = k; _fillColor = fillColor; _fillOpacity = Math.Max(0, Math.Min(1, opacity));
            UpdateBandBrush();
        }
        public BollingerOverlay(int period, double k, Color fillColor, double opacity, Color edgeColor, Color midColor)
        {
            _period = Math.Max(1, period); _k = k; _fillColor = fillColor; _fillOpacity = Math.Max(0, Math.Min(1, opacity));
            _edgeBrush = new SolidColorBrush(edgeColor);
            _midBrush = new SolidColorBrush(midColor);
            UpdateBandBrush();
        }

        public (int period, double k) GetParameters() => (_period, _k);
        public (Color fill, double opacity, Color edge, Color mid) GetAppearance()
            => (_fillColor, _fillOpacity, ((SolidColorBrush)_edgeBrush).Color, ((SolidColorBrush)_midBrush).Color);
        public void SetParameters(int period, double k) { _period = Math.Max(1, period); _k = k; Recalc(); }
        public void SetAppearance(Color fill, double opacity)
        {
            _fillColor = fill; _fillOpacity = Math.Max(0, Math.Min(1, opacity));
            UpdateBandBrush();
        }
        public void SetEdgeColor(Color edge)
        {
            _edgeBrush = new SolidColorBrush(edge);
        }
        public void SetMidColor(Color mid)
        {
            _midBrush = new SolidColorBrush(mid);
        }

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
            // 暫不顯示數值；若未來要顯示，可在此更新主圖左側或 tooltip
        }

        public void Draw(Canvas layer, ChartPane pane)
        {
            if (_bars.Count == 0 || _visCount <= 0) return;
            // 裁切在主圖可繪區域內
            layer.Clip = new RectangleGeometry(pane.GetChartDrawRect());

            int start = Math.Max(0, _visStart);
            int end = Math.Min(_bars.Count - 1, _visStart + _visCount - 1);
            if (end <= start) return;

            var bandGeo = new StreamGeometry();
            var upGeo = new StreamGeometry();
            var dnGeo = new StreamGeometry();
            var midGeo = new StreamGeometry();

            using (var gBand = bandGeo.Open())
            using (var gUp = upGeo.Open())
            using (var gDn = dnGeo.Open())
            using (var gMid = midGeo.Open())
            {
                bool bandStarted = false;
                for (int vis = 0; vis <= end - start; vis++)
                {
                    int idx = start + vis;
                    double xLeft = pane.XLeftByVisibleIndex(vis, _visCount, _spacing);
                    double xCenter = xLeft + pane.HalfBarWidth();

                    if (idx < 0 || idx >= _mid.Length) continue;
                    double yMid = pane.PriceToY((decimal)_mid[idx]);
                    double yUp = pane.PriceToY((decimal)_up[idx]);
                    double yDn = pane.PriceToY((decimal)_dn[idx]);

                    Point pm = new Point(pane.ChartTheme.ChartMargin.Left + xCenter, pane.ChartTheme.ChartMargin.Top + yMid);
                    Point pu = new Point(pane.ChartTheme.ChartMargin.Left + xCenter, pane.ChartTheme.ChartMargin.Top + yUp);
                    Point pd = new Point(pane.ChartTheme.ChartMargin.Left + xCenter, pane.ChartTheme.ChartMargin.Top + yDn);

                    if (vis == 0)
                    {
                        gMid.BeginFigure(pm, false, false);
                        gUp.BeginFigure(pu, false, false);
                        gDn.BeginFigure(pd, false, false);
                    }
                    else
                    {
                        gMid.LineTo(pm, true, false);
                        gUp.LineTo(pu, true, false);
                        gDn.LineTo(pd, true, false);
                    }

                    // 帶：上緣向右，之後再回來連下緣
                    if (!bandStarted)
                    {
                        gBand.BeginFigure(pu, true, true);
                        bandStarted = true;
                    }
                    else
                    {
                        gBand.LineTo(pu, true, false);
                    }
                }

                // 走回下緣
                for (int vis = end - start; vis >= 0; vis--)
                {
                    int idx = start + vis;
                    if (idx < 0 || idx >= _dn.Length) continue;
                    double xLeft = pane.XLeftByVisibleIndex(vis, _visCount, _spacing);
                    double xCenter = xLeft + pane.HalfBarWidth();
                    double yDn = pane.PriceToY((decimal)_dn[idx]);
                    Point pd = new Point(pane.ChartTheme.ChartMargin.Left + xCenter, pane.ChartTheme.ChartMargin.Top + yDn);
                    gBand.LineTo(pd, true, false);
                }
            }

            bandGeo.Freeze(); upGeo.Freeze(); dnGeo.Freeze(); midGeo.Freeze();
            var bandPath = new Path { Data = bandGeo, Fill = _bandBrush };
            var upPath = new Path { Data = upGeo, Stroke = _edgeBrush, StrokeThickness = 1 };
            var dnPath = new Path { Data = dnGeo, Stroke = _edgeBrush, StrokeThickness = 1 };
            var midPath = new Path { Data = midGeo, Stroke = _midBrush, StrokeThickness = 1 };

            layer.Children.Add(bandPath);
            layer.Children.Add(upPath);
            layer.Children.Add(dnPath);
            layer.Children.Add(midPath);
        }

        private void Recalc()
        {
            int n = Math.Max(1, _period);
            int count = _bars.Count;
            _mid = new double[count];
            _up = new double[count];
            _dn = new double[count];
            if (count == 0) return;

            double sum = 0;
            Queue<double> win = new();
            for (int i = 0; i < count; i++)
            {
                double c = (double)_bars[i].Close;
                win.Enqueue(c);
                sum += c;
                if (win.Count > n) sum -= win.Dequeue();
                double ma = sum / win.Count;
                _mid[i] = ma;
                // std
                double var = 0; foreach (var v in win) { double d = v - ma; var += d * d; }
                double std = Math.Sqrt(var / win.Count);
                _up[i] = ma + _k * std; _dn[i] = ma - _k * std;

                if (i < _bars.Count)
                {
                    var indicators = _bars[i].Indicators;
                    indicators[KeyMid] = (decimal)_mid[i];
                    indicators[KeyUp] = (decimal)_up[i];
                    indicators[KeyDn] = (decimal)_dn[i];
                    indicators[KeyMetaPeriod] = _period;
                    indicators[KeyMetaK] = (decimal)_k;
                }
            }
        }

        private bool TryLoadFromIndicators()
        {
            if (_bars.Count == 0) return false;
            _mid = new double[_bars.Count];
            _up = new double[_bars.Count];
            _dn = new double[_bars.Count];
            for (int i = 0; i < _bars.Count; i++)
            {
                var ind = _bars[i].Indicators;
                if (ind == null) return false;
                if (!ind.TryGetValue(KeyMetaPeriod, out var cachedPeriod) ||
                    !ind.TryGetValue(KeyMetaK, out var cachedK))
                {
                    return false;
                }

                if ((int)cachedPeriod != _period || Math.Abs((double)cachedK - _k) > 1e-12)
                {
                    return false;
                }

                if (!ind.TryGetValue(KeyMid, out var mid) ||
                    !ind.TryGetValue(KeyUp, out var up) ||
                    !ind.TryGetValue(KeyDn, out var dn))
                {
                    return false;
                }
                _mid[i] = (double)mid;
                _up[i] = (double)up;
                _dn[i] = (double)dn;
            }
            return true;
        }

        private void UpdateBandBrush()
        {
            byte a = (byte)Math.Round(255 * _fillOpacity);
            _bandBrush = new SolidColorBrush(Color.FromArgb(a, _fillColor.R, _fillColor.G, _fillColor.B));
            _bandBrush.Freeze();
        }

        public IEnumerable<PriceInfoPanel.InfoLine> GetInfoLines(int dataIndex, int prevDataIndex, int priceDecimals)
        {
            var list = new List<PriceInfoPanel.InfoLine>();
            if (_mid.Length == 0) return list;
            int i = Math.Max(0, Math.Min(_mid.Length - 1, dataIndex));
            int p = Math.Max(0, Math.Min(_mid.Length - 1, prevDataIndex));
            int dir(double curr, double prev) => curr > prev ? 1 : (curr < prev ? -1 : 0);
            string fmt = "F" + Math.Max(0, priceDecimals).ToString();
            // Compact labels: Up / Mid / Low
            list.Add(new PriceInfoPanel.InfoLine { Label = "Up:", ValueText = _up[i].ToString(fmt), ValueBrush = _edgeBrush, ArrowDir = dir(_up[i], _up[p]) });
            list.Add(new PriceInfoPanel.InfoLine { Label = "Mid:", ValueText = _mid[i].ToString(fmt), ValueBrush = _midBrush, ArrowDir = dir(_mid[i], _mid[p]) });
            list.Add(new PriceInfoPanel.InfoLine { Label = "Low:", ValueText = _dn[i].ToString(fmt), ValueBrush = _edgeBrush, ArrowDir = dir(_dn[i], _dn[p]) });
            return list;
        }
    }
}
