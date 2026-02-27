---
name: txno2-backtest-db
description: Maintain the 台指二號 backtest database contract (.btdb) between ZenPlatform recorder and BackTestReviewer viewer. Use when adding/changing recorder writes, schema evolution, event/log/session serialization, strategy settings snapshots, or viewer data loading behavior.
---

# TxNo2 Backtest DB

Keep `.btdb` stable and evolvable.

## Scope

- Recorder side: `ZenPlatform/SessionManager/Backtest/BacktestRecorder.cs` and backtest pipeline call sites.
- Viewer side: `BackTestReviewer` database readers and chart/task/log render pipeline.

## Contract Rules

- Treat `.btdb` as an integration contract, not a local implementation detail.
- Keep timestamp ordering deterministic for bars/events/logs.
- Keep session-scoped and global log semantics distinguishable.
- Keep strategy settings snapshot persisted for post-run inspection.
- Keep final backtest summary persisted for viewer analysis.

## Compatibility Rules

- Prefer additive schema changes (new table/column) over destructive changes.
- Keep old columns readable when introducing new fields.
- Guard viewer reads against missing optional tables/columns.
- If behavior depends on new schema, fail gracefully with explicit message.

## Data Semantics

- Bars and indicators are recorded values from backtest flow; viewer displays, not recomputes strategy decisions.
- Execution markers (entry/exit/reverse/finish close) must preserve original event meaning.
- Session IDs must remain stable keys for task list, chart markers, and scoped logs.

## Release vs Debug

- Keep debug-only diagnostics out of release `.btdb` payload unless explicitly requested.
- When temporary diagnostic tables/fields are added for investigation, remove or gate them before release.

## Validation Checklist

- Run one fast backtest and one precise backtest.
- Open generated `.btdb` in viewer and verify:
  - task list appears
  - bars render
  - markers align with session timeline
  - selected session log range is correct
  - strategy settings tab is populated
  - pnl chart is populated
- Verify viewer behavior when optional datasets are absent.
