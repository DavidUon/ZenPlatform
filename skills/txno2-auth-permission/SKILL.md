---
name: txno2-auth-permission
description: Maintain 台指二號 authentication and permission behavior, including Magistock login/logout, trial expiration, HKCU first-run registry, program stopped mode, duplicate-login handling, and real-trade quota checks across all strategies.
---

# TxNo2 Auth Permission

Apply these rules when changing auth/permission behavior.

## Core Files

- `ZenPlatform/Core/UserInfoCtrl.cs`
- `ZenPlatform/Core/Core.cs`
- `ZenPlatform/MVVM/Cs/MainWindow.xaml.cs`
- `ZenPlatform/MVVM/Xaml/PurchaseReminderWindow.xaml`
- `ZenPlatform/MVVM/UserControls/SessionPageControl.xaml(.cs)`

## First-Run and Trial

- Persist first-run date in `HKCU\\Software\\Magistock\\TxNo2\\FirstRunDate`.
- Keep trial-day logic based on that persisted first-run date.
- Do not move this persistence back to HKLM.

## Permission Source Priority

- Prefer live server permission after successful login.
- Allow local snapshot fallback for resilience, but still evaluate permission validity.
- Keep auth decision centralized in `UserInfoCtrl`.

## Program Stopped Mode

- When program enters stopped mode:
  - stop all running strategies
  - disable strategy-start action
  - unsubscribe network quote
  - drop all incoming quotes (network + DDE)
- Keep login path available so user can recover by re-login.

## Recovery by Login

- User can click status bar "未登入" to login.
- If login returns valid permission/date, clear stopped state and restore feature availability.
- Do not auto-restart strategies after recovery; user restarts explicitly.

## Duplicate Login

- Run duplicate-connection check after startup/login readiness.
- If duplicate is detected and concurrent login is disallowed:
  - show blocking dialog
  - close app immediately after acknowledgment

## Real Trade Quota Rules

- Check quota only when switching to real trade.
- Ignore simulation strategies in quota usage.
- Product weight:
  - 微型台指 = 1
  - 小型台指 = 5
  - 大型台指 = 20
- Required quota per strategy:
  - `OrderSize * MaxSessionCount * ProductWeight`
- Remaining quota:
  - `PermissionCount - sum(used by other real-trade strategies)`
- If insufficient, reject switch and show clear reason.
- If `UnlimitedPermission == true`, allow directly.

## UX Text Consistency

- Keep user-facing Chinese text simple and action-oriented.
- Keep purchase reminder text including:
  - "如果您已經購買過使用權限，請登入 Magistock 帳號獲取權限。"
- Keep stop dialog primary action label aligned with current UX decision (e.g. "關閉").

## Change Safety Checklist

- Test flows:
  - guest startup
  - login success/fail
  - expired trial
  - expired permission
  - permission recovery by login
  - duplicate login close
  - real/sim toggle with multi-strategy quota consumption
