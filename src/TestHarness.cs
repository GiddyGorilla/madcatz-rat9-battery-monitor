using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Rat9Battery
{
    internal static class TestHarness
    {
        private static int _passed;

        public static int Main()
        {
            Run(
                "Device info command uses the Dakai HID header",
                DeviceInfoCommandUsesDakaiHeader);
            Run(
                "Physical V102 response decodes battery and identity",
                PhysicalResponseDecodesBatteryAndIdentity);
            Run(
                "Charging and battery flags match E HUB",
                ChargingAndBatteryFlagsMatchEHub);
            Run(
                "Malformed responses are rejected",
                MalformedResponsesAreRejected);
            Run(
                "Runtime is learned and never invented",
                RuntimeIsLearned);
            Run(
                "Lighting configuration round-trips",
                LightingConfigurationRoundTrips);
            Run(
                "Profiles survive save and reload",
                ProfilesSurviveSaveAndReload);
            Run(
                "Game profiles use foreground priority and restore",
                GameProfilesUseForegroundPriority);
            Run(
                "Lighting form constructs",
                LightingFormConstructs);

            Console.WriteLine(
                "All {0} V102 protocol tests passed.",
                _passed);
            return 0;
        }

        private static void DeviceInfoCommandUsesDakaiHeader()
        {
            byte[] command =
                V102Protocol.EncodeDeviceInfoCommand();

            AssertEqual(32, command.Length, "command length");
            AssertEqual((byte)0x55, command[0], "response marker");
            AssertEqual((byte)0xAA, command[1], "command marker");
            AssertEqual((byte)0x80, command[2], "device info command");
            for (int index = 3; index < command.Length; index++)
            {
                AssertEqual(
                    (byte)0,
                    command[index],
                    "command padding " + index);
            }
        }

        private static void PhysicalResponseDecodesBatteryAndIdentity()
        {
            byte[] response = CreatePhysicalResponse();
            V102DeviceInfo info =
                V102Protocol.DecodeDeviceInfoResponse(response);

            AssertEqual(55, info.Battery.Level, "battery level");
            AssertEqual(true, info.Battery.IsCharging, "charging");
            AssertEqual(false, info.Battery.IsFull, "full");
            AssertEqual(true, info.Battery.IsPluggedIn, "plugged in");
            AssertEqual(2, info.ConnectionMode, "connection mode");
            AssertEqual(
                "250928V201A",
                info.FirmwareVersion,
                "firmware version");
            AssertEqual(256, info.DriverSerial, "driver serial");
            AssertEqual(591, info.ProductId, "product id");
            AssertEqual(1452, info.VendorId, "vendor id");
            AssertEqual(
                "D0:00:0C:53:3B:38",
                info.MacAddressText,
                "MAC address");
        }

        private static void ChargingAndBatteryFlagsMatchEHub()
        {
            V102BatteryStatus full = V102Protocol.DecodeBatteryResponse(
                CreateResponse(0xC0, 67));
            AssertEqual(true, full.IsPluggedIn, "full plugged");
            AssertEqual(true, full.IsFull, "full flag");
            AssertEqual(false, full.IsCharging, "full charging");
            AssertEqual(100, full.Level, "full level");

            V102BatteryStatus low = V102Protocol.DecodeBatteryResponse(
                CreateResponse(0x20, 8));
            AssertEqual(false, low.IsPluggedIn, "low plugged");
            AssertEqual(true, low.IsLowBattery, "low flag");
            AssertEqual(false, low.IsCharging, "low charging");
            AssertEqual(8, low.Level, "low level");
        }

        private static void MalformedResponsesAreRejected()
        {
            ExpectProtocolError(
                new byte[20],
                "short response");

            byte[] wrongMarker = CreatePhysicalResponse();
            wrongMarker[0] = 0x54;
            ExpectProtocolError(
                wrongMarker,
                "wrong response marker");

            byte[] wrongCommand = CreatePhysicalResponse();
            wrongCommand[2] = 0x81;
            ExpectProtocolError(
                wrongCommand,
                "wrong response command");

            byte[] errorStatus = CreatePhysicalResponse();
            errorStatus[3] = 0xFF;
            errorStatus[4] = 0xFF;
            errorStatus[5] = 0xFF;
            ExpectProtocolError(
                errorStatus,
                "unavailable device");

            ExpectProtocolError(
                CreateResponse(0x00, 101),
                "out-of-range battery level");
        }

        private static void RuntimeIsLearned()
        {
            DateTime now =
                new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            DeviceBatteryReading reading = new DeviceBatteryReading
            {
                Device = new DeviceIdentity(
                    "v102-pro-logo",
                    "EWEADN V102 Pro",
                    "2,4 GHz"),
                ConnectionState = DeviceConnectionState.Connected,
                BatteryState = DeviceBatteryState.Available,
                Percentage = 50,
                MeasuredAtUtc = now
            };

            V102TimeEstimate empty =
                V102TimeEstimator.Calculate(
                    reading,
                    new List<V102BatterySample>());
            AssertEqual(
                false,
                empty.HasEstimate,
                "empty estimate");

            List<V102BatterySample> samples =
                new List<V102BatterySample>
                {
                    new V102BatterySample(
                        now.AddHours(-1),
                        60,
                        false),
                    new V102BatterySample(
                        now,
                        50,
                        false)
                };
            V102TimeEstimate estimate =
                V102TimeEstimator.Calculate(reading, samples);
            AssertEqual(true, estimate.HasEstimate, "learned estimate");
            AssertEqual(252, estimate.LowMinutes, "low minutes");
            AssertEqual(348, estimate.HighMinutes, "high minutes");

            reading.IsCharging = true;
            reading.BatteryState = DeviceBatteryState.Charging;
            AssertEqual(
                false,
                V102TimeEstimator.Calculate(
                    reading,
                    samples).HasEstimate,
                "charging estimate");
        }

        private static void LightingConfigurationRoundTrips()
        {
            byte[] response = new byte[64];
            response[0] = 0x55;
            response[1] = 0xBB;
            response[2] = 0x81;
            response[3] = 0x11;
            response[4] = 0x10;
            response[5] = 0x02;
            response[6] = 4;
            response[7] = 4;
            response[8] = 0;
            for (int index = 9; index <= 17; index++)
            {
                response[index] = 0x45;
            }
            response[18] = 0x21;
            response[19] = 3;
            response[20] = 1;

            V102KeyboardConfig config =
                V102Protocol.DecodeKeyboardConfigResponse(
                    response);
            AssertEqual(true, config.LightingEnabled, "lighting");
            AssertEqual(4, config.ProfileLightingSet, "effect");
            AssertEqual(3, config.LogoColorType, "logo");
            AssertEqual(2, config.BreatheMonoWaveOption >> 4,
                "breathe color");
            AssertEqual(1, config.BreatheMonoWaveOption & 0x0F,
                "wave color");

            V102LightingProfile profile =
                V102LightingService.FromCurrent(config);
            profile.EffectMode = 21;
            profile.Brightness = 3;
            profile.Speed = 2;
            profile.LogoMode = 2;
            V102KeyboardConfig merged =
                V102LightingService.Merge(config, profile);
            AssertEqual(21, merged.ProfileLightingSet,
                "merged profile effect");
            AssertEqual(2, merged.LogoColorType,
                "merged logo");
            AssertEqual(config.SleepEnabled, merged.SleepEnabled,
                "preserved sleep flag");
            AssertEqual(config.SleepTimer, merged.SleepTimer,
                "preserved sleep timer");
            AssertEqual(config.WinKeyMode, merged.WinKeyMode,
                "preserved Windows mode");
            AssertEqual((byte)3,
                merged.SpeedAndBrightness[0],
                "static brightness");

            merged.UsbLightingSet = 16;
            merged.ProfileLightingSet = 16;
            merged.BleLightingSet = 16;
            merged.LogoColorType = 9;
            merged.SpeedAndBrightness[0] = 4;
            byte[] command =
                V102Protocol.EncodeSetKeyboardConfigCommand(
                    merged);
            AssertEqual((byte)0x55, command[0], "config marker");
            AssertEqual((byte)0xAA, command[1], "config command marker");
            AssertEqual((byte)0x82, command[2], "set config command");
            AssertEqual((byte)0x11, command[3], "config type");
            AssertEqual((byte)0x10, command[4], "preserved status");
            AssertEqual((byte)16, command[6], "USB static effect");
            AssertEqual((byte)16, command[7], "profile static effect");
            AssertEqual((byte)9, command[19], "logo off");
        }

        private static void ProfilesSurviveSaveAndReload()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "rat9-v102-tests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(
                directory,
                "profiles.json");
            try
            {
                KeyboardProfileStore store =
                    new KeyboardProfileStore(path);
                V102ProfilesDocument document =
                    store.LoadOrCreate();
                V102LightingProfile game =
                    KeyboardProfileStore.CreateDefaultProfile();
                game.Id = "game";
                game.Name = "Game";
                game.EffectMode = 16;
                game.Brightness = 4;
                game.Executables.Add(
                    @"C:\Games\Example\game.exe");
                document.Profiles.Add(game);
                store.Save(document);
                string json = File.ReadAllText(path);
                AssertEqual(
                    true,
                    json.IndexOf(
                        "\"baseProfileId\"",
                        StringComparison.Ordinal) >= 0,
                    "camel-case schema");

                V102ProfilesDocument loaded =
                    store.LoadOrCreate();
                AssertEqual(2, loaded.Profiles.Count, "profile count");
                AssertEqual(
                    "Game",
                    loaded.Profiles[1].Name,
                    "profile name");
                AssertEqual(
                    @"C:\Games\Example\game.exe",
                    loaded.Profiles[1].Executables[0],
                    "executable");
                AssertEqual("base", loaded.BaseProfileId,
                    "base profile");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void GameProfilesUseForegroundPriority()
        {
            V102LightingProfile gameA =
                KeyboardProfileStore.CreateDefaultProfile();
            gameA.Id = "game-a";
            gameA.Name = "Game A";
            gameA.Executables.Add(@"C:\Games\A\game.exe");

            V102LightingProfile gameB =
                KeyboardProfileStore.CreateDefaultProfile();
            gameB.Id = "game-b";
            gameB.Name = "Game B";
            gameB.Executables.Add(@"C:\Games\B\game.exe");

            List<V102LightingProfile> profiles =
                new List<V102LightingProfile>
                {
                    KeyboardProfileStore.CreateDefaultProfile(),
                    gameA,
                    gameB
                };
            GameProfileMonitor monitor =
                new GameProfileMonitor(profiles, "base");
            List<GameProcessInfo> running =
                new List<GameProcessInfo>
                {
                    new GameProcessInfo
                    {
                        ProcessId = 10,
                        ExecutablePath = @"C:\Games\A\game.exe"
                    },
                    new GameProcessInfo
                    {
                        ProcessId = 20,
                        ExecutablePath = @"C:\Games\B\game.exe"
                    }
                };

            GameProfileDecision decision =
                monitor.Evaluate(running, 20);
            AssertEqual(true, decision.Changed, "game changed");
            AssertEqual("game-b", decision.ActiveProfileId,
                "foreground game");

            decision = monitor.Evaluate(running, 20);
            AssertEqual(false, decision.Changed,
                "same game is stable");

            decision = monitor.Evaluate(
                new List<GameProcessInfo>(),
                null);
            AssertEqual(true, decision.Changed, "game exited");
            AssertEqual("base", decision.ActiveProfileId,
                "base restored");
        }

        private static void LightingFormConstructs()
        {
            Exception failure = null;
            Thread thread = new Thread(delegate()
            {
                string directory = Path.Combine(
                    Path.GetTempPath(),
                    "rat9-v102-ui-" + Guid.NewGuid().ToString("N"));
                try
                {
                    KeyboardProfileStore store =
                        new KeyboardProfileStore(
                            Path.Combine(
                                directory,
                                "profiles.json"));
                    V102ProfilesDocument document =
                        store.LoadOrCreate();
                    using (V102LightingForm form =
                        new V102LightingForm(
                            store,
                            document,
                            delegate
                            {
                                throw new InvalidOperationException(
                                    "not called");
                            },
                            delegate { },
                            delegate { }))
                    {
                        form.CreateControl();
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null)
            {
                throw new InvalidOperationException(
                    "Lighting form failed to construct.",
                    failure);
            }
        }

        private static byte[] CreatePhysicalResponse()
        {
            byte[] response = new byte[64];
            response[0] = 0x55;
            response[1] = 0xBB;
            response[2] = 0x80;
            response[3] = 0x20;
            response[4] = 0x80;
            response[5] = 55;
            response[6] = 2;

            byte[] version =
                System.Text.Encoding.ASCII.GetBytes(
                    "250928V201A");
            Array.Copy(version, 0, response, 8, version.Length);

            response[24] = 0x00;
            response[25] = 0x01;
            response[26] = 0x4F;
            response[27] = 0x02;
            response[28] = 0xAC;
            response[29] = 0x05;

            byte[] mac =
            {
                0xD0, 0x00, 0x0C, 0x53, 0x3B, 0x38
            };
            Array.Copy(mac, 0, response, 30, mac.Length);
            return response;
        }

        private static byte[] CreateResponse(
            byte batteryState,
            byte percentage)
        {
            byte[] response = CreatePhysicalResponse();
            response[4] = batteryState;
            response[5] = percentage;
            return response;
        }

        private static void ExpectProtocolError(
            byte[] response,
            string description)
        {
            try
            {
                V102Protocol.DecodeDeviceInfoResponse(response);
            }
            catch (V102ProtocolException)
            {
                return;
            }

            throw new InvalidOperationException(
                "Expected protocol rejection for " + description + ".");
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + name);
                Console.Error.WriteLine(exception);
                Environment.Exit(1);
            }
        }

        private static void AssertEqual<T>(
            T expected,
            T actual,
            string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    description + ": expected " + expected +
                    ", actual " + actual + ".");
            }
        }
    }
}
