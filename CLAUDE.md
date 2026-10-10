# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & run

```powershell
dotnet build
dotnet test
dotnet run --project usb-headset-tray.csproj
dotnet publish usb-headset-tray.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

A running instance locks `bin\Debug\...\UsbHeadsetTray.exe`; exit it from the tray before rebuilding.

## Tests

xUnit project in `tests/UsbHeadsetTray.Tests` (`net8.0-windows`, sees internals via `InternalsVisibleTo`). The app csproj excludes `tests\**` from its own compile items. Tests cover the pure logic only: `BatteryEstimator`, `BatteryNotifier`, `FallbackPolicy`, `HeadsetPoller.Parse` (against a real headsetcontrol 4.1.0 output fixture), `UpdateChecker.IsNewer`, and `AppSettings` migration. Keep new logic out of `TrayApp` and COM code so it stays testable; pass clocks and device-state lookups in rather than calling `DateTime.UtcNow` / `AudioDeviceManager` directly.

## Architecture

`.NET 8` WinForms app (`net8.0-windows`). No forms — the app is an `ApplicationContext` subclass that owns a `NotifyIcon`. All UI runs on the WinForms message-loop thread. Root namespace: `UsbHeadsetTray`, output: `UsbHeadsetTray.exe`. Dev builds are version `0.0.0`; `release.yml` sets the version from the tag.

### File roles

| File | Role |
|---|---|
| `Program.cs` | Entry point; single-instance mutex, then `Application.Run(new TrayApp())` |
| `TrayApp.cs` | `ApplicationContext` that owns `NotifyIcon`, the poll and update timers, the menu, and wires everything together |
| `HeadsetPoller.cs` | `PollAsync()` spawns `headsetcontrol -o json`; `Parse(json)` returns a `PollResult` (status + `PollFailure`) |
| `BatteryNotifier.cs` | Decides when to show low-battery / critical / charge-complete notifications |
| `FallbackPolicy.cs` | Pure fallback-priority rules; device state lookups are passed in |
| `UpdateChecker.cs` | Queries GitHub's latest-release API and compares the tag with the assembly version |
| `HeadsetControlModels.cs` | JSON deserialization records matching headsetcontrol's api_version 1.5 output |
| `AudioDeviceManager.cs` | Enumerates playback or recording devices (`IMMDeviceEnumerator`) and sets the default (`IPolicyConfig`) |
| `AudioDeviceWatcher.cs` | `IMMNotificationClient` implementation; posts device-state and default-device changes to the UI thread |
| `AudioInterop.cs` | All COM interface/class declarations for Windows audio (see notes below) |
| `BatteryEstimator.cs` | Estimates time remaining from discharge rate; exposes a blended `LearnedRate` that `TrayApp` saves as `BatteryDrainPerHour` |
| `IconRenderer.cs` | Generates a `System.Drawing.Icon` via GDI+. Battery bar (fixed 16 px) or, with `IconShowsPercentage`, the level as white digits on a darkened color badge drawn at `SystemInformation.SmallIconSize` so it stays sharp at high DPI. Charging/offline always use the bar. `DrawPercentage(level, size)` is internal so the icon can be previewed at any size |
| `AppSettings.cs` | JSON persistence to `%APPDATA%\usb-headset-tray\settings.json` |
| `Log.cs` | Append-only log at `%APPDATA%\usb-headset-tray\usb-headset-tray.log` (rolls to `.old.log` at 1 MB). `Program` routes unhandled exceptions here instead of the WinForms Continue/Quit dialog |

### Data flow

`System.Windows.Forms.Timer` (5 s) → `HeadsetPoller.PollAsync()` on a background thread → result returned to UI thread via `async void Timer_Tick` → `TrayApp.ApplyStatus()` updates the icon, tooltip, estimate and notifications, and (if state changed) calls `AudioDeviceManager.SetDefaultPlaybackDevice`.

Poll failures (`PollFailure`):
- `NoDevice`: valid JSON with no device (e.g. dongle unplugged) → treated as an offline status, so auto-switch falls back
- `NotFound`: `headsetcontrol.exe` couldn't be started → shown immediately in the menu/tooltip, with a one-time notification
- `Failed`: unreadable output → shown only after 3 consecutive failures
- `NotFound`/`Failed` leave `_lastState` and the audio device untouched, since the headset's state is unknown

### Menu layout

Top level is kept short: status line, update item (when available), `Output ▸`, `Input ▸`, `Headset Controls ▸` (when online), `Settings ▸`, `Exit`. Each direction submenu holds its Auto-switch toggle, headset device picker, and fallback list; the pickers are disabled while that toggle is off (updated live on toggle, values kept). Set-once options (tray icon style, alerts, update check, start with Windows) live under Settings.

### Notifications

All go through `TrayApp.ShowBalloon`, which also records what clicking the balloon does (only the update notification has an action).
- Low battery at `LowBatteryThreshold` (0 = off) and critical at 5%; each fires once and re-arms on charging or when the level rises 5% above the threshold
- Charge complete: level 100 while charging, or charging stops at ≥ 95% while the headset is still on
- Update available: checked 30 s after startup, then daily, if `CheckForUpdates`; skipped for 0.0.0 dev builds

### HeadsetControl JSON contract (api_version 1.5)

- `devices[0].battery.status`:
  - `"BATTERY_AVAILABLE"` → headset on, battery known
  - `"BATTERY_CHARGING"` → headset on, charging
  - `"BATTERY_UNAVAILABLE"` → headset off (dongle present, headset unreachable)
- `devices[0].battery.level`: 0–100 when available, `-1` otherwise
- `devices[0].status`: `"partial"` when dongle is found but headset is off; `"success"` when fully on

### Auto-switch logic (`TrayApp.HandleAutoSwitch`)

Runs once per direction (`EDataFlow.eRender` = output, `eCapture` = input) with identical logic. Settings are read through `AppSettings.IsAutoSwitchOn(flow)` / `HeadsetDevice(flow)` / `Fallbacks(flow)`; output uses the original property names (`AutoSwitch`, `HeadsetDeviceId`, `FallbackDeviceIds`) for settings compatibility, input uses `AutoSwitchInput`, `HeadsetInputDeviceId`, `FallbackInputDeviceIds`. Per-direction runtime state lives in `_previousDefault[flow]` / `_observedDefault[flow]`.

- `Unknown/Offline → Online`: snapshot current default into `_previousDefault[flow]`, then `SetDefaultDevice(headset device)`
- `Online → Offline`: switch to the highest-ranked connected fallback, or `_previousDefault[flow]` if none is connected
- No action for a direction when its auto-switch is off or its headset device is null. Input is off by default and its headset mic is never auto-detected
- State changes are detected by comparing `_lastState` (updated at end of `ApplyStatus`) to the new status

### Battery time estimate (`BatteryEstimator`)

- A discharge run covers consecutive polls while online and not charging; it resets on charging, offline, or a gap over 2 min
- Rate is measured between the times the level first reaches new lows (not per sample), since many headsets report in 5–10% steps
- Live rate needs ≥ 15 min between the first and latest new low; until then the saved `BatteryDrainPerHour` is used
- Saved rate = average of the rate saved when the run started and the live rate (recomputed from the run's start value on each new low, so it doesn't compound)
- Within a step, the estimate counts down by time since the level was reached, capped at one average step

### Fallback priority (`FallbackPolicy`, called from `TrayApp.OnDeviceStateChanged` / `OnDefaultDeviceChanged`)

Mimics Windows remembering a preferred default, which `SetDefaultEndpoint` overwrites. Applies per direction, only while that direction's auto-switch is on and the headset isn't holding the default (`FallbackRulesActive`). Device ids are direction-specific, so a state change matches at most one direction's list.

- A fallback device becoming active switches to it if it outranks the current default (unlisted devices rank lowest)
- When the default changes because the old default disconnected, re-apply the priority order instead of keeping Windows' choice; a change away from a still-connected device is treated as manual and left alone
- `HeadsetDeviceId` is never used as a fallback, since the dongle endpoint stays active while the headset is off
- Legacy `FallbackDeviceId` setting is migrated into `FallbackDeviceIds` on load

### COM interop notes (`AudioInterop.cs`)

- `CPolicyConfigClient` CLSID: `870AF99C-171D-4F9E-AF0D-E63DF40C2BC9`
- `IPolicyConfig` IID: `F8679F50-850A-41CF-9C72-430F290290C8`
- **Do not reorder methods in `IPolicyConfig`** — the vtable order is fixed by the undocumented Windows COM ABI
- All three `ERole` values (eConsole, eMultimedia, eCommunications) must be set when switching
- `PROPVARIANT` uses `LayoutKind.Explicit, Size=24`; the string pointer sits at offset 8 (64-bit layout); VT_LPWSTR = 31

### Distribution

GitHub Actions (`.github/workflows/release.yml`) runs the tests, then publishes a self-contained single-file win-x64 exe (versioned from the tag) to a GitHub Release when a `v*` tag is pushed.

HeadsetControl (GPL-3.0) is downloaded by the release workflow and shipped next to the exe. `HeadsetPoller` uses that copy if present, otherwise `headsetcontrol` on PATH.
