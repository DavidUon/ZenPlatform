using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;

namespace Charts
{
    public class ChartPane : Grid
    {
        public ChartStyle ChartTheme { get; set; }
        public bool IsMainPane { get; set; } = false; // 僅主圖可拖曳/縮放，且顯示價格提示
        public bool ShowXAxisTimeLabels { get; set; } = true; // 副圖不顯示時間刻度
        public bool ShowHighLowLabels { get; set; } = true;   // 副圖不顯示高低點標籤
        public int MaxBars { get; set; } = 10000; // 預設保留上限

        // X 視圖變更事件：(visibleStartIndex, barSpacing, barWidth)
        public event Action<int, double, double>? XViewChanged;
        // 十字線同步事件：(senderPane, visibleIndexInView, isValid)
        public event Action<ChartPane, int, bool>? CrosshairMoved;
        // 主圖標題點擊事件：用於彈出設定視窗
        public event Action? MainPaneTitleClicked;
        private bool _crosshairBroadcastEnabled = true;
        public void SetCrosshairBroadcastEnabled(bool enabled) { _crosshairBroadcastEnabled = enabled; if (!enabled) HideCrosshairAndTags(); }

        // 十字線和查價視窗顯示控制
        private bool _crosshairVisible = true;
        private bool _tooltipVisible = true;

        public void SetCrosshairVisible(bool visible)
        {
            _crosshairVisible = visible;
            if (!visible)
            {
                // 十字線關閉時，查價視窗也必須關閉
                HideCrosshairAndTags();
            }
        }

        public void SetTooltipVisible(bool visible)
        {
            _tooltipVisible = visible;
            // 如果當前正在顯示提示框，立即更新顯示狀態
            if (!visible && _crosshairTooltip != null)
            {
                _crosshairTooltip.Visibility = Visibility.Collapsed;
            }
        }

        public bool IsCrosshairVisible => _crosshairVisible;
        public bool IsTooltipVisible => _tooltipVisible;

        // 基底圖（主圖）可啟用批次繪製以提升效能；副圖多半覆寫 DrawSingleBar，建議關閉。
        protected virtual bool EnableBatchDrawing => true;

        #region Fields

        // --- 子元件 ---
        public readonly PriceInfoPanel _priceInfoPanel;
        private readonly Grid _chartCanvasContainer;
        private readonly Grid _rightPanel; // 右側繪圖區容器（上:工具列/下:畫布）
        private readonly Border _topRightHost; // 上方區塊容器（能設定背景）
        private readonly ContentPresenter _topRightPresenter; // 右側上方可自訂內容
        private CrosshairTooltip? _crosshairTooltip;

        // --- 繪圖圖層 ---
        private readonly Canvas _bottomLayer;
        protected readonly Canvas _kBarLayer;
        private readonly Canvas _indicatorLayer;
        private readonly Canvas _topLayer;
        private readonly List<IOverlayIndicator> _overlays = new();

        // --- 資料 ---
        protected readonly List<GraphKBar> _hisBars = new();
        protected GraphKBar? _floatingKBar;

        // --- 座標計算器 ---
        protected readonly CoordinateCalculator _coordinateCalculator = new();

        // --- 繪圖與座標參數 ---
        protected decimal _displayMinPrice;
        protected decimal _displayMaxPrice;
        protected double _barSpacing;
        protected double _barWidth;
        protected int _priceDecimalPlaces = 0; // 預設 0，小數位外部可設定
        public void SetPriceDecimalPlaces(int places)
        {
            _priceDecimalPlaces = Math.Max(0, places);
            _priceInfoPanel.SetPriceDecimalPlaces(_priceDecimalPlaces);
            RedrawChart();
        }
        public int GetPriceDecimalPlaces() => _priceDecimalPlaces;

        // --- 可見範圍與尺寸 ---
        protected int _visibleStartIndex = 0;
        protected int _visibleBarCount = 0;
        protected double _chartWidth = 0;
        protected double _chartHeight = 0;

        // --- 記憶體管理 ---
        private readonly List<UIElement> _createdUIElements = new();

        // --- UI 元素 ---
        private TextBlock? _highLabel;
        private TextBlock? _lowLabel;
        private Line? _crosshairVLine;
        private Line? _crosshairHLine;
        private Border? _yPriceTag;
        private TextBlock? _yPriceText;
        private Border? _xTimeTag;
        private TextBlock? _xTimeText;
        // 主圖左側 Overlay 標籤
        private readonly HashSet<string> _overlayTags = new();

        // --- 互動狀態 ---
        private bool _isDraggingX = false;
        private Point _lastMousePos;
        private const double _zoomStep = 0.12;
        private const double _minSpacing = 5.0;
        private const double _maxSpacing = 40.0;
        private const double _barWidthPadding = 2.0;

        // 記錄十字線最後一次換算的價格（供外部取用）
        private decimal _lastCrosshairPrice;
        private bool _hasCrosshairPrice;

        #endregion

