using System;
using System.Text;

namespace Rat9Battery
{
    internal class V102ProtocolException : Exception
    {
        public V102ProtocolException(string message)
            : base(message)
        {
        }
    }

    internal sealed class V102BatteryStatus
    {
        public int Level { get; set; }
        public bool IsCharging { get; set; }
        public bool IsFull { get; set; }
        public bool IsPluggedIn { get; set; }
        public bool IsLowBattery { get; set; }
    }

    internal sealed class V102DeviceInfo
    {
        public V102BatteryStatus Battery { get; set; }
        public int ConnectionMode { get; set; }
        public string FirmwareVersion { get; set; }
        public int DriverSerial { get; set; }
        public ushort ProductId { get; set; }
        public ushort VendorId { get; set; }
        public byte[] MacAddress { get; set; }
        public string MacAddressText { get; set; }
    }

    internal sealed class V102KeyboardConfig
    {
        public bool SleepEnabled { get; set; }
        public int SleepTimer { get; set; }
        public bool LightingEnabled { get; set; }
        public int IdleTime { get; set; }
        public bool WinKeyEnabled { get; set; }
        public int OsType { get; set; }
        public bool FnFree { get; set; }
        public int UsbLightingSet { get; set; }
        public int ProfileLightingSet { get; set; }
        public int BleLightingSet { get; set; }
        public byte[] SpeedAndBrightness { get; set; }
        public int BreatheMonoWaveOption { get; set; }
        public int LogoColorType { get; set; }
        public int WinKeyMode { get; set; }
    }

    internal static class V102Protocol
    {
        public const int CommandLength = 32;
        public const int ResponseHeaderOffset = 0;
        public const byte ResponseMarker = 0x55;
        public const byte CommandMarker = 0xAA;
        public const byte DeviceInfoCommand = 0x80;
        public const byte DeviceInfoResponse = 0x80;
        public const byte GetKeyboardConfigCommand = 0x81;
        public const byte SetKeyboardConfigCommand = 0x82;
        public const byte KeyboardConfigType = 0x11;
        public const int BatteryStatusOffset = 4;
        public const int BatteryPercentOffset = 5;
        public const int FirmwareVersionOffset = 8;
        public const int FirmwareVersionLength = 16;
        public const int DriverSerialOffset = 24;
        public const int ProductIdOffset = 26;
        public const int VendorIdOffset = 28;
        public const int MacAddressOffset = 30;

        public static byte[] EncodeDeviceInfoCommand()
        {
            byte[] command = new byte[CommandLength];
            command[0] = ResponseMarker;
            command[1] = CommandMarker;
            command[2] = DeviceInfoCommand;
            return command;
        }

        public static V102DeviceInfo DecodeDeviceInfoResponse(
            byte[] response)
        {
            if (response == null || response.Length < 36)
            {
                throw new V102ProtocolException(
                    "V102-svaret är kortare än 36 byte.");
            }

            if (response[0] != ResponseMarker ||
                response[2] != DeviceInfoResponse)
            {
                throw new V102ProtocolException(
                    "V102-svaret har fel kommandohuvud.");
            }

            if (response[3] == 0xFF &&
                response[4] == 0xFF &&
                response[5] == 0xFF)
            {
                throw new V102ProtocolException(
                    "V102-dongeln svarade att enheten inte är " +
                    "tillgänglig.");
            }

            if (response[3] != 0x20)
            {
                throw new V102ProtocolException(
                    "V102-svaret har okänd status 0x" +
                    response[3].ToString("x2") + ".");
            }

            byte batteryState = response[BatteryStatusOffset];
            int percentage = response[BatteryPercentOffset];
            if (percentage > 100)
            {
                throw new V102ProtocolException(
                    "V102-batteriprocenten ligger utanför 0-100.");
            }

            bool isPluggedIn = (batteryState & 0x80) != 0;
            bool isFull =
                isPluggedIn && (batteryState & 0x40) != 0;
            bool isCharging = isPluggedIn && !isFull;
            bool isLowBattery =
                !isPluggedIn && (batteryState & 0x20) != 0;

            byte[] mac = new byte[6];
            Array.Copy(
                response,
                MacAddressOffset,
                mac,
                0,
                mac.Length);

            return new V102DeviceInfo
            {
                Battery = new V102BatteryStatus
                {
                    Level = isFull ? 100 : percentage,
                    IsCharging = isCharging,
                    IsFull = isFull,
                    IsPluggedIn = isPluggedIn,
                    IsLowBattery = isLowBattery
                },
                ConnectionMode = response[6],
                FirmwareVersion = ParseVersion(response),
                DriverSerial = ReadUInt16LittleEndian(
                    response,
                    DriverSerialOffset),
                ProductId = (ushort)ReadUInt16LittleEndian(
                    response,
                    ProductIdOffset),
                VendorId = (ushort)ReadUInt16LittleEndian(
                    response,
                    VendorIdOffset),
                MacAddress = mac,
                MacAddressText = FormatMacAddress(mac)
            };
        }

