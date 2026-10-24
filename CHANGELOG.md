# Changelog

All notable changes to this project are documented here.

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
