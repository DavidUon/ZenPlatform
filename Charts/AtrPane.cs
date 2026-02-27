using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    public class AtrPane : ChartPane
    {
        private const string KeyAtr = "ATR";
        private const string KeyMetaPeriod = "ATR_META_PERIOD";

        protected override bool EnableBatchDrawing => false;

        private struct AtrPoint
        {
            public DateTime Time;
            public decimal Atr;
        }

        private readonly List<AtrPoint> _atr = new();
        private readonly Dictionary<GraphKBar, int> _indexMap = new();
        private SolidColorBrush _lineBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x9E, 0xFF));
        private const double _lineThickness = 1.0;
        private double? _prevX, _prevY;

        private int _period = 14;

        public AtrPane() : base()
        {
            _priceInfoPanel.Visibility = Visibility.Visible;
            _priceInfoPanel.SetTitle("ATR");
            _priceInfoPanel.SetTitleClickable(true);
            _priceInfoPanel.TitleClicked += () =>
            {
                try
                {
                    var (p, c) = GetParameters();
                    var dlg = new AtrSettingsDialog(p, c) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true)
                    {
                        SetParameters(dlg.Period, dlg.LineColor);
                    }
                }
                catch { }
            };

            if (this.ColumnDefinitions.Count > 0)
            {
                this.ColumnDefinitions[0].Width = new GridLength(ChartTheme.LeftPanelWidth);
            }

            ShowXAxisTimeLabels = false;
            ShowHighLowLabels = false;
            IsMainPane = false;
            ChartTheme.XAxisHeight = 0;
            ChartTheme.YAxisMinTickCount = 3;
            ChartTheme.YAxisMaxTickCount = 5;
            ChartTheme.YAxisTargetPixelsPerStep = 140;
            ChartTheme.AxisFontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14);
        }

        public (int period, Color lineColor) GetParameters() => (_period, _lineBrush.Color);

        public void SetParameters(int period, Color lineColor)
        {
            _period = Math.Max(1, period);
            _lineBrush = new SolidColorBrush(lineColor);
            RecalculateAtr();
            ApplyXViewState(_visibleStartIndex, _barSpacing);
        }

        private void EnsureAtrReady()
        {
            bool needRecalc = _atr.Count != _hisBars.Count;
            if (!needRecalc && _hisBars.Count > 0 && !_indexMap.ContainsKey(_hisBars[0])) needRecalc = true;
            if (needRecalc)
            {
                if (!TryLoadFromIndicators())
                {
                    RecalculateAtr();
                }
            }
        }

        private bool TryLoadFromIndicators()
        {
            _atr.Clear();
            _indexMap.Clear();
            if (_hisBars.Count == 0) return false;
            for (int i = 0; i < _hisBars.Count; i++)
            {
                var bar = _hisBars[i];
                if (bar.Indicators == null ||
                    !bar.Indicators.TryGetValue(KeyMetaPeriod, out var period) ||
                    (int)period != _period ||
                    !bar.Indicators.TryGetValue(KeyAtr, out var v))
                {
                    _atr.Clear();
                    _indexMap.Clear();
                    return false;
                }
                _atr.Add(new AtrPoint { Time = bar.Time, Atr = v });
                _indexMap[bar] = i;
            }
            return true;
        }

        private void RecalculateAtr()
        {
            _atr.Clear();
            _indexMap.Clear();
            if (_hisBars.Count == 0) return;

            int n = Math.Max(1, _period);
            decimal prevAtr = 0m;
            for (int i = 0; i < _hisBars.Count; i++)
            {
                var bar = _hisBars[i];
                decimal tr;
                if (i == 0)
                {
                    tr = bar.High - bar.Low;
                }
                else
                {
                    var prevClose = _hisBars[i - 1].Close;
                    var hl = bar.High - bar.Low;
                    var hc = Math.Abs(bar.High - prevClose);
                    var lc = Math.Abs(bar.Low - prevClose);
                    tr = Math.Max(hl, Math.Max(hc, lc));
                }
                decimal atr = (i == 0) ? tr : (prevAtr * (n - 1) + tr) / n;
                prevAtr = atr;
                _atr.Add(new AtrPoint { Time = bar.Time, Atr = atr });
                _indexMap[bar] = i;
                bar.Indicators[KeyAtr] = atr;
                bar.Indicators[KeyMetaPeriod] = _period;
            }
        }

        protected override void CalculateYAxisRange()
        {
            EnsureAtrReady();
            if (_atr.Count == 0)
            {
                _displayMaxPrice = 1;
                _displayMinPrice = 0;
                return;
            }

            int firstDataIndexInView = Math.Max(0, _visibleStartIndex);
            int lastDataIndexInView = Math.Min(_hisBars.Count, _visibleStartIndex + _visibleBarCount);
            int count = lastDataIndexInView - firstDataIndexInView;
            if (count <= 0)
            {
                _displayMaxPrice = 1;
                _displayMinPrice = 0;
                return;
            }

            var slice = _atr.Skip(firstDataIndexInView).Take(count);
            decimal maxVal = slice.Max(p => p.Atr);
            if (maxVal <= 0) maxVal = 1m;
            _displayMaxPrice = maxVal * 1.2m;
            _displayMinPrice = 0;
        }

        protected override void DrawSingleBar(GraphKBar bar, double x)
        {
            EnsureCoordinateCacheValid();
            EnsureAtrReady();
            if (!_indexMap.TryGetValue(bar, out int idx)) return;
            if (idx < 0 || idx >= _atr.Count) return;

            var p = _atr[idx];
            double xCenter = x + _coordinateCalculator.GetHalfBarWidth();
            double firstX = _coordinateCalculator.GetBarXByVisibleIndex(0, _visibleBarCount, _barSpacing);
            if (Math.Abs(x - firstX) < 0.5)
            {
                _prevX = _prevY = null;
            }
            double y = _coordinateCalculator.PriceToY(p.Atr, _displayMaxPrice, _displayMinPrice);

            if (_prevX.HasValue && _prevY.HasValue)
            {
                var seg = new Line
                {
                    X1 = ChartTheme.ChartMargin.Left + _prevX.Value,
                    Y1 = ChartTheme.ChartMargin.Top + _prevY.Value,
                    X2 = ChartTheme.ChartMargin.Left + xCenter,
                    Y2 = ChartTheme.ChartMargin.Top + y,
                    Stroke = _lineBrush,
                    StrokeThickness = _lineThickness
                };
                TrackUIElement(seg);
                _kBarLayer.Children.Add(seg);
            }
            _prevX = xCenter;
            _prevY = y;
        }

        protected override void OnCrosshairIndexChanged(int visibleIndex, bool isValid)
        {
            EnsureAtrReady();
            if (_atr.Count == 0) { _priceInfoPanel.ClearLines(); return; }
            int barIndex = isValid ? _visibleStartIndex + visibleIndex : Math.Max(0, _atr.Count - 1);
            if (barIndex < 0 || barIndex >= _atr.Count) { _priceInfoPanel.ClearLines(); return; }
            var p = _atr[barIndex];
            int prevIndex = Math.Max(0, barIndex - 1);
            int dir = _atr[barIndex].Atr > _atr[prevIndex].Atr ? 1 : (_atr[barIndex].Atr < _atr[prevIndex].Atr ? -1 : 0);
            _priceInfoPanel.SetStructuredLines(new[]
            {
                new PriceInfoPanel.InfoLine
                {
                    Label = "ATR:",
                    ValueText = p.Atr.ToString("F2"),
                    ValueBrush = _lineBrush,
                    ArrowDir = dir
                }
            });
        }
    }
}
