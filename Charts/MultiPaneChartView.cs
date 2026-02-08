using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Text.Json;
using Utility;
using KChartCore;

namespace Charts
{
    public enum PriceType
    {
        報價時間,
        買價,
        成交價,
        賣價,
        成交量,
        漲跌
    }

    public class MultiPaneChartView : Grid
    {
        private readonly ChartPane _mainPricePane;
        private readonly List<ChartPane> _indicatorPanes = new();
        private readonly Dictionary<IndicatorPanelType, ChartPane> _paneByType = new();
        private SessionScheduleOverlay? _sessionScheduleOverlay; // Session 清單驅動的預約線
        private int _maxBars = 10000; // 預設最多保留 1 萬根
        private const double SplitterThickness = 6.0; // 擴大 hit-test 範圍
        private const double MinPaneHeight = 50.0;
        private readonly Dictionary<ChartPane, double> _savedHeights = new();
        private List<ChartKBar> _historyCache = new();
        private int _estimatedPeriodMinutes = 1; // 估算的當前週期（分鐘）
        private const string ConfigFileName = "charts_config.json";
        private static string GetConfigPath() => System.IO.Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        private static string ColorToHex(System.Windows.Media.Color c) => string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        private static System.Windows.Media.Color HexToColor(string hex) => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        // 暴露合約變更請求事件
        public event Action? OnContractChgReq;

        public MultiPaneChartView()
        {
            _mainPricePane = new ChartPane();
            _mainPricePane.IsMainPane = true;
            _mainPricePane.ShowXAxisTimeLabels = true;
            _mainPricePane.ShowHighLowLabels = true;
            _mainPricePane.XViewChanged += OnMainXViewChanged;
            _mainPricePane.CrosshairMoved += OnAnyCrosshairMoved;
            _mainPricePane.MainPaneTitleClicked += () => ShowAppearLayerWindow();
            _mainPricePane.SetPanelTitle("K線圖");
            _mainPricePane.SetTitleClickable(true);
            // 初始就顯示商品/報價區塊，以 "---" 佔位
            try { _mainPricePane.SetContractInfo("---", "---"); _mainPricePane.SetQuotePlaceholders(); } catch { }

            // 訂閱 PriceInfoPanel 的合約變更請求事件並轉發
            _mainPricePane._priceInfoPanel.OnContractChgReq += () => OnContractChgReq?.Invoke();

            RebuildLayout(); // Initial layout with just the main pane

            this.SizeChanged += (_, __) => SaveCurrentHeights();
            // Loaded 後再載入設定，避免版面尚未完成導致權重套用不生效
            this.Loaded += (_, __) => { try { LoadConfig(); } catch { } };
        }

        public ChartPane GetMainPricePane() => _mainPricePane;
        public void SetPriceDecimals(int places) => _mainPricePane.SetPriceDecimalPlaces(places);
        public void SetMainTopRightContent(System.Windows.UIElement? content) => _mainPricePane.SetRightTopContent(content);
        public int GetEstimatedPeriodMinutes() => _estimatedPeriodMinutes;
        // 便利轉接：供 UI 以 Y 座標查價與取得小數位
        public decimal GetPriceAtY(double paneY) => _mainPricePane.GetPriceAtY(paneY);
        public int GetPriceDecimalPlaces() => _mainPricePane.GetPriceDecimalPlaces();
        public decimal GetPriceAtScreen(double screenX, double screenY) => _mainPricePane.GetPriceAtScreen(screenX, screenY);
        public decimal GetPriceAtScreenY(double screenY) => _mainPricePane.GetPriceAtScreenY(screenY);
        public bool TryGetCrosshairPrice(out decimal price) => _mainPricePane.TryGetCrosshairPrice(out price);

        // 設定十字線顯示狀態（套用到所有面板）
        public void SetCrosshairVisible(bool visible)
        {
            _mainPricePane.SetCrosshairVisible(visible);
            foreach (var pane in _indicatorPanes)
            {
                pane.SetCrosshairVisible(visible);
            }
        }

        // 設定查價視窗顯示狀態（套用到所有面板）
        public void SetTooltipVisible(bool visible)
        {
            _mainPricePane.SetTooltipVisible(visible);
            foreach (var pane in _indicatorPanes)
            {
                pane.SetTooltipVisible(visible);
            }
        }

        // 同步所有面板的十字線和查價視窗設定
        public void SyncCrosshairSettings()
        {
            bool crosshairVisible = _mainPricePane.IsCrosshairVisible;
            bool tooltipVisible = _mainPricePane.IsTooltipVisible;

            foreach (var pane in _indicatorPanes)
            {
                pane.SetCrosshairVisible(crosshairVisible);
                pane.SetTooltipVisible(tooltipVisible);
            }
        }

        // 設定最大保留K棒數（套用到所有面板）
        public void SetMaxBars(int maxBars)
        {
            _maxBars = Math.Max(100, maxBars);
            _mainPricePane.MaxBars = _maxBars;
            foreach (var p in _indicatorPanes) p.MaxBars = _maxBars;
            // 同步修剪快取
            if (_historyCache != null && _historyCache.Count > _maxBars)
            {
                int remove = _historyCache.Count - _maxBars;
                _historyCache.RemoveRange(0, remove);
            }
        }

