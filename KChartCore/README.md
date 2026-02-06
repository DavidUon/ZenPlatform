# KChartCore2 - 新版 K線引擎

## 專案概述

KChartCore2 是重新設計的 K 線圖表引擎，採用簡化架構取代原有 KChartCore 的複雜實現。專注於職責分離、可擴展性和除錯友善的設計原則。

## 核心架構

### 🏗️ 主要組件

1. **KChartEngine** - K 線引擎核心
2. **IMarketRule** - 市場規則介面  
3. **TaifexMarketRule** - 台指期貨規則實作
4. **TradingCalendar** - 交易日曆管理
5. **FunctionKBar** - K 線資料結構

### 📊 資料結構

#### FunctionKBar (K線資料)
```csharp
public struct FunctionKBar
{
    public DateTime StartTime { get; set; }         // K棒開始時間
    public DateTime CloseTime { get; set; }        // K棒結束時間
    public decimal Open { get; set; }              // 開盤價
    public decimal High { get; set; }              // 最高價
    public decimal Low { get; set; }               // 最低價
    public decimal Close { get; set; }             // 收盤價
    public int Volume { get; set; }                // 成交量
    
    public bool IsMarketOpen { get; set; }         // 市場開市狀態
    public bool ContainsMarketOpen { get; set; }   // 包含開盤時刻
    public bool ContainsMarketClose { get; set; }  // 包含收盤時刻
    public bool IsNullBar { get; set; }            // 空K棒標記
}
```

## 核心功能

### ⏰ 時間管理

#### SetCurrentTime(DateTime time)
設定系統絕對參考時間，所有時間相關計算以此為準。

```csharp
var engine = new KChartEngine();
engine.SetCurrentTime(new DateTime(2025, 1, 15, 9, 30, 0));
```

### 📈 Tick 資料處理

#### SetNewTick(decimal price)
接收外界傳入的價格 tick 資料。

**首次 Tick 邏輯 (IsNullBar == true)**：
- 設定 `Open = High = Low = Close = price`
- 記錄 `StartTime = CurrentTime`
- 設定 `IsNullBar = false`

**後續 Tick 邏輯 (IsNullBar == false)**：
- 更新 `High = Math.Max(High, price)`
- 更新 `Low = Math.Min(Low, price)`  
- 更新 `Close = price`

```csharp
engine.SetNewTick(100.5m);  // 第一個tick: OHLC=100.5
engine.SetNewTick(101.0m);  // High=101.0, Close=101.0
engine.SetNewTick(99.8m);   // Low=99.8, Close=99.8
```

### 🔒 K棒封存

#### SealCurrentBar()
外界通知系統進行 K 棒切割封存。

**執行邏輯**：
1. **市場檢查**：呼叫 `_tfxRule.IsMarketOpen(CurrentTime)`
   - 如果休市：直接 `return`，不做任何處理
   - 如果開市：繼續執行封存邏輯

2. **封存當前 K 棒**：
   - 設定 `_floatingBar.CloseTime = CurrentTime`
   - 將 `_floatingBar` 加入 `_oneMinuteHistory` 清單
   - 觸發 `OnKbarCompleted?.Invoke(1, _floatingBar)` 事件

3. **準備新 K 棒**：
   - 建立新的空 `_floatingBar`
   - 設定 `IsNullBar = true`
   - 設定 `StartTime = CurrentTime`

```csharp
// 正常交易時間
engine.SetCurrentTime(new DateTime(2025, 1, 2, 9, 30, 0));  // 日盤
engine.SealCurrentBar();  // 生成K棒並觸發事件

// 休市時間  
engine.SetCurrentTime(new DateTime(2025, 1, 1, 10, 0, 0));  // 元旦
engine.SealCurrentBar();  // 直接返回，不處理
```

## 市場規則系統

### 🏛️ TaifexMarketRule (台指期貨規則)

