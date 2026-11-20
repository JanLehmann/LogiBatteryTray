# LogiBatteryTray

A lightweight Windows system tray utility that displays battery levels for Logitech Bluetooth LE and LIGHTSPEED USB devices without running Logitech G HUB or Logi Options+ in the background.

## Overview

Windows only exposes a basic battery percentage for Bluetooth LE peripherals hidden inside the Settings app, and does not expose battery levels for Logitech LIGHTSPEED USB dongles at all. LogiBatteryTray runs in the notification area and renders the current battery percentage directly as a tray icon.

The application has no external NuGet dependencies and uses native Win32 / HID APIs (`cfgmgr32.dll`, `hid.dll`, `setupapi.dll`) on .NET 10 Windows Forms.

## How It Works

1. **Bluetooth LE discovery (`cfgmgr32.dll`)**  
   Enumerates paired `BTHLE` device nodes with Logitech vendor ID (`046D`) or known device names, reading the Windows PnP battery property (`DEVPKEY_Bluetooth_Battery`) and connection state.
2. **Direct HID++ 2.0 / 4.2 queries (`UsagePage 0xFF43`)**  
   Opens shared read/write handles to Logitech long-report (`0x11`, 20-byte) HID collections over both Bluetooth LE and USB receivers:
   - **`0x1004` (Unified Battery):** Reads state of charge (%) and charging status directly on supported mice and keyboards.
   - **`0x1F20` (ADC Measurement):** Reads raw battery voltage in millivolts and charging bit-flags on headsets that do not report a percentage in hardware.
   - **`0x0005` (Device Name / Type):** Resolves the connected device name and category through the USB receiver.
3. **ADC voltage curve & CC/CV tracking**  
   For `0x1F20` headsets (such as the PRO X Wireless), raw millivolts are mapped using separate discharge, ascending charge (constant-current), and descending charge (constant-voltage topping) curves. A local history tracker smooths short-term ADC jitter, anchors plug-in transitions so the reading does not jump when connecting USB-C power, and estimates remaining charge or runtime.

## Tested Hardware

- Logitech MX Keys S (Bluetooth LE)
- Logitech MX Master 3S (Bluetooth LE)
- Logitech PRO X Wireless Gaming Headset (USB LIGHTSPEED receiver `046D:0ABA`)

Other Logitech BLE keyboards/mice and USB HID++ peripherals implementing features `0x1004` or `0x1F20` should work automatically.

## Features

- Pixel-aligned tray icons rendered for 16, 20, 24, and 32 px tray sizes (100% to 200% DPI), following the Windows light/dark taskbar theme.
- Four tray display modes:
  - Separate icon per device (device glyph + percentage)
  - Separate icon per device (percentage only)
  - Single icon cycling through visible devices
  - Single combined icon stacking up to three devices
- Per-device tray visibility toggle.
- Remaining charge time and discharge runtime estimation.
- Local CSV logging (`%LOCALAPPDATA%\LogiBatteryTray\battery_log.csv`) for voltage and state changes.
- Optional low-battery balloon warning and per-user autostart (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).

## Installation & Usage

No installer is required.

1. Download the latest archive from the [Releases](../../releases) page and extract it to a permanent folder (for example `C:\Tools\LogiBatteryTray`).
2. Run `LogiBatteryTray.exe`.
3. By default, Windows 11 places new tray icons inside the overflow popup (`^`). To keep the icon visible next to the clock, click **"Taskleistensymbole anheften..."** in the flyout (or open `Windows Settings -> Personalization -> Taskbar -> Other system tray icons`) and switch `LogiBatteryTray` on.

*(Note: The UI labels inside the application are currently in German.)*

### Command-Line Options

```powershell
# Print detected devices, battery percentages, voltages, and status to stdout
LogiBatteryTray.exe --dump
```

### Local Data Files

Settings and logs are stored in `%LOCALAPPDATA%\LogiBatteryTray`:
- `settings.json` – display mode, hidden devices, polling interval, and alert threshold
- `battery_calibration.json` – rolling ADC calibration state and learned discharge rates
- `battery_log.csv` – timestamped battery and voltage log

## Building from Source

Requires the .NET 10 SDK on Windows 10 or Windows 11.

```powershell
git clone https://github.com/JanLehmann/LogiBatteryTray.git
cd LogiBatteryTray
dotnet build -c Release
```

The compiled executable will be placed in `bin\Release\net10.0-windows\LogiBatteryTray.exe`.

## License

MIT
