# Troubleshooting

## The tray icon is hidden

Open **Settings → Personalization → Taskbar → Other system tray icons** and
enable `RAT9BatteryTray.exe`. On some Windows 11 builds it can also be dragged
from the overflow menu onto the taskbar.

## “Driver not found” or “Cannot read battery”

Check that:

1. Mad Catz SmartTechnology is installed.
2. `C:\Program Files\SmartTechnology\Software\Saitek.Devices.dll` exists.
3. Device Manager shows `Mad Catz R.A.T.9 Mouse (HID)` without an error.
4. The receiver is connected and the mouse is paired.

Run the diagnostic program:

```powershell
.\RAT9BatteryProbe.exe
```

A healthy result looks like:

```text
R.A.T. 9: 62%
Raw=62; Status=Ok
Tid kvar: ca 4 t 20 min – 6 t 50 min kvar
```

## “No battery data”

The original SmartTechnology control uses value `201` while the mouse is
docked or asleep. Move the mouse to wake it; the monitor will retry
automatically.

## Mouse movement works poorly after changing the battery

R.A.T. 9's wireless receiver can keep a stale HID state after a hot battery
swap. Version 1.0.1 isolates battery-driver calls in a worker process. If a
read blocks for more than eight seconds, the worker is terminated, its HID
handle is released, and version 1.0.2 waits 15 seconds before opening a fresh
reader.

For scheduled battery changes, right-click the tray icon and choose
**Battery swap: pause reader** before opening the mouse. Use the same menu entry
to finish. The monitor waits ten seconds before reading the new battery.

If the pointer is still unreliable, right-click the tray icon and choose
**Reconnect mouse**. As a final fallback, switch the mouse off, reseat the
battery, and switch it back on. If Windows already holds a stale receiver
connection, unplug the small receiver for five seconds and reconnect it.

### No-reboot administrator recovery

For a receiver that stays stuck even after reconnecting it:

1. Run `Install-RAT9-Recovery.cmd` once from the application folder.
2. Approve the administrator prompt.
3. After that, use **Reset mouse connection (admin)** from the tray menu.

The helper resets only the `VID_0738&PID_1709` receiver and the
`_hid_0738_1709` filter driver, then restarts SmartTechnology and the tray app.
It does not restart Windows or terminate games.

## V102 Pro is not detected

Check that the 2.4 GHz dongle is connected and run:

```powershell
.\V102BatteryProbe.exe
```

Windows should expose the vendor HID identity `05AC:024F` with usage page
`0x00C0`, usage `0x0001`, and report ID `0`. If the probe reports that the
device was not found, unplug the dongle for five seconds and reconnect it.

The Bluetooth channel is not used for configuration or lighting. Use the
2.4 GHz dongle or USB-C for V102 lighting writes.

## V102 lighting does not change

Close E HUB while the tray application controls the keyboard. Then:

1. Right-click the tray icon.
2. Open **V102 RGB och spelprofiler**.
3. Select the profile and choose **Läs från tangentbord**.
4. Change one setting and choose **Spara och applicera**.

The official V102 protocol exposes global lighting zones and predefined
palette colors. Independent colors for every key are not available from this
model's protocol.

If a configured game profile does not activate, confirm that the stored path
points to the exact `.exe` file, not only the launcher or a shortcut.

## Clear the history

Exit the application, then delete:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\history-v1.txt
%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-history-v1.txt
```

The next estimate will start from the 9-hour specification again.

## Application error

Local diagnostics are written to:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\error.log
```

Remove any personal paths before attaching the log to an issue.
