# How it works

## Driver access

The R.A.T. 9 uses Mad Catz's SmartTechnology HID filter driver rather than a
standard Windows battery collection. The application discovers the programmable
device interface and calls the vendor API in `Saitek.Devices.dll`.

The repository intentionally does not link against or redistribute this
proprietary DLL during compilation. The runtime locates the installed copy and
uses reflection, so the source can still be built and tested in CI without the
driver package.

The expected USB identity is:

- Vendor ID: `0x0738`
- Product ID: `0x1709`

## Battery encoding

The vendor API returns an integer:

| Raw value | Meaning |
| --- | --- |
| `0` | Empty |
| `1–100` | Discharging percentage |
| `101–200` | Charging percentage; displayed value is `raw - 100` |
| `201` | Battery measurement unavailable |
| `< 0` | Read error |

## Battery-swap recovery

The original SmartTechnology HID call can block while the R.A.T. 9 battery is
being removed. When that happens from the tray process, the stale handle can
also prevent the receiver from returning to a normal input state.

The monitor therefore runs vendor access in a separate worker process. The tray
waits at most eight seconds for one response. If the worker is still blocked,
the tray terminates it, which releases every HID handle owned by that process,
and starts a clean worker. A manual **Reconnect mouse** action performs the same
recovery immediately.

## Time estimate

The first estimate is based on the documented maximum of about 9 hours of
continuous gaming. A single point-in-time estimate is deliberately shown as a
range because battery age, polling rate, sensor settings, lighting, sleep
behavior, and workload all affect actual runtime.

The application records local samples every five minutes when the value is
stable, and immediately when the percentage or charging state changes. A
continuous discharge segment can calibrate the estimate after at least:

- 3 percentage points of decline.
- 20 minutes of measured discharge.
- A gap no longer than 6 hours between samples.

The resulting effective full-charge runtime is constrained to a plausible
2–12 hour range. Once at least 10 percentage points and one hour have been
observed, the detail panel also reports estimated capacity relative to the
9-hour specification.

The estimate intentionally resets on charging transitions and does not count
long gaps as reliable active-use data.

## Storage

Settings are stored under:

```text
HKEY_CURRENT_USER\Software\MadCatzRat9BatteryTray
```

Battery samples are stored as a local line-oriented history file:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\history-v1.txt
```

No data leaves the machine.
