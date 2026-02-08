Charts Library 設計說明

概觀
- 本專案提供一個可重用的 K 線圖（Candlestick）WPF 元件，支援多窗格（主圖 + 副圖指標）、主圖疊加指標（均線、布林通道）、精緻的游標互動與版面持久化。目標是效能穩定、擴充容易、介面清楚，便於交易軟體與研究工具整合。

核心設計理念
- 高效繪圖：主圖 K 線優先使用 StreamGeometry 批次描邊/填色，減少 UIElement 數量以避免 WPF 排版壅塞。
- 清楚分層：將主圖窗格（ChartPane）與多窗格容器（MultiPaneChartView）分離，Overlay（主圖疊加）與 Indicator（副圖）再分出責任，便於維護與擴充。
- 一致互動：游標垂直線跨窗格同步、時間與提示框位置一致、在右側空白區也能顯示時間與最後一根資訊、離開時維持「最後一根數值」。
- 可持久化：包含分割比例、疊加指標、指標參數、價格小數位等，固定儲存於程式執行檔同目錄的 charts_config.json。

專案結構與元件
- MultiPaneChartView（Charts/MultiPaneChartView.cs）
  - Grid 容器，管理主圖 + 多個副圖（以 GridSplitter 調整高度）。
  - 保存/載入版面（row star 權重）、指標清單、主圖疊加、價格小數位。
  - 負責跨窗格游標同步與資料快取派發（LoadHistory）。

- ChartPane（Charts/ChartPane.cs）
  - 單一圖窗格抽象，內含：
    - 繪圖層：_bottomLayer、_kBarLayer、_indicatorLayer、_topLayer。
    - 左側資訊面板：PriceInfoPanel（標題 + 區塊化資訊）。
    - 十字線與提示：CrosshairTooltip、時間/價格標籤。
  - 功能：K 線繪製（StreamGeometry）、Y/X 軸、縮放/拖曳、十字線、價格/時間標籤、Overlay 管理。
  - API：SetPriceDecimalPlaces()/GetPriceDecimalPlaces()、Add/Remove/Clear Overlays、SetOverlaysFromConfigs()、GetOverlayConfigs() 等。

- Overlays（Charts/Overlays）
  - 介面 IOverlayIndicator：OnDataChanged/OnViewportChanged/OnCrosshairIndexChanged/Draw/GetInfoLines。
  - MaOverlay：SMA/EMA，顏色可自訂，於左側「均線」區塊顯示 MA 值與方向箭頭。
  - BollingerOverlay：Mid/Up/Low 計算與帶狀填色，12 色 + 透明度設定，於左側「布林通道」區塊顯示值與箭頭。
  - BbiOverlay：BBI（MA3/6/12/24 平均），顏色可自訂，於左側「BBI」區塊顯示值與方向箭頭。
  - BbiOverlay：BBI（MA3/6/12/24 平均），顯示於左側「BBI」區塊。