#### 交易時段
- **日盤**: 08:45 - 13:45
- **夜盤**: 15:00 - 05:00 (跨日)

#### 交易日判斷
- 自動載入 `台灣國定假日定義.txt`
- 檔案不存在時自動建立預設假日資料
- 支援週末和國定假日檢查

#### 假日檔案格式
```
# 格式: YYYY-MM-DD,狀態,註解
2025-01-01,休市,元旦
2025-01-28,休市,農曆春節 (除夕)
2025-04-04,休市,兒童節及清明節
```

### 📅 市場開市邏輯

#### IsMarketOpen(DateTime time) 判斷流程
1. **交易日檢查**: 呼叫 `IsTradingDay(date)`
   - 檢查是否為國定假日
   - 檢查是否為週末
   
2. **交易時段檢查**:
   - **日盤時段**: 08:45 ≤ time ≤ 13:45 且**當日為交易日**
   - **夜盤前段**: 15:00 ≤ time ≤ 24:00 且**當日為交易日**
   - **夜盤後段**: 00:00 ≤ time ≤ 05:00 且**前一日為交易日**

#### 夜盤跨日邏輯說明

**核心概念**: 夜盤是連續性交易時段 (15:00 → 05:00)，跨日部分只依賴夜盤開始日的狀態。

**判斷規則**:
- **夜盤前段** (15:00-24:00): 檢查當日是否開市
- **夜盤後段** (00:00-05:00): 檢查前一日是否開市 (夜盤開始日)

**實際案例分析**:

```csharp
// ✅ 週五夜盤 → 週六凌晨 (正常跨日)
IsMarketOpen(2025-01-03 15:00:00);  // 週五夜盤開始 → 檢查週五 = true
IsMarketOpen(2025-01-03 23:59:00);  // 週五夜盤中 → 檢查週五 = true  
IsMarketOpen(2025-01-04 02:00:00);  // 週六凌晨 → 檢查週五 = true ✅
IsMarketOpen(2025-01-04 05:00:00);  // 週六凌晨 → 檢查週五 = true ✅

// ❌ 週日 → 週一凌晨 (週日沒夜盤)
IsMarketOpen(2025-01-05 15:00:00);  // 週日 → 檢查週日 = false ❌
IsMarketOpen(2025-01-06 02:00:00);  // 週一凌晨 → 檢查週日 = false ❌

// ❌ 假日影響夜盤
IsMarketOpen(2025-01-01 15:00:00);  // 元旦 → 檢查元旦 = false ❌
IsMarketOpen(2025-01-02 02:00:00);  // 元旦隔日凌晨 → 檢查元旦 = false ❌
```

#### 開市時間範例
```csharp
// ✅ 開市時間
IsMarketOpen(2025-01-02 09:30:00);  // 日盤，平日
IsMarketOpen(2025-01-02 16:00:00);  // 夜盤前段，平日
IsMarketOpen(2025-01-04 02:00:00);  // 週六凌晨，週五夜盤延續

// ❌ 休市時間  
IsMarketOpen(2025-01-01 10:00:00);  // 元旦假日
IsMarketOpen(2025-01-06 02:00:00);  // 週一凌晨，週日沒夜盤
IsMarketOpen(2025-01-02 14:30:00);  // 中午休市時段
IsMarketOpen(2025-01-04 06:00:00);  // 週六早上，夜盤已結束
```

## 事件系統

### 📡 OnKbarCompleted 事件
K 棒完成時觸發，傳遞週期和 K 棒資料。

```csharp
engine.OnKbarCompleted += (period, kbar) => 
{
    Console.WriteLine($"{period}分K棒完成:");
    Console.WriteLine($"時間: {kbar.StartTime:HH:mm} -> {kbar.CloseTime:HH:mm}");
    Console.WriteLine($"OHLC: {kbar.Open}/{kbar.High}/{kbar.Low}/{kbar.Close}");
};
```

