# V102 Pro Game RGB Integration

Status: Implemented on 2026-10-25 after verification against the physical V102
Pro and its current E HUB protocol module.

Hardware-verification amendment: E HUB's `sdk-kbd-dakai` module controls global
V102 lighting zones and effects. It does not expose an addressable per-key LED
matrix for this model. The implementation therefore provides verified global
lighting profiles and game switching instead of inventing unverified per-key
commands.

## Summary

Extend the existing Windows tray application to support both the Mad Catz
R.A.T. 9 and the EWEADN V102 Pro Logo Edition keyboard.

The keyboard integration provides:

- Live battery percentage and charging state over the 2.4 GHz receiver.
- A learned runtime estimate based on measured discharge.
- A local lighting editor for the global effects and colors exposed by V102.
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
- The 20-byte matrix structures in E HUB are key-remapping data, not LED data.
- Logo light: supported.

Bluetooth is not supported by EWEADN for keyboard configuration and RGB.
Bluetooth may be used for battery display only if Windows exposes a standard
battery service; this is not required for the first release.

The R.A.T. 9 integration remains independent and continues to use only
`_hid_0738_1709.sys` plus the local `Saitek.Devices.dll`.

## Goals

1. Show battery and charging state for both devices in one application.
2. Let the user choose every lighting effect, palette color, brightness, and
   speed exposed by the official V102 protocol.
3. Save lighting configurations as named local profiles.
4. Bind one or more executable paths to each game profile.
5. Apply a game's profile when its process starts.
6. Restore the base profile when no configured game remains active.
7. Keep the application safe around game processes and device firmware.

## Non-goals

The first release does not include:

- Per-key addressable RGB, because the official V102 HID protocol does not
  expose it for this model.
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

### Lighting editor

The lighting window exposes only capabilities present in the verified Dakai
HID protocol:

- Animated effects `0–7`.
- Static palette colors `16–24`.
- Brightness level `1–5`.
- Speed level `0–4`.
- Logo synchronized, solid red/green/blue/yellow/cyan/purple/white, or off.
- Lighting on/off.
- Separate breath and flowing-color palette selectors where supported.

The user can read the current keyboard settings, create, rename, delete, save,
and apply local profiles. The selected base profile is marked explicitly.


### Game profiles

Each profile contains:

- Profile name.
- One or more selected executable paths.
- Global lighting effect and palette color.
- Lighting speed.
- Logo-light mode.
- Brightness.

The user selects a game executable through a standard Windows file picker.
The full normalized path is stored, not only the process filename.

Behavior:

1. The app scans running processes at startup and every two seconds.
2. A configured game becomes active when its executable process is running.
3. If several configured games run simultaneously, the game owning the
   foreground window takes priority.
4. The profile is sent on the next two-second scan.
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

ILightingDevice
    SupportsLighting
    ReadKeyboardConfig()
    ApplyKeyboardConfig()
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
- Reading current keyboard lighting.
- Applying the selected effect and palette color.
- Applying lighting brightness and speed.
- Applying the logo-light mode.

### V102 battery protocol

The physical V102 uses the Dakai `sdk-kbd-dakai` protocol, not the separate
`beiying` command set. Device information and battery state come from a
32-byte HID command beginning `55 AA 80`. The verified response begins
`55 BB 80 20`; byte 4 is the charge-state bit field and byte 5 is the
percentage. The adapter validates:

- Response marker and command identity.
- Expected response length and status.
- Level range of 0 through 100.
- The identity reports V102 model serial `257`.

A malformed or unexpected response is treated as an unavailable measurement,
not as 0 percent.

### V102 lighting protocol

Reading settings uses `55 AA 81`. Applying settings uses `55 AA 82 11`,
followed by the existing keyboard configuration with only lighting fields
changed. Sleep, idle, operating-system, and Windows-key settings are preserved.

The 2.4 GHz effect field is one of:

- `0–7` for animated effects.
- `16–24` for static palette colors.

The nine effect brightness/speed bytes use `(speed << 4) | brightness` at
`effect + 1`. Static modes use the brightness nibble in byte 0. Logo modes and
the two breath/flow palette selectors are stored in their verified
configuration bytes.

The protocol has no addressable per-key LED matrix for this model. Write
operations are serialized with battery reads. A failed write remains visibly
failed and is not retried in an unbounded loop.

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
  "baseProfileId": "base",
  "profiles": [
    {
      "id": "game-a",
      "name": "Game A",
      "executables": [
        "D:\\Games\\GameA\\GameA.exe"
      ],
      "enabled": true,
      "effectMode": 16,
      "brightness": 4,
      "speed": 3,
      "logoMode": 9,
      "breathColor": 2,
      "waveColor": 5
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
- If lighting programming fails, the app reports the error and retries only
  after reconnecting or after a bounded backoff.
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
- Device-information and battery responses are validated.
- Lighting configuration preserves non-lighting fields and round-trips.
- Runtime estimator rejects charging gaps and implausible rates.
- Single-instance behavior remains intact.

Physical integration checks:

- Windows detects `1452:591` through the 2.4 GHz receiver.
- Battery level matches E HUB's value on the same device.
- Charging status changes when USB-C is connected.
- Every published animated effect and static palette color can be selected.
- Logo lighting can be changed independently.
- A layout survives app restart and reconnects correctly.
- Launching a configured executable applies its profile within three seconds.
- Closing it restores the base profile.
- Disconnecting and reconnecting during a game reapplies its profile.
- R.A.T. 9 battery monitoring continues while V102 RGB is active.

## Delivery phases

1. Protocol and battery support.
2. Unified device UI and battery history.
3. Verified global lighting profiles and local persistence.
4. Game process profiles and automatic restoration.

Each phase must work on the physical V102 before the next phase is considered
complete.
