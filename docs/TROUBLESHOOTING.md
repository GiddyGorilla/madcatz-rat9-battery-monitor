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

## Clear the history

Exit the application, then delete:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\history-v1.txt
```

The next estimate will start from the 9-hour specification again.

## Application error

Local diagnostics are written to:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\error.log
```

Remove any personal paths before attaching the log to an issue.
