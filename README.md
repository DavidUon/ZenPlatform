# ZenPlatform Backtest `.btdb` 寫入說明

本文件說明目前專案中，回測結果如何寫入 `.btdb`（SQLite）檔案。

## 1. 寫入流程總覽

1. 回測開始時建立暫存資料庫 `BackTestingTemp.btdb`，並建立 `BacktestRecorder`。
2. 呼叫 `BeginRun(...)` 寫入一筆 `runs`（狀態為 `Running`）。
3. 回測進行中由 `BacktestEngine` 逐筆寫入：
   - 1 分 K 到 `bars`
   - 多空狀態到 `trend_states`
   - 事件到 `events`
4. 回測交易發生時由 `RuleBase.Trade/TradeAtPrice` 寫入 `order_marks`（含任務編號、方向、價格、原因）。
5. 回測結束前將目前 `SessionManager` 的任務清單與 log 寫入：
   - `sessions`（任務列表）
   - `logs`（時間 + 訊息）
6. 回測結束呼叫 `EndRun(...)` 更新 `runs`（`Completed` 或 `Canceled`），並同步寫入 `run_summary`。
5. 若非取消，詢問使用者儲存路徑，將暫存檔複製為最終 `.btdb`，最後刪除 temp 檔。

## 2. 主要程式入口

- 建立 recorder、開始 run、關閉時存檔流程：  
  `ZenPlatform/MVVM/UserControls/SessionPageControl.xaml.cs`
- 寫入器本體（SQLite schema + insert/update）：  
  `ZenPlatform/SessionManager/Backtest/BacktestRecorder.cs`
- 回測事件轉成資料寫入（KBar 完成時寫 bars/trend）：  
  `ZenPlatform/SessionManager/Backtest/BacktestEngine.cs`

## 3. 目前資料表

- `runs`
  - 每次回測一筆
  - 欄位含 `run_id`, `created_at_utc`, `start_time_utc`, `end_time_utc`, `mode`, `strategy_name`, `params_json`, `status`, `summary_json`
- `bars`
  - 目前寫入 `period = 1` 的 OHLCV
  - unique key: `(run_id, time_utc, period)`，重複時間會 `upsert`
- `events`
  - 回測事件與訊息（例如 `BacktestStart`）
- `trend_states`
  - 多空狀態快照（`side`）
  - unique key: `(run_id, time_utc, period)`，同一時間重寫會覆蓋
- `sessions`
  - 任務列表快照（`session_id`, `start_time_utc`, `end_time_utc`, `start_position`, `is_finished`, `realized_profit`, `trade_count`）
- `order_marks`
  - 下單/平倉標記（`event_type`, `side`, `qty`, `price`, `reason`）
  - 可用於 Viewer 主圖顯示任務交易點
- `logs`
  - 回測期間 log（僅 `time_utc + message`）
- `strategy_snapshot`
  - 策略參數快照（`params_json`）
- `run_summary`
  - 回測最終摘要（`summary_json`）

## 4. 實際寫入點

在 `BacktestEngine.OnKBarCompleted(...)`：

- `period == 1` 時呼叫 `Recorder.AppendBar(...)`
- 策略執行後呼叫 `Recorder.AppendTrendState(...)`

在 `SessionPageControl`：

- 回測開始後呼叫 `AppendEvent(..., "BacktestStart", ...)`
- 回測停止時先 `FlushBacktestArtifacts(...)`（寫 `sessions/logs`）再呼叫 `EndRun(...)`

在 `RuleBase`：

- `Trade(...)` / `TradeAtPrice(...)` 成交後呼叫 `AppendOrderMark(...)`

## 5. 檔案產生與儲存行為

- 暫存檔固定在程式目錄：`BackTestingTemp.btdb`
- 回測取消：直接刪除 temp
- 回測完成：開啟儲存對話框，使用者決定是否另存
  - 選擇儲存：`File.Copy(temp, target, overwrite: true)` 後刪除 temp
  - 取消儲存：刪除 temp
- 上次儲存資料夾會記錄於 `backtest_save_settings.json`

## 6. 寫入一致性與效能設定

- `BacktestRecorder` 使用單一連線 + transaction
- SQLite pragma:
  - `journal_mode=WAL`
  - `synchronous=NORMAL`
- `Flush()` 可手動 commit 並開新 transaction（目前主要在 `Dispose`/結束時提交）

## 7. 若要擴充寫入內容

目前已實作：`runs`, `bars(1m)`, `events`, `trend_states`, `sessions`, `order_marks`, `logs`, `strategy_snapshot`, `run_summary`。  
若要新增例如 `indicator_values`：

1. 在 `BacktestRecorder.CreateSchema(...)` 加表
2. 加對應 prepared command 與 `AppendXxx(...)`
3. 在回測流程適當節點呼叫 `AppendXxx(...)`
4. `BackTestReviewer` 端再補讀取與顯示

## 8. BackTestReviewer 讀取對照

`BackTestReviewer` 主要在 `BackTestReviewer/MainWindow.xaml.cs` 讀取 `.btdb`：

- 檔案掃描
  - 掃描 `*.btdb` 並顯示於左側檔案清單
- `runs`（任務列表）
  - `ReadRunSummaryRows(...)` 讀取 run 摘要
  - 會統計該 run 的 bars/events/indicator_values 筆數（若表存在）
- `events` / `logs`（右側 Log）
  - `events`：回測流程事件
  - `logs`：策略執行訊息（可依任務起訖時間切片）
- `bars(period=1)`（K 線主資料）
  - 由 `BarDataProvider` 讀取（`LoadEarliest/LoadBeforeUtc/LoadAfterUtc/...`）
  - 轉成 `ChartKBar` 後顯示在 `MultiPaneChartView`
- `trend_states`（多空副圖）
  - `BarDataProvider` 以 `LEFT JOIN trend_states` 讀取 `side`
  - 映射到 `ChartKBar.Indicators["TREND_SIDE"]`
  - 副圖標題由 run 的 `params_json` 組出（如「多空(自動判斷)」「多空(均線144)」）

### 目前未接線的表

- `indicator_values`
  - 目前 viewer 只做筆數統計，尚未實際渲染到圖上
- `order_marks`, `sessions`, `logs`, `run_summary`, `strategy_snapshot`
  - 寫入端已完成，viewer 尚待完整對應 UI

### 讀寫相容重點

1. 時間欄位皆使用 UTC ISO-8601（`ToString("O")`）  
2. viewer 端會轉成台北時間顯示與定位  
3. `bars` 與 `trend_states` 均以 `(run_id, time_utc, period)` 作為對位鍵  
4. 若新增表，建議同樣包含 `run_id + time_utc (+ period)`，可維持一致的時間對齊策略
