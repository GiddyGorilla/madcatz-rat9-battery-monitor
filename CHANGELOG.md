# Changelog

All notable changes to this project are documented here.

## 1.1.0 — 2026-10-25

### Added

- EWEADN V102 Pro battery, charging state, firmware, serial, mode, and MAC
  reporting over its native HID protocol.
- Combined tray state that displays the lowest connected device battery.
- V102 24-hour history and learned remaining-time estimates.
- V102 lighting profiles using the effects, palette colors, brightness, speed,
  and logo modes exposed by the official protocol.
- Local named profiles with optional executable paths.
- Automatic game-profile activation with foreground-game priority, base-profile
  restoration, and reconnect retry.
- Standalone V102 hardware diagnostic.
- Deterministic protocol, estimator, profile, and game-priority tests.

### Changed

- Raised the release number to 1.1.0 and expanded the project documentation
  for both supported devices.
- Battery detail panel now shows R.A.T. 9 and V102 Pro together.

### Notes

- V102's official protocol does not expose an addressable per-key RGB matrix.
  The app implements the verified global lighting controls instead.
- V102 runtime is never seeded with a fabricated full-charge duration.

## 1.0.3 — 2026-10-24

### Added

- Optional administrator-assisted recovery tool for a receiver that remains
  stuck after a battery swap.
- One-time installer for a narrowly scoped on-demand scheduled task.
- Automatic driver/device reset when an installed recovery task is available.
- Manual **Reset mouse connection (admin)** tray action.

### Changed

- Recovery resets only hardware matching `VID_0738&PID_1709`.
- The installed task receives no arguments and starts no network access.

## 1.0.2 — 2026-10-24

### Fixed

- Delay automatic worker recovery for 15 seconds after a blocked swap read,
  giving the wireless receiver time to reinitialize before the HID handle is
  opened again.
- Added an explicit **Battery swap: pause reader** tray action. It releases the
  HID handle before the battery is removed and waits ten seconds before reading
  again after the swap.
- Cancel stale scheduled restarts when the user starts a battery swap or
  manually reconnects.

## 1.0.1 — 2026-10-24

### Fixed

- Isolated battery-driver access in a supervised worker process.
- Automatically terminate and restart the worker if a vendor read hangs for
  more than eight seconds, such as during a R.A.T. 9 battery swap.
- Release the stale driver handle without requiring a tray or Windows restart.
- Added a manual **Reconnect mouse** action to the tray menu.

## 1.0.0 — 2026-10-24

### Added

- Live battery percentage in the Windows system tray.
- Remaining-time range based on a 9-hour reference and measured discharge.
- Persistent local battery history and 24-hour chart.
- Estimated full runtime and capacity after calibration.
- Low-battery, full-charge, disconnect, and reconnect notifications.
- Click-to-open detail panel with automatic dismissal.
- Optional desktop pinning and Windows autostart.
- Portable release bundle and diagnostic command-line probe.
- GitHub Actions build workflow.