        // 顯示商品與年月於主圖左側資訊面板（標題下方區塊）
        public void SetContract(Contracts contract)
        {
            if (contract == null) return;
            var yy = contract.Year % 100;
            var ym = $"{yy:00}/{contract.Month:00}";
            _mainPricePane.SetContractInfo(contract.Name, ym);
            _mainPricePane.SetQuotePlaceholders();
        }

        // 更新報價資訊
        public void SetPrice(PriceType priceType, string value)
        {
            switch (priceType)
            {
                case PriceType.報價時間:
                    _mainPricePane.SetQuoteInfo(value, null, null, null, null, null);
                    break;
                case PriceType.買價:
                    _mainPricePane.SetQuoteInfo(null, value, null, null, null, null);
                    break;
                case PriceType.成交價:
                    _mainPricePane.SetQuoteInfo(null, null, value, null, null, null);
                    break;
                case PriceType.賣價:
                    _mainPricePane.SetQuoteInfo(null, null, null, value, null, null);
                    break;
                case PriceType.成交量:
                    _mainPricePane.SetQuoteInfo(null, null, null, null, value, null);
                    break;
                case PriceType.漲跌:
                    _mainPricePane.SetQuoteInfo(null, null, null, null, null, value);
                    break;
            }
        }

        public void AddIndicatorPane(ChartPane pane)
        {
            if (!_indicatorPanes.Contains(pane))
            {
                SaveCurrentHeights();
                _indicatorPanes.Add(pane);
                pane.MaxBars = _maxBars;
                pane.IsMainPane = false;
                pane.ShowXAxisTimeLabels = false;
                pane.ShowHighLowLabels = false;

                // 同步主圖的十字線和查價視窗顯示設定
                pane.SetCrosshairVisible(_mainPricePane.IsCrosshairVisible);
                pane.SetTooltipVisible(_mainPricePane.IsTooltipVisible);
                // 若已有歷史資料，立即載入到新副圖並同步X視圖
                if (_historyCache != null && _historyCache.Count > 0)
                {
                    pane.LoadHistory(_historyCache);
                    var (start, spacing, _) = _mainPricePane.GetXViewState();
                    pane.ApplyXViewState(start, spacing);
                }
                pane.CrosshairMoved += OnAnyCrosshairMoved;
                RebuildLayout();
            }
        }

        // 外部新增一根完成K棒（來自 KChartCore.FunctionKBar）
        public void AddBar(FunctionKBar bar, bool isClearFloating = true)
        {
            var ck = new ChartKBar
            {
                Time = bar.CloseTime,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
                IsAlignmentBar = bar.IsAlignmentBar,
                IsFloating = false
            };

            if (_historyCache == null) _historyCache = new List<ChartKBar>();
            _historyCache.Add(ck);
            if (_historyCache.Count > _maxBars)
            {
                int remove = _historyCache.Count - _maxBars;
                _historyCache.RemoveRange(0, remove);
            }

            // 直接增量加入各窗格
            _mainPricePane.AddBar(ck, isClearFloating);
            foreach (var pane in _indicatorPanes)
            {
                pane.AddBar(ck, isClearFloating);
            }

            // 以最後兩根完成棒估算當前週期（忽略異常大差值）
            try
            {
                UpdateEstimatedPeriodFromCache();
            }
            catch { }
        }

        public void AddTick(decimal price, int volume = 1)
        {
            _mainPricePane.AddTick(price, volume);
            foreach (var pane in _indicatorPanes)
            {
                if (pane is VolumePane)
                {
                    pane.AddTick(price, volume);
                }
            }
        }

        // 對外提供清除浮動K棒 API
        public void ClearFloatingBar()
        {
            _mainPricePane.ClearFloatingBar();
        }

        // 以 FunctionKBar 清空並載入新的歷史（供外部一次性指定歷史）
        public void AddBarList(IEnumerable<FunctionKBar> bars)
        {
            // 暫停十字線廣播，避免在重載期間觸發跨窗格同步造成競態
            _mainPricePane.SetCrosshairBroadcastEnabled(false);
            foreach (var p in _indicatorPanes) p.SetCrosshairBroadcastEnabled(false);
            var list = new List<ChartKBar>();
            foreach (var b in bars)
            {
                list.Add(new ChartKBar
                {
                    Time = b.CloseTime,
                    Open = b.Open,
                    High = b.High,
                    Low = b.Low,
                    Close = b.Close,
                    Volume = b.Volume,
                    IsAlignmentBar = b.IsAlignmentBar,
                    IsFloating = b.IsFloating
                });
            }

            _historyCache = list;
            // 更新估算週期（優先使用完成棒差值；必要時落回包含浮動棒）
            try
            {
                UpdateEstimatedPeriodFromFunctionBars(bars?.ToList() ?? new List<FunctionKBar>());
            }
            catch { }
            _mainPricePane.LoadHistory(_historyCache);
            foreach (var pane in _indicatorPanes)
            {
                pane.LoadHistory(_historyCache);
            }

            var (start, spacing, _) = _mainPricePane.GetXViewState();
            foreach (var pane in _indicatorPanes)
            {
                pane.ApplyXViewState(start, spacing);
            }

            // 主動觸發副圖指標的參數套用以便立即計算（避免等到第一次繪製時才重算，造成空白或索引競態）
            foreach (var pane in _indicatorPanes)
            {
                if (pane is KdPane kd)
                {
                    var (p, sk, sd) = kd.GetParameters();
                    kd.SetParameters(p, sk, sd);
                    kd.ApplyXViewState(start, spacing);
                }
                else if (pane is MacdPane macd)
                {
                    var (f, s, sig) = macd.GetParameters();
                    macd.SetParameters(f, s, sig);
                    macd.ApplyXViewState(start, spacing);
                }
            }

            // 恢復十字線廣播
            _mainPricePane.SetCrosshairBroadcastEnabled(true);
            foreach (var p in _indicatorPanes) p.SetCrosshairBroadcastEnabled(true);
        }

