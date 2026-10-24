# Mad Catz R.A.T. 9 Battery Monitor v1.0.1

This update fixes unreliable mouse input after a R.A.T. 9 battery swap.

## Fix

The aging Mad Catz driver can block while the battery is removed. Previously,
that call ran inside the tray process and could keep a stale HID handle open,
leaving the mouse receiver in a state where movement or clicks behaved badly.

Battery access now runs in an isolated worker process. If one read takes longer
than eight seconds, the tray terminates and restarts that worker, releasing its
HID handles and allowing the receiver to recover automatically.

## Also included

- A manual **Reconnect mouse** action in the tray menu.
- Worker crash detection and automatic restart.
- Fresh diagnostic and release binaries with updated 1.0.1 version metadata.

## Requirements

- Windows 10 or Windows 11.
- .NET Framework 4.8.
- Mad Catz SmartTechnology 7.x with the R.A.T. 9 receiver paired.

No telemetry, cloud account, or proprietary Mad Catz DLL is included.
