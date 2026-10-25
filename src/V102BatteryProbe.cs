using System;
using Rat9Battery.V102;

namespace Rat9Battery
{
    internal static class V102BatteryProbe
    {
        public static int Main()
        {
            try
            {
                using (V102HidDevice device =
                    V102HidDevice.Open())
                {
                    if (Array.IndexOf(
                            Environment.GetCommandLineArgs(),
                            "--trace") >= 0)
                    {
                        device.Trace =
                            message => Console.WriteLine(message);
                    }

                    V102HidDescriptor descriptor =
                        device.Descriptor;
                    V102DeviceInfo info =
                        device.ReadDeviceInfo();
                    V102KeyboardConfig config =
                        device.ReadKeyboardConfig();
                    if (Array.IndexOf(
                            Environment.GetCommandLineArgs(),
                            "--verify-lighting-cycle") >= 0)
                    {
                        V102LightingProfile original =
                            V102LightingService.FromCurrent(config);
                        V102LightingProfile test =
                            original.Clone();
                        test.EffectMode = 16;
                        test.Brightness = 4;
                        test.LogoMode = 9;
                        device.ApplyKeyboardConfig(
                            V102LightingService.Merge(
                                config,
                                test));
                        System.Threading.Thread.Sleep(2500);
                        V102KeyboardConfig changed =
                            device.ReadKeyboardConfig();
                        Console.WriteLine(
                            "Lighting cycle changed: effect=" +
                            changed.ProfileLightingSet +
                            "; logo=" +
                            changed.LogoColorType);

                        device.ApplyKeyboardConfig(
                            V102LightingService.Merge(
                                changed,
                                original));
                        System.Threading.Thread.Sleep(2500);
                        V102KeyboardConfig restored =
                            device.ReadKeyboardConfig();
                        Console.WriteLine(
                            "Lighting cycle restored: effect=" +
                            restored.ProfileLightingSet +
                            "; logo=" +
                            restored.LogoColorType);
                        if (changed.ProfileLightingSet != 16 ||
                            changed.LogoColorType != 9 ||
                            restored.ProfileLightingSet !=
                                original.EffectMode ||
                            restored.LogoColorType !=
                                original.LogoMode)
                        {
                            Console.Error.WriteLine(
                                "Lighting verification mismatch.");
                            return 1;
                        }
                    }

                    if (Array.IndexOf(
                            Environment.GetCommandLineArgs(),
                            "--verify-lighting") >= 0)
                    {
                        V102LightingProfile profile =
                            V102LightingService.FromCurrent(config);
                        device.ApplyKeyboardConfig(
                            V102LightingService.Merge(
                                config,
                                profile));
                        System.Threading.Thread.Sleep(2500);
                        V102KeyboardConfig verified =
                            device.ReadKeyboardConfig();
                        Console.WriteLine(
                            "Lighting write verified: effect=" +
                            verified.ProfileLightingSet +
                            "; brightness byte=" +
                            verified.SpeedAndBrightness[
                                verified.ProfileLightingSet < 16
                                    ? verified.ProfileLightingSet + 1
                                    : 0]);
                    }

                    Console.WriteLine(
                        "Device=" + descriptor.Product +
                        "; Manufacturer=" +
                        descriptor.Manufacturer);
                    Console.WriteLine(
                        "HID report ID=" +
                        descriptor.ReportId +
                        "; Input=" +
                        descriptor.InputReportByteLength +
                        "; Output=" +
                        descriptor.OutputReportByteLength +
                        "; Feature=" +
                        descriptor.FeatureReportByteLength);
                    Console.WriteLine(
                        "Firmware=" +
                        info.FirmwareVersion +
                        "; Driver serial=" +
                        info.DriverSerial +
                        "; Mode=" +
                        info.ConnectionMode);
                    Console.WriteLine(
                        "Identity=" +
                        info.VendorId.ToString("x4") +
                        ":" +
                        info.ProductId.ToString("x4") +
                        "; MAC=" +
                        info.MacAddressText);
                    Console.WriteLine(
                        "V102 Pro: " +
                        info.Battery.Level +
                        "%" +
                        (info.Battery.IsCharging
                            ? " (laddar)"
                            : info.Battery.IsFull
                                ? " (fullt)"
                                : info.Battery.IsLowBattery
                                    ? " (lågt)"
                                    : string.Empty));
                    Console.WriteLine(
                        "Lighting: enabled=" +
                        config.LightingEnabled +
                        "; effect=" +
                        config.ProfileLightingSet +
                        "; logo=" +
                        config.LogoColorType +
                        "; brightness bytes=" +
                        BitConverter.ToString(
                            config.SpeedAndBrightness));
                    return 0;
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
    }
}
