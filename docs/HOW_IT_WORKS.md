# How it works

## Device support

The application supports two independent battery sources:

- Mad Catz R.A.T. 9 through the narrowly scoped SmartTechnology HID setup.
- EWEADN V102 Pro through the Dakai-compatible HID collection exposed by its
  2.4 GHz receiver.

Neither source blocks or changes the other.

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

## V102 battery and HID protocol

The 2.4 GHz receiver exposes a vendor HID collection with:

- Vendor ID `0x05AC`.
- Product ID `0x024F`.
- Usage page `0x00C0`, usage `0x0001`.
- Report ID `0`.

The app sends the official 32-byte Dakai device-information command:

```text
55 AA 80 00 00 00 ...
```

The verified response starts `55 BB 80 20`. Its battery fields are:

| Offset | Meaning |
| --- | --- |
| 4 | Charge-state bit field |
| 5 | Battery percentage |

Charge-state decoding matches E HUB:

- Bit 7: external power is connected.
- Bit 6, when bit 7 is set: fully charged.
- Bit 5, when bit 7 is clear: low-battery warning.
- If bit 7 is set and bit 6 is clear: charging.

Malformed headers, error responses, and percentages outside 0–100 are rejected
instead of being displayed as 0%.

No firmware, DFU, bootloader, erase, macro, or key-remapping command is used.

## V102 lighting protocol

V102 lighting is stored in the official keyboard configuration command,
`55 AA 82 11`. Reading it uses `55 AA 81`. The settings are:

- Global effect for USB, profile, and Bluetooth channels.
- Per-effect speed and brightness bytes.
- Separate brightness for static modes.
- Logo-light mode.
- Lighting enable/disable state.

The supported effect values are `0–7` for animated effects and `16–24` for
static colors. Logo values are `0` for synchronized lighting, `1–7` for the
predefined solid colors, and `9` for off.

E HUB does not expose an addressable per-key LED map for this model. The app
therefore does not invent one or send unverified custom matrix commands.

## V102 profiles and games

Profiles are versioned JSON stored under:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-profiles-v1.json
```

Writes use a temporary file and replacement. Invalid profiles are skipped,
and a missing or invalid base profile is repaired to a safe default.

Every two seconds the app snapshots process paths. Matching uses the normalized
full executable path case-insensitively. It does not read game memory, inject
code, hook graphics APIs, or interact with anti-cheat software. If several
configured games run, the foreground process wins. The base profile is restored
after the last configured game exits, and a pending profile is retried after
the keyboard reconnects.

## Battery-swap recovery

The original SmartTechnology HID call can block while the R.A.T. 9 battery is
being removed. When that happens from the tray process, the stale handle can
also prevent the receiver from returning to a normal input state.

The monitor therefore runs vendor access in a separate worker process. The tray
waits at most eight seconds for one response. If the worker is still blocked,
the tray terminates it, which releases every HID handle owned by that process,
and waits 15 seconds before starting a clean worker. A manual **Reconnect mouse**
action performs the same recovery immediately.

The tray menu also offers a battery-swap mode. It stops the worker before the
cell is removed, so the app holds no vendor HID handle during the physical
swap. Finishing the mode delays the next read by ten seconds.

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

The V102 has no dependable published runtime for every effect and brightness
combination. Its estimator therefore starts with no remaining-time number and
only shows a range after at least 20 minutes and three percentage points of
measured discharge have been observed. Charging gaps and implausible rates are
rejected.

## Storage

Settings are stored under:

```text
HKEY_CURRENT_USER\Software\MadCatzRat9BatteryTray
```

Battery samples are stored as a local line-oriented history file:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\history-v1.txt
%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-history-v1.txt
%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-profiles-v1.json
```

No data leaves the machine.
