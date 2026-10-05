# usb-headset-tray

A Windows system tray app that monitors your USB gaming headset via [HeadsetControl](https://github.com/Sapd/HeadsetControl) and automatically switches your default audio output when the headset powers on or off.

## Features

- Tray icon with color-coded battery bar (green / orange / red) and charging indicator, or the battery percentage as a number
- Battery level with estimated time remaining in the tooltip and menu (e.g. `Battery: 62% (~9h 40m)`)
- **Auto-switch**: sets your chosen headset output as the default playback device when it powers on; switches to your highest-priority connected fallback (or the previous default) when it powers off
- **Fallback priority list**: rank your other outputs (e.g. Bluetooth headphones above monitor speakers). When a higher-ranked device connects, such as turning on Bluetooth headphones, the app switches to it
- **Optional input switching**: the same on/off and fallback behavior for microphones, with its own toggle. Off by default
- Headset output device picker with auto-detection
- **Notifications**: low battery (configurable threshold, plus a critical alert at 5%) and charge complete
- Headset controls submenu (sidetone, mic volume, inactive time, EQ preset) — shown only for capabilities the connected headset supports
- Daily update check against GitHub Releases (can be turned off)
- Clear status in the tray if HeadsetControl is missing or failing
- Start with Windows
- Settings persisted in `%APPDATA%\usb-headset-tray\settings.json`

## Why this exists

The USB dongle stays enumerated by Windows even when the headset is off, so Windows never switches devices on its own. This app polls HeadsetControl every 5 seconds and acts only on on/off state transitions, so manual audio switches in between are never overridden.

Setting the default device also replaces the device Windows remembers as your preferred default. Normally Windows would switch back to, say, your Bluetooth headphones when they reconnect. The fallback priority list restores that behavior: the app switches when a fallback device connects or the current device disconnects, and leaves any switch you make yourself alone.

## Requirements

- Windows 10 / 11 (x64)

HeadsetControl is bundled in the release zip — no separate install needed.

## Installation

**Installer (recommended):** Download `usb-headset-tray-setup.exe` from [Releases](../../releases) and run it. Installs to `%LOCALAPPDATA%\usb-headset-tray` with no UAC prompt.

**Portable:** Download `usb-headset-tray.zip`, extract both `UsbHeadsetTray.exe` and `headsetcontrol.exe` to a permanent folder, then run `UsbHeadsetTray.exe`. Enable **Settings → Start with Windows** from the tray menu *after* placing the files in their final location.

## Usage

Right-click the tray icon to open the menu:

- **Battery status** (top line): level and estimated time remaining, or charging / offline. Shows "HeadsetControl not found" or "HeadsetControl error" if the app can't read the headset
- **Update available** (only when a newer release exists): opens the download page
- **Output** / **Input**: playback and recording (microphone) switching, each with the same items:
  - **Auto-switch**: toggle automatic switching. While it's off, the device pickers below are greyed out, but your choices stay saved. On by default for output. Off by default for input, where it also does nothing until you pick a headset microphone, so leave it off if you use one standalone mic
  - **Headset device** / **Headset microphone**: the device to make default when the headset turns on. The output device is auto-detected on first connect; the microphone must be picked manually
  - **Fallback devices**: ranked list of devices to use when the headset is off. Use **Add device** to add one (disconnected devices are listed too, so you can add Bluetooth headphones while they're off), and each entry's submenu to move it up or down or remove it. With an empty list, the app restores whatever was default before the headset turned on
- **Headset Controls** — sidetone, mic volume, inactive time, EQ preset (visible only while headset is online)
- **Settings**
  - **Tray icon**: **Battery bar** (default) or **Percentage**, which shows the level as a number on a colored badge. While charging or offline, the icon shows the battery bar either way
  - **Low battery alert**: off, or at 10 / 15 (default) / 20 / 25%. A second, critical alert fires at 5%. Each alert fires once per discharge and includes the estimated time left when known
  - **Charge complete alert**: notifies when the headset reaches 100% while charging, or stops charging at 95% or higher while still on. If your headset only charges while switched off, the app can't see it charging, so this won't fire
  - **Check for updates**: checks GitHub Releases shortly after startup and then daily; click the notification or menu item to open the download page
  - **Start with Windows**: adds/removes the app from `HKCU\...\Run`
- **Exit**

### Battery time estimate

The estimate comes from how fast the battery level drops while the headset is connected and discharging. The first time, it appears once the level has dropped at least twice over 15+ minutes, which can take an hour or two on headsets that report in 5–10% steps. After that, a learned drain rate is saved, so an estimate shows as soon as the headset turns on. It assumes a steady drain rate, so treat it as approximate.

## Building

```powershell
dotnet run --project usb-headset-tray.csproj
dotnet test

dotnet publish usb-headset-tray.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Local builds are version 0.0.0 and skip the update check; release builds take their version from the git tag.

## Third-party software

This release includes [HeadsetControl](https://github.com/Sapd/HeadsetControl) by Sapd,
distributed under the [GNU General Public License v3.0](https://github.com/Sapd/HeadsetControl/blob/master/LICENSE).

## License

GPL-3.0 — see [LICENSE](LICENSE).
