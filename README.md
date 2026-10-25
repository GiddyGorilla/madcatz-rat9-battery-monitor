<p align="center">
  <img src="assets/app-icon.png" width="112" alt="Battery monitor icon">
</p>

<h1 align="center">R.A.T. 9 + V102 Pro Battery Monitor</h1>

<p align="center">
  A local-first Windows tray monitor for the Mad Catz R.A.T. 9 and EWEADN V102 Pro.
</p>

<p align="center">
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?logo=windows">
  <img alt=".NET Framework 4.8" src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet">
  <img alt="No telemetry" src="https://img.shields.io/badge/telemetry-none-success">
  <img alt="MIT License" src="https://img.shields.io/github/license/GiddyGorilla/madcatz-rat9-battery-monitor">
</p>

<p align="center">
  <a href="https://github.com/GiddyGorilla/madcatz-rat9-battery-monitor/releases/latest"><strong>Download the latest release</strong></a>
</p>

The app shows the lowest connected battery directly in the Windows
notification area. It reads the R.A.T. 9 through the minimal Mad Catz HID
setup and the V102 Pro through its receiver's standard HID interface.

## Features

- Live battery percentage for both devices in one tray icon and popup.
- Charging and full-battery state for the V102 Pro.
- Learned remaining-time ranges based on measured discharge.
- Local 24-hour battery history for both devices.
- R.A.T. 9 battery-swap and receiver-recovery safeguards from v1.0.x.
- V102 lighting profiles with all effects exposed by the official protocol:
  breathing, spectrum, runner, tide, flowing color, ripple, three-color,
  collision, and static color modes.
- Independent logo-light mode, brightness, speed, and lighting on/off.
- Locally saved base profiles and game profiles.
- Automatic profile activation when a configured `.exe` starts.
- Foreground-game priority when several configured games are open.
- Automatic base-profile restoration when the last configured game exits.
- Pending profile reapplication after the keyboard reconnects.

Everything runs locally. No account, analytics, telemetry, cloud service, or
vendor desktop application is required for V102 control.

## Important V102 limitation

The official V102 HID protocol controls global lighting zones, not an
addressable per-key LED matrix. E HUB therefore exposes effects, palette
colors, brightness, speed, and the logo light. This app implements those
verified controls and always shows their predefined V102 color palettes.
Arbitrary RGB values per individual key are not exposed by the hardware
protocol and are intentionally not guessed.

## Quick start

### Requirements

1. Windows 10 or Windows 11.
2. .NET Framework 4.8.
3. For the R.A.T. 9: a working Mad Catz HID/SmartTechnology setup.
4. For the V102 Pro: the 2.4 GHz dongle in a USB port.

### Run

1. Download the newest ZIP from
   [Releases](https://github.com/GiddyGorilla/madcatz-rat9-battery-monitor/releases/latest).
2. Extract it to a permanent folder.
3. Run `RAT9BatteryTray.exe`.
4. Left-click the tray percentage to open the combined status panel.
5. Right-click and choose **V102 RGB och spelprofiler** to create profiles.

Windows may initially hide the icon under the notification-area chevron.
Enable it under **Settings → Personalization → Taskbar → Other system tray
icons**.

## Profile behavior

Each profile stores a lighting effect, brightness, speed, logo mode, and an
optional list of full executable paths. Profiles are stored at:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-profiles-v1.json
```

The app scans running processes every two seconds without injecting into them
or reading their memory. If multiple configured games are running, the game
that owns the foreground window wins. When the last game closes, the selected
base profile is restored.

## Battery behavior

The R.A.T. 9 vendor API returns values from 1 to 200:

- `1–100`: discharging.
- `101–200`: charging; subtract 100 for the level.
- `201`: no current measurement.

For the R.A.T. 9, the initial estimate uses Mad Catz's approximate 9-hour
maximum and adapts to measured discharge. The V102 estimate is learned from
real discharge only; the app does not fabricate an initial runtime.

## Safety

- No firmware, DFU, bootloader, erase, macro, or key-remapping commands are
  sent by this project.
- No process injection, DLL loading, memory access, or anti-cheat hooks are
  used.
- V102 transport is limited to device information, battery status, reading
  settings, and verified lighting configuration.
- R.A.T. 9 recovery remains narrowly scoped to `VID_0738&PID_1709`.

## Compatibility

| Item | Supported |
| --- | --- |
| Mad Catz R.A.T. 9 | Yes |
| EWEADN V102 Pro Logo Edition, 2.4 GHz | Verified |
| Windows 10 / 11, x64 | Yes |
| V102 over USB-C | Protocol supported |
| V102 addressable per-key RGB | Not exposed by the official HID protocol |
| Other EWEADN keyboards | Not verified |

## Build from source

Visual Studio Build Tools with the Roslyn C# compiler is required. The build
also runs the deterministic protocol/profile tests.

```powershell
pwsh -ExecutionPolicy Bypass -File .\build.ps1
```

Outputs in `dist/`:

- `RAT9BatteryTray.exe` — combined tray application.
- `RAT9BatteryProbe.exe` — R.A.T. 9 diagnostic.
- `V102BatteryProbe.exe` — V102 battery and identity diagnostic.
- `MadCatz-RAT9-Battery-Monitor-v1.1.0-win.zip` — release bundle.
- `SHA256SUMS.txt` — checksums.

## Privacy

- No network requests from the application.
- No telemetry or crash upload.
- R.A.T. 9 history:
  `%LOCALAPPDATA%\MadCatzRat9BatteryTray\history-v1.txt`
- V102 history:
  `%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-history-v1.txt`
- V102 profiles:
  `%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-profiles-v1.json`
- Profiles contain executable paths and lighting settings only.

## Contributing

Bug reports and compatibility findings are welcome. Include the Windows
version and the relevant diagnostic output. See
[CONTRIBUTING.md](CONTRIBUTING.md) and
[Troubleshooting](docs/TROUBLESHOOTING.md).

## License and trademarks

The source code is available under the [MIT License](LICENSE).

Mad Catz, R.A.T., SmartTechnology, EWEADN, and V102 are trademarks of their
respective owners. This is an independent community project and is not
affiliated with or endorsed by those owners.
