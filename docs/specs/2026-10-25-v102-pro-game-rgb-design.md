# V102 Pro Game RGB Integration

Status: Design approved in conversation on 2026-10-25. Awaiting spec review.

## Summary

Extend the existing Windows tray application to support both the Mad Catz
R.A.T. 9 and the EWEADN V102 Pro Logo Edition keyboard.

The keyboard integration provides:

- Live battery percentage and charging state over the 2.4 GHz receiver.
- A learned runtime estimate based on measured discharge.
- A local per-key RGB editor.
- Saved keyboard profiles.
- Automatic profile activation when configured game processes start.
- Automatic restoration of a base profile when games exit.

The integration talks directly to the keyboard's official HID protocol. It
does not inject code into games, alter firmware, or require EWEADN's desktop
software or a cloud account.

## Device

Target keyboard:

- Model: EWEADN V102 Pro Logo Edition.
- Battery: 5000 mAh.
- Connection for RGB and configuration: 2.4 GHz receiver or USB-C.
- Receiver identity observed on the host:
  - Vendor ID: `1452` (`0x05AC`)
  - Product ID: `591` (`0x024F`)
  - Receiver serial family: `DONGLE_250530V206C`
- E HUB protocol provider: `dakai-hid`
- V102 Logo model ID: `257`
- Matrix data length: 20 bytes.
- Matrix key count: 19.
- Logo light: supported.

Bluetooth is not supported by EWEADN for keyboard configuration and RGB.
Bluetooth may be used for battery display only if Windows exposes a standard
battery service; this is not required for the first release.

The R.A.T. 9 integration remains independent and continues to use only
`_hid_0738_1709.sys` plus the local `Saitek.Devices.dll`.

## Goals

1. Show battery and charging state for both devices in one application.
2. Let the user assign an independent RGB color to every V102 key.
3. Save RGB configurations as named local profiles.
4. Bind one or more executable paths to each game profile.
5. Apply a game's profile when its process starts.
6. Restore the base profile when no configured game remains active.
7. Keep the application safe around game processes and device firmware.

## Non-goals

The first release does not include:

- Per-frame animated RGB authored inside this application.
- Macros, key remapping, or keyboard firmware updates.
- Cloud synchronization or an EWEADN account.
- RGB control over Bluetooth.
- Support for other EWEADN keyboard models.
- Process injection, DLL loading, or anti-cheat hooks.
- Editing the keyboard's LCD on the non-display Logo Edition; this model
  exposes a logo light rather than the LCD variant.

## User Experience

### Tray behavior

The application uses one tray icon. It displays the lowest battery percentage
among connected supported devices. Its tooltip lists each connected device,
for example:

```text
R.A.T. 9: 62% · V102 Pro: 78%
```

Left-clicking the icon opens a tabbed details window:

- Overview
- R.A.T. 9
- V102 Pro
- RGB
- Game profiles
- Settings

The existing R.A.T. 9 behavior remains available. The details window still
closes when the user clicks outside unless the user pins it.

### V102 status

The V102 tab shows:

- Battery percentage.
- Charging, discharging, or full state.
- Connection mode.
- Last successful update time.
- Remaining-time range once sufficient history exists.
- A shortcut to the RGB tab.

No fabricated runtime is displayed before calibration. Until enough discharge
data exists, the app shows `samlar mätdata`.

### RGB editor

The RGB tab contains a 99-key visual representation of the V102 Pro. Clicking
or dragging across keys selects them, and the selector applies a color to all
selected keys. The logo light is selectable as a separate element.

Controls:

- Color picker with hexadecimal input.
- Brightness from 0 to 100 percent.
- Select all, clear selection, copy, paste, and undo.
- Reset to black.
- Save, duplicate, rename, and delete profile.
- Apply changes immediately to the keyboard.

The editor maintains a local draft. Moving away does not silently discard
unsaved changes; the app asks whether to save, apply once, or discard.

### Game profiles

Each profile contains:

- Profile name.
- One or more selected executable paths.
- Per-key RGB colors.
- Logo color.
- Brightness.

The user selects a game executable through a standard Windows file picker.
The full normalized path is stored, not only the process filename.

Behavior:

1. The app scans running processes at startup and every two seconds.
2. A configured game becomes active when its executable process is running.
3. If several configured games run simultaneously, the game owning the
   foreground window takes priority.
4. The profile is sent after a one-second settle delay.
5. If the keyboard is disconnected, the app remembers the pending profile and
   applies it after reconnection while the game remains active.
6. When the last configured game exits, the selected base profile is restored.
7. Reapplying the same active profile is skipped unless the keyboard reports
   that it reconnected or the device state changed.

The default base profile is user-selectable. If no base profile is configured,
the app restores the profile that was active before the first configured game
started.

## Architecture

The application remains a single .NET Framework 4.8 WinForms tray program.

### Device abstraction

Introduce a device coordinator with independent adapters:

```text
BatteryReading
    DeviceId
    DisplayName
    Percentage
    IsCharging
    IsConnected
    MeasuredAt
    Error

IDeviceBatterySource
    ReadBattery()

IRgbDevice
    SupportsRgb
    ReadLayout()
    ApplyLayout()
```

Adapters:

