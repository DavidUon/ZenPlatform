using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Charts
{
    public class VolumePane : ChartPane
    {
        protected override bool EnableBatchDrawing => false; // 用單一 Path 合批
        public VolumePane() : base()
        {
            // 顯示左側資訊面板（透明背景），用於顯示層名與游標值
            _priceInfoPanel.Visibility = Visibility.Visible;
            _priceInfoPanel.SetTitle("成交量");
            _priceDecimalPlaces = 0; // 成交量不顯示小數
            // 保留與主圖一致的左側欄寬，讓主/副圖的可繪圖區域 X 起點一致（避免時間軸/格線錯位）
            if (this.ColumnDefinitions.Count > 0)
            {
                this.ColumnDefinitions[0].Width = new GridLength(ChartTheme.LeftPanelWidth);
            }

            // 副圖設定：不顯示時間刻度、不顯示高低價標籤、也不接受拖曳縮放
            ShowXAxisTimeLabels = false;
            ShowHighLowLabels = false;
            IsMainPane = false;

            // 回收底部時間軸高度空間
            ChartTheme.XAxisHeight = 0;

            // 副圖Y軸格線：少一點，約3條
            ChartTheme.YAxisMinTickCount = 3;
            ChartTheme.YAxisMaxTickCount = 4;
            ChartTheme.YAxisTargetPixelsPerStep = 140; // 略寬鬆，減少格線密度

            // 副圖Y軸字體較小
            ChartTheme.AxisFontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14);
        }

        protected override void OnCrosshairIndexChanged(int visibleIndex, bool isValid)
        {
            int barIndex;
            if (!isValid)
            {
                if (_hisBars.Count == 0) { _priceInfoPanel.ClearLines(); return; }
                barIndex = _hisBars.Count - 1;
            }
            else
            {
                barIndex = _visibleStartIndex + visibleIndex;
            }
            if (barIndex < 0 || barIndex >= _hisBars.Count) { _priceInfoPanel.ClearLines(); return; }
            var bar = _hisBars[barIndex];
            _priceInfoPanel.SetStructuredLines(new []
            {
                new PriceInfoPanel.InfoLine{ Label = "Vol:", ValueText = bar.Volume.ToString(), ValueBrush=Brushes.White, ArrowDir=0 }
            });
        }

        // 覆寫Y軸的計算方式，改為根據成交量（含可視區內的浮動棒）
        protected override void CalculateYAxisRange()
        {
            if (_hisBars.Count == 0 && (_floatingKBar == null || _floatingKBar.IsNullBar))
            {
                _displayMaxPrice = 1000; _displayMinPrice = 0; return;
            }

            int firstDataIndexInView = Math.Max(0, _visibleStartIndex);
            int lastDataIndexInView = Math.Min(_hisBars.Count, _visibleStartIndex + _visibleBarCount);
            int dataCountInView = lastDataIndexInView - firstDataIndexInView;

            decimal maxVolume = 0m;
            if (dataCountInView > 0)
            {
                var visibleBars = _hisBars.Skip(firstDataIndexInView).Take(dataCountInView);
                if (visibleBars.Any()) maxVolume = visibleBars.Max(b => (decimal)b.Volume);
            }

            if (_floatingKBar != null && !_floatingKBar.IsNullBar)
            {
                int floatDataIndex = _hisBars.Count; // 視為下一根
                int visIndex = floatDataIndex - _visibleStartIndex;
                bool floatingVisible = visIndex >= 0 && visIndex < _visibleBarCount;
                if (floatingVisible && (decimal)_floatingKBar.Volume > maxVolume)
                    maxVolume = _floatingKBar.Volume;
            }

            if (maxVolume <= 0) maxVolume = 1000m;
            _displayMinPrice = 0m;
            _displayMaxPrice = maxVolume * 1.2m;
        }

        /// <summary>
        /// 將數字轉換為「漂亮的」刻度間距（至少大於等於輸入值）
        /// </summary>
        private static decimal NiceNumber(decimal value)
        {
            if (value <= 0) return 100m;

            // 取得數字的數量級
            double exp = Math.Floor(Math.Log10((double)value));
            decimal power = (decimal)Math.Pow(10, exp); // 例如: 23000 -> 10000
            decimal normalized = value / power; // 正規化到 1.0 ~ 10.0 之間

            // 選擇漂亮的倍數: 1, 2, 5, 10
            if (normalized <= 1m) return 1m * power;
            else if (normalized <= 2m) return 2m * power;
            else if (normalized <= 5m) return 5m * power;
            else return 10m * power;
        }

        protected override void DrawYAxis()
        {
            // 繪製 Y 軸線
            var yAxisLine = new Line { X1 = ChartTheme.ChartMargin.Left + _chartWidth, X2 = ChartTheme.ChartMargin.Left + _chartWidth, Y1 = ChartTheme.ChartMargin.Top, Y2 = ChartTheme.ChartMargin.Top + _chartHeight, Stroke = ChartTheme.AxisLineColor, StrokeThickness = 1 };
            AddToBottomLayer(yAxisLine);

            if (_visibleBarCount <= 0) return;

            // 簡化邏輯：成交量使用固定的刻度間距，最多顯示 3 條格線
            decimal priceRange = _displayMaxPrice - _displayMinPrice;
            if (priceRange <= 0) return;

            // 根據範圍決定刻度大小，確保格線數量不超過 3 條
            // tickSize 至少要是 priceRange / 3
            decimal minTickSize = priceRange / 3m;

            // 找到合適的「漂亮數字」作為 tickSize
            decimal tickSize = NiceNumber(minTickSize);

            decimal startPrice = Math.Ceiling(_displayMinPrice / tickSize) * tickSize;
            for (decimal price = startPrice; price <= _displayMaxPrice; price += tickSize)
            {
                // 跳過 0 值標籤 - 成交量底部就是 0，不需要顯示
                if (price == 0m) continue;

                double y = _coordinateCalculator.PriceToY(price, _displayMaxPrice, _displayMinPrice);
                if (y < 0 || y > _chartHeight) continue;

                // 繪製格線
                var gridLine = new Line { X1 = ChartTheme.ChartMargin.Left, X2 = ChartTheme.ChartMargin.Left + _chartWidth, Y1 = ChartTheme.ChartMargin.Top + y, Y2 = ChartTheme.ChartMargin.Top + y, Stroke = ChartTheme.GridLineColor, StrokeThickness = 1 };
                AddToBottomLayer(gridLine);

                // 繪製價格標籤（不含小數點，因為成交量是整數）
                var priceLabel = new TextBlock { Text = price.ToString("F0"), Foreground = ChartTheme.YAxisTextColor, FontSize = ChartTheme.AxisFontSize, Background = ChartTheme.BackgroundColor, Padding = new Thickness(2,0,2,0) };
                priceLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textHeight = priceLabel.DesiredSize.Height;
                double finalTop = ChartTheme.ChartMargin.Top + y - (textHeight / 2);
                finalTop = Math.Max(ChartTheme.ChartMargin.Top, finalTop);
                finalTop = Math.Min(ChartTheme.ChartMargin.Top + _chartHeight - textHeight, finalTop);
                Canvas.SetLeft(priceLabel, ChartTheme.ChartMargin.Left + _chartWidth + 5);
                Canvas.SetTop(priceLabel, finalTop);
                AddToBottomLayer(priceLabel);
            }
        }

        // 批次暫存（僅用於歷史棒）
        private List<RectangleGeometry>? _bullRects;
        private List<RectangleGeometry>? _bearRects;

        /// <summary>
        /// 歷史棒繪製完成，提交批次
        /// </summary>
        protected override void OnHistoricalBarsDrawn()
        {
            if (_bullRects != null && _bullRects.Count > 0)
            {
                var g = new GeometryGroup();
                foreach (var rg in _bullRects) g.Children.Add(rg);
                g.Freeze();
                var p = new System.Windows.Shapes.Path { Data = g, Fill = ChartTheme.BullishColor };
                TrackUIElement(p);
                _kBarLayer.Children.Add(p);
            }
            if (_bearRects != null && _bearRects.Count > 0)
            {
                var g = new GeometryGroup();
                foreach (var rg in _bearRects) g.Children.Add(rg);
                g.Freeze();
                var p = new System.Windows.Shapes.Path { Data = g, Fill = ChartTheme.BearishColor };
                TrackUIElement(p);
                _kBarLayer.Children.Add(p);
            }
            _bullRects = null;
            _bearRects = null;
        }

        /// <summary>
        /// 浮動棒直接繪製，不使用批次
        /// </summary>
        protected override void DrawFloatingBar(GraphKBar bar, double x, ref decimal maxHigh, ref decimal minLow, ref int maxHighVisIndex, ref int minLowVisIndex, int visIndex)
        {
            // 成交量副圖不需要更新 maxHigh/minLow（這些是價格用的）
            if (bar.Volume <= 0) return;

            EnsureCoordinateCacheValid();

            double yVolume = _coordinateCalculator.PriceToY(bar.Volume, _displayMaxPrice, _displayMinPrice);
            double yZero = _coordinateCalculator.PriceToY(0, _displayMaxPrice, _displayMinPrice);
            double height = Math.Abs(yZero - yVolume);
            if (height < 1) height = 1;

            bool isBull = bar.Close >= bar.Open;
            var rect = new Rectangle
            {
                Width = _barWidth,
                Height = height,
                Fill = isBull ? ChartTheme.BullishColor : ChartTheme.BearishColor
            };

            TrackUIElement(rect);
            Canvas.SetLeft(rect, ChartTheme.ChartMargin.Left + x);
            Canvas.SetTop(rect, ChartTheme.ChartMargin.Top + yVolume);
            _kBarLayer.Children.Add(rect);
        }


        /// <summary>
        /// 覆寫單一K棒的繪製方式，改成合批長條圖（僅用於歷史棒）
        /// </summary>
        protected override void DrawSingleBar(GraphKBar bar, double x)
        {
            if (bar.Volume <= 0) return;

            EnsureCoordinateCacheValid();

            // 判斷是否為第一根可視棒（用於初始化批次緩衝）
            double firstX = _coordinateCalculator.GetBarXByVisibleIndex(0, _visibleBarCount, _barSpacing);
            bool isFirst = Math.Abs(x - firstX) < 0.5;

            // 初始化批次緩衝
            if (isFirst || _bullRects == null || _bearRects == null)
            {
                _bullRects = new List<RectangleGeometry>(Math.Max(32, _visibleBarCount / 2));
                _bearRects = new List<RectangleGeometry>(Math.Max(32, _visibleBarCount / 2));
            }

            double yVolume = _coordinateCalculator.PriceToY(bar.Volume, _displayMaxPrice, _displayMinPrice);
            double yZero = _coordinateCalculator.PriceToY(0, _displayMaxPrice, _displayMinPrice);
            double height = Math.Abs(yZero - yVolume);
            if (height < 1) height = 1;

            var rect = new RectangleGeometry(new Rect(
                new Point(ChartTheme.ChartMargin.Left + x, ChartTheme.ChartMargin.Top + yVolume),
                new Size(_barWidth, height)));

            bool isBull = bar.Close >= bar.Open;
            if (isBull) _bullRects!.Add(rect); else _bearRects!.Add(rect);
        }
    }
}