        public ChartPane()
        {
            ChartFontManager.EnsureInitialized();

            this.ChartTheme = new ChartStyle();
            _barSpacing = this.ChartTheme.DefaultBarSpacing;
            _barWidth = this.ChartTheme.DefaultBarWidth;

            this.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChartTheme.LeftPanelWidth) });
            this.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _priceInfoPanel = new PriceInfoPanel();
            _priceInfoPanel.SectionHeaderClicked += OnInfoPanelSectionClicked;
            _priceInfoPanel.TitleClicked += () => { if (IsMainPane) MainPaneTitleClicked?.Invoke(); };
            Grid.SetColumn(_priceInfoPanel, 0);
            this.Children.Add(_priceInfoPanel);

            // 右側面板：分成上下兩列（上:自訂區，下:實際畫布）
            _rightPanel = new Grid { Background = ChartTheme.BackgroundColor };
            _rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_rightPanel, 1);

            // 上方可插入的區域，預設不顯示（用 Border 供背景色）
            _topRightHost = new Border
            {
                Background = ChartTheme.BackgroundColor,
                Margin = new Thickness(0),
                Visibility = Visibility.Collapsed
            };
            _topRightPresenter = new ContentPresenter();
            _topRightHost.Child = _topRightPresenter;
            Grid.SetRow(_topRightHost, 0);
            _rightPanel.Children.Add(_topRightHost);

            // 下方實際的畫布容器
            _chartCanvasContainer = new Grid();
            Grid.SetRow(_chartCanvasContainer, 1);
            _rightPanel.Children.Add(_chartCanvasContainer);

            _bottomLayer = new Canvas { Background = this.ChartTheme.BackgroundColor, IsHitTestVisible = false };
            _kBarLayer = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
            _indicatorLayer = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
            _topLayer = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = true };

            _chartCanvasContainer.Children.Add(_bottomLayer);
            _chartCanvasContainer.Children.Add(_kBarLayer);
            _chartCanvasContainer.Children.Add(_indicatorLayer);
            _chartCanvasContainer.Children.Add(_topLayer);

            this.Children.Add(_rightPanel);

            InitializeCrosshairElements();

            _chartCanvasContainer.SizeChanged += OnCanvasSizeChanged;
            _topLayer.MouseDown += OnMouseDown;
            _topLayer.MouseUp += OnMouseUp;
            _topLayer.MouseMove += OnMouseMove;
            _topLayer.MouseWheel += OnMouseWheel;
            _topLayer.MouseLeave += OnMouseLeave;
        }

        private void OnInfoPanelSectionClicked(string id)
        {
            try
            {
                var owner = Window.GetWindow(this);
                if (id == "MA")
                {
                    var cfgs = GetOverlayConfigs().Where(o => o.Type == "MA").ToList();
                    var defaults = new List<(int period, string maType, Color color)>();
                    if (cfgs.Count == 0)
                    {
                        defaults.Add((144, "SMA", Color.FromRgb(0xFF, 0xD7, 0x00)));
                    }
                    else
                    {
                        foreach (var c in cfgs)
                        {
                            var col = string.IsNullOrEmpty(c.ColorHex) ? Color.FromRgb(0xFF, 0xD7, 0x00) : (Color)ColorConverter.ConvertFromString(c.ColorHex!);
                            defaults.Add((Math.Max(1, c.Period), c.MaType ?? "MA", col));
                        }
                    }
                    var dlg = new MaSettingsDialog(defaults) { Owner = owner };
                    if (dlg.ShowDialog() == true)
                    {
                        RemoveOverlaysByTag("均線");
                        foreach (var ln in dlg.ResultLines)
                        {
                            AddOverlay(new MaOverlay(ln.Period, ln.MaType, ln.Color, 1.0));
                        }
                        UpdateOverlaySectionsLatest();
                    }
                }
                else if (id == "BOLL")
                {
                    // 取目前值作為預設
                    int defP = 20; double defK = 2.0; Color defFill = Color.FromRgb(0xB7,0xB8,0xB7);
                    Color defEdge = Color.FromRgb(0xB4,0xB4,0xB4);
                    Color defMid = Color.FromRgb(0xD7,0xD4,0xD5);
                    double defOpa = 0.059597315436241624;
                    var curr = GetOverlayConfigs().FirstOrDefault(o => o.Type == "BOLL");
                    if (curr != null)
                    {
                        if (curr.Period > 0) defP = curr.Period;
                        if (curr.K > 0) defK = curr.K;
                        if (!string.IsNullOrEmpty(curr.FillHex)) defFill = (Color)ColorConverter.ConvertFromString(curr.FillHex!);
                        if (!string.IsNullOrEmpty(curr.EdgeColorHex)) defEdge = (Color)ColorConverter.ConvertFromString(curr.EdgeColorHex!);
                        if (!string.IsNullOrEmpty(curr.MidColorHex)) defMid = (Color)ColorConverter.ConvertFromString(curr.MidColorHex!);
                        if (curr.Opacity >= 0 && curr.Opacity <= 1) defOpa = curr.Opacity;
                    }
                    var dlg = new BollSettingsDialog(defP, defK, defFill, defEdge, defMid, defOpa) { Owner = owner };
                    if (dlg.ShowDialog() == true)
                    {
                        RemoveOverlaysByTag("布林通道");
                        AddOverlay(new BollingerOverlay(dlg.Period, dlg.K, dlg.FillColor, dlg.FillOpacity, dlg.EdgeColor, dlg.MidColor));
                        UpdateOverlaySectionsLatest();
                    }
                }
                else if (id == "BBI")
                {
                    int[] defPeriods = new[] { 3, 6, 12, 24 };
                    Color defColor = Color.FromRgb(0xFF, 0x8C, 0x00);
                    var curr = GetOverlayConfigs().FirstOrDefault(o => o.Type == "BBI");
                    if (curr != null)
                    {
                        if (!string.IsNullOrWhiteSpace(curr.BbiPeriodsCsv))
                        {
                            var parsed = ParseBbiPeriods(curr.BbiPeriodsCsv);
                            if (parsed.Length > 0) defPeriods = parsed;
                        }
                        if (!string.IsNullOrEmpty(curr.ColorHex)) defColor = (Color)ColorConverter.ConvertFromString(curr.ColorHex!);
                    }
                    var dlg = new BbiSettingsDialog(defPeriods, defColor) { Owner = owner };
                    if (dlg.ShowDialog() == true)
                    {
                        RemoveOverlaysByTag("BBI");
                        AddOverlay(new BbiOverlay(dlg.Periods, dlg.LineColor));
                        UpdateOverlaySectionsLatest();
                    }
                }
            }
            catch { }
        }

        private void InitializeCrosshairElements()
        {
            _crosshairVLine = new Line
            {
                Stroke = ChartTheme.CrosshairColor,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Visibility = Visibility.Collapsed
            };
            _crosshairHLine = new Line
            {
                Stroke = ChartTheme.CrosshairColor,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Visibility = Visibility.Collapsed
            };
            _topLayer.Children.Add(_crosshairVLine);
            _topLayer.Children.Add(_crosshairHLine);
            Panel.SetZIndex(_crosshairVLine, 800);
            Panel.SetZIndex(_crosshairHLine, 800);

            _crosshairTooltip = new CrosshairTooltip { Visibility = Visibility.Collapsed };
            _topLayer.Children.Add(_crosshairTooltip);

            _yPriceText = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(6, 2, 6, 2), FontSize = ChartTheme.AxisFontSize };
            _yPriceText.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeLg");
            _yPriceTag = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = _yPriceText,
                Visibility = Visibility.Collapsed
            };
            _topLayer.Children.Add(_yPriceTag);
            Panel.SetZIndex(_yPriceTag, 1000);

            _xTimeText = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(6, 2, 6, 2), FontSize = ChartTheme.AxisFontSize };
            _xTimeText.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeLg");
            _xTimeTag = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = _xTimeText,
                Visibility = Visibility.Collapsed
            };
            _topLayer.Children.Add(_xTimeTag);
            Panel.SetZIndex(_xTimeTag, 1000);
        }

        #region Public Methods
        // 於右側繪圖區上方安插自訂 UI（僅主圖會用到；副圖可不設定）
        public void SetRightTopContent(UIElement? content)
        {
            _topRightPresenter.Content = content;
            _topRightHost.Visibility = content == null ? Visibility.Collapsed : Visibility.Visible;
        }

        // 供外部以面板座標換算價格（相容舊版 API）
        public decimal GetPriceAtY(double paneY)
        {
            // 轉為繪圖區相對座標（扣除上邊界）
            double relY = paneY - ChartTheme.ChartMargin.Top;
            if (relY < 0) relY = 0;
            if (relY > _chartHeight) relY = _chartHeight;
            EnsureCoordinateCacheValid();
            return _coordinateCalculator.YToPrice(relY, _displayMaxPrice, _displayMinPrice);
        }

        // 供外部以螢幕 Y 座標換算價格：使用 _topLayer 作為來源座標，避免外層行列/邊距偏移
        public decimal GetPriceAtScreen(double screenX, double screenY)
        {
            try
            {
                var pt = _topLayer.PointFromScreen(new System.Windows.Point(screenX, screenY));
                double relY = pt.Y - ChartTheme.ChartMargin.Top;
                if (relY < 0) relY = 0;
                if (relY > _chartHeight) relY = _chartHeight;
                EnsureCoordinateCacheValid();
                return _coordinateCalculator.YToPrice(relY, _displayMaxPrice, _displayMinPrice);
            }
            catch { return 0m; }
        }

        // 保留舊 API（僅用 Y），可能在多螢幕/DPI 情境下有偏差，建議改用 GetPriceAtScreen(x,y)
        public decimal GetPriceAtScreenY(double screenY) => GetPriceAtScreen(0, screenY);

        public bool TryGetCrosshairPrice(out decimal price)
        {
            price = _lastCrosshairPrice;
            return _hasCrosshairPrice;
        }

        public void LoadHistory(List<ChartKBar> bars)
        {
            if (bars == null) return;

            _hisBars.Clear();
            _floatingKBar = new GraphKBar { IsNullBar = true };

            var barsList = bars.ToList();
            // 僅保留最後 MaxBars 筆（若超過）
            if (barsList.Count > MaxBars)
            {
                barsList = barsList.Skip(barsList.Count - MaxBars).ToList();
            }

            if (barsList.Count > 0 && barsList.Last().IsFloating)
            {
                // 載入浮動K棒（上游已經處理過期判斷）
                var lastBar = barsList.Last();
                _floatingKBar = new GraphKBar(lastBar);
                barsList.RemoveAt(barsList.Count - 1);
            }

            foreach (var bar in barsList)
            {
                _hisBars.Add(new GraphKBar(bar));
            }

            int padding = (_hisBars.Count > 0 ? _hisBars.Count / 2 : 0) + 25;
            int virtualCount = _hisBars.Count + padding;
            int tempVisibleCount = (_chartWidth > 0 && _barSpacing > 0) ? Math.Max(1, (int)Math.Floor(_coordinateCalculator.GetChartWidthMinusBarWidth() / _barSpacing) + 1) : 50;
            int endIndex = Math.Max(0, virtualCount - tempVisibleCount);
            _visibleStartIndex = endIndex;

            UpdateVisibleRange();

            // 先通知 overlays 資料變更，避免第一次 Redraw 時指標陣列尚未就緒
            foreach (var ov in _overlays)
            {
                ov.OnDataChanged(_hisBars);
            }

            RedrawChart();
            UpdateOverlaySectionsLatest();
            UpdateLatestBarInfo();
            if (!_crosshairVisible)
            {
                OnCrosshairIndexChanged(-1, false);
            }
            RaiseXViewChangedIfMain();
        }

        // 新增一根完成K棒至視圖
        public void AddBar(ChartKBar newbar, bool clearFloating = true)
        {
            if (newbar == null) return;
            _hisBars.Add(new GraphKBar(newbar));

            // 修剪至上限
            if (_hisBars.Count > MaxBars)
            {
                int remove = _hisBars.Count - MaxBars;
                _hisBars.RemoveRange(0, remove);
                // 調整可視起點避免落在已刪區間
                _visibleStartIndex = Math.Max(0, _visibleStartIndex - remove);
            }
            if (clearFloating)
            {
                _floatingKBar = new GraphKBar { IsNullBar = true };
            }

            // 先讓 overlays 接收新資料，避免重繪時使用到過期的長度造成索引越界
            foreach (var ov in _overlays) ov.OnDataChanged(_hisBars);

            // 自動卷頁：只有當使用者正在關注最新區域時，才自動卷頁跟隨新 K 棒。
            // 判斷方法：舊的最後一根 K 棒（lastIdx - 1）是否在可視範圍內。
            // 如果在，代表使用者正在看最新區域，則進行自動卷頁；否則不卷頁（使用者在看歷史）。
            if (_visibleBarCount > 0)
            {
                int lastIdx = _hisBars.Count - 1;
                int prevLastIdx = lastIdx - 1; // AddBar 前的最後一根

                // 檢查 AddBar 之前的最後一根是否在可視範圍內
                bool wasTrackingLatest = prevLastIdx >= 0 &&
                                         prevLastIdx >= _visibleStartIndex &&
                                         prevLastIdx <= _visibleStartIndex + _visibleBarCount - 1;

                if (wasTrackingLatest)
                {
                    // 使用者正在關注最新區域，進行自動卷頁
                    int rightMost = _visibleStartIndex + _visibleBarCount - 1;
                    int rightSpace = rightMost - lastIdx;
                    int desiredRight = Math.Max(1, (int)Math.Round(_visibleBarCount * 0.30));
                    if (rightSpace < desiredRight)
                    {
                        int newStart = lastIdx + desiredRight - (_visibleBarCount - 1);
                        if (newStart < 0) newStart = 0;
                        _visibleStartIndex = newStart;
                    }
                }
            }

            UpdateCoordinateCache();
            UpdateVisibleRange();
            RedrawChart();
            RaiseXViewChangedIfMain();

            UpdateOverlaySectionsLatest();
            if (!_crosshairVisible)
            {
                OnCrosshairIndexChanged(-1, false);
            }
        }
        
        public void UpdateLatestBarInfo()
        {
            // The PriceInfoPanel is currently a placeholder, so this method does nothing for now.
        }
        #endregion

        #region Drawing Methods
        private void RedrawChart()
        {
            if (_chartWidth <= 0 || _chartHeight <= 0) return;
            CalculateYAxisRange();
            DrawAllBars();

            // 畫 overlays（主圖）
            foreach (var ov in _overlays)
            {
                ov.OnViewportChanged(_visibleStartIndex, _visibleBarCount, _barSpacing);
                ov.Draw(_indicatorLayer, this);
            }
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _chartWidth = e.NewSize.Width - ChartTheme.YAxisWidth - ChartTheme.ChartMargin.Left - ChartTheme.ChartMargin.Right;
            _chartHeight = e.NewSize.Height - ChartTheme.XAxisHeight - ChartTheme.ChartMargin.Top - ChartTheme.ChartMargin.Bottom;

            if (_chartWidth <= 0 || _chartHeight <= 0) return;

            UpdateCoordinateCache();
            UpdateVisibleRange();
            RedrawChart();
        }

        

        private void UpdateCoordinateCache()
        {
            _coordinateCalculator.UpdateCache(_chartWidth, _chartHeight, _barWidth, ChartTheme.XAxisHeight,
                ChartTheme.ChartMargin.Left, ChartTheme.ChartMargin.Top, ChartTheme.ChartMargin.Right, ChartTheme.ChartMargin.Bottom);
        }

        protected void EnsureCoordinateCacheValid()
        {
            if (!_coordinateCalculator.IsCacheValid)
            {
                UpdateCoordinateCache();
            }
        }

        private void DrawAllBars()
        {
            ClearTrackedUIElements();
            _kBarLayer.Children.Clear();
            _bottomLayer.Children.Clear();
            _indicatorLayer.Children.Clear();
            // _topLayer is not cleared here because it holds persistent crosshair elements

            DrawYAxis();
            DrawXAxis();

            bool hasHistory = _hisBars.Count > 0;
            if (!hasHistory && (_floatingKBar == null || _floatingKBar.IsNullBar)) return;

            EnsureCoordinateCacheValid();

            decimal maxHigh = decimal.MinValue;
            decimal minLow = decimal.MaxValue;
            int maxHighVisIndex = -1;
            int minLowVisIndex = -1;

            if (hasHistory && EnableBatchDrawing)
            {
                // 批次繪製：將所有K棒合併為少量幾何，避免大量 UIElement 造成卡頓
                var bullBody = new StreamGeometry();
                var bearBody = new StreamGeometry();
                var flatBody = new StreamGeometry();
                var bullWick = new StreamGeometry();
                var bearWick = new StreamGeometry();
                var flatWick = new StreamGeometry();

                using (var gcBullBody = bullBody.Open())
                using (var gcBearBody = bearBody.Open())
                using (var gcFlatBody = flatBody.Open())
                using (var gcBullWick = bullWick.Open())
                using (var gcBearWick = bearWick.Open())
                using (var gcFlatWick = flatWick.Open())
                {
                    for (int i = 0; i < _visibleBarCount; i++)
                    {
                        int dataIndex = _visibleStartIndex + i;
                        if (dataIndex < 0 || dataIndex >= _hisBars.Count) continue;
                        var bar = _hisBars[dataIndex];
                        if (bar.High > maxHigh) { maxHigh = bar.High; maxHighVisIndex = i; }
                        if (bar.Low < minLow) { minLow = bar.Low; minLowVisIndex = i; }

                        double x = _coordinateCalculator.GetBarXByVisibleIndex(i, _visibleBarCount, _barSpacing);
                        double centerX = x + _coordinateCalculator.GetHalfBarWidth();
                        double yOpen = _coordinateCalculator.PriceToY(bar.Open, _displayMaxPrice, _displayMinPrice);
                        double yClose = _coordinateCalculator.PriceToY(bar.Close, _displayMaxPrice, _displayMinPrice);
                        double yHigh = _coordinateCalculator.PriceToY(bar.High, _displayMaxPrice, _displayMinPrice);
                        double yLow = _coordinateCalculator.PriceToY(bar.Low, _displayMaxPrice, _displayMinPrice);

                        // Wick
                        var gcWick = gcFlatWick; // default neutral
                        if (bar.Close > bar.Open) gcWick = gcBullWick; else if (bar.Close < bar.Open) gcWick = gcBearWick;
                        gcWick.BeginFigure(new Point(ChartTheme.ChartMargin.Left + centerX, ChartTheme.ChartMargin.Top + yHigh), false, false);
                        gcWick.LineTo(new Point(ChartTheme.ChartMargin.Left + centerX, ChartTheme.ChartMargin.Top + yLow), true, false);

                        // Body
                        double yTop = Math.Min(yOpen, yClose);
                        double height = Math.Abs(yClose - yOpen);
                        if (height < 1) height = 1;
                        var gcBody = gcFlatBody;
                        if (bar.Close > bar.Open) gcBody = gcBullBody; else if (bar.Close < bar.Open) gcBody = gcBearBody;
                        Point p1 = new Point(ChartTheme.ChartMargin.Left + x, ChartTheme.ChartMargin.Top + yTop);
                        Point p2 = new Point(ChartTheme.ChartMargin.Left + x + _barWidth, ChartTheme.ChartMargin.Top + yTop);
                        Point p3 = new Point(ChartTheme.ChartMargin.Left + x + _barWidth, ChartTheme.ChartMargin.Top + yTop + height);
                        Point p4 = new Point(ChartTheme.ChartMargin.Left + x, ChartTheme.ChartMargin.Top + yTop + height);
                        gcBody.BeginFigure(p1, true, true);
                        gcBody.LineTo(p2, true, false);
                        gcBody.LineTo(p3, true, false);
                        gcBody.LineTo(p4, true, false);
                    }
                }

                bullBody.Freeze(); bearBody.Freeze(); flatBody.Freeze();
                bullWick.Freeze(); bearWick.Freeze(); flatWick.Freeze();

                var bullBodyPath = new Path { Data = bullBody, Fill = ChartTheme.BullishColor };
                var bearBodyPath = new Path { Data = bearBody, Fill = ChartTheme.BearishColor };
                var flatBodyPath = new Path { Data = flatBody, Fill = Brushes.Gray };
                var bullWickPath = new Path { Data = bullWick, Stroke = ChartTheme.BullishColor, StrokeThickness = 1 };
                var bearWickPath = new Path { Data = bearWick, Stroke = ChartTheme.BearishColor, StrokeThickness = 1 };
                var flatWickPath = new Path { Data = flatWick, Stroke = Brushes.Gray, StrokeThickness = 1 };

                TrackUIElement(bullBodyPath); _kBarLayer.Children.Add(bullBodyPath);
                TrackUIElement(bearBodyPath); _kBarLayer.Children.Add(bearBodyPath);
                TrackUIElement(flatBodyPath); _kBarLayer.Children.Add(flatBodyPath);
                TrackUIElement(bullWickPath); _kBarLayer.Children.Add(bullWickPath);
                TrackUIElement(bearWickPath); _kBarLayer.Children.Add(bearWickPath);
                TrackUIElement(flatWickPath); _kBarLayer.Children.Add(flatWickPath);
            }
            else if (hasHistory)
            {
                for (int i = 0; i < _visibleBarCount; i++)
                {
                    int dataIndex = _visibleStartIndex + i;
                    if (dataIndex < 0 || dataIndex >= _hisBars.Count) continue;
                    var bar = _hisBars[dataIndex];
                    if (bar.High > maxHigh) { maxHigh = bar.High; maxHighVisIndex = i; }
                    if (bar.Low < minLow) { minLow = bar.Low; minLowVisIndex = i; }
                    double x = _coordinateCalculator.GetBarXByVisibleIndex(i, _visibleBarCount, _barSpacing);
                    DrawSingleBar(bar, x);
                }
            }

            // 歷史棒繪製完成，通知子類別（例如提交批次）
            OnHistoricalBarsDrawn();

            // 如有浮動K棒，額外繪製在末端，並更新當前可視高低點
            if (_floatingKBar != null && !_floatingKBar.IsNullBar)
            {
                int floatDataIndex = _hisBars.Count; // 浮動棒視為下一根
                int visIndex = floatDataIndex - _visibleStartIndex;
                if (visIndex >= 0 && visIndex < _visibleBarCount)
                {
                    double x = _coordinateCalculator.GetBarXByVisibleIndex(visIndex, _visibleBarCount, _barSpacing);
                    DrawFloatingBar(_floatingKBar, x, ref maxHigh, ref minLow, ref maxHighVisIndex, ref minLowVisIndex, visIndex);
                }
            }

            DrawHighLowLabels(maxHigh, minLow, maxHighVisIndex, minLowVisIndex);
        }

        /// <summary>
        /// 歷史K棒繪製完成後呼叫，供子類別處理批次提交等收尾工作
        /// </summary>
        protected virtual void OnHistoricalBarsDrawn()
        {
            // 基底類別無需動作
        }

        /// <summary>
        /// 繪製浮動K棒（預設呼叫 DrawSingleBar）
        /// </summary>
        protected virtual void DrawFloatingBar(GraphKBar bar, double x, ref decimal maxHigh, ref decimal minLow, ref int maxHighVisIndex, ref int minLowVisIndex, int visIndex)
        {
            DrawSingleBar(bar, x);
            if (bar.High > maxHigh) { maxHigh = bar.High; maxHighVisIndex = visIndex; }
            if (bar.Low < minLow) { minLow = bar.Low; minLowVisIndex = visIndex; }
        }

        protected virtual void DrawSingleBar(GraphKBar bar, double x)
        {
            EnsureCoordinateCacheValid();
            Brush color = GetBarColor(bar.Open, bar.Close);
            double yOpen = _coordinateCalculator.PriceToY(bar.Open, _displayMaxPrice, _displayMinPrice);
            double yClose = _coordinateCalculator.PriceToY(bar.Close, _displayMaxPrice, _displayMinPrice);
            double yHigh = _coordinateCalculator.PriceToY(bar.High, _displayMaxPrice, _displayMinPrice);
            double yLow = _coordinateCalculator.PriceToY(bar.Low, _displayMaxPrice, _displayMinPrice);
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

        public void AddTick(decimal price, int volume = 1)
        {
            if (_floatingKBar == null || _floatingKBar.IsNullBar)
            {
                // IsNullBar = true 時，第一個 tick 就是 OHLC 的價格（產生缺口）
                // 使用「上一根時間 + 推估週期」作為浮動棒時間（若無資料則維持 MinValue）
                int estPeriod = 1;
                try { estPeriod = Math.Max(1, EstimatePeriodMinutes()); } catch { estPeriod = 1; }
                DateTime barTime;
                if (_hisBars.Count > 0)
                {
                    var prev = _hisBars[_hisBars.Count - 1].Time;
                    barTime = prev == DateTime.MinValue ? prev : prev.AddMinutes(estPeriod);
                }
                else
                {
                    barTime = DateTime.MinValue;
                }
                _floatingKBar = new GraphKBar
                {
                    Time = barTime,
                    Open = price,
                    High = price,
                    Low = price,
                    Close = price,
                    Volume = volume,
                    IsNullBar = false
                };
            }
            else
            {
                if (price > _floatingKBar.High) _floatingKBar.High = price;
                if (price < _floatingKBar.Low) _floatingKBar.Low = price;
                _floatingKBar.Close = price;
                _floatingKBar.Volume = volume; // 直接設定總量，不是累加
            }

            // 僅重繪一次（包含浮動棒）
            UpdateCoordinateCache();
            UpdateVisibleRange();
            RedrawChart();
        }

        // 清除浮動K棒並重繪
        public void ClearFloatingBar()
        {
            _floatingKBar = new GraphKBar { IsNullBar = true };
            UpdateCoordinateCache();
            UpdateVisibleRange();
            RedrawChart();
        }

        protected virtual void DrawYAxis()
        {
            var yAxisLine = new Line { X1 = ChartTheme.ChartMargin.Left + _chartWidth, X2 = ChartTheme.ChartMargin.Left + _chartWidth, Y1 = ChartTheme.ChartMargin.Top, Y2 = ChartTheme.ChartMargin.Top + _chartHeight, Stroke = ChartTheme.AxisLineColor, StrokeThickness = 1 };
            TrackUIElement(yAxisLine);
            _bottomLayer.Children.Add(yAxisLine);
            if (_visibleBarCount <= 0) return;
            decimal tickSize = GetAppropriateTickSize();
            if (tickSize <= 0) return;
            decimal startPrice = Math.Ceiling(_displayMinPrice / tickSize) * tickSize;
            for (decimal price = startPrice; price <= _displayMaxPrice; price += tickSize)
            {
                double y = _coordinateCalculator.PriceToY(price, _displayMaxPrice, _displayMinPrice);
                if (y < 0 || y > _chartHeight) continue;
                var gridLine = new Line { X1 = ChartTheme.ChartMargin.Left, X2 = ChartTheme.ChartMargin.Left + _chartWidth, Y1 = ChartTheme.ChartMargin.Top + y, Y2 = ChartTheme.ChartMargin.Top + y, Stroke = ChartTheme.GridLineColor, StrokeThickness = 1 };
                TrackUIElement(gridLine);
                _bottomLayer.Children.Add(gridLine);
                var priceLabel = new TextBlock { Text = price.ToString($"F{_priceDecimalPlaces}"), Foreground = ChartTheme.YAxisTextColor, FontSize = ChartTheme.AxisFontSize, Background = ChartTheme.BackgroundColor, Padding = new Thickness(2,0,2,0) };
                TrackUIElement(priceLabel);
                priceLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textHeight = priceLabel.DesiredSize.Height;
                double finalTop = ChartTheme.ChartMargin.Top + y - (textHeight / 2);
                finalTop = Math.Max(ChartTheme.ChartMargin.Top, finalTop);
                finalTop = Math.Min(ChartTheme.ChartMargin.Top + _chartHeight - textHeight, finalTop);
                Canvas.SetLeft(priceLabel, ChartTheme.ChartMargin.Left + _chartWidth + 5);
                Canvas.SetTop(priceLabel, finalTop);
                _topLayer.Children.Add(priceLabel);
                Panel.SetZIndex(priceLabel, 900);
            }
        }

        private void DrawXAxis()
        {
            if (ShowXAxisTimeLabels)
            {
                // 填滿底部X軸區域背景，避免透出下方窗格
                var axisBg = new Rectangle { Width = _chartWidth, Height = ChartTheme.XAxisHeight, Fill = ChartTheme.BackgroundColor };
                TrackUIElement(axisBg);
                Canvas.SetLeft(axisBg, ChartTheme.ChartMargin.Left);
                Canvas.SetTop(axisBg, ChartTheme.ChartMargin.Top + _chartHeight);
                _bottomLayer.Children.Add(axisBg);

                // 移除底部白色軸線，避免與上方工具列之間出現白線感
            }
            if (_visibleBarCount <= 0 || _hisBars.Count == 0) return;
            double minLabelSpacing = ChartTheme.XAxisMinLabelSpacing <= 0 ? 80 : ChartTheme.XAxisMinLabelSpacing;
            int intervalMinutes = SelectXAxisIntervalMinutes();
            double lastLabelX = -1000;
            for (int i = 0; i < _visibleBarCount; i++)
            {
                int dataIndex = _visibleStartIndex + i;
                if (dataIndex < 0 || dataIndex >= _hisBars.Count) continue;
                var bar = _hisBars[dataIndex];
                bool isAlignmentBar = bar.Tag.HasFlag(KBarTag.xAxisMarkSplit);
                int totalMinutes = bar.Time.Hour * 60 + bar.Time.Minute;
                bool isMinorTick = (intervalMinutes > 0) && (totalMinutes % intervalMinutes == 0);
                if (!isAlignmentBar && !isMinorTick) continue;
                double xCenter = _coordinateCalculator.GetBarXByVisibleIndex(i, _visibleBarCount, _barSpacing) + _coordinateCalculator.GetHalfBarWidth();
                var gridLine = new Line { X1 = ChartTheme.ChartMargin.Left + xCenter, X2 = ChartTheme.ChartMargin.Left + xCenter, Y1 = ChartTheme.ChartMargin.Top, Y2 = ChartTheme.ChartMargin.Top + _chartHeight, Stroke = isAlignmentBar ? ChartTheme.AlignmentLineColor : ChartTheme.GridLineColor, StrokeThickness = 1, };
                if (isAlignmentBar) { gridLine.StrokeDashArray = new DoubleCollection { 4, 2 }; }
                TrackUIElement(gridLine);
                _bottomLayer.Children.Add(gridLine);
                if (ShowXAxisTimeLabels && Math.Abs(xCenter - lastLabelX) > minLabelSpacing)
                {
                    var tb = new TextBlock { Text = bar.Time.ToString("HH:mm"), Foreground = ChartTheme.XAxisTextColor, FontSize = ChartTheme.AxisFontSize, Background = ChartTheme.BackgroundColor, Padding = new Thickness(4,0,4,0) };
                    TrackUIElement(tb);
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double textWidth = tb.DesiredSize.Width;
                    Canvas.SetLeft(tb, ChartTheme.ChartMargin.Left + xCenter - (textWidth / 2));
                    Canvas.SetTop(tb, ChartTheme.ChartMargin.Top + _chartHeight + 5);
                    _topLayer.Children.Add(tb);
                    lastLabelX = xCenter;
                }
            }
        }

        private void DrawHighLowLabels(decimal maxHigh, decimal minLow, int maxHighVisIndex, int minLowVisIndex)
        {
            if (_highLabel != null) { _kBarLayer.Children.Remove(_highLabel); TrackedUIElementRemove(_highLabel); } 
            if (_lowLabel != null) { _kBarLayer.Children.Remove(_lowLabel); TrackedUIElementRemove(_lowLabel); }

            if (!ShowHighLowLabels) return;

            if (maxHighVisIndex != -1)
            {
                double x = _coordinateCalculator.GetBarXByVisibleIndex(maxHighVisIndex, _visibleBarCount, _barSpacing);
                double centerX = x + _coordinateCalculator.GetHalfBarWidth();
                double yHigh = _coordinateCalculator.PriceToY(maxHigh, _displayMaxPrice, _displayMinPrice);
                _highLabel = new TextBlock { Text = maxHigh.ToString($"F{_priceDecimalPlaces}"), Foreground = ChartTheme.HighPriceLabelColor, FontSize = ChartTheme.HighLowFontSize, Background = ChartTheme.BackgroundColor };
                TrackUIElement(_highLabel);
                _highLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double labelWidth = _highLabel.DesiredSize.Width;
                double labelHeight = _highLabel.DesiredSize.Height;
                double finalTop = ChartTheme.ChartMargin.Top + yHigh - labelHeight - 5;
                finalTop = Math.Max(ChartTheme.ChartMargin.Top, finalTop);
                double xLeftHigh = ChartTheme.ChartMargin.Left + centerX - labelWidth / 2;
                xLeftHigh = Math.Max(ChartTheme.ChartMargin.Left, Math.Min(ChartTheme.ChartMargin.Left + _chartWidth - labelWidth, xLeftHigh));
                Canvas.SetLeft(_highLabel, xLeftHigh);
                Canvas.SetTop(_highLabel, finalTop);
                _kBarLayer.Children.Add(_highLabel);
            }
            if (minLowVisIndex != -1)
            {
                double x = _coordinateCalculator.GetBarXByVisibleIndex(minLowVisIndex, _visibleBarCount, _barSpacing);
                double centerX = x + _coordinateCalculator.GetHalfBarWidth();
                double yLow = _coordinateCalculator.PriceToY(minLow, _displayMaxPrice, _displayMinPrice);
                _lowLabel = new TextBlock { Text = minLow.ToString($"F{_priceDecimalPlaces}"), Foreground = ChartTheme.LowPriceLabelColor, FontSize = ChartTheme.HighLowFontSize, Background = ChartTheme.BackgroundColor };
                TrackUIElement(_lowLabel);
                _lowLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double labelWidthLow = _lowLabel.DesiredSize.Width;
                double labelHeight = _lowLabel.DesiredSize.Height;
                double finalTop = ChartTheme.ChartMargin.Top + yLow + 5;
                finalTop = Math.Min(finalTop, ChartTheme.ChartMargin.Top + _chartHeight - labelHeight);
                double xLeftLow = ChartTheme.ChartMargin.Left + centerX - labelWidthLow / 2;
                xLeftLow = Math.Max(ChartTheme.ChartMargin.Left, Math.Min(ChartTheme.ChartMargin.Left + _chartWidth - labelWidthLow, xLeftLow));
                Canvas.SetLeft(_lowLabel, xLeftLow);
                Canvas.SetTop(_lowLabel, finalTop);
                _kBarLayer.Children.Add(_lowLabel);
            }
        }
        #endregion

        #region Calculation & Helpers
        private decimal GetAppropriateTickSize()
        {
            if (_chartHeight <= 0) return 100m;
            decimal priceRange = _displayMaxPrice - _displayMinPrice;
            if (priceRange <= 0) return 100m;

            double targetPixels = ChartTheme.YAxisTargetPixelsPerStep <= 0 ? 120 : ChartTheme.YAxisTargetPixelsPerStep;
            int minTicks = Math.Max(2, ChartTheme.YAxisMinTickCount);
            int maxTicks = Math.Max(minTicks, ChartTheme.YAxisMaxTickCount);

            decimal desiredTicks = (decimal)(_chartHeight / targetPixels);
            if (desiredTicks < minTicks) desiredTicks = minTicks;
            if (desiredTicks > maxTicks) desiredTicks = maxTicks;

            decimal desiredRawStep = priceRange / desiredTicks;
            decimal stepFloor = NiceStepFloor(desiredRawStep);
            decimal stepCeil = NiceStepCeil(desiredRawStep);

            // 以邊界保證 tick 數介於 [min, max]
            decimal minBoundStep = NiceStepCeil(priceRange / maxTicks); // 不超過最多線 => step 不能太小
            decimal maxBoundStep = NiceStepFloor(priceRange / minTicks); // 至少要有最少線 => step 不能太大

            decimal candidate = stepFloor; // 優先選 floor 讓線不要太少
            if (candidate < minBoundStep) candidate = minBoundStep;
            if (candidate > maxBoundStep) candidate = maxBoundStep;
            return candidate > 0 ? candidate : 1m;
        }

        // 估算目前K棒的週期（分鐘）。優先取可視區內相鄰K棒的最小正差，避免因為午休/斷層被誤判。
        private int EstimatePeriodMinutes()
        {
            if (_hisBars.Count < 2) return 1;
            int start = Math.Max(1, _visibleStartIndex);
            int end = Math.Min(_hisBars.Count - 1, _visibleStartIndex + Math.Max(1, _visibleBarCount));
            int best = int.MaxValue;
            int sampleCount = 0;
            for (int i = start; i <= end; i++)
            {
                var t1 = _hisBars[i - 1].Time;
                var t2 = _hisBars[i].Time;
                int diff = (int)Math.Round((t2 - t1).TotalMinutes);
                if (diff > 0)
                {
                    if (diff < best) best = diff;
                    sampleCount++;
                    if (sampleCount >= 50) break;
                }
            }
            if (best == int.MaxValue)
            {
                int fallback = (int)Math.Round((_hisBars[1].Time - _hisBars[0].Time).TotalMinutes);
                return fallback > 0 ? fallback : 1;
            }
            return Math.Max(1, best);
        }

        // 根據可視區密度，決定X軸垂直格線的時間間距（30/60/120分鐘）。
        // 基本為30分鐘，若格線太密則升級至60或120分鐘。
        private int SelectXAxisIntervalMinutes()
        {
            // 候選清單：30 -> 60 -> 120 分鐘
            int[] candidates = new[] { 30, 60, 120 };
            if (_visibleBarCount <= 0 || _hisBars.Count == 0) return candidates[0];

            double minPx = ChartTheme.XAxisMinGridSpacing <= 0 ? 50 : ChartTheme.XAxisMinGridSpacing;

            foreach (int iv in candidates)
            {
                double lastTickX = double.NegativeInfinity;
                bool ok = true;
                for (int i = 0; i < _visibleBarCount; i++)
                {
                    int dataIndex = _visibleStartIndex + i;
                    if (dataIndex < 0 || dataIndex >= _hisBars.Count) continue;
                    var bar = _hisBars[dataIndex];
                    int totalMinutes = bar.Time.Hour * 60 + bar.Time.Minute;
                    bool onIntervalTick = (iv > 0) && (totalMinutes % iv == 0);
                    if (!onIntervalTick) continue; // 只用刻度線檢查密度，不讓對齊線影響選擇

                    double xCenter = _coordinateCalculator.GetBarXByVisibleIndex(i, _visibleBarCount, _barSpacing) + _coordinateCalculator.GetHalfBarWidth();
                    if (double.IsNegativeInfinity(lastTickX))
                    {
                        lastTickX = xCenter;
                    }
                    else
                    {
                        if (xCenter - lastTickX < minPx)
                        {
                            ok = false;
                            break;
                        }
                        lastTickX = xCenter;
                    }
                }
                if (ok) return iv;
            }
            return candidates[^1];
        }

        // 取「漂亮數字」上界（>= raw）：1, 2, 2.5, 5, 10 × 10^n
        private static decimal NiceStepCeil(decimal raw)
        {
            if (raw <= 0) return 1m;
            double d = (double)raw;
            double exp = Math.Floor(Math.Log10(d));
            decimal baseVal = (decimal)Math.Pow(10, exp);
            decimal norm = raw / baseVal;
            if (norm <= 1m) return 1m * baseVal;
            if (norm <= 2m) return 2m * baseVal;
            if (norm <= 2.5m) return 2.5m * baseVal;
            if (norm <= 5m) return 5m * baseVal;
            return 10m * baseVal;
        }

        // 取「漂亮數字」下界（<= raw）：1, 2, 2.5, 5, 10 × 10^n
        private static decimal NiceStepFloor(decimal raw)
        {
            if (raw <= 0) return 1m;
            double d = (double)raw;
            double exp = Math.Floor(Math.Log10(d));
            decimal baseVal = (decimal)Math.Pow(10, exp);
            decimal norm = raw / baseVal;
            if (norm >= 10m) return 10m * baseVal;
            if (norm >= 5m) return 5m * baseVal;
            if (norm >= 2.5m) return 2.5m * baseVal;
            if (norm >= 2m) return 2m * baseVal;
            return 1m * baseVal;
        }

        protected virtual void CalculateYAxisRange()
        {
            if (_hisBars.Count == 0 && (_floatingKBar == null || _floatingKBar.IsNullBar)) { _displayMaxPrice = 100; _displayMinPrice = 0; return; }

            decimal? maxHigh = null, minLow = null;
            int firstDataIndexInView = Math.Max(0, _visibleStartIndex), lastDataIndexInView = Math.Min(_hisBars.Count, _visibleStartIndex + _visibleBarCount);
            int dataCountInView = lastDataIndexInView - firstDataIndexInView;
            if (dataCountInView > 0)
            {
                var visibleRealBars = _hisBars.Skip(firstDataIndexInView).Take(dataCountInView);
                maxHigh = visibleRealBars.Max(b => b.High);
                minLow = visibleRealBars.Min(b => b.Low);
            }

            // 將浮動棒也納入可視範圍的估算（避免超出邊界不重繪）
            bool floatingVisible = false;
            int floatVisIndex = -1;
            if (_floatingKBar != null && !_floatingKBar.IsNullBar)
            {
                int floatDataIndex = _hisBars.Count; // 視為下一根
                floatVisIndex = floatDataIndex - _visibleStartIndex;
                floatingVisible = (floatVisIndex >= 0 && floatVisIndex < _visibleBarCount);
                if (floatingVisible)
                {
                    if (!maxHigh.HasValue || _floatingKBar.High > maxHigh.Value) maxHigh = _floatingKBar.High;
                    if (!minLow.HasValue || _floatingKBar.Low < minLow.Value) minLow = _floatingKBar.Low;
                }
            }

            if (maxHigh.HasValue && minLow.HasValue)
            {
                if (maxHigh.Value == minLow.Value)
                {
                    decimal buffer = Math.Abs(maxHigh.Value * 0.001m); if (buffer == 0) buffer = 1m;
                    _displayMaxPrice = maxHigh.Value + buffer;
                    _displayMinPrice = minLow.Value - buffer;
                }
                else
                {
                    decimal range = maxHigh.Value - minLow.Value;
                    _displayMaxPrice = maxHigh.Value + range * 0.10m;
                    _displayMinPrice = minLow.Value - range * 0.05m;
                }
            }
            else
            {
                // 沒有任何可視實體棒，但若有浮動棒也要提供基本範圍
                if (_floatingKBar != null && !_floatingKBar.IsNullBar)
                {
                    decimal hi = _floatingKBar.High, lo = _floatingKBar.Low;
                    decimal range = Math.Max(1m, hi - lo);
                    _displayMaxPrice = hi + range * 0.10m;
                    _displayMinPrice = lo - range * 0.05m;
                }
                else if (_displayMaxPrice == 0 && _displayMinPrice == 0)
                {
                    _displayMaxPrice = 100; _displayMinPrice = 0;
                }
            }
        }

        private void UpdateVisibleRange()
        {
            if (_chartWidth <= 0) return;
            int capacity = Math.Max(1, (int)Math.Floor(_coordinateCalculator.GetChartWidthMinusBarWidth() / _barSpacing) + 1);
            _visibleBarCount = capacity;
            int padding = _visibleBarCount / 2;
            int virtualCount = _hisBars.Count + padding;
            int maxStartIndex = Math.Max(0, virtualCount - _visibleBarCount);
            _visibleStartIndex = Math.Max(0, Math.Min(maxStartIndex, _visibleStartIndex));
        }

        protected Brush GetBarColor(decimal open, decimal close) => close > open ? ChartTheme.BullishColor : (close < open ? ChartTheme.BearishColor : Brushes.Gray);
        protected void TrackUIElement(UIElement element) { if (element != null && !_createdUIElements.Contains(element)) _createdUIElements.Add(element); }
        protected void TrackedUIElementRemove(UIElement element) { if(element != null) _createdUIElements.Remove(element); }
        protected void AddToBottomLayer(UIElement element) { if (element != null) { TrackUIElement(element); _bottomLayer.Children.Add(element); } }
        private void ClearTrackedUIElements()
        {
            foreach (var element in _createdUIElements.ToList()) // ToList creates a copy for safe removal
            {
                if (element is FrameworkElement fe && fe.Parent is Panel parent) parent.Children.Remove(element);
            }
            _createdUIElements.Clear();
        }
        #endregion

        #region X-View Sync API
        // 提供 Overlays 使用的座標輔助
        public double XLeftByVisibleIndex(int visibleIndexFromLeft, int visibleBarCount, double spacing)
            => _coordinateCalculator.GetBarXByVisibleIndex(visibleIndexFromLeft, visibleBarCount, spacing);
        public double HalfBarWidth() => _coordinateCalculator.GetHalfBarWidth();
        public double PriceToY(decimal price) => _coordinateCalculator.PriceToY(price, _displayMaxPrice, _displayMinPrice);

        public void SetPanelTitle(string title) { _priceInfoPanel.SetTitle(title); }
        public void SetTitleClickable(bool clickable) { _priceInfoPanel.SetTitleClickable(clickable); }
        public void SetContractInfo(string name, string ym) { _priceInfoPanel.SetContractInfo(name, ym); }
        public void SetQuotePlaceholders() { _priceInfoPanel.SetQuotePlaceholders(); }
        public void SetQuoteInfo(string? quoteTime, string? ask, string? last, string? bid, string? volume, string? dayChange)
            => _priceInfoPanel.SetQuoteInfo(quoteTime, ask, last, bid, volume, dayChange);
        public void AddOverlayTag(string tag) { if (!string.IsNullOrWhiteSpace(tag) && _overlayTags.Add(tag)) _priceInfoPanel.SetTags(_overlayTags); }
        public void RemoveOverlayTag(string tag) { if (!string.IsNullOrWhiteSpace(tag) && _overlayTags.Remove(tag)) _priceInfoPanel.SetTags(_overlayTags); }
        public void ClearOverlayTags() { _overlayTags.Clear(); _priceInfoPanel.SetTags(_overlayTags); }
        public void AddOverlay(IOverlayIndicator overlay)
        {
            if (!_overlays.Contains(overlay))
            {
                _overlays.Add(overlay);
                overlay.OnDataChanged(_hisBars);
                overlay.OnViewportChanged(_visibleStartIndex, _visibleBarCount, _barSpacing);
                RedrawChart();
                UpdateOverlaySectionsLatest();
            }
        }
        public void RemoveOverlay(IOverlayIndicator overlay)
        {
            if (_overlays.Remove(overlay)) RedrawChart();
        }
        public void ClearOverlays()
        {
            _overlays.Clear();
            RedrawChart();
        }
        public void RemoveOverlaysByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            _overlays.RemoveAll(o => o.TagName == tag);
            RedrawChart();
            UpdateOverlaySectionsLatest();
        }

        public void RemoveMaByPeriod(int period)
        {
            _overlays.RemoveAll(o => o is MaOverlay ma && ma.Period == period);
            RedrawChart();
            UpdateOverlaySectionsLatest();
        }

        // 提供 overlays 的座標與裁切資訊
        public Rect GetChartDrawRect() => new Rect(ChartTheme.ChartMargin.Left, ChartTheme.ChartMargin.Top, _chartWidth, _chartHeight);
        protected virtual void OnCrosshairIndexChanged(int visibleIndex, bool isValid) { }

        private void UpdateOverlaySectionsLatest()
        {
            if (!IsMainPane)
                return;
            if (_overlays.Count == 0)
            {
                _priceInfoPanel.RemoveSection("MA");
                _priceInfoPanel.RemoveSection("BOLL");
                _priceInfoPanel.RemoveSection("BBI");
                return;
            }

            // 若尚未有任何資料，但已經加入 Overlay，仍然顯示預告區塊，數值以 "---" 佔位
            if (_hisBars.Count == 0)
            {
                var maPlaceholders = new List<PriceInfoPanel.InfoLine>();
                foreach (var ov in _overlays.Where(o => o is MaOverlay).Cast<MaOverlay>())
                {
                    maPlaceholders.Add(new PriceInfoPanel.InfoLine
                    {
                        Label = $"MA{ov.Period}:",
                        ValueText = "---",
                        ValueBrush = new SolidColorBrush(ov.LineColor),
                        ArrowDir = null
                    });
                }
                if (maPlaceholders.Count > 0) _priceInfoPanel.SetSection("MA", "均線", maPlaceholders); else _priceInfoPanel.RemoveSection("MA");

                bool hasBoll = _overlays.Any(o => o is BollingerOverlay);
                if (hasBoll)
                {
                    var bbPlaceholders = new List<PriceInfoPanel.InfoLine>
                    {
                        new PriceInfoPanel.InfoLine { Label = "Up:",  ValueText = "---", ValueBrush = new SolidColorBrush(Color.FromRgb(0xB4,0xB4,0xB4)), ArrowDir = null },
                        new PriceInfoPanel.InfoLine { Label = "Mid:", ValueText = "---", ValueBrush = new SolidColorBrush(Color.FromRgb(0xD7,0xD4,0xD5)),  ArrowDir = null },
                        new PriceInfoPanel.InfoLine { Label = "Low:", ValueText = "---", ValueBrush = new SolidColorBrush(Color.FromRgb(0xB4,0xB4,0xB4)), ArrowDir = null }
                    };
                    _priceInfoPanel.SetSection("BOLL", "布林通道", bbPlaceholders);
                }
                else _priceInfoPanel.RemoveSection("BOLL");

                bool hasBbi = _overlays.Any(o => o is BbiOverlay);
                if (hasBbi)
                {
                    var bbiPlaceholders = new List<PriceInfoPanel.InfoLine>
                    {
                        new PriceInfoPanel.InfoLine { Label = "BBI:", ValueText = "---", ValueBrush = new SolidColorBrush(Color.FromRgb(0xE9,0x1E,0x1F)), ArrowDir = null }
                    };
                    _priceInfoPanel.SetSection("BBI", "BBI", bbiPlaceholders);
                }
                else _priceInfoPanel.RemoveSection("BBI");
                return;
            }
            int dataIndex = _hisBars.Count - 1;
            int prevIndex = Math.Max(0, dataIndex - 1);
            var maLines = new List<PriceInfoPanel.InfoLine>();
            foreach (var ov in _overlays.Where(o => o.TagName == "均線"))
                maLines.AddRange(ov.GetInfoLines(dataIndex, prevIndex, _priceDecimalPlaces));
            if (maLines.Count > 0) _priceInfoPanel.SetSection("MA", "均線", maLines); else _priceInfoPanel.RemoveSection("MA");

            var bbLines = new List<PriceInfoPanel.InfoLine>();
            foreach (var ov in _overlays.Where(o => o.TagName == "布林通道"))
                bbLines.AddRange(ov.GetInfoLines(dataIndex, prevIndex, _priceDecimalPlaces));
            if (bbLines.Count > 0) _priceInfoPanel.SetSection("BOLL", "布林通道", bbLines); else _priceInfoPanel.RemoveSection("BOLL");

            var bbiLines = new List<PriceInfoPanel.InfoLine>();
            foreach (var ov in _overlays.Where(o => o.TagName == "BBI"))
                bbiLines.AddRange(ov.GetInfoLines(dataIndex, prevIndex, _priceDecimalPlaces));
            if (bbiLines.Count > 0) _priceInfoPanel.SetSection("BBI", "BBI", bbiLines); else _priceInfoPanel.RemoveSection("BBI");
        }

        // Export/Import overlays for persistence
        public List<OverlayConfig> GetOverlayConfigs()
        {
            var list = new List<OverlayConfig>();
            foreach (var ov in _overlays)
            {
                if (ov is MaOverlay ma)
                {
                    list.Add(new OverlayConfig { Type = "MA", Period = ma.Period, MaType = ma.MaType, ColorHex = ColorToHex(ma.LineColor) });
                }
                else if (ov is BollingerOverlay bb)
                {
                    var (period, k) = bb.GetParameters();
                    var (fill, opacity, edge, mid) = bb.GetAppearance();
                    list.Add(new OverlayConfig
                    {
                        Type = "BOLL",
                        Period = period,
                        K = k,
                        FillHex = ColorToHex(fill),
                        MidColorHex = ColorToHex(mid),
                        Opacity = opacity,
                        EdgeColorHex = ColorToHex(edge)
                    });
                }
                else if (ov is BbiOverlay bbi)
                {
                    list.Add(new OverlayConfig { Type = "BBI", ColorHex = ColorToHex(bbi.LineColor), BbiPeriodsCsv = string.Join(",", bbi.GetParameters()) });
                }
            }
            return list;
        }

        public void SetOverlaysFromConfigs(IEnumerable<OverlayConfig> configs)
        {
            ClearOverlays();
            foreach (var c in configs)
            {
                if (c.Type == "MA")
                {
                    var col = string.IsNullOrEmpty(c.ColorHex) ? Color.FromRgb(0xFF,0xD7,0x00) : HexToColor(c.ColorHex!);
                    AddOverlay(new MaOverlay(c.Period, c.MaType ?? "MA", col));
                }
                else if (c.Type == "BOLL")
                {
                    var fill = string.IsNullOrEmpty(c.FillHex) ? Color.FromRgb(0x64,0xA0,0xFF) : HexToColor(c.FillHex!);
                    var mid = string.IsNullOrEmpty(c.MidColorHex) ? Color.FromRgb(236, 210, 0) : HexToColor(c.MidColorHex!);
                    var edge = string.IsNullOrEmpty(c.EdgeColorHex) ? fill : HexToColor(c.EdgeColorHex!);
                    AddOverlay(new BollingerOverlay(c.Period, c.K, fill, c.Opacity, edge, mid));
                }
                else if (c.Type == "BBI")
                {
                    var col = string.IsNullOrEmpty(c.ColorHex) ? Color.FromRgb(0xFF,0x8C,0x00) : HexToColor(c.ColorHex!);
                    var periods = ParseBbiPeriods(c.BbiPeriodsCsv);
                    if (periods.Length == 0) periods = new[] { 5, 10, 30, 60 };
                    if (string.IsNullOrEmpty(c.ColorHex)) col = Color.FromRgb(0xE9, 0x1E, 0x1F);
                    AddOverlay(new BbiOverlay(periods, col));
                }
            }
        }

        private static int[] ParseBbiPeriods(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
            var parts = csv.Split(new[] { ',', ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>();
            foreach (var p in parts)
            {
                if (int.TryParse(p, out var v) && v > 0) list.Add(v);
            }
            return list.ToArray();
        }

        private static string ColorToHex(Color c) => string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        private static Color HexToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        public (int visibleStartIndex, double barSpacing, double barWidth) GetXViewState()
            => (_visibleStartIndex, _barSpacing, _barWidth);

        public void ApplyXViewState(int visibleStartIndex, double barSpacing)
        {
            _visibleStartIndex = Math.Max(0, visibleStartIndex);
            _barSpacing = Math.Max(_minSpacing, Math.Min(_maxSpacing, barSpacing));
            _barWidth = Math.Max(1.0, _barSpacing - _barWidthPadding);
            UpdateCoordinateCache();
            UpdateVisibleRange();
            RedrawChart();
        }

        protected void RaiseXViewChangedIfMain()
        {
            if (IsMainPane)
            {
                XViewChanged?.Invoke(_visibleStartIndex, _barSpacing, _barWidth);
            }
        }
        #endregion

        #region Mouse Interaction
        private bool IsMouseOnChartArea(Point pos) => pos.X >= 0 && pos.X <= _chartCanvasContainer.ActualWidth && pos.Y >= 0 && pos.Y <= _chartCanvasContainer.ActualHeight;

        private void OnMouseDown(object sender, MouseButtonEventArgs e) { if (!IsMainPane) return; if (e.LeftButton == MouseButtonState.Pressed && IsMouseOnChartArea(e.GetPosition(_topLayer))) { _isDraggingX = true; _lastMousePos = e.GetPosition(_topLayer); _topLayer.CaptureMouse(); _topLayer.Cursor = Cursors.SizeWE; EnableLayerCache(true); } }
        private void OnMouseUp(object sender, MouseButtonEventArgs e) { if (!IsMainPane) return; if (_isDraggingX) { _isDraggingX = false; _topLayer.ReleaseMouseCapture(); _topLayer.Cursor = Cursors.Arrow; EnableLayerCache(false); } }
        private void OnMouseLeave(object sender, MouseEventArgs e) => HideCrosshairAndTags();

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(_topLayer);
            if (IsMainPane && _isDraggingX && e.LeftButton == MouseButtonState.Pressed)
            {
                double deltaX = pos.X - _lastMousePos.X;
                int shiftBars = -(int)Math.Round(deltaX / _barSpacing);
                if (shiftBars != 0)
                {
                    int newStart = _visibleStartIndex + shiftBars;
                    int padding = _visibleBarCount / 2;
                    int virtualCount = _hisBars.Count + padding;
                    int maxStartIndex = Math.Max(0, virtualCount - _visibleBarCount);
                    newStart = Math.Max(0, Math.Min(maxStartIndex, newStart));
                    if (newStart != _visibleStartIndex) { _visibleStartIndex = newStart; RedrawChart(); RaiseXViewChangedIfMain(); }
                    _lastMousePos = pos;
                }
            }
            UpdateCrosshair(pos);
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_hisBars.Count == 0 || _visibleBarCount == 0) return;
            if (!IsMainPane) return; // 副圖不接受縮放
            Point pos = e.GetPosition(_topLayer);
            if (!IsMouseOnChartArea(pos)) return;
            double direction = e.Delta > 0 ? 1 : -1;
            ZoomAt(pos, direction);
            e.Handled = true;
        }

        private void ZoomAt(Point position, double zoomDirection)
        {
            double xrOld = _coordinateCalculator.GetRightmostCenterX();
            int idxFromRightOld = (int)Math.Round((xrOld - (position.X - ChartTheme.ChartMargin.Left)) / _barSpacing);
            int visIdxOld = _visibleBarCount - 1 - idxFromRightOld;
            visIdxOld = Math.Max(0, Math.Min(_visibleBarCount - 1, visIdxOld));
            int anchorIndex = _visibleStartIndex + visIdxOld;

            double factor = 1.0 + _zoomStep * zoomDirection;
            double newSpacing = Math.Max(_minSpacing, Math.Min(_maxSpacing, _barSpacing * factor));
            if (Math.Abs(newSpacing - _barSpacing) < 0.01) return;

            _barSpacing = newSpacing;
            _barWidth = Math.Max(1.0, _barSpacing - _barWidthPadding);

            UpdateCoordinateCache();
            UpdateVisibleRange();

            double xrNew = _coordinateCalculator.GetRightmostCenterX();
            int idxFromRightNew = (int)Math.Round((xrNew - (position.X - ChartTheme.ChartMargin.Left)) / _barSpacing);
            int visIdxNew = _visibleBarCount - 1 - idxFromRightNew;
            visIdxNew = Math.Max(0, Math.Min(_visibleBarCount - 1, visIdxNew));

            int desiredStart = anchorIndex - visIdxNew;
            
            int padding = _visibleBarCount / 2;
            int virtualCount = _hisBars.Count + padding;
            int maxStartIndex = Math.Max(0, virtualCount - _visibleBarCount);
            desiredStart = Math.Max(0, Math.Min(maxStartIndex, desiredStart));

            if (desiredStart != _visibleStartIndex) _visibleStartIndex = desiredStart;
            
            RedrawChart();
            RaiseXViewChangedIfMain();
        }

        private void HideCrosshairAndTags()
        {
            if (_crosshairVLine != null) _crosshairVLine.Visibility = Visibility.Collapsed;
            if (_crosshairHLine != null) _crosshairHLine.Visibility = Visibility.Collapsed;
            if (_crosshairTooltip != null) _crosshairTooltip.Visibility = Visibility.Collapsed;
            if (_yPriceTag != null) _yPriceTag.Visibility = Visibility.Collapsed;
            if (_xTimeTag != null) _xTimeTag.Visibility = Visibility.Collapsed;

            // 任何窗格隱藏時都廣播隱藏
            if (_crosshairBroadcastEnabled) CrosshairMoved?.Invoke(this, -1, false);

            // 主圖在滑鼠離開時顯示最後一根的 MA/BOLL 數值
            if (IsMainPane)
            {
                UpdateOverlaySectionsLatest();
            }
        }

        private void UpdateCrosshair(Point pos)
        {
            if (!IsMouseOnChartArea(pos) || _hisBars.Count == 0 || _visibleBarCount == 0)
            {
                HideCrosshairAndTags();
                return;
            }

            EnsureCoordinateCacheValid();
            Point relativePos = new Point(pos.X - ChartTheme.ChartMargin.Left, pos.Y - ChartTheme.ChartMargin.Top);
            // 計算實際的 K 棒總數（包含浮動 K 棒）
            int totalBarCount = _hisBars.Count;
            if (_floatingKBar != null && !_floatingKBar.IsNullBar)
                totalBarCount++;
            var mouseResult = _coordinateCalculator.CalculateMousePosition(relativePos, _visibleStartIndex, _visibleBarCount, _barSpacing, totalBarCount);
            int barIndex = mouseResult.BarIndex;
            double snapX = mouseResult.SnapX;

            _crosshairVLine!.Visibility = _crosshairVisible ? Visibility.Visible : Visibility.Collapsed;
            _crosshairHLine!.Visibility = _crosshairVisible ? Visibility.Visible : Visibility.Collapsed;
            _crosshairVLine.X1 = _crosshairVLine.X2 = ChartTheme.ChartMargin.Left + snapX;
            _crosshairVLine.Y1 = ChartTheme.ChartMargin.Top;
            _crosshairVLine.Y2 = ChartTheme.ChartMargin.Top + _chartHeight;
            _crosshairHLine.X1 = ChartTheme.ChartMargin.Left;
            _crosshairHLine.X2 = ChartTheme.ChartMargin.Left + _chartWidth;
            _crosshairHLine.Y1 = _crosshairHLine.Y2 = pos.Y;

            if (mouseResult.IsValidBar)
            {
                // 判斷是歷史 K 棒還是浮動 K 棒
                GraphKBar bar;
                if (barIndex < _hisBars.Count)
                {
                    bar = _hisBars[barIndex];
                }
                else
                {
                    // barIndex == _hisBars.Count，即浮動 K 棒
                    bar = _floatingKBar!;
                }

                if (_crosshairTooltip != null)
                {
                    var ohlc = new OhlcData { Open = bar.Open.ToString($"F{_priceDecimalPlaces}"), High = bar.High.ToString($"F{_priceDecimalPlaces}"), Low = bar.Low.ToString($"F{_priceDecimalPlaces}"), Close = bar.Close.ToString($"F{_priceDecimalPlaces}") };
                    var maValues = new List<MaValue>(); // TODO: Get real MA values
                    if (IsMainPane)
                    {
                        Brush accent = GetBarColor(bar.Open, bar.Close);
                        _crosshairTooltip.UpdateData(ohlc, maValues, accent);
                        _crosshairTooltip.Visibility = (_crosshairVisible && _tooltipVisible) ? Visibility.Visible : Visibility.Collapsed;
                    }

                    _crosshairTooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var sz = _crosshairTooltip.DesiredSize;

                    // 預設放在左下角
                    double ox = pos.X - sz.Width - 15;
                    double oy = pos.Y + 15;

                    // 如果會超出邊界，則翻轉位置
                    if (ox < 0) ox = pos.X + 15; // 超出左邊界，翻到右邊
                    if (oy + sz.Height > _chartCanvasContainer.ActualHeight) oy = pos.Y - sz.Height - 15; // 超出下邊界，翻到上面

                    Canvas.SetLeft(_crosshairTooltip, ox);
                    Canvas.SetTop(_crosshairTooltip, oy);
                }

                if (ShowXAxisTimeLabels && _crosshairVisible)
                {
                    _xTimeTag!.Visibility = Visibility.Visible;
                    _xTimeText!.Text = bar.Time.ToString("M/d HH:mm");
                    _xTimeTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double xTagW = _xTimeTag.DesiredSize.Width;
                    double xLeft = Math.Max(ChartTheme.ChartMargin.Left, Math.Min(ChartTheme.ChartMargin.Left + _chartWidth - xTagW, ChartTheme.ChartMargin.Left + snapX - xTagW / 2));
                    Canvas.SetLeft(_xTimeTag, xLeft);
                    Canvas.SetTop(_xTimeTag, ChartTheme.ChartMargin.Top + _chartHeight + 2);
                }

                // 任一窗格移動十字線時，廣播同一可視索引
                if (_crosshairBroadcastEnabled) CrosshairMoved?.Invoke(this, mouseResult.VisibleIndex, true);
                OnCrosshairIndexChanged(mouseResult.VisibleIndex, true);
                foreach (var ov in _overlays) ov.OnCrosshairIndexChanged(mouseResult.VisibleIndex, true);
                if (IsMainPane)
                {
                    int dataIndex = _visibleStartIndex + mouseResult.VisibleIndex;
                    int prevIndex = Math.Max(0, dataIndex - 1);
                    // MA section
            var maLines = new List<PriceInfoPanel.InfoLine>();
            foreach (var ov in _overlays.Where(o => o.TagName == "均線"))
                maLines.AddRange(ov.GetInfoLines(dataIndex, prevIndex, _priceDecimalPlaces));
                    if (maLines.Count > 0) _priceInfoPanel.SetSection("MA", "均線", maLines);
                    else _priceInfoPanel.RemoveSection("MA");
                    // BOLL section
            var bbLines = new List<PriceInfoPanel.InfoLine>();
            foreach (var ov in _overlays.Where(o => o.TagName == "布林通道"))
                bbLines.AddRange(ov.GetInfoLines(dataIndex, prevIndex, _priceDecimalPlaces));
                    if (bbLines.Count > 0) _priceInfoPanel.SetSection("BOLL", "布林通道", bbLines);
                    else _priceInfoPanel.RemoveSection("BOLL");
                }
            }
            else
            {
                // 沒有對應K棒：仍保留十字線垂直線於當前X，並顯示「最後一根」的狀態（時間/OHLC）
                if (_hisBars.Count > 0)
                {
                    var last = _hisBars[_hisBars.Count - 1];
                    if (IsMainPane)
                    {
                        var ohlc = new OhlcData
                        {
                            Open = last.Open.ToString($"F{_priceDecimalPlaces}"),
                            High = last.High.ToString($"F{_priceDecimalPlaces}"),
                            Low = last.Low.ToString($"F{_priceDecimalPlaces}"),
                            Close = last.Close.ToString($"F{_priceDecimalPlaces}")
                        };
                        var maValues = new List<MaValue>(); // TODO: 真實MA
                        Brush accent = GetBarColor(last.Open, last.Close);
                        _crosshairTooltip!.UpdateData(ohlc, maValues, accent);
                        _crosshairTooltip.Visibility = (_crosshairVisible && _tooltipVisible) ? Visibility.Visible : Visibility.Collapsed;

                        _crosshairTooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        var sz = _crosshairTooltip.DesiredSize;
                        double ox = pos.X - sz.Width - 15;
                        double oy = pos.Y + 15;
                        if (ox < 0) ox = pos.X + 15;
                        if (oy + sz.Height > _chartCanvasContainer.ActualHeight) oy = pos.Y - sz.Height - 15;
                        Canvas.SetLeft(_crosshairTooltip, ox);
                        Canvas.SetTop(_crosshairTooltip, oy);
                    }

                    if (ShowXAxisTimeLabels && _crosshairVisible)
                    {
                        _xTimeTag!.Visibility = Visibility.Visible;
                        _xTimeText!.Text = last.Time.ToString("M/d HH:mm");
                        _xTimeTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        double xTagW = _xTimeTag.DesiredSize.Width;
                        double xLeft = Math.Max(ChartTheme.ChartMargin.Left, Math.Min(ChartTheme.ChartMargin.Left + _chartWidth - xTagW, ChartTheme.ChartMargin.Left + snapX - xTagW / 2));
                        Canvas.SetLeft(_xTimeTag, xLeft);
                        Canvas.SetTop(_xTimeTag, ChartTheme.ChartMargin.Top + _chartHeight + 2);
                    }

                    // 廣播可視索引（沿用目前視覺索引），並通知子類用「最後一根」
                    if (_crosshairBroadcastEnabled) CrosshairMoved?.Invoke(this, mouseResult.VisibleIndex, false);
                    OnCrosshairIndexChanged(mouseResult.VisibleIndex, false);
                    foreach (var ov in _overlays) ov.OnCrosshairIndexChanged(mouseResult.VisibleIndex, false);
                    if (IsMainPane)
                    {
                        if (_overlays.Count > 0 && _hisBars.Count > 0)
                        {
                            int dataIndex = _hisBars.Count - 1;
                            int prevIndex = Math.Max(0, dataIndex - 1);
                            var maLines = new List<PriceInfoPanel.InfoLine>();
                            foreach (var ov in _overlays.Where(o => o.TagName == "均線"))
                                maLines.AddRange(ov.GetInfoLines(dataIndex, prevIndex, _priceDecimalPlaces));
                            if (maLines.Count > 0) _priceInfoPanel.SetSection("MA", "均線", maLines); else _priceInfoPanel.RemoveSection("MA");

                            var bbLines = new List<PriceInfoPanel.InfoLine>();
                            foreach (var ov in _overlays.Where(o => o.TagName == "布林通道"))
                                bbLines.AddRange(ov.GetInfoLines(dataIndex, prevIndex, _priceDecimalPlaces));
                            if (bbLines.Count > 0) _priceInfoPanel.SetSection("BOLL", "布林通道", bbLines); else _priceInfoPanel.RemoveSection("BOLL");
                        }
                        else
                        {
                            _priceInfoPanel.RemoveSection("MA");
                            _priceInfoPanel.RemoveSection("BOLL");
                        }
                    }
                }
                else
                {
                    // 沒有任何資料
                    _crosshairTooltip!.Visibility = Visibility.Collapsed;
                    if (ShowXAxisTimeLabels) _xTimeTag!.Visibility = Visibility.Collapsed;
                    if (_crosshairBroadcastEnabled) CrosshairMoved?.Invoke(this, mouseResult.VisibleIndex, false);
                    OnCrosshairIndexChanged(mouseResult.VisibleIndex, false);
                    foreach (var ov in _overlays) ov.OnCrosshairIndexChanged(mouseResult.VisibleIndex, false);
                    if (IsMainPane) { _priceInfoPanel.RemoveSection("MA"); _priceInfoPanel.RemoveSection("BOLL"); }
                }
            }

            if (IsMainPane && _crosshairVisible)
            {
                _yPriceTag!.Visibility = Visibility.Visible;
                decimal price = _coordinateCalculator.YToPrice(relativePos.Y, _displayMaxPrice, _displayMinPrice);
                _yPriceText!.Text = price.ToString($"F{_priceDecimalPlaces}");
                _lastCrosshairPrice = price; _hasCrosshairPrice = true;
                _yPriceTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double yTagH = _yPriceTag.DesiredSize.Height;
                double yTop = Math.Max(ChartTheme.ChartMargin.Top, Math.Min(ChartTheme.ChartMargin.Top + _chartHeight - yTagH, pos.Y - yTagH / 2));
                Canvas.SetLeft(_yPriceTag, ChartTheme.ChartMargin.Left + _chartWidth + 2);
                Canvas.SetTop(_yPriceTag, yTop);
            }
            else
            {
                _yPriceTag!.Visibility = Visibility.Collapsed;
                _hasCrosshairPrice = false;
            }
        }
        #endregion

        private void EnableLayerCache(bool enable)
        {
            if (enable)
            {
                var cache = new System.Windows.Media.BitmapCache();
                _bottomLayer.CacheMode = cache;
                _kBarLayer.CacheMode = cache;
                _indicatorLayer.CacheMode = cache;
            }
            else
            {
                _bottomLayer.CacheMode = null;
                _kBarLayer.CacheMode = null;
                _indicatorLayer.CacheMode = null;
            }
        }

        #region Crosshair Sync API
        // 從主圖同步十字線至本窗格（只顯示垂直線，不顯示提示與價格框）
        public void SyncCrosshairFromMain(int visibleIndex, bool isValid)
        {
            // 如果十字線被設定為不顯示，直接隱藏
            if (!_crosshairVisible)
            {
                if (_crosshairVLine != null) _crosshairVLine.Visibility = Visibility.Collapsed;
                OnCrosshairIndexChanged(visibleIndex, false);
                return;
            }

            // 當滑鼠離開時（visibleIndex < 0 且 isValid=false），隱藏垂直線
            if (!isValid && visibleIndex < 0)
            {
                if (_crosshairVLine != null) _crosshairVLine.Visibility = Visibility.Collapsed;
                OnCrosshairIndexChanged(visibleIndex, false);
                return;
            }

            if (_visibleBarCount <= 0)
            {
                if (_crosshairVLine != null) _crosshairVLine.Visibility = Visibility.Collapsed;
                OnCrosshairIndexChanged(visibleIndex, isValid);
                return;
            }

            EnsureCoordinateCacheValid();
            int visIdx = Math.Max(0, Math.Min(_visibleBarCount - 1, visibleIndex));
            double xLeft = _coordinateCalculator.GetBarXByVisibleIndex(visIdx, _visibleBarCount, _barSpacing);
            double xCenter = xLeft + _coordinateCalculator.GetHalfBarWidth();

            _crosshairVLine!.Visibility = _crosshairVisible ? Visibility.Visible : Visibility.Collapsed;
            _crosshairVLine.X1 = _crosshairVLine.X2 = ChartTheme.ChartMargin.Left + xCenter;
            _crosshairVLine.Y1 = ChartTheme.ChartMargin.Top;
            _crosshairVLine.Y2 = ChartTheme.ChartMargin.Top + _chartHeight;

            // 同步時只控制垂直線與隱藏提示，不動各窗格自身的時間/價格框
            if (_crosshairTooltip != null) _crosshairTooltip.Visibility = Visibility.Collapsed;

            OnCrosshairIndexChanged(visibleIndex, isValid);
        }
        #endregion
    }
}