        public void AddIndicatorPanel(IndicatorPanelType type)
        {
            if (_paneByType.ContainsKey(type)) return; // 已存在
            var pane = IndicatorPaneFactory.Create(type);
            _paneByType[type] = pane;
            AddIndicatorPane(pane);
        }

        public void RemoveIndicatorPane(ChartPane pane)
        {
            if (_indicatorPanes.Remove(pane))
            {
                SaveCurrentHeights();
                pane.CrosshairMoved -= OnAnyCrosshairMoved;
                // 從類型映射中移除該面板
                var kvToRemove = _paneByType.FirstOrDefault(kv => ReferenceEquals(kv.Value, pane));
                if (!kvToRemove.Equals(default(KeyValuePair<IndicatorPanelType, ChartPane>)))
                {
                    _paneByType.Remove(kvToRemove.Key);
                }
                RebuildLayout();
            }
        }

        public void RemoveIndicatorPanel(IndicatorPanelType type)
        {
            if (_paneByType.TryGetValue(type, out var pane))
            {
                _paneByType.Remove(type);
                RemoveIndicatorPane(pane);
            }
        }

        public bool HasIndicatorPanel(IndicatorPanelType type) => _paneByType.ContainsKey(type);
        public bool TryGetIndicatorPane(IndicatorPanelType type, out ChartPane pane) => _paneByType.TryGetValue(type, out pane!);

        public void ClearIndicatorPanes()
        {
            SaveCurrentHeights();
            _indicatorPanes.Clear();
            _paneByType.Clear();
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            this.Children.Clear();
            this.RowDefinitions.Clear();

            var allPanes = new List<ChartPane> { _mainPricePane };
            allPanes.AddRange(_indicatorPanes);
            if (allPanes.Count == 0) return;

            bool hasSaved = _savedHeights.Count > 0;
            // 權重計算：若本次加入了新的（尚未在 _savedHeights 的）副圖，
            // 其預設高度為整體的 15%，其餘窗格依原比例縮放填滿剩餘 85%。
            // 若無新增副圖，沿用已儲存比例。

            // 收集已儲存權重
            double savedMain = hasSaved && _savedHeights.TryGetValue(_mainPricePane, out var mv) ? mv : 1.0;
            var savedIndicatorWeights = new Dictionary<ChartPane, double>();
            foreach (var p in _indicatorPanes)
            {
                if (_savedHeights.TryGetValue(p, out var w)) savedIndicatorWeights[p] = w;
            }
            int unsavedCount = _indicatorPanes.Count - savedIndicatorWeights.Count;

            // 計算縮放係數
            double savedSum = savedMain + savedIndicatorWeights.Values.Sum();
            double defaultUnsavedWeight = 0.15; // 單一新副圖預設 15%
            double unsavedTotal = Math.Max(0, unsavedCount) * defaultUnsavedWeight;
            if (unsavedTotal > 0.95) unsavedTotal = 0.95; // 上限保護，避免極端情況
            double scale = savedSum > 0 ? (1.0 - unsavedTotal) / savedSum : 0.0;

            int row = 0;
            // 主圖內容列
            double mainWeight = hasSaved ? savedMain * (unsavedCount > 0 ? scale : 1.0) : 1.0;
            AddContentRowStar(_mainPricePane, mainWeight, row);
            row++;

            // 在每個內容列之間插入 GridSplitter 列
            for (int i = 0; i < _indicatorPanes.Count; i++)
            {
                // 插入一個 splitter 列
                AddSplitterRow(row);
                row++;

                var pane = _indicatorPanes[i];
                pane.IsMainPane = false;
                pane.ShowXAxisTimeLabels = false;
                pane.ShowHighLowLabels = false;

                double weight;
                if (hasSaved && savedIndicatorWeights.TryGetValue(pane, out var wSaved))
                {
                    // 既有副圖：按既有比例縮放
                    weight = wSaved * (unsavedCount > 0 ? scale : 1.0);
                }
                else
                {
                    // 新增副圖：給 15% 預設高度
                    weight = defaultUnsavedWeight;
                }
                AddContentRowStar(pane, weight, row);
                row++;
            }

            // 重新正規化避免總和偏移
            NormalizeRowHeights();
        }

        // === Period Estimation Helpers ===
        private static readonly int[] _stdPeriods = new[] { 1, 3, 5, 10, 15, 20, 30, 45, 60, 90, 120 };

