using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    // 簡易 MACD 副圖面板（目前僅作為佈局與同步範本，未實作實際繪圖）
    public class MacdPane : ChartPane
    {
        private const string KeyDif = "MACD_DIF";
        private const string KeyDea = "MACD_DEA";
        private const string KeyHist = "MACD_HIST";
        private const string KeyMetaFast = "MACD_META_FAST";
        private const string KeyMetaSlow = "MACD_META_SLOW";
        private const string KeyMetaSignal = "MACD_META_SIGNAL";

        protected override bool EnableBatchDrawing => false; // 使用 DrawSingleBar，但內部做批次合併
        private struct MacdPoint
        {
            public DateTime Time;
            public decimal Dif;
            public decimal Dea;
            public decimal Hist; // Dif - Dea
        }

        private readonly List<MacdPoint> _macd = new();
        private readonly Dictionary<GraphKBar, int> _indexMap = new();
        private SolidColorBrush _difBrush = new SolidColorBrush(Color.FromRgb(0x64, 0xC8, 0xFF));
        private SolidColorBrush _deaBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00));
        private const double _lineThickness = 1.0;
        private double? _prevDifX, _prevDifY, _prevDeaX, _prevDeaY;
        private decimal? _prevDifVal, _prevDeaVal, _prevHistVal;
        private int _lastDifDir = 0, _lastDeaDir = 0, _lastHistDir = 0; // 1 up, -1 down, 0 none

        // 批次繪製暫存（每次重繪視窗時重置）
        private List<RectangleGeometry>? _histPosRects;
        private List<RectangleGeometry>? _histNegRects;
        private PathFigure? _difFig;
        private PolyLineSegment? _difSeg;
        private PathFigure? _deaFig;
        private PolyLineSegment? _deaSeg;

        // 參數（可由外部設定）
        private int _fast = 12;
        private int _slow = 26;
        private int _signal = 9;

        public MacdPane() : base()
        {
            _priceInfoPanel.Visibility = Visibility.Visible;
            _priceInfoPanel.SetTitle("MACD");
            _priceInfoPanel.SetTitleClickable(true);
            _priceInfoPanel.TitleClicked += () =>
            {
                try
                {
                    var (f, s, sig) = GetParameters();
                    var (dc, ec) = GetLineColors();
                    var dlg = new MacdSettingsDialog(f, s, sig, dc, ec) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true)
                    {
                        SetParameters(dlg.Ema1, dlg.Ema2, dlg.Day);
                        SetLineColors(dlg.DifColor, dlg.DeaColor);
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

            // 副圖Y軸字體較小
            ChartTheme.AxisFontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14);
        }

        private void EnsureMacdReady()
        {
            bool needRecalc = _macd.Count != _hisBars.Count;
            if (!needRecalc && _hisBars.Count > 0)
            {
                if (!_indexMap.ContainsKey(_hisBars[0])) needRecalc = true;
            }
            if (needRecalc)
            {
                if (!TryLoadFromIndicators())
                {
                    RecalculateMacd();
                }
            }
        }

        private bool TryLoadFromIndicators()
        {
            _macd.Clear();
            _indexMap.Clear();
            if (_hisBars.Count == 0) return false;

            for (int i = 0; i < _hisBars.Count; i++)
            {
                var bar = _hisBars[i];
                if (bar.Indicators == null ||
                    !bar.Indicators.TryGetValue(KeyMetaFast, out var fast) ||
                    !bar.Indicators.TryGetValue(KeyMetaSlow, out var slow) ||
                    !bar.Indicators.TryGetValue(KeyMetaSignal, out var signal) ||
                    (int)fast != _fast ||
                    (int)slow != _slow ||
                    (int)signal != _signal ||
                    !bar.Indicators.TryGetValue(KeyDif, out var dif) ||
                    !bar.Indicators.TryGetValue(KeyDea, out var dea) ||
                    !bar.Indicators.TryGetValue(KeyHist, out var hist))
                {
                    _macd.Clear();
                    _indexMap.Clear();
                    return false;
                }

                _macd.Add(new MacdPoint
                {
                    Time = bar.Time,
                    Dif = dif,
                    Dea = dea,
                    Hist = hist
                });
                _indexMap[bar] = i;
            }
            return true;
        }

        private void RecalculateMacd()
        {
            _macd.Clear();
            _indexMap.Clear();
            if (_hisBars.Count == 0) return;

            // EMA 計算
            int fast = _fast, slow = _slow, signal = _signal;
            decimal alphaFast = 2m / (fast + 1);
            decimal alphaSlow = 2m / (slow + 1);
            decimal alphaSignal = 2m / (signal + 1);

            decimal emaFast = _hisBars[0].Close;
            decimal emaSlow = _hisBars[0].Close;
            decimal dif = 0m;
            decimal dea = 0m;

            for (int i = 0; i < _hisBars.Count; i++)
            {
                var c = _hisBars[i].Close;
                emaFast = emaFast + alphaFast * (c - emaFast);
                emaSlow = emaSlow + alphaSlow * (c - emaSlow);
                dif = emaFast - emaSlow;
                dea = dea + alphaSignal * (dif - dea);
                var macd = new MacdPoint
                {
                    Time = _hisBars[i].Time,
                    Dif = dif,
                    Dea = dea,
                    Hist = dif - dea
                };
                _macd.Add(macd);
                _indexMap[_hisBars[i]] = i;
                _hisBars[i].Indicators[KeyDif] = dif;
                _hisBars[i].Indicators[KeyDea] = dea;
                _hisBars[i].Indicators[KeyHist] = dif - dea;
                _hisBars[i].Indicators[KeyMetaFast] = _fast;
                _hisBars[i].Indicators[KeyMetaSlow] = _slow;
                _hisBars[i].Indicators[KeyMetaSignal] = _signal;
            }
        }

        protected override void CalculateYAxisRange()
        {
            EnsureMacdReady();
            if (_macd.Count == 0)
            {
                _displayMaxPrice = 1;
                _displayMinPrice = -1;
                return;
            }

            int firstDataIndexInView = Math.Max(0, _visibleStartIndex);
            int lastDataIndexInView = Math.Min(_hisBars.Count, _visibleStartIndex + _visibleBarCount);
            int count = lastDataIndexInView - firstDataIndexInView;
            if (count <= 0)
            {
                _displayMaxPrice = 1;
                _displayMinPrice = -1;
                return;
            }

            var slice = _macd.Skip(firstDataIndexInView).Take(count);
            decimal maxVal = new[] { slice.Max(p => p.Dif), slice.Max(p => p.Dea), slice.Max(p => p.Hist) }.Max();
            decimal minVal = new[] { slice.Min(p => p.Dif), slice.Min(p => p.Dea), slice.Min(p => p.Hist) }.Min();
            decimal maxAbs = Math.Max(Math.Abs(maxVal), Math.Abs(minVal));
            if (maxAbs <= 0) maxAbs = 1m;
            _displayMaxPrice = maxAbs * 1.2m;
            _displayMinPrice = -_displayMaxPrice;
        }

        public (int fast, int slow, int signal) GetParameters() => (_fast, _slow, _signal);
        public (Color difColor, Color deaColor) GetLineColors() => (_difBrush.Color, _deaBrush.Color);

        public void SetParameters(int fast, int slow, int signal)
        {
            _fast = Math.Max(1, fast);
            _slow = Math.Max(_fast + 1, slow);
            _signal = Math.Max(1, signal);
            RecalculateMacd();
        }

        public void SetLineColors(Color difColor, Color deaColor)
        {
            _difBrush = new SolidColorBrush(difColor);
            _deaBrush = new SolidColorBrush(deaColor);
            ApplyXViewState(_visibleStartIndex, _barSpacing);
        }

        protected override void DrawSingleBar(GraphKBar bar, double x)
        {
            EnsureCoordinateCacheValid();
            EnsureMacdReady();
            if (!_indexMap.TryGetValue(bar, out int idx)) return;
            if (idx < 0 || idx >= _macd.Count) return;

            var m = _macd[idx];
            double xCenter = x + _coordinateCalculator.GetHalfBarWidth();

            // 取得本次可視範圍索引
            int firstDataIndexInView = Math.Max(0, _visibleStartIndex);
            int lastDataIndexInView = Math.Min(_hisBars.Count, _visibleStartIndex + _visibleBarCount);
            bool isFirst = idx == firstDataIndexInView;
            bool isLast = idx == lastDataIndexInView - 1;

            if (isFirst)
            {
                // 重置暫存器
                _histPosRects = new List<RectangleGeometry>(Math.Max(32, _visibleBarCount / 2));
                _histNegRects = new List<RectangleGeometry>(Math.Max(32, _visibleBarCount / 2));
                _difSeg = new PolyLineSegment();
                _difFig = new PathFigure();
                _difFig.Segments.Add(_difSeg);
                _deaSeg = new PolyLineSegment();
                _deaFig = new PathFigure();
                _deaFig.Segments.Add(_deaSeg);
                _prevDifX = _prevDifY = _prevDeaX = _prevDeaY = null;
            }

            // Histogram
            double yZero = _coordinateCalculator.PriceToY(0, _displayMaxPrice, _displayMinPrice);
            double yVal = _coordinateCalculator.PriceToY(m.Hist, _displayMaxPrice, _displayMinPrice);
            double rectTop = Math.Min(yZero, yVal);
            double rectHeight = Math.Abs(yZero - yVal);
            if (rectHeight < 1) rectHeight = 1;
            var rectGeo = new RectangleGeometry(new Rect(
                new Point(ChartTheme.ChartMargin.Left + x, ChartTheme.ChartMargin.Top + rectTop),
                new Size(_barWidth, rectHeight)));
            if (m.Hist >= 0) _histPosRects!.Add(rectGeo); else _histNegRects!.Add(rectGeo);

            // Lines (DIF & DEA) as segments
            double yDif = _coordinateCalculator.PriceToY(m.Dif, _displayMaxPrice, _displayMinPrice);
            double yDea = _coordinateCalculator.PriceToY(m.Dea, _displayMaxPrice, _displayMinPrice);

            // 折線：累積到同一個 PathFigure 中
            Point difPoint = new Point(ChartTheme.ChartMargin.Left + xCenter, ChartTheme.ChartMargin.Top + yDif);
            Point deaPoint = new Point(ChartTheme.ChartMargin.Left + xCenter, ChartTheme.ChartMargin.Top + yDea);
            if (_prevDifX.HasValue)
                _difSeg!.Points.Add(difPoint);
            else
                _difFig!.StartPoint = difPoint;
            if (_prevDeaX.HasValue)
                _deaSeg!.Points.Add(deaPoint);
            else
                _deaFig!.StartPoint = deaPoint;
            _prevDifX = xCenter; _prevDifY = yDif; _prevDeaX = xCenter; _prevDeaY = yDea;

            if (isLast)
            {
                // 直方圖兩色合併成兩個 Path
                if (_histPosRects!.Count > 0)
                {
                    var g = new GeometryGroup(); foreach (var rg in _histPosRects) g.Children.Add(rg); g.Freeze();
                    var p = new Path { Data = g, Fill = ChartTheme.BullishColor, Opacity = 0.7 };
                    TrackUIElement(p); _kBarLayer.Children.Add(p);
                }
                if (_histNegRects!.Count > 0)
                {
                    var g = new GeometryGroup(); foreach (var rg in _histNegRects) g.Children.Add(rg); g.Freeze();
                    var p = new Path { Data = g, Fill = ChartTheme.BearishColor, Opacity = 0.7 };
                    TrackUIElement(p); _kBarLayer.Children.Add(p);
                }

                // DIF/DEA 兩條 Path
                var difGeo = new PathGeometry(new[] { _difFig! }); difGeo.Freeze();
                var deaGeo = new PathGeometry(new[] { _deaFig! }); deaGeo.Freeze();
                var difPath = new Path { Data = difGeo, Stroke = _difBrush, StrokeThickness = _lineThickness };
                var deaPath = new Path { Data = deaGeo, Stroke = _deaBrush, StrokeThickness = _lineThickness };
                TrackUIElement(difPath); TrackUIElement(deaPath);
                _kBarLayer.Children.Add(difPath); _kBarLayer.Children.Add(deaPath);
            }
        }

        protected override void OnCrosshairIndexChanged(int visibleIndex, bool isValid)
        {
            EnsureMacdReady();
            if (_macd.Count == 0) { _priceInfoPanel.ClearLines(); return; }
            int barIndex;
            if (!isValid)
            {
                barIndex = Math.Max(0, _macd.Count - 1);
            }
            else
            {
                barIndex = _visibleStartIndex + visibleIndex;
            }
            if (barIndex < 0 || barIndex >= _macd.Count) { _priceInfoPanel.ClearLines(); return; }
            var m = _macd[barIndex];
            // 比較方向：與前一根比較（若是顯示最後一根，與倒數第二根比）
            decimal? prevDif = null, prevDea = null, prevHist = null;
            if (barIndex - 1 >= 0) { var pm = _macd[barIndex - 1]; prevDif = pm.Dif; prevDea = pm.Dea; prevHist = pm.Hist; }
            int dirDif = prevDif.HasValue ? (m.Dif > prevDif.Value ? 1 : (m.Dif < prevDif.Value ? -1 : _lastDifDir)) : _lastDifDir;
            int dirDea = prevDea.HasValue ? (m.Dea > prevDea.Value ? 1 : (m.Dea < prevDea.Value ? -1 : _lastDeaDir)) : _lastDeaDir;
            int dirMacd = prevHist.HasValue ? (m.Hist > prevHist.Value ? 1 : (m.Hist < prevHist.Value ? -1 : _lastHistDir)) : _lastHistDir;
            _lastDifDir = dirDif; _lastDeaDir = dirDea; _lastHistDir = dirMacd;
            _priceInfoPanel.SetStructuredLines(new []
            {
                new PriceInfoPanel.InfoLine{ Label = "DIF:", ValueText = m.Dif.ToString("F2"), ValueBrush=_difBrush, ArrowDir=dirDif },
                new PriceInfoPanel.InfoLine{ Label = "DEA:", ValueText = m.Dea.ToString("F2"), ValueBrush=_deaBrush, ArrowDir=dirDea },
                new PriceInfoPanel.InfoLine{ Label = "MACD:", ValueText = m.Hist.ToString("F2"), ValueBrush=(m.Hist>=0? ChartTheme.BullishColor: ChartTheme.BearishColor), ArrowDir=dirMacd },
            });
            _prevDifVal = m.Dif; _prevDeaVal = m.Dea; _prevHistVal = m.Hist;
        }
    }
}