- `Rat9BatterySource`: wraps the existing isolated worker.
- `V102HidDevice`: discovers and communicates with the V102 receiver.

### V102 HID transport

The V102 adapter enumerates HID collections for:

- Vendor ID `0x05AC`
- Product ID `0x024F`
- Product name containing `2.4G`
- E HUB `dakai-hid` protocol collections

The transport uses the same standard Windows HID feature/output/input report
mechanisms used by E HUB Web. It does not require a third-party kernel driver.

Protocol operations are limited to:

- Device information and model detection.
- Battery status.
- Charging status.
- Reading current RGB.
- Applying per-key RGB.
- Applying logo RGB.
- Applying brightness.

### V102 battery protocol

The official E HUB V102 adapter obtains battery state with command `0x87`
(`135`). The response includes battery level and a separate charge-status
value. The adapter validates:

- Response command identity.
- Expected response length.
- Level range of 0 through 100.
- Device model remains V102 Logo.

A malformed or unexpected response is treated as an unavailable measurement,
not as 0 percent.

### V102 RGB protocol

The V102 Logo adapter uses the official 20-byte matrix format and the key map
published by E HUB for keyboard type `v102`.

The UI maps 99 logical keys to protocol key IDs. The transport converts the
logical map to the V102 matrix format and writes it in bounded report chunks.
The logo light is a separate logical element and is sent together with the
main lighting update.

Write operations are serialized. Only one RGB transaction may be active at a
time. A failed transaction does not retry indefinitely; the app reports the
error and allows the user to retry.

### Runtime estimate

The app records local battery samples for the V102 with:

- UTC timestamp.
- Percentage.
- Charging state.
- Connection mode.

Runtime estimation follows the same principles as the R.A.T. 9 estimator:

- Ignore intervals containing charging transitions.
- Require meaningful decline over a minimum elapsed period.
- Reject implausible rates rather than showing a precise but false countdown.
- Present a range and confidence state.

Because EWEADN does not publish a dependable runtime for arbitrary RGB
settings, the app does not seed the keyboard estimate with a fixed number of
hours.

### Process monitoring

`GameProfileMonitor` uses a two-second process scan. It does not use process
injection, window hooks, DLL loading, or anti-cheat interfaces.

The monitor compares normalized executable paths case-insensitively. It also
queries the foreground process so simultaneous games have deterministic
priority.

The monitor is suspended while the app is exiting and does not apply profiles
after settings have been locked.

## Persistence

All keyboard settings use a versioned JSON document:

```text
%LOCALAPPDATA%\MadCatzRat9BatteryTray\v102-profiles-v1.json
```

Schema version 1:

```json
{
  "version": 1,
  "baseProfileId": "default",
  "profiles": [
    {
      "id": "game-a",
      "name": "Game A",
      "executables": [
        "D:\\Games\\GameA\\GameA.exe"
      ],
      "keyColors": {
        "ESC": "#ff0000",
        "W": "#00ff00"
      },
      "logoColor": "#0000ff",
      "brightness": 70
    }
  ]
}
```

Writes use a temporary file plus replacement so an interrupted write cannot
leave a partially written profile file.

Unknown JSON fields are ignored. Invalid profiles are skipped and reported;
they do not prevent the application from starting.

## Failure handling

- If the V102 dongle is missing, the keyboard tab shows disconnected.
- If another process has exclusive access, the app displays an access error.
- If RGB programming fails, existing keyboard lighting remains unchanged
  where the protocol supports transactional updates.
- If a game profile references a missing executable, the profile remains
  stored and the missing path is visibly marked.
- If the app starts with a configured game already running, it applies the
  matching profile after device initialization.
- If the keyboard reconnects during a game, the active game profile is
  re-applied.
- The app never sends DFU, firmware, bootloader, or erase commands.

## Security and privacy

- No network requests are required for battery or RGB.
- No EWEADN account is used.
- No game process memory is read or modified.
- No game is injected or instrumented.
- Stored data contains executable paths and lighting profiles only.
- Diagnostics contain protocol status codes, not captured keystrokes.

## Verification

Automated checks:

- Profile JSON round-trip and corruption handling.
- Process priority selection and base-profile restoration with a fake monitor.
- Battery response validation and invalid-response handling.
- RGB logical key map maps every supported key exactly once.
- RGB report chunking preserves complete transaction boundaries.
- Runtime estimator rejects charging gaps and implausible rates.
- Single-instance behavior remains intact.

Physical integration checks:

- Windows detects `1452:591` through the 2.4 GHz receiver.
- Battery level matches E HUB's value on the same device.
- Charging status changes when USB-C is connected.
- Each key can be assigned distinctly and visibly.
- Logo lighting can be changed independently.
- A layout survives app restart and reconnects correctly.
- Launching a configured executable applies its profile within three seconds.
- Closing it restores the base profile.
- Disconnecting and reconnecting during a game reapplies its profile.
- R.A.T. 9 battery monitoring continues while V102 RGB is active.

## Delivery phases

1. Protocol and battery support.
2. Unified device UI and battery history.
3. Per-key RGB read/write and local profiles.
4. Game process profiles and automatic restoration.

Each phase must work on the physical V102 before the next phase is considered
complete.