        private static int MapToStandard(int minutes)
        {
            if (minutes <= 0) return 1;
            int best = _stdPeriods[0];
            int bestDiff = Math.Abs(best - minutes);
            for (int i = 1; i < _stdPeriods.Length; i++)
            {
                int d = Math.Abs(_stdPeriods[i] - minutes);
                if (d < bestDiff) { best = _stdPeriods[i]; bestDiff = d; }
            }
            return best;
        }

        private void UpdateEstimatedPeriodFromCache()
        {
            if (_historyCache == null || _historyCache.Count < 2) return;
            // 只取最後兩根完成棒估算（忽略浮動）
            for (int i = _historyCache.Count - 1; i >= 1; i--)
            {
                var t2 = _historyCache[i].Time;
                var t1 = _historyCache[i - 1].Time;
                if (t1 == DateTime.MinValue || t2 == DateTime.MinValue) continue;
                int diff = (int)Math.Round((t2 - t1).TotalMinutes);
                if (diff > 0 && diff <= 180)
                {
                    _estimatedPeriodMinutes = MapToStandard(diff);
                    return;
                }
            }
        }

        private void UpdateEstimatedPeriodFromFunctionBars(IReadOnlyList<FunctionKBar> bars)
        {
            if (bars == null || bars.Count < 2) return;
            // 優先使用已封存棒的時間差
            var times = new List<DateTime>();
            for (int i = Math.Max(0, bars.Count - 100); i < bars.Count; i++)
            {
                if (!bars[i].IsFloating && bars[i].CloseTime != default(DateTime))
                    times.Add(bars[i].CloseTime);
            }

            int? best = null;
            for (int i = 1; i < times.Count; i++)
            {
                int diff = (int)Math.Round((times[i] - times[i - 1]).TotalMinutes);
                if (diff > 0 && diff <= 180)
                {
                    best = best.HasValue ? Math.Min(best.Value, diff) : diff;
                }
            }

            // 若只有一根已封存且尾端浮動，嘗試以最後封存 vs 浮動估算
            if (!best.HasValue && bars.Count >= 2)
            {
                var last = bars[bars.Count - 1];
                var prev = bars[bars.Count - 2];
                if (last.CloseTime != default(DateTime) && prev.CloseTime != default(DateTime))
                {
                    int diff = (int)Math.Round((last.CloseTime - prev.CloseTime).TotalMinutes);
                    if (diff > 0 && diff <= 180) best = diff;
                }
            }

            if (best.HasValue)
            {
                _estimatedPeriodMinutes = MapToStandard(best.Value);
            }
        }

        private void AddContentRowStar(ChartPane pane, double starWeight, int rowIndex)
        {
            var rd = new RowDefinition { Height = new GridLength(starWeight, GridUnitType.Star), MinHeight = MinPaneHeight };
            this.RowDefinitions.Add(rd);
            Grid.SetRow(pane, rowIndex);
            this.Children.Add(pane);
        }

        private void AddContentRowPixel(ChartPane pane, double heightPx, int rowIndex)
        {
            var rd = new RowDefinition { Height = new GridLength(heightPx, GridUnitType.Pixel), MinHeight = MinPaneHeight };
            this.RowDefinitions.Add(rd);
            Grid.SetRow(pane, rowIndex);
            this.Children.Add(pane);
        }

        private void AddSplitterRow(int rowIndex)
        {
            this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Height = SplitterThickness,
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(0, 1, 0, 1),
                ShowsPreview = true,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Cursor = System.Windows.Input.Cursors.SizeNS
            };
            Grid.SetRow(splitter, rowIndex);
            Grid.SetColumnSpan(splitter, 1);
            this.Children.Add(splitter);
        }

        private void SaveCurrentHeights()
        {
            // 儲存目前各內容 Pane 的相對高度權重（以實際高度比例計算）
            var panes = new List<ChartPane> { _mainPricePane };
            panes.AddRange(_indicatorPanes);

            double total = panes.Sum(p => (double)Math.Max(1, p.RenderSize.Height));
            if (total <= 0) return;
            foreach (var p in panes)
            {
                double h = (double)Math.Max(1, p.RenderSize.Height);
                _savedHeights[p] = h / total;
            }
        }

        private double GetSavedWeightOrDefault(ChartPane pane, double @default)
        {
            if (_savedHeights.TryGetValue(pane, out double v))
            {
                return v;
            }
            return @default;
        }

        private void NormalizeRowHeights()
        {
            // 將所有內容列的 Star 權重正規化為總和1，避免多次重建後權重漂移
            var contentRows = this.RowDefinitions.Where(rd => rd.Height.IsStar).ToList();
            double sum = contentRows.Sum(rd => rd.Height.Value);
            if (sum <= 0) return;
            foreach (var rd in contentRows)
            {
                rd.Height = new GridLength(rd.Height.Value / sum, GridUnitType.Star);
            }
        }