### 🐛 DebugMsg 事件  
除錯訊息輸出，支援多層級轉發。

```csharp
engine.DebugMsg += (msg) => Console.WriteLine($"[DEBUG] {msg}");
```

**訊息流向**:
```
TaifexMarketRule → KChartEngine → 外部系統
     ↓               ↓              ↓
  WriteDebugMsg()  加上前綴轉發   接收除錯訊息
```

## 使用範例

### 🚀 基本使用流程

```csharp
// 1. 建立引擎
var engine = new KChartEngine();

// 2. 訂閱事件
engine.OnKbarCompleted += (period, kbar) => 
{
    Console.WriteLine($"新K棒: {kbar.StartTime:HH:mm} OHLC={kbar.Open}/{kbar.High}/{kbar.Low}/{kbar.Close}");
};

engine.DebugMsg += (msg) => Console.WriteLine($"[DEBUG] {msg}");

// 3. 模擬 tick 資料處理
DateTime currentTime = new DateTime(2025, 1, 2, 9, 30, 0);

for (int i = 0; i < 60; i++)  // 模擬1小時的tick
{
    engine.SetCurrentTime(currentTime);
    engine.SetNewTick(100m + (decimal)Random.Shared.NextDouble() * 2);  // 隨機價格
    
    // 每分鐘封存K棒
    if (currentTime.Second == 0)
    {
        engine.SealCurrentBar();
    }
    
    currentTime = currentTime.AddMinutes(1);
}
```

### 🧪 測試建議

#### 時間邏輯測試
```csharp
// 測試跨多天的K棒生成
DateTime startTime = new DateTime(2025, 1, 1, 0, 0, 0);
DateTime endTime = new DateTime(2025, 1, 10, 0, 0, 0);

int kbarCount = 0;
int skipCount = 0;

for (DateTime time = startTime; time < endTime; time = time.AddMinutes(1))
{
    engine.SetCurrentTime(time);
    engine.SetNewTick(100m);
    
    // 記錄封存前的狀態
    bool wasMarketOpen = engine._tfxRule.IsMarketOpen(time);
    engine.SealCurrentBar();
    
    if (wasMarketOpen)
        kbarCount++;
    else
        skipCount++;
}

Console.WriteLine($"生成K棒: {kbarCount} 根, 跳過: {skipCount} 次");
```

#### 假日測試案例
- ✅ **工作日日盤**: 2025-01-02 09:30 (週四日盤)
- ✅ **工作日夜盤**: 2025-01-02 20:00 (週四夜盤)  
- ❌ **國定假日**: 2025-01-01 10:00 (元旦)
- ❌ **週末**: 2025-01-04 10:00 (週六)
- ❌ **休市時段**: 2025-01-02 14:30 (中午休市)

## 設計原則

### 🎯 職責分離
- **外部系統**: 控制時間推進和 K 棒切割時機
- **KChartEngine**: 負責 tick 處理和 K 棒生成邏輯
- **MarketRule**: 負責交易時間和假日判斷

### 🔧 可擴展性
- 透過 `IMarketRule` 介面支援不同期貨商品
- 事件驅動架構便於外部系統整合
- 簡化的資料結構便於序列化和傳輸

### 🐛 除錯友善
- 統一的 `WriteDebugMsg()` 方法
- 完整的事件通知機制
- 清晰的狀態轉換邏輯

## 後續擴展

### 🚧 待實作功能
- 多週期 K 棒聚合 (5分、15分、60分等)
- Volume 成交量處理
- 歷史資料匯入功能
- K 棒資料持久化

### 💡 改進方向
- 效能優化: 大量 tick 處理
- 記憶體管理: 歷史資料清理機制
- 容錯處理: 異常 tick 資料過濾
- 配置化: 支援不同交易所規則

---

*最後更新: 2025年9月13日*
*版本: KChartCore2 v1.0*