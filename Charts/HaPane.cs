using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    public class HaPane : ChartPane
    {
        protected override bool EnableBatchDrawing => false; // 逐根繪製 HA 蠟燭

        private struct HaBar
        {
            public decimal Open;
            public decimal High;
            public decimal Low;
            public decimal Close;
        }

        private readonly List<HaBar> _ha = new();
        private readonly Dictionary<GraphKBar, int> _indexMap = new();
        private readonly SolidColorBrush _upBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));   // 上漲紅
        private readonly SolidColorBrush _downBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x00)); // 下跌綠
        private readonly SolidColorBrush _flatBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

        public HaPane() : base()
        {
            _priceInfoPanel.Visibility = Visibility.Visible;
            _priceInfoPanel.SetTitle("HA 裁縫線");
            _priceInfoPanel.SetTitleClickable(false);

            if (this.ColumnDefinitions.Count > 0)
            {
                this.ColumnDefinitions[0].Width = new GridLength(ChartTheme.LeftPanelWidth);
            }

            ShowXAxisTimeLabels = false;
            ShowHighLowLabels = false;
            IsMainPane = false;
            ChartTheme.XAxisHeight = 0;

            ChartTheme.YAxisMinTickCount = 3;
            ChartTheme.YAxisMaxTickCount = 4;
            ChartTheme.YAxisTargetPixelsPerStep = 140;
            ChartTheme.AxisFontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14);
        }

        private void EnsureHaReady()
        {
            bool needRecalc = _ha.Count != _hisBars.Count;
            if (!needRecalc && _hisBars.Count > 0)
            {
                if (!_indexMap.ContainsKey(_hisBars[0])) needRecalc = true;
            }
            if (needRecalc)
            {
                if (!TryLoadFromIndicators())
                {
                    RecalculateHa();
                }
            }
        }

        private bool TryLoadFromIndicators()
        {
            _ha.Clear();
            _indexMap.Clear();
            if (_hisBars.Count == 0) return false;

            for (int i = 0; i < _hisBars.Count; i++)
            {
                var bar = _hisBars[i];
                if (bar.Indicators == null ||
                    !bar.Indicators.TryGetValue("HA_O", out var o) ||
                    !bar.Indicators.TryGetValue("HA_H", out var h) ||
                    !bar.Indicators.TryGetValue("HA_L", out var l) ||
                    !bar.Indicators.TryGetValue("HA_C", out var c))
                {
                    _ha.Clear();
                    _indexMap.Clear();
                    return false;
                }
                _ha.Add(new HaBar { Open = o, High = h, Low = l, Close = c });
                _indexMap[bar] = i;
            }
            return true;
        }

        private void RecalculateHa()
        {
            _ha.Clear();
            _indexMap.Clear();
            if (_hisBars.Count == 0) return;

            decimal prevOpen = 0m;
            decimal prevClose = 0m;

            for (int i = 0; i < _hisBars.Count; i++)
            {
                var bar = _hisBars[i];
                decimal haClose = (bar.Open + bar.High + bar.Low + bar.Close) / 4m;
                decimal haOpen = (i == 0) ? (bar.Open + bar.Close) / 2m : (prevOpen + prevClose) / 2m;
                decimal haHigh = Math.Max(bar.High, Math.Max(haOpen, haClose));
                decimal haLow = Math.Min(bar.Low, Math.Min(haOpen, haClose));

                var ha = new HaBar { Open = haOpen, High = haHigh, Low = haLow, Close = haClose };
                _ha.Add(ha);
                _indexMap[bar] = i;

                bar.Indicators["HA_O"] = haOpen;
                bar.Indicators["HA_H"] = haHigh;
                bar.Indicators["HA_L"] = haLow;
                bar.Indicators["HA_C"] = haClose;

                prevOpen = haOpen;
                prevClose = haClose;
            }
        }

        protected override void CalculateYAxisRange()
        {
            EnsureHaReady();
            if (_ha.Count == 0) { _displayMaxPrice = 100; _displayMinPrice = 0; return; }

            int first = Math.Max(0, _visibleStartIndex);
            int last = Math.Min(_ha.Count, _visibleStartIndex + _visibleBarCount);
            int count = last - first;
            if (count <= 0) { _displayMaxPrice = 100; _displayMinPrice = 0; return; }

            decimal maxHigh = decimal.MinValue;
            decimal minLow = decimal.MaxValue;
            for (int i = first; i < last; i++)
            {
                var h = _ha[i].High;
                var l = _ha[i].Low;
                if (h > maxHigh) maxHigh = h;
                if (l < minLow) minLow = l;
            }

            if (maxHigh == minLow)
            {
                decimal buffer = Math.Abs(maxHigh * 0.001m); if (buffer == 0) buffer = 1m;
                _displayMaxPrice = maxHigh + buffer;
                _displayMinPrice = minLow - buffer;
            }
            else
            {
                decimal range = maxHigh - minLow;
                _displayMaxPrice = maxHigh + range * 0.10m;
                _displayMinPrice = minLow - range * 0.05m;
            }
        }

        protected override void OnCrosshairIndexChanged(int visibleIndex, bool isValid)
        {
            EnsureHaReady();
            int barIndex;
            if (!isValid)
            {
                if (_ha.Count == 0) { _priceInfoPanel.ClearLines(); return; }
                barIndex = _ha.Count - 1;
            }
            else
            {
                barIndex = _visibleStartIndex + visibleIndex;
            }
            if (barIndex < 0 || barIndex >= _ha.Count) { _priceInfoPanel.ClearLines(); return; }

            var ha = _ha[barIndex];
            string fmt = "F" + Math.Max(0, _priceDecimalPlaces).ToString();
            _priceInfoPanel.SetStructuredLines(new[]
            {
                new PriceInfoPanel.InfoLine { Label = "O:", ValueText = ha.Open.ToString(fmt), ValueBrush = Brushes.White, ArrowDir = 0 },
                new PriceInfoPanel.InfoLine { Label = "H:", ValueText = ha.High.ToString(fmt), ValueBrush = Brushes.White, ArrowDir = 0 },
                new PriceInfoPanel.InfoLine { Label = "L:", ValueText = ha.Low.ToString(fmt), ValueBrush = Brushes.White, ArrowDir = 0 },
                new PriceInfoPanel.InfoLine { Label = "C:", ValueText = ha.Close.ToString(fmt), ValueBrush = Brushes.White, ArrowDir = 0 }
            });
        }

        protected override void DrawSingleBar(GraphKBar bar, double x)
        {
            EnsureHaReady();
            if (!_indexMap.TryGetValue(bar, out var idx)) return;
            if (idx < 0 || idx >= _ha.Count) return;

            var ha = _ha[idx];
            Brush color = ha.Close > ha.Open ? _upBrush : (ha.Close < ha.Open ? _downBrush : _flatBrush);

            EnsureCoordinateCacheValid();
            double yOpen = _coordinateCalculator.PriceToY(ha.Open, _displayMaxPrice, _displayMinPrice);
            double yClose = _coordinateCalculator.PriceToY(ha.Close, _displayMaxPrice, _displayMinPrice);
            double yHigh = _coordinateCalculator.PriceToY(ha.High, _displayMaxPrice, _displayMinPrice);
            double yLow = _coordinateCalculator.PriceToY(ha.Low, _displayMaxPrice, _displayMinPrice);
            double centerX = x + _coordinateCalculator.GetHalfBarWidth();

            var wick = new Line { X1 = ChartTheme.ChartMargin.Left + centerX, X2 = ChartTheme.ChartMargin.Left + centerX, Y1 = ChartTheme.ChartMargin.Top + yHigh, Y2 = ChartTheme.ChartMargin.Top + yLow, Stroke = color, StrokeThickness = 1 };
            TrackUIElement(wick);
            _kBarLayer.Children.Add(wick);

            double yTop = Math.Min(yOpen, yClose);
            double height = Math.Abs(yClose - yOpen);
            var body = new Rectangle { Width = _barWidth, Height = height < 1 ? 1 : height, Fill = color };
            TrackUIElement(body);
            Canvas.SetLeft(body, ChartTheme.ChartMargin.Left + x);
            Canvas.SetTop(body, ChartTheme.ChartMargin.Top + yTop);
            _kBarLayer.Children.Add(body);
        }
    }
}