- 副圖指標（Charts/*.Pane.cs）
  - VolumePane：成交量長條；Y 軸固定 0 位小數；左側顯示 Vol。
  - KdPane：RSV 平滑計算（K/D），中線 50；左側標題「KD指標」。
  - MacdPane：DIF/DEA/MACD（Hist），左側標題「MACD指標」。

- UI 組件
  - PriceInfoPanel：左側資訊區。標題列 + 可多段 Section（均線、布林通道…），每行右對齊數值與方向箭頭。
  - CrosshairTooltip：主圖游標提示，顯示 OHLC 與可擴充的資訊。
- 設定視窗：MaSettingsDialog（可一次設定多條）、BollSettingsDialog、KdSettingsDialog、MacdSettingsDialog。

- 其他
  - CoordinateCalculator：座標換算與游標索引計算。
  - DataStructures：ChartKBar（原始資料）、GraphKBar（繪圖快取）。
  - ChartStyle：主題色與尺寸設定（背景、網格、軸、字體大小、邊距、Bar 寬距等）。
  - ConfigDtos：ChartViewConfig/OverlayConfig/IndicatorConfig（JSON 序列化）。

持久化（charts_config.json）
- 位置：AppContext.BaseDirectory/charts_config.json（與執行檔同目錄）。
- 內容：
  - PriceDecimals：價格顯示小數位數（預設 0；成交量總為 0）。
  - RowHeights：主圖與各副圖的 Star 權重集合（總和 1）。
  - Overlays：主圖疊加（MA/BOLL/BBI）型別與參數／色彩（BBI 支援 4 組期間）。
  - Indicators：副圖清單（VOL/KD/MACD）及其參數。
- 載入策略：
  - 自動在 MultiPaneChartView Loaded 時載入；若 RowHeights 與現有窗格數不符則改用預設比例（主圖 0.7、其餘平均）。
  - KD/MACD 等會在載入後套參數並重新運算，再同步 X 視圖。
- 儲存策略：
  - 呼叫 SaveConfig() 前會先根據 RenderSize 計算並刷新 Star 權重。

互動與行為細節
- 十字線
  - 垂直線跨窗格同步；水平線僅在觸發的窗格顯示（主圖顯示價籤與提示，副圖僅顯示垂直線）。
  - 在右側空白區仍顯示時間與提示（以最後一根為基準）。
  - 滑鼠離開時：隱藏線與標籤，但主圖左側的 MA/BOLL 區會顯示「最後一根數值」。

- 左側資訊面板（主圖）
  - 標題「K線圖」。
  - 區塊：均線、布林通道、BBI；標題列深灰色；內容右對齊數值，並以紅▲/綠▼ 指示與前一根的變化方向（持平延續前一次方向）。

- 軸與小數位
  - SetPriceDecimalPlaces(n) 可調整主圖價格小數位；套用於 Y 軸、十字線價格框、最高/最低點標籤、Overlay 計算的輸出。
  - VolumePane 強制 0 位小數。
  - X 軸格線：預設每 30 分鐘一條；若視圖過密會自動改為每 60 或 120 分鐘。對齊線（如開盤）仍優先顯示。密度可由 `ChartStyle.XAxisMinGridSpacing` 與 `ChartStyle.XAxisMinLabelSpacing` 調整。

- 縮放與平移
  - 滑鼠滾輪縮放（以游標位置為錨點）；拖曳左鍵平移（主圖）。
  - X 視圖改變時，會同步到所有副圖。

擴充指引
- 新增「主圖疊加」
  1) 實作 IOverlayIndicator：計算、視窗變更、十字線同步、Draw(Canvas, ChartPane) 與 GetInfoLines。
  2) 在 ChartPane.SetOverlaysFromConfigs()/GetOverlayConfigs() 加入序列化邏輯。
  3) 若需要設定視窗，可在 MultiPaneChartView 增加便捷 API 與 Dialog。

- 新增「副圖指標」
  1) 新建類別繼承 ChartPane，通常關閉 EnableBatchDrawing（逐點繪線/長條）。
  2) 在 IndicatorPanelType 與 IndicatorPaneFactory 註冊類型。
  3) 於 MultiPaneChartView.CaptureIndicatorConfigs()/LoadConfig() 增加參數保存/載入。

- 風格統一
  - ChartStyle 控制背景、網格、軸、字體大小、Bar 間距與邊距，避免在視圖以外重複定義色彩與尺寸。

維護與開發約定
- 繪圖
  - 主圖大量圖元以 StreamGeometry 批次繪製；副圖與 Overlay 盡量減少 UIElement 產生，避免在 Move/Zoom 時反覆新增/移除太多物件。
  - 每次重繪時先清理追蹤的 UIElement（Tracked UI）以免殘留。

- 效能
  - 拖曳時啟用 Canvas BitmapCache，釋放後關閉（避免持續佔用記憶體與模糊文字）。
  - 計算座標時使用 CoordinateCalculator 快取，僅在尺寸/參數變動時更新。

- 執行緒
  - 本元件為 WPF UI 元件，請在 UI 執行緒呼叫 LoadHistory/新增指標/改參數等 API。

- 相容性
  - charts_config.json 的欄位新增時應保留舊欄位解析；無法對應的新欄位應預設安全值以避免例外。

使用方式（簡例）
1) XAML 參考
```
xmlns:charts="clr-namespace:Charts;assembly=Charts"

<charts:MultiPaneChartView x:Name="MainChart" />
```

2) 載入資料
```
var bars = new List<ChartKBar>
{
    new ChartKBar { Time = DateTime.Now, Open=100, High=110, Low=95, Close=105, Volume=1234 },
    // ...
};
MainChart.SetPriceDecimals(0); // 預設 0，可依商品設定
MainChart.AddIndicatorPanel(IndicatorPanelType.Vol);
MainChart.LoadHistory(bars);
```

3) 增減副圖指標
```
MainChart.AddIndicatorPanel(IndicatorPanelType.Kd);
MainChart.RemoveIndicatorPanel(IndicatorPanelType.Kd);
MainChart.AddIndicatorPanel(IndicatorPanelType.Macd);
```

4) 主圖疊加
```
// 均線
MainChart.AddMaOverlay(period:20, maType:"SMA", color:Colors.Gold);
MainChart.RemoveMaOverlayByPeriod(20);
MainChart.ClearMaOverlays();

// BBI
MainChart.AddBbiOverlay();
MainChart.RemoveBbiOverlay();
MainChart.SetOverlayPara_Bbi(this);

// 布林通道（可用設定視窗）
MainChart.AddBollingerOverlay(period:20, k:2.0);
MainChart.SetOverlayPara_Bollinger(this);
```

5) 儲存/載入設定
```
// MultiPaneChartView 會在 Loaded 自動載入
// 建議視窗關閉前呼叫一次 SaveConfig
MainChart.SaveConfig();
```

注意事項
- CSV/資料來源：Dev_Charts 範例以 Big5 編碼讀取示例檔，實務上請依來源格式解析，並確保時間單位與窗格對齊。
- 小數位：價格小數位影響 Y 軸、十字線價籤、高低點標籤與 Overlay 值；成交量永遠 0 小數。
- 版面：RowHeights 為 Star 權重，如保存的窗格數與目前不同，會回退到預設比例。請在新增/移除窗格後再儲存設定。
- 游標：只有主圖顯示提示框與價格標籤；副圖同步垂直線與自己的左側資訊區。
- 顏色與樣式：統一由 ChartStyle 調整；左側面板區塊標題使用深灰底，內容為右對齊數值與漲跌箭頭。

API 一覽（主要）
- MultiPaneChartView
  - `LoadHistory(List<ChartKBar>)`
  - `SetPriceDecimals(int places)`
  - `AddIndicatorPanel(IndicatorPanelType)` / `RemoveIndicatorPanel(IndicatorPanelType)` / `HasIndicatorPanel`
- `AddBollingerOverlay(int, double, Color?, double)` / `RemoveBollingerOverlay()` / `SetOverlayPara_Bollinger(Window?)`
- `AddBbiOverlay(Color?)` / `RemoveBbiOverlay()` / `SetOverlayPara_Bbi(Window?)`
- `AddMaOverlay(int, string, Color)` / `RemoveMaOverlayByPeriod(int)` / `ClearMaOverlays()` / `SetOverlayPara_Ma(Window?)`
- `PromptRemoveMaOverlayByPeriod(Window?)`：由 Charts 內建視窗詢問期間後移除對應均線
- `ShowAppearLayerWindow(Window?)`：開啟圖層顯示視窗（勾選成交量/KD/MACD、MA、BBI、BOLL）
- `UseDefaultTimeframeBar()`：將內建 TimeframeBar 放到主圖右上角並回傳控制項（訂閱 `OnTimeframeChange` 即可）

### TimeframeBar（內建控制項）
- 事件：`OnTimeframeChange(object sender, int minutes)`
- 預設預設值：`[1,2,3,5,10,15,20,30,45,60,90,120]`
- 自訂輸入：使用內建 `InputPeriodDialog`（1–120 驗證）
- 嵌入方式：
  ```csharp
  var tfBar = MainChart.UseDefaultTimeframeBar();
  tfBar.OnTimeframeChange += (s, minutes) => {
      // 請於外部自行聚合/載入資料，然後：
      // MainChart.AddBarList(list);
  };
  ```
  - `SaveConfig()` / `LoadConfig()`

- ChartPane（主圖可透過 `GetMainPricePane()` 取得）
  - `SetPriceDecimalPlaces(int)` / `GetPriceDecimalPlaces()`
  - `AddOverlay(IOverlayIndicator)` / `RemoveOverlaysByTag(string)` / `RemoveMaByPeriod(int)`
  - `GetOverlayConfigs()` / `SetOverlaysFromConfigs(IEnumerable<OverlayConfig>)`

版本與相依
- .NET/WPF（專案為 WPF Desktop）。
- 不需外部網路資源；設定檔為純 JSON。

範例應用
- Dev_Charts：快速測試 UI + 行為；提供載入 CSV、加入/移除指標、開啟設定對話等。

維護聯絡
- 若要新增指標或 Overlay、或調整持久化欄位，請一併更新本文件與 ConfigDtos，並在 Dev_Charts 加入最小可驗證操作路徑。
