using System;

namespace Rat9Battery
{
    internal enum DeviceConnectionState
    {
        Connected,
        Disconnected,
        Error
    }

    internal enum DeviceBatteryState
    {
        Available,
        Charging,
        Full,
        Unavailable,
        Error
    }

    internal sealed class DeviceIdentity
    {
        public DeviceIdentity(
            string id,
            string displayName,
            string connectionMode)
        {
            Id = id;
            DisplayName = displayName;
            ConnectionMode = connectionMode;
        }

        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string ConnectionMode { get; private set; }
    }

    internal sealed class DeviceBatteryReading
    {
        public DeviceIdentity Device { get; set; }
        public DeviceConnectionState ConnectionState { get; set; }
        public DeviceBatteryState BatteryState { get; set; }
        public int? Percentage { get; set; }
        public bool IsCharging { get; set; }
        public bool IsFull { get; set; }
        public DateTime MeasuredAtUtc { get; set; }
        public string Error { get; set; }

        public bool HasPercentage
        {
            get { return Percentage.HasValue; }
        }
    }

    internal interface IDeviceBatterySource
    {
        DeviceBatteryReading ReadBattery();
    }
}
