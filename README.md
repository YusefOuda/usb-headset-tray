# usb-headset-tray

A Windows system tray app that monitors your USB gaming headset via [HeadsetControl](https://github.com/Sapd/HeadsetControl) and automatically switches your default audio output when the headset powers on or off.

## Features

- Tray icon with color-coded battery bar (green / orange / red) and charging indicator
- **Auto-switch**: sets your chosen headset output as the default playback device when it powers on; restores a fallback (or the previous default) when it powers off
- Configurable headset and fallback output device pickers with auto-detection
- Headset controls submenu (sidetone, mic volume, inactive time, EQ preset) — shown only for capabilities the connected headset supports
- Start with Windows
- Settings persisted in `%APPDATA%\usb-headset-tray\settings.json`

## Why this exists

The USB dongle stays enumerated by Windows even when the headset is off, so Windows never switches devices on its own. This app polls HeadsetControl every 3 seconds and acts only on on/off state transitions, so manual audio switches in between are never overridden.

## Requirements

- Windows 10 / 11 (x64)

HeadsetControl is bundled in the release zip — no separate install needed.

## Installation

1. Download `usb-headset-tray.zip` from [Releases](../../releases)
2. Extract both `UsbHeadsetTray.exe` and `headsetcontrol.exe` to the same folder
3. Run `UsbHeadsetTray.exe`

## Usage

Right-click the tray icon to open the menu:

- **Auto-switch audio** — toggle automatic device switching
- **Headset output device** — pick which playback device to activate when the headset turns on (auto-detected on first connect)
- **Fallback output device** — pick what to switch back to when it turns off
- **Headset Controls** — sidetone, mic volume, inactive time, EQ preset (visible only while headset is online)
- **Start with Windows** — adds/removes the app from `HKCU\...\Run`
- **Exit**

## Building

```powershell
dotnet run

dotnet publish usb-headset-tray.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Third-party software

This release includes [HeadsetControl](https://github.com/Sapd/HeadsetControl) by Sapd,
distributed under the [GNU General Public License v3.0](https://github.com/Sapd/HeadsetControl/blob/master/LICENSE).

## License

GPL-3.0 — see [LICENSE](LICENSE).