        // ====== Persistence (Save/Load) ======
        public void SaveConfig()
        {
            // 先更新一次目前比例
            SaveCurrentHeights();
            var cfg = new ChartViewConfig
            {
                PriceDecimals = _mainPricePane.GetPriceDecimalPlaces(),
                RowHeights = CaptureRowHeights(),
                Overlays = _mainPricePane.GetOverlayConfigs(),
                Indicators = CaptureIndicatorConfigs(),
                CrosshairVisible = _mainPricePane.IsCrosshairVisible,
                TooltipVisible = _mainPricePane.IsTooltipVisible
            };
            var path = GetConfigPath();
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public void LoadConfig()
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return;
            var cfg = JsonSerializer.Deserialize<ChartViewConfig>(File.ReadAllText(path));
            if (cfg == null) return;

            // Price decimals
            _mainPricePane.SetPriceDecimalPlaces(cfg.PriceDecimals);

            // Indicators
            ClearIndicatorPanes();
            foreach (var ic in cfg.Indicators ?? new List<IndicatorConfig>())
            {
                switch (ic.Type)
                {
                    case "VOL":
                        AddIndicatorPanel(IndicatorPanelType.Vol);
                        break;
                    case "KD":
                        AddIndicatorPanel(IndicatorPanelType.Kd);
                        if (_paneByType.TryGetValue(IndicatorPanelType.Kd, out var kdPane) && kdPane is KdPane kd)
                        {
                            kd.SetParameters(ic.Period, ic.SmoothK, ic.SmoothD);
                            if (!string.IsNullOrEmpty(ic.KColorHex) || !string.IsNullOrEmpty(ic.DColorHex))
                            {
                                var (curK, curD) = kd.GetLineColors();
                                var kc = string.IsNullOrEmpty(ic.KColorHex) ? curK : HexToColor(ic.KColorHex!);
                                var dc = string.IsNullOrEmpty(ic.DColorHex) ? curD : HexToColor(ic.DColorHex!);
                                kd.SetLineColors(kc, dc);
                            }
                            if (_historyCache != null && _historyCache.Count > 0)
                            {
                                kd.LoadHistory(_historyCache);
                                var (start, spacing, _) = _mainPricePane.GetXViewState();
                                kd.ApplyXViewState(start, spacing);
                            }
                        }
                        break;
                    case "MACD":
                        AddIndicatorPanel(IndicatorPanelType.Macd);
                        if (_paneByType.TryGetValue(IndicatorPanelType.Macd, out var macdPane) && macdPane is MacdPane macd)
                        {
                            macd.SetParameters(ic.EMA1, ic.EMA2, ic.Day);
                            if (!string.IsNullOrEmpty(ic.DifColorHex) || !string.IsNullOrEmpty(ic.DeaColorHex))
                            {
                                var (curDif, curDea) = macd.GetLineColors();
                                var dif = string.IsNullOrEmpty(ic.DifColorHex) ? curDif : HexToColor(ic.DifColorHex!);
                                var dea = string.IsNullOrEmpty(ic.DeaColorHex) ? curDea : HexToColor(ic.DeaColorHex!);
                                macd.SetLineColors(dif, dea);
                            }
                            if (_historyCache != null && _historyCache.Count > 0)
                            {
                                macd.LoadHistory(_historyCache);
                                var (start, spacing, _) = _mainPricePane.GetXViewState();
                                macd.ApplyXViewState(start, spacing);
                            }
                        }
                        break;
                }
            }

            // Overlays
            _mainPricePane.SetOverlaysFromConfigs(cfg.Overlays ?? new List<OverlayConfig>());

            // 十字線和查價視窗設定
            _mainPricePane.SetCrosshairVisible(cfg.CrosshairVisible);
            _mainPricePane.SetTooltipVisible(cfg.TooltipVisible);
            SyncCrosshairSettings(); // 同步到所有副圖

            // Row heights（內容列）
            if (cfg.RowHeights != null && cfg.RowHeights.Count == 1 + _indicatorPanes.Count)
                ApplyRowHeights(cfg.RowHeights);
        }

        private List<double> CaptureRowHeights()
        {
            // 以 _savedHeights 映射為準，順序：主圖 + 現有副圖
            var list = new List<double>();
            list.Add(GetSavedWeightOrDefault(_mainPricePane, 0.7));
            foreach (var p in _indicatorPanes)
                list.Add(GetSavedWeightOrDefault(p, 0.3 / (_indicatorPanes.Count == 0 ? 1 : _indicatorPanes.Count)));
            return list;
        }

        private void ApplyRowHeights(List<double> values)
        {
            // 依照 values 設定主圖與副圖權重後重建版面
            if (values.Count != 1 + _indicatorPanes.Count) return;
            _savedHeights.Clear();
            _savedHeights[_mainPricePane] = values[0];
            for (int i = 0; i < _indicatorPanes.Count; i++)
                _savedHeights[_indicatorPanes[i]] = values[i + 1];
            RebuildLayout();
        }

        private List<IndicatorConfig> CaptureIndicatorConfigs()
        {
            var list = new List<IndicatorConfig>();
            foreach (var p in _indicatorPanes)
            {
                switch (p)
                {
                    case VolumePane:
                        list.Add(new IndicatorConfig { Type = "VOL" });
                        break;
                    case KdPane kd:
                        var (period, sk, sd) = kd.GetParameters();
                        var (kc, dc) = kd.GetLineColors();
                        list.Add(new IndicatorConfig
                        {
                            Type = "KD",
                            Period = period,
                            SmoothK = sk,
                            SmoothD = sd,
                            KColorHex = ColorToHex(kc),
                            DColorHex = ColorToHex(dc)
                        });
                        break;
                    case MacdPane macd:
                        var (e1, e2, day) = macd.GetParameters();
                        var (difC, deaC) = macd.GetLineColors();
                        list.Add(new IndicatorConfig
                        {
                            Type = "MACD",
                            EMA1 = e1,
                            EMA2 = e2,
                            Day = day,
                            DifColorHex = ColorToHex(difC),
                            DeaColorHex = ColorToHex(deaC)
                        });
                        break;
                }
            }
            return list;
        }

