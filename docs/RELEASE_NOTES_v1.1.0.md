# v1.1.0 — V102 Pro integration

This release adds the EWEADN V102 Pro Logo Edition to the existing R.A.T. 9
battery tray application.

## Battery

- Live V102 battery percentage and charging state over the 2.4 GHz receiver.
- Firmware, device serial, connection mode, and MAC diagnostics.
- Combined tray icon showing the lowest connected device battery.
- Learned V102 remaining-time ranges from measured discharge only.
- Local V102 battery history.

## Lighting and games

- All verified animated effects and static palette colors.
- Brightness, speed, breathing/flow color, and logo-light controls.
- Atomic local lighting profiles.
- Automatic game-profile activation using full executable paths.
- Foreground priority when several configured games run.
- Base-profile restoration and reconnect retry.

## Hardware limitation

The official Dakai HID protocol for V102 Logo controls global lighting zones.
It does not expose an addressable per-key RGB matrix. This release implements
the complete verified control surface and does not send guessed custom-matrix
commands.

## Diagnostics

Run `V102BatteryProbe.exe` to verify:

- HID identity and report sizes.
- Battery and charging state.
- Firmware, driver serial, VID/PID, and MAC.
- Current lighting configuration.

The automated release build runs nine deterministic tests before producing
the ZIP.
