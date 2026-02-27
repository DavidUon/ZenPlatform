---
name: txno2-core
description: Maintain and extend 台指二號 (ZenPlatform + BackTestReviewer) with project-specific behavior for strategy lifecycle, permission gating, backtest recording (.btdb), viewer rendering, and UI contracts. Use when changing strategy logic, SessionManager flow, auth/duplicate-login behavior, backtest export/viewer integration, or any feature that can break existing product conventions.
---

# TxNo2 Core

Follow these rules when working on 台指二號。

## Keep Architecture Boundaries

- Keep strategy execution and decision logic in `ZenPlatform/Strategy` and `ZenPlatform/SessionManager`.
- Keep page behavior in `ZenPlatform/MVVM/UserControls/SessionPageControl.xaml(.cs)`.
- Keep app-level lifecycle and global status in `ZenPlatform/MVVM/Cs/MainWindow.xaml.cs` and `ZenPlatform/Core/Core.cs`.
- Keep viewer-only behavior in `BackTestReviewer`.

## Session and Log Rules

- Treat each `SessionManager` as owning its own log stream.
- Never mix logs across different managers.
- Preserve session-prefixed log semantics (e.g. `[N] ...`) where existing flow depends on them.

## Trading and PnL Contracts

- Keep simulated strategy PnL in one-lot scale unless explicitly changed.
- Keep real order quantity from rule-set order size.
- Keep position display reflecting actual position size.
- Preserve entry/exit reason text order when writing logs and backtest records.

## Real-Trade Permission Contracts

- Validate permission when switching to real trade.
- Use cross-strategy global permission consumption (only `IsRealTrade == true` strategies consume quota).
- Use product weight mapping:
  - 微型台指 = 1
  - 小型台指 = 5
  - 大型台指 = 20
- Required quota = `OrderSize * MaxSessionCount * Weight`.
- Reject switch when insufficient quota and show clear message.

## Program Stop / Authorization Contracts

- `IsProgramStopped` must immediately stop running strategies.
- Disable strategy-start action when program is stopped.
- On stop state, unsubscribe network quote and drop all incoming quotes (DDE + network).
- Keep login path usable so user can re-login to recover permission.
- Keep first-run date persisted under `HKCU\\Software\\Magistock\\TxNo2\\FirstRunDate`.

## Duplicate Login Contract

- Keep duplicate login check after startup/login completes.
- If duplicate is detected and concurrent login is not allowed, show blocking dialog and close app.

## Backtest and Viewer Contracts

- Keep `.btdb` as backtest output contract between TxNo2 and BackTestReviewer.
- Keep recorder writes deterministic and time-ordered.
- Keep viewer as read-only for DB content (no strategy recalculation in viewer).
- Keep viewer launch path compatibility for Debug and Release split-folder layouts.

## UI Contract Discipline

- Preserve existing Chinese labels and product wording unless user requests text changes.
- Preserve dark-theme visual language and current layout conventions.
- Lock core strategy fields during strategy running when product requires immutability.

## Safe Change Workflow

- Read affected files first (`rg`, then targeted `sed`).
- Patch minimum surface area.
- Prefer behavior-preserving refactors over wide rewrites.
- If diagnosis would be materially faster with DebugBus or equivalent runtime tracing, proactively tell the user and request enabling it.
- When touching auth/permission/strategy-start/quote flow, verify full chain:
  - startup
  - login/logout
  - stop/recover
  - real/sim trade toggle
  - backtest start/stop/export