        public void LoadHistory(List<ChartKBar> bars)
        {
            // Load history into all panes
            _historyCache = bars?.ToList() ?? new List<ChartKBar>();
            _mainPricePane.LoadHistory(_historyCache);
            foreach (var pane in _indicatorPanes)
            {
                // For now, we load the same data. Later, we'll pass indicator-specific data.
                pane.LoadHistory(_historyCache);
            }

            // 同步X視圖狀態到副圖
            var (start, spacing, _) = _mainPricePane.GetXViewState();
            foreach (var pane in _indicatorPanes)
            {
                pane.ApplyXViewState(start, spacing);
            }
        }

        // KD 參數設定入口
        public void SetKdParameters(int period, int smoothK, int smoothD, System.Windows.Media.Color? kColor = null, System.Windows.Media.Color? dColor = null)
        {
            if (_paneByType.TryGetValue(IndicatorPanelType.Kd, out var pane) && pane is KdPane kd)
            {
                kd.SetParameters(period, smoothK, smoothD);
                if (kColor.HasValue || dColor.HasValue)
                {
                    var (curK, curD) = kd.GetLineColors();
                    kd.SetLineColors(kColor ?? curK, dColor ?? curD);
                }
                // 使用快取資料重載，並保持 X 視圖對齊
                if (_historyCache != null && _historyCache.Count > 0)
                {
                    kd.LoadHistory(_historyCache);
                    var (start, spacing, _) = _mainPricePane.GetXViewState();
                    kd.ApplyXViewState(start, spacing);
                }
            }
        }

        public void SetMacdParameters(int fast, int slow, int signal)
        {
            if (_paneByType.TryGetValue(IndicatorPanelType.Macd, out var pane) && pane is MacdPane macd)
            {
                macd.SetParameters(fast, slow, signal);
                if (_historyCache != null && _historyCache.Count > 0)
                {
                    macd.LoadHistory(_historyCache);
                    var (start, spacing, _) = _mainPricePane.GetXViewState();
                    macd.ApplyXViewState(start, spacing);
                }
            }
        }

        // 外部只需呼叫一次：自動彈出設定視窗並套用
        public bool SetIndicatorPara(IndicatorPanelType type, Window? owner = null)
        {
            switch (type)
            {
                case IndicatorPanelType.Kd:
                    if (!_paneByType.ContainsKey(IndicatorPanelType.Kd))
                        AddIndicatorPanel(IndicatorPanelType.Kd);
                    var kdPane = (KdPane)_paneByType[IndicatorPanelType.Kd];
                    var (p, sk, sd) = kdPane.GetParameters();
                    var (kc, dc) = kdPane.GetLineColors();
                    var dlg = new KdSettingsDialog(p, sk, sd, kc, dc) { Owner = owner };
                    var ok = dlg.ShowDialog() == true;
                    if (ok)
                    {
                        SetKdParameters(dlg.Period, dlg.SmoothK, dlg.SmoothD, dlg.KColor, dlg.DColor);
                    }
                    return ok;
                case IndicatorPanelType.Macd:
                    if (!_paneByType.ContainsKey(IndicatorPanelType.Macd))
                        AddIndicatorPanel(IndicatorPanelType.Macd);
                    var macdPane = (MacdPane)_paneByType[IndicatorPanelType.Macd];
                    var (f, s, si) = macdPane.GetParameters();
                    var (difC, deaC) = macdPane.GetLineColors();
                    var dlgM = new MacdSettingsDialog(f, s, si, difC, deaC) { Owner = owner };
                    var okM = dlgM.ShowDialog() == true;
                    if (okM)
                    {
                        SetMacdParameters(dlgM.Ema1, dlgM.Ema2, dlgM.Day);
                        macdPane.SetLineColors(dlgM.DifColor, dlgM.DeaColor);
                    }
                    return okM;
                default:
                    // 其他指標之後擴充
                    return false;
            }
        }

        // Overlay: Bollinger
        public void AddBollingerOverlay(int period = 20, double k = 2.0, System.Windows.Media.Color? fillColor = null, double opacity = 0.2)
        {
            if (fillColor.HasValue)
            {
                var mid = System.Windows.Media.Color.FromRgb(0xD7, 0xD4, 0xD5);
                var bb = new BollingerOverlay(period, k, fillColor.Value, opacity, fillColor.Value, mid);
                _mainPricePane.AddOverlay(bb);
            }
            else
            {
                var bb = new BollingerOverlay(period, k);
                _mainPricePane.AddOverlay(bb);
            }
        }

        public void RemoveBollingerOverlay()
        {
            _mainPricePane.RemoveOverlaysByTag("布林通道");
            _mainPricePane.RemoveOverlayTag("布林通道");
        }

