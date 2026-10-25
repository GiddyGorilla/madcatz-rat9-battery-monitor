using System;
using Rat9Battery.V102;

namespace Rat9Battery
{
    internal sealed class V102BatterySource :
        IDeviceBatterySource,
        IDisposable
    {
        private readonly object _sync = new object();
        private V102HidDevice _device;
        private bool _disposed;

        public static DeviceIdentity Identity
        {
            get
            {
                return new DeviceIdentity(
                    "v102-pro-logo",
                    "EWEADN V102 Pro",
                    "2,4 GHz");
            }
        }

        public DeviceBatteryReading ReadBattery()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return ErrorReading(
                        "Batteriläsaren har stängts.");
                }

                try
                {
                    if (_device == null)
                    {
                        _device = V102HidDevice.Open();
                    }

                    V102BatteryStatus status =
                        _device.QueryBattery();
                    DeviceBatteryState state =
                        status.IsCharging
                            ? DeviceBatteryState.Charging
                            : status.IsFull
                                ? DeviceBatteryState.Full
                                : DeviceBatteryState.Available;
                    return new DeviceBatteryReading
                    {
                        Device = Identity,
                        ConnectionState =
                            DeviceConnectionState.Connected,
                        BatteryState = state,
                        Percentage = status.Level,
                        IsCharging = status.IsCharging,
                        IsFull = status.IsFull,
                        MeasuredAtUtc = DateTime.UtcNow
                    };
                }
                catch (Exception exception)
                {
                    DisposeDevice();
                    DeviceBatteryReading reading =
                        ErrorReading(GetRootMessage(exception));
                    reading.ConnectionState =
                        IsNotFoundMessage(exception)
                            ? DeviceConnectionState.Disconnected
                            : DeviceConnectionState.Error;
                    return reading;
                }
            }
        }

        public V102KeyboardConfig ReadKeyboardConfig()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(
                        "V102BatterySource");
                }

                try
                {
                    if (_device == null)
                    {
                        _device = V102HidDevice.Open();
                    }

                    return _device.ReadKeyboardConfig();
                }
                catch
                {
                    DisposeDevice();
                    throw;
                }
            }
        }

        public void ApplyLightingProfile(
            V102LightingProfile profile)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(
                        "V102BatterySource");
                }

                try
                {
                    if (_device == null)
                    {
                        _device = V102HidDevice.Open();
                    }

                    V102KeyboardConfig current =
                        _device.ReadKeyboardConfig();
                    V102KeyboardConfig updated =
                        V102LightingService.Merge(
                            current,
                            profile);
                    _device.ApplyKeyboardConfig(updated);
                }
                catch
                {
                    DisposeDevice();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                DisposeDevice();
            }
        }

        private static DeviceBatteryReading ErrorReading(
            string message)
        {
            return new DeviceBatteryReading
            {
                Device = Identity,
                ConnectionState = DeviceConnectionState.Error,
                BatteryState = DeviceBatteryState.Error,
                MeasuredAtUtc = DateTime.UtcNow,
                Error = message
            };
        }

        private static bool IsNotFoundMessage(
            Exception exception)
        {
            return GetRootMessage(exception).IndexOf(
                "hittades inte",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetRootMessage(
            Exception exception)
        {
            Exception root = exception;
            while (root.InnerException != null)
            {
                root = root.InnerException;
            }

            return string.IsNullOrWhiteSpace(root.Message)
                ? "okänt V102-fel"
                : root.Message;
        }

        private void DisposeDevice()
        {
            if (_device == null)
            {
                return;
            }

            try
            {
                _device.Dispose();
            }
            catch
            {
                // The next read will open a fresh HID handle.
            }

            _device = null;
        }
    }
}