        public static V102BatteryStatus DecodeBatteryResponse(
            byte[] response)
        {
            return DecodeDeviceInfoResponse(response).Battery;
        }

        public static byte[] EncodeKeyboardConfigRequest()
        {
            byte[] command = new byte[CommandLength];
            command[0] = ResponseMarker;
            command[1] = CommandMarker;
            command[2] = GetKeyboardConfigCommand;
            return command;
        }

        public static V102KeyboardConfig DecodeKeyboardConfigResponse(
            byte[] response)
        {
            if (response == null ||
                response.Length < 21 ||
                response[0] != ResponseMarker ||
                response[2] != GetKeyboardConfigCommand ||
                response[3] != KeyboardConfigType)
            {
                throw new V102ProtocolException(
                    "V102 skickade en ogiltig tangentbordskonfiguration.");
            }

            byte settings = response[4];
            byte miscellaneous = response[5];
            byte[] speed = new byte[9];
            Array.Copy(response, 9, speed, 0, speed.Length);
            return new V102KeyboardConfig
            {
                SleepEnabled = (settings & 0x01) != 0,
                SleepTimer = (settings >> 1) & 0x07,
                LightingEnabled = (settings & 0x10) != 0,
                IdleTime = (settings >> 5) & 0x03,
                WinKeyEnabled = (settings & 0x80) != 0,
                OsType = miscellaneous & 0x03,
                FnFree = (miscellaneous & 0x04) != 0,
                UsbLightingSet = response[6],
                ProfileLightingSet = response[7],
                BleLightingSet = response[8],
                SpeedAndBrightness = speed,
                BreatheMonoWaveOption = response[18],
                LogoColorType = response[19],
                WinKeyMode = response[20]
            };
        }

        public static byte[] EncodeSetKeyboardConfigCommand(
            V102KeyboardConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            byte[] command = new byte[CommandLength];
            command[0] = ResponseMarker;
            command[1] = CommandMarker;
            command[2] = SetKeyboardConfigCommand;
            command[3] = KeyboardConfigType;
            command[4] = (byte)(
                (config.SleepEnabled ? 0x01 : 0) |
                ((config.SleepTimer & 0x07) << 1) |
                (config.LightingEnabled ? 0x10 : 0) |
                ((config.IdleTime & 0x03) << 5) |
                (config.WinKeyEnabled ? 0x80 : 0));
            command[5] = (byte)(
                (config.OsType & 0x03) |
                (config.FnFree ? 0x04 : 0));
            command[6] = (byte)config.UsbLightingSet;
            command[7] = (byte)config.ProfileLightingSet;
            command[8] = (byte)config.BleLightingSet;
            byte[] speedAndBrightness =
                config.SpeedAndBrightness ?? new byte[9];
            for (int index = 0;
                 index < 9 &&
                 index < speedAndBrightness.Length;
                 index++)
            {
                command[9 + index] =
                    speedAndBrightness[index];
            }
            command[18] = (byte)config.BreatheMonoWaveOption;
            command[19] = (byte)config.LogoColorType;
            command[20] = (byte)config.WinKeyMode;
            return command;
        }

        public static bool HasDeviceInfoHeader(byte[] data)
        {
            return data != null &&
                data.Length > 2 &&
                data[0] == ResponseMarker &&
                data[2] == DeviceInfoResponse;
        }

        private static string ParseVersion(byte[] response)
        {
            int length = Math.Min(
                FirmwareVersionLength,
                response.Length - FirmwareVersionOffset);
            StringBuilder version = new StringBuilder(length);
            for (int index = 0; index < length; index++)
            {
                byte value =
                    response[FirmwareVersionOffset + index];
                if (value == 0)
                {
                    break;
                }

                if (value >= 0x20 && value <= 0x7E)
                {
                    version.Append((char)value);
                }
            }

            return version.Length == 0
                ? "000000V000E"
                : version.ToString().Trim();
        }

        private static int ReadUInt16LittleEndian(
            byte[] data,
            int offset)
        {
            return data[offset] | (data[offset + 1] << 8);
        }

        private static string FormatMacAddress(byte[] mac)
        {
            if (mac == null || mac.Length != 6)
            {
                return "00:00:00:00:00:00";
            }

            StringBuilder result = new StringBuilder(17);
            for (int index = 0; index < mac.Length; index++)
            {
                if (index > 0)
                {
                    result.Append(':');
                }

                result.Append(mac[index].ToString("X2"));
            }

            return result.ToString();
        }
    }
}