        public bool SetOverlayPara_Bollinger(Window? owner = null)
        {
            // 讀取目前的 BB 參數（若沒有則使用預設）
            int defPeriod = 20; double defK = 2.0; System.Windows.Media.Color defFill = System.Windows.Media.Color.FromRgb(0xB7,0xB8,0xB7);
            System.Windows.Media.Color defEdge = System.Windows.Media.Color.FromRgb(0xB4,0xB4,0xB4);
            System.Windows.Media.Color defMid = System.Windows.Media.Color.FromRgb(0xD7,0xD4,0xD5);
            double defOpacity = 0.059597315436241624;
            try
            {
                var curr = _mainPricePane.GetOverlayConfigs().FirstOrDefault(o => o.Type == "BOLL");
                if (curr != null && curr.Type == "BOLL")
                {
                    defPeriod = curr.Period > 0 ? curr.Period : defPeriod;
                    defK = curr.K > 0 ? curr.K : defK;
                    if (!string.IsNullOrEmpty(curr.FillHex))
                    {
                        var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(curr.FillHex!);
                        defFill = col;
                    }
                    if (!string.IsNullOrEmpty(curr.EdgeColorHex))
                    {
                        var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(curr.EdgeColorHex!);
                        defEdge = col;
                    }
                    if (!string.IsNullOrEmpty(curr.MidColorHex))
                    {
                        var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(curr.MidColorHex!);
                        defMid = col;
                    }
                    if (curr.Opacity >= 0 && curr.Opacity <= 1) defOpacity = curr.Opacity;
                }
            }
            catch { }

            var dlg = new BollSettingsDialog(defPeriod, defK, defFill, defEdge, defMid, defOpacity) { Owner = owner };
            var ok = dlg.ShowDialog() == true;
            if (ok)
            {
                // 移除舊的 BB（以 TagName 判斷）再加新的
                _mainPricePane.RemoveOverlaysByTag("布林通道");
                _mainPricePane.AddOverlay(new BollingerOverlay(dlg.Period, dlg.K, dlg.FillColor, dlg.FillOpacity, dlg.EdgeColor, dlg.MidColor));
            }
            return ok;
        }

        // Overlay: BBI (Bull and Bear Index)
        public void AddBbiOverlay(System.Windows.Media.Color? color = null)
        {
            var bbi = color.HasValue ? new BbiOverlay(new[] { 3, 6, 12, 24 }, color.Value) : new BbiOverlay();
            _mainPricePane.AddOverlay(bbi);
        }

        public void RemoveBbiOverlay()
        {
            _mainPricePane.RemoveOverlaysByTag("BBI");
            _mainPricePane.RemoveOverlayTag("BBI");
        }

