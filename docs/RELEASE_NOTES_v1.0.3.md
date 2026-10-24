# Mad Catz R.A.T. 9 Battery Monitor v1.0.3

This release adds an optional no-reboot recovery helper for receivers that
remain stuck after a battery change.

## One-time setup

1. Run `Install-RAT9-Recovery.cmd` from the release folder.
2. Approve the Windows administrator prompt once.
3. The tray app can now trigger a targeted driver and device reset without
   another prompt.

The helper is copied to `C:\Program Files\MadCatz RAT9 Battery Monitor` and a
single on-demand task named `MadCatzRat9Recovery` is created.

## Recovery sequence

- Stop the battery tray and SmartTechnology user processes.
- Disable the R.A.T. 9 USB receiver.
- Stop and reload the Mad Catz `_hid_0738_1709` filter driver.
- Re-enable the receiver and rescan devices.
- Restart SmartTechnology and the battery monitor.

The sequence resets only devices matching `VID_0738&PID_1709`. It does not
restart Windows.

## Automatic use

After installation the tray app runs the recovery task automatically if a
battery read stalls. The manual action **Reset mouse connection (admin)** uses
the same task.

## Requirements

- Windows 10 or Windows 11.
- One administrator approval for installation.
- Mad Catz SmartTechnology 7.x and the R.A.T. 9 receiver.

The project remains local-only with no telemetry or bundled proprietary driver.
