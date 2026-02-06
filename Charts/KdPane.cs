using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    // 簡易 KD 副圖面板（目前僅作為佈局與同步範本，未實作實際繪圖）
    public class KdPane : ChartPane
    {
        protected override bool EnableBatchDrawing => false; // 逐點繪製線段

        private struct KdPoint
        {
            public DateTime Time;
            public decimal K;
            public decimal D;
        }

        private readonly List<KdPoint> _kd = new();
        private readonly Dictionary<GraphKBar, int> _indexMap = new();
        private readonly Brush _kBrush = new SolidColorBrush(Color.FromRgb(255, 210, 0)); // 黃
        private readonly Brush _dBrush = new SolidColorBrush(Color.FromRgb(100, 200, 255)); // 淺藍
        private const double _lineThickness = 1.5;
        private double? _prevKX, _prevKY, _prevDX, _prevDY;
        private decimal? _prevKVal, _prevDVal;
        private int _lastKDir = 0, _lastDDir = 0; // 1 up, -1 down, 0 none

        // 參數（可由外部設定）
        private int _period = 9;
        private int _smoothK = 3;
        private int _smoothD = 3;

        public KdPane() : base()
        {
            _priceInfoPanel.Visibility = Visibility.Visible;
            _priceInfoPanel.SetTitle("KD指標");
            _priceInfoPanel.SetTitleClickable(true);
            _priceInfoPanel.TitleClicked += () =>
            {
                try
                {
                    var (p, sk, sd) = GetParameters();
                    var dlg = new KdSettingsDialog(p, sk, sd) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true)
                    {
                        SetParameters(dlg.Period, dlg.SmoothK, dlg.SmoothD);
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

        // 不覆寫基底 LoadHistory（非 virtual）。改為在使用前檢查並按需重算。

        private void EnsureKdReady()
        {
            bool needRecalc = _kd.Count != _hisBars.Count;
            if (!needRecalc && _hisBars.Count > 0)
            {
                // 若 LoadHistory 後 _hisBars 物件已更換，_indexMap 會找不到當前 bar
                if (!_indexMap.ContainsKey(_hisBars[0])) needRecalc = true;
            }
            if (needRecalc) RecalculateKd();
        }

        private void RecalculateKd()
        {
            _kd.Clear();
            _indexMap.Clear();
            if (_hisBars.Count == 0) return;

            int period = _period;
            int smoothK = _smoothK;
            int smoothD = _smoothD;

            decimal prevK = 50m; // 常見預設
            decimal prevD = 50m;
            var window = new Queue<GraphKBar>();

            for (int i = 0; i < _hisBars.Count; i++)
            {
                var bar = _hisBars[i];
                window.Enqueue(bar);
                if (window.Count > period) window.Dequeue();

                decimal highestHigh = window.Max(b => b.High);
                decimal lowestLow = window.Min(b => b.Low);
                decimal rsv = 0m;
                if (highestHigh != lowestLow)
                {
                    rsv = (bar.Close - lowestLow) / (highestHigh - lowestLow) * 100m;
                }

                // 平滑：K = 2/3*K(prev) + 1/3*RSV；D 同理
                decimal k = (2m * prevK + rsv) / 3m;
                decimal d = (2m * prevD + k) / 3m;
                prevK = k;
                prevD = d;

                _kd.Add(new KdPoint { Time = bar.Time, K = k, D = d });
                _indexMap[bar] = i;
            }
        }

        public (int period, int smoothK, int smoothD) GetParameters() => (_period, _smoothK, _smoothD);

        public void SetParameters(int period, int smoothK, int smoothD)
        {
            _period = Math.Max(1, period);
            _smoothK = Math.Max(1, smoothK);
            _smoothD = Math.Max(1, smoothD);
            RecalculateKd();
        }

        protected override void CalculateYAxisRange()
        {
            EnsureKdReady();
            // KD 固定 0..100
            _displayMinPrice = 0;
            _displayMaxPrice = 100;
        }

        protected override void DrawYAxis()
        {
            // 繪製 Y 軸線
            var yAxisLine = new Line { X1 = ChartTheme.ChartMargin.Left + _chartWidth, X2 = ChartTheme.ChartMargin.Left + _chartWidth, Y1 = ChartTheme.ChartMargin.Top, Y2 = ChartTheme.ChartMargin.Top + _chartHeight, Stroke = ChartTheme.AxisLineColor, StrokeThickness = 1 };
            AddToBottomLayer(yAxisLine);

            if (_visibleBarCount <= 0) return;

            // KD 指標只顯示關鍵參考線：20, 50, 80
            var keyLevels = new decimal[] { 20m, 50m, 80m };

            foreach (var level in keyLevels)
            {
                double y = _coordinateCalculator.PriceToY(level, _displayMaxPrice, _displayMinPrice);
                if (y < 0 || y > _chartHeight) continue;

                // 繪製格線 - 50 線使用較深色彩作為中線
                var lineColor = (level == 50m) ? new SolidColorBrush(Color.FromRgb(90, 90, 90)) : ChartTheme.GridLineColor;
                var gridLine = new Line { X1 = ChartTheme.ChartMargin.Left, X2 = ChartTheme.ChartMargin.Left + _chartWidth, Y1 = ChartTheme.ChartMargin.Top + y, Y2 = ChartTheme.ChartMargin.Top + y, Stroke = lineColor, StrokeThickness = 1 };
                AddToBottomLayer(gridLine);

                // 繪製標籤
                var levelLabel = new TextBlock { Text = level.ToString("0"), Foreground = ChartTheme.YAxisTextColor, FontSize = ChartTheme.AxisFontSize, Background = ChartTheme.BackgroundColor, Padding = new Thickness(2,0,2,0) };
                levelLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textHeight = levelLabel.DesiredSize.Height;
                double finalTop = ChartTheme.ChartMargin.Top + y - (textHeight / 2);
                finalTop = Math.Max(ChartTheme.ChartMargin.Top, finalTop);
                finalTop = Math.Min(ChartTheme.ChartMargin.Top + _chartHeight - textHeight, finalTop);
                Canvas.SetLeft(levelLabel, ChartTheme.ChartMargin.Left + _chartWidth + 5);
                Canvas.SetTop(levelLabel, finalTop);
                AddToBottomLayer(levelLabel);
            }
        }

        protected override void DrawSingleBar(GraphKBar bar, double x)
        {
            EnsureCoordinateCacheValid();
            EnsureKdReady();
            if (!_indexMap.TryGetValue(bar, out int idx)) return;
            if (idx < 0 || idx >= _kd.Count) return;

            var p = _kd[idx];
            double xCenter = x + _coordinateCalculator.GetHalfBarWidth();
            double firstX = _coordinateCalculator.GetBarXByVisibleIndex(0, _visibleBarCount, _barSpacing);
            if (Math.Abs(x - firstX) < 0.5)
            {
                _prevKX = _prevKY = _prevDX = _prevDY = null;
            }
            double yK = _coordinateCalculator.PriceToY(p.K, _displayMaxPrice, _displayMinPrice);
            double yD = _coordinateCalculator.PriceToY(p.D, _displayMaxPrice, _displayMinPrice);

            if (_prevKX.HasValue && _prevKY.HasValue)
            {
                var seg = new Line
                {
                    X1 = ChartTheme.ChartMargin.Left + _prevKX.Value,
                    Y1 = ChartTheme.ChartMargin.Top + _prevKY.Value,
                    X2 = ChartTheme.ChartMargin.Left + xCenter,
                    Y2 = ChartTheme.ChartMargin.Top + yK,
                    Stroke = _kBrush,
                    StrokeThickness = _lineThickness
                };
                TrackUIElement(seg);
                _kBarLayer.Children.Add(seg);
            }
            _prevKX = xCenter;
            _prevKY = yK;

            if (_prevDX.HasValue && _prevDY.HasValue)
            {
                var seg = new Line
                {
                    X1 = ChartTheme.ChartMargin.Left + _prevDX.Value,
                    Y1 = ChartTheme.ChartMargin.Top + _prevDY.Value,
                    X2 = ChartTheme.ChartMargin.Left + xCenter,
                    Y2 = ChartTheme.ChartMargin.Top + yD,
                    Stroke = _dBrush,
                    StrokeThickness = _lineThickness
                };
                TrackUIElement(seg);
                _kBarLayer.Children.Add(seg);
            }
            _prevDX = xCenter;
            _prevDY = yD;
        }

        protected override void OnCrosshairIndexChanged(int visibleIndex, bool isValid)
        {
            EnsureKdReady();
            if (_kd.Count == 0) { _priceInfoPanel.ClearLines(); return; }
            int barIndex;
            if (!isValid)
            {
                barIndex = Math.Max(0, _kd.Count - 1);
            }
            else
            {
                barIndex = _visibleStartIndex + visibleIndex;
            }
            if (barIndex < 0 || barIndex >= _kd.Count) { _priceInfoPanel.ClearLines(); return; }
            var p = _kd[barIndex];
            // 比較方向：與前一根比較（若是顯示最後一根，與倒數第二根比）
            decimal? refKPrev = null, refDPrev = null;
            if (barIndex - 1 >= 0) { refKPrev = _kd[barIndex - 1].K; refDPrev = _kd[barIndex - 1].D; }
            int dirK = refKPrev.HasValue ? (p.K > refKPrev.Value ? 1 : (p.K < refKPrev.Value ? -1 : _lastKDir)) : _lastKDir;
            int dirD = refDPrev.HasValue ? (p.D > refDPrev.Value ? 1 : (p.D < refDPrev.Value ? -1 : _lastDDir)) : _lastDDir;
            _lastKDir = dirK; _lastDDir = dirD;
            _priceInfoPanel.SetStructuredLines(new []
            {
                new PriceInfoPanel.InfoLine{ Label = "K:", ValueText = p.K.ToString("F2"), ValueBrush=_kBrush, ArrowDir=dirK },
                new PriceInfoPanel.InfoLine{ Label = "D:", ValueText = p.D.ToString("F2"), ValueBrush=_dBrush, ArrowDir=dirD },
            });
            _prevKVal = p.K; _prevDVal = p.D;
        }
    }
}