        public bool SetOverlayPara_Bbi(Window? owner = null)
        {
            int[] defPeriods = new[] { 3, 6, 12, 24 };
            System.Windows.Media.Color defColor = System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00);
            try
            {
                var curr = _mainPricePane.GetOverlayConfigs().FirstOrDefault(o => o.Type == "BBI");
                if (curr != null)
                {
                    if (!string.IsNullOrWhiteSpace(curr.BbiPeriodsCsv))
                    {
                        var parts = curr.BbiPeriodsCsv.Split(new[] { ',', ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                        var list = new List<int>();
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out var v) && v > 0) list.Add(v);
                        }
                        if (list.Count > 0) defPeriods = list.ToArray();
                    }
                    if (!string.IsNullOrEmpty(curr.ColorHex))
                    {
                        defColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(curr.ColorHex!);
                    }
                }
            }
            catch { }

            var dlg = new BbiSettingsDialog(defPeriods, defColor) { Owner = owner };
            var ok = dlg.ShowDialog() == true;
            if (ok)
            {
                _mainPricePane.RemoveOverlaysByTag("BBI");
                _mainPricePane.AddOverlay(new BbiOverlay(dlg.Periods, dlg.LineColor));
            }
            return ok;
        }

        // === MA Overlay ===
        public void AddMaOverlay(int period, string maType, System.Windows.Media.Color color)
        {
            var ma = new MaOverlay(period, maType, color);
            _mainPricePane.AddOverlay(ma);
        }

        public void RemoveLastMaOverlay()
        {
            // 簡化：移除最後加入的均線
            // ChartPane 沒有直接存取 _overlays，因此透過清除 tag 再重建不是好方法；此處先移除所有均線。
            // 改為：先記錄是否只剩一個，若只有一個則移除標籤。
            _mainPricePane.RemoveOverlaysByTag("均線");
            // 標籤區停用
        }

        public void ClearMaOverlays()
        {
            _mainPricePane.RemoveOverlaysByTag("均線");
            // 標籤區停用
        }

        public void RemoveMaOverlayByPeriod(int period)
        {
            _mainPricePane.RemoveMaByPeriod(period);
        }

        public bool SetOverlayPara_Ma(Window? owner = null)
        {
            var cfgs = _mainPricePane.GetOverlayConfigs().Where(c => c.Type == "MA").ToList();
            var defaults = new List<(int period, string maType, System.Windows.Media.Color color)>();
            if (cfgs.Count == 0)
            {
                defaults.Add((144, "SMA", System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)));
            }
            else
            {
                foreach (var c in cfgs)
                {
                    var col = string.IsNullOrEmpty(c.ColorHex)
                        ? System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)
                        : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(c.ColorHex!);
                    defaults.Add((Math.Max(1, c.Period), c.MaType ?? "MA", col));
                }
            }

            var dlg = new MaSettingsDialog(defaults) { Owner = owner };
            var ok = dlg.ShowDialog() == true;
            if (ok)
            {
                _mainPricePane.RemoveOverlaysByTag("均線");
                foreach (var ln in dlg.ResultLines)
                {
                    _mainPricePane.AddOverlay(new MaOverlay(ln.Period, ln.MaType, ln.Color));
                }
            }
            return ok;
        }

        // === Helpers that open internal dialogs ===
        public bool PromptRemoveMaOverlayByPeriod(Window? owner = null)
        {
            var dlg = new InputPeriodDialog { Owner = owner };
            if (dlg.ShowDialog() == true)
            {
                RemoveMaOverlayByPeriod(dlg.Period);
                return true;
            }
            return false;
        }

        public void ShowAppearLayerWindow(Window? owner = null)
        {
            var resolvedOwner = owner ?? Application.Current?.MainWindow;
            var win = new AppearLayerWindow(this)
            {
                Owner = resolvedOwner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.ShowDialog();
        }

        // ==== 綁定 SessionSchedule 清單 ====
        private System.Collections.IList? _sessionScheduleSource;
        private System.Collections.Specialized.INotifyCollectionChanged? _sessionScheduleObs;
        private readonly System.Collections.Generic.HashSet<System.ComponentModel.INotifyPropertyChanged> _sessionItemSubs = new();

        public void SetSessionScheduleSource(System.Collections.IList? source)
        {
            // 解除舊訂閱
            if (_sessionScheduleObs != null)
            {
                _sessionScheduleObs.CollectionChanged -= OnSessionScheduleCollectionChanged;
                _sessionScheduleObs = null;
            }
            foreach (var it in _sessionItemSubs)
            {
                it.PropertyChanged -= OnSessionScheduleItemChanged;
            }
            _sessionItemSubs.Clear();

            _sessionScheduleSource = source;

            // 確保 overlay 存在並指向來源
            if (_sessionScheduleOverlay == null)
            {
                _sessionScheduleOverlay = new SessionScheduleOverlay();
                _mainPricePane.AddOverlay(_sessionScheduleOverlay);
            }
            _sessionScheduleOverlay.SetSource(_sessionScheduleSource);

            // 新訂閱
            if (source is System.Collections.Specialized.INotifyCollectionChanged obs)
            {
                _sessionScheduleObs = obs;
                obs.CollectionChanged += OnSessionScheduleCollectionChanged;
            }
            if (source != null)
            {
                foreach (var o in source)
                {
                    if (o is System.ComponentModel.INotifyPropertyChanged npc && _sessionItemSubs.Add(npc))
                        npc.PropertyChanged += OnSessionScheduleItemChanged;
                }
            }

            TriggerScheduleRedraw();
        }

        private void OnSessionScheduleCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var o in e.OldItems)
                {
                    if (o is System.ComponentModel.INotifyPropertyChanged npc && _sessionItemSubs.Remove(npc))
                        npc.PropertyChanged -= OnSessionScheduleItemChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (var o in e.NewItems)
                {
                    if (o is System.ComponentModel.INotifyPropertyChanged npc && _sessionItemSubs.Add(npc))
                        npc.PropertyChanged += OnSessionScheduleItemChanged;
                }
            }
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset && _sessionScheduleSource != null)
            {
                foreach (var it in _sessionItemSubs) it.PropertyChanged -= OnSessionScheduleItemChanged;
                _sessionItemSubs.Clear();
                foreach (var o in _sessionScheduleSource)
                {
                    if (o is System.ComponentModel.INotifyPropertyChanged npc && _sessionItemSubs.Add(npc))
                        npc.PropertyChanged += OnSessionScheduleItemChanged;
                }
            }
            TriggerScheduleRedraw();
        }

        private void OnSessionScheduleItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            TriggerScheduleRedraw();
        }

        private void TriggerScheduleRedraw()
        {
            void redraw()
            {
                var (start, spacing, _) = _mainPricePane.GetXViewState();
                _mainPricePane.ApplyXViewState(start, spacing);
            }
            if (!_mainPricePane.Dispatcher.CheckAccess())
                _mainPricePane.Dispatcher.BeginInvoke(new System.Action(redraw));
            else redraw();
        }

        private void OnMainXViewChanged(int startIndex, double barSpacing, double barWidth)
        {
            foreach (var pane in _indicatorPanes)
            {
                pane.ApplyXViewState(startIndex, barSpacing);
            }
        }

        private void OnAnyCrosshairMoved(ChartPane sender, int visibleIndex, bool isValid)
        {
            if (!ReferenceEquals(sender, _mainPricePane))
                _mainPricePane.SyncCrosshairFromMain(visibleIndex, isValid);
            foreach (var pane in _indicatorPanes)
            {
                if (!ReferenceEquals(sender, pane))
                    pane.SyncCrosshairFromMain(visibleIndex, isValid);
            }
        }

        // 便利方法：將內建 TimeframeBar 放到主圖右上角並回傳控制項
        public TimeframeBar UseDefaultTimeframeBar()
        {
            var tf = new TimeframeBar();
            SetMainTopRightContent(tf);
            return tf;
        }
    }
}
