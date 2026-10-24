# Mad Catz R.A.T. 9 Battery Monitor v1.0.2

This update improves recovery for the R.A.T. 9's receiver during battery swaps.

## What changed

- The automatic recovery cooldown is now 15 seconds. Opening a new HID handle
  immediately after a stalled battery read was too aggressive for the wireless
  receiver.
- Added a dedicated **Battery swap: pause reader** tray action. Use it before
  removing the battery and again after the new battery is fitted.
- The battery-swap mode releases the vendor HID handle before the physical swap
  and waits ten seconds before resuming measurements.
- Pending worker restarts are cancelled when entering swap mode or using the
  reconnect action.

## Recovery guidance

If the receiver is already stuck from an earlier hot swap, switch the mouse off
and on first. If Windows still does not receive button input, unplug the small
USB receiver for five seconds and reconnect it. Windows requires administrator
access to restart a stuck PnP device programmatically.

The application remains local-only and includes no telemetry or proprietary
Mad Catz binaries.
