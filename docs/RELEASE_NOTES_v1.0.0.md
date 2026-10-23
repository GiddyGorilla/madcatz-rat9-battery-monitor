# Mad Catz R.A.T. 9 Battery Monitor v1.0.0

The first public release of a local-first battery monitor for the wireless
Mad Catz R.A.T. 9.

## Highlights

- Live battery percentage directly in the Windows system tray.
- Click-to-open details with remaining-time range and battery history.
- Runtime estimates that adapt to measured discharge over time.
- Alerts at 20%, 10%, and 5%, on full charge, and on disconnect/reconnect.
- Optional desktop pinning and automatic startup with Windows.
- No telemetry, cloud account, or proprietary driver redistribution.

## Requirements

- Windows 10 or Windows 11.
- .NET Framework 4.8.
- Mad Catz SmartTechnology 7.x with the R.A.T. 9 receiver paired.

## Installation

1. Download and extract the Windows ZIP below.
2. Run `RAT9BatteryTray.exe`.
3. Left-click the tray icon for details.

The ZIP includes the application, diagnostic probe, quick-start guide, license,
and README. SHA-256 checksums are attached to the release.

## Verification

The project builds with GitHub Actions using the open-source source tree only.
The installed SmartTechnology assembly is loaded at runtime and is not
distributed with this release.

## Known limitations

- Only the Mad Catz R.A.T. 9 is supported and tested.
- Runtime is initially approximate and improves after real discharge data is
  collected.
- Windows may place a new tray icon in the overflow menu until the user enables
  it under Taskbar settings.
