using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Rat9Battery
{
    internal static class SaitekAssemblyResolver
    {
        private static bool _installed;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            AssemblyName requestedName;
            try
            {
                requestedName = new AssemblyName(args.Name);
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrEmpty(requestedName.Name) ||
                !requestedName.Name.StartsWith(
                    "Saitek.",
                    StringComparison.OrdinalIgnoreCase) ||
                requestedName.Name.EndsWith(
                    ".resources",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string executableDirectory =
                AppDomain.CurrentDomain.BaseDirectory;
            string[] directories =
            {
                executableDirectory,
                @"C:\Program Files\SmartTechnology\Software",
                @"C:\Program Files (x86)\SmartTechnology\Software"
            };

            foreach (string directory in directories)
            {
                if (string.IsNullOrEmpty(directory))
                {
                    continue;
                }

                string candidate = Path.Combine(
                    directory,
                    requestedName.Name + ".dll");
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }

            return null;
        }
    }

    internal enum BatteryStatus
    {
        Ok,
        Empty,
        Disconnected,
        Unknown,
        Error
    }

    internal sealed class BatteryReading
    {
        public BatteryStatus Status { get; private set; }
        public int? Percentage { get; private set; }
        public int RawValue { get; private set; }
        public bool IsCharging { get; private set; }
        public string Message { get; private set; }
        public DateTime MeasuredAt { get; private set; }

        public bool HasPercentage
        {
            get { return Percentage.HasValue; }
        }

        public static BatteryReading Create(
            BatteryStatus status,
            int? percentage,
            int rawValue,
            bool isCharging,
            string message,
            DateTime? measuredAt = null)
        {
            return new BatteryReading
            {
                Status = status,
                Percentage = percentage,
                RawValue = rawValue,
                IsCharging = isCharging,
                Message = message,
                MeasuredAt = measuredAt ?? DateTime.Now
            };
        }

        public string ToDisplayText()
        {
            if (HasPercentage)
            {
                string suffix = IsCharging ? " (laddar)" : string.Empty;
                return "R.A.T. 9: " + Percentage.Value + "%" + suffix;
            }

            return "R.A.T. 9: " + Message;
        }
    }

    internal sealed class Rat9BatteryReader
    {
        private const ushort MadCatzVendorId = 0x0738;
        private const ushort Rat9ProductId = 0x1709;

        public BatteryReading Read()
        {
            object deviceList = null;
            object batteryDevice = null;

            try
            {
                Assembly vendorAssembly = LoadVendorAssembly();
                Type deviceListType = vendorAssembly.GetType(
                    "Saitek.Devices.DeviceList",
                    true);
                Type saitekDeviceType = vendorAssembly.GetType(
                    "Saitek.Devices.SaitekDevice",
                    true);

                PropertyInfo interfaceGuidProperty =
                    saitekDeviceType.GetProperty("InterfaceGuid");
                Guid interfaceGuid = (Guid)interfaceGuidProperty.GetValue(
                    null,
                    null);

                deviceList = Activator.CreateInstance(
                    deviceListType,
                    new object[] { interfaceGuid });
                Invoke(
                    deviceList,
                    "EnumerateExistingDevices");

                IEnumerable devices = (IEnumerable)ReadProperty(
                    deviceList,
                    "Devices");
                if (devices == null)
                {
                    return DisconnectedReading();
                }

                foreach (object candidate in devices)
                {
                    string linkName = ReadProperty(
                        candidate,
                        "LinkName") as string;
                    if (string.IsNullOrEmpty(linkName) ||
                        linkName.IndexOf(
                            "PROGRAMMABLEDEVICE",
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    batteryDevice = Activator.CreateInstance(
                        saitekDeviceType,
                        new object[] { linkName });

                    bool isOpen = (bool)ReadProperty(
                        batteryDevice,
                        "IsOpen");
                    if (!isOpen &&
                        !(bool)Invoke(
                            batteryDevice,
                            "Open",
                            true))
                    {
                        CloseDevice(batteryDevice);
                        batteryDevice = null;
                        continue;
                    }

                    ushort vendorId = (ushort)ReadProperty(
                        batteryDevice,
                        "Vid");
                    ushort productId = (ushort)ReadProperty(
                        batteryDevice,
                        "Pid");
                    if (vendorId != MadCatzVendorId ||
                        productId != Rat9ProductId)
                    {
                        CloseDevice(batteryDevice);
                        batteryDevice = null;
                        continue;
                    }

                    int rawValue = (int)Invoke(
                        batteryDevice,
                        "GetBatteryChargeLevel");
                    return Decode(rawValue);
                }

                return DisconnectedReading();
            }
            catch (Exception exception)
            {
                Exception root = exception;
                while (root.InnerException != null)
                {
                    root = root.InnerException;
                }

                string message = root.Message;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "kunde inte läsas";
                }

                return BatteryReading.Create(
                    BatteryStatus.Error,
                    null,
                    -1,
                    false,
                    message);
            }
            finally
            {
                CloseDevice(batteryDevice);
                if (deviceList != null)
                {
                    DisposeObject(deviceList);
                }
            }
        }

        private static Assembly LoadVendorAssembly()
        {
            string[] candidates =
            {
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Saitek.Devices.dll"),
                @"C:\Program Files\SmartTechnology\Software\Saitek.Devices.dll",
                @"C:\Program Files (x86)\SmartTechnology\Software\Saitek.Devices.dll"
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }

            throw new FileNotFoundException(
                "Mad Catz SmartTechnology-drivrutinen hittades inte.");
        }

        private static BatteryReading DisconnectedReading()
        {
            return BatteryReading.Create(
                BatteryStatus.Disconnected,
                null,
                -1,
                false,
                "hittades inte");
        }

        private static BatteryReading Decode(int rawValue)
        {
            if (rawValue < 0)
            {
                return BatteryReading.Create(
                    BatteryStatus.Error,
                    null,
                    rawValue,
                    false,
                    "inget svar från musen");
            }

            if (rawValue == 0)
            {
                return BatteryReading.Create(
                    BatteryStatus.Empty,
                    0,
                    rawValue,
                    false,
                    "tomt batteri");
            }

            if (rawValue <= 100)
            {
                return BatteryReading.Create(
                    BatteryStatus.Ok,
                    rawValue,
                    rawValue,
                    false,
                    "batteri " + rawValue + "%");
            }

            if (rawValue <= 200)
            {
                return BatteryReading.Create(
                    BatteryStatus.Ok,
                    rawValue - 100,
                    rawValue,
                    true,
                    "laddar, " + (rawValue - 100) + "%");
            }

            // 201 is used by the original Mad Catz control when the mouse
            // is docked/asleep and no battery measurement is available.
            return BatteryReading.Create(
                BatteryStatus.Unknown,
                null,
                rawValue,
                false,
                "ingen batteridata");
        }

        private static object ReadProperty(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(name);
            if (property == null)
            {
                throw new MissingMemberException(
                    instance.GetType().FullName,
                    name);
            }

            return property.GetValue(instance, null);
        }

        private static object Invoke(
            object instance,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                throw new MissingMethodException(
                    instance.GetType().FullName,
                    methodName);
            }

            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }

                throw;
            }
        }

        private static void CloseDevice(object device)
        {
            if (device == null)
            {
                return;
            }

            try
            {
                if ((bool)ReadProperty(device, "IsOpen"))
                {
                    Invoke(device, "Close");
                }
            }
            catch
            {
                // A removed receiver can fail while its handle is closing.
            }

            try
            {
                DisposeObject(device);
            }
            catch
            {
                // Nothing useful can be done if final cleanup fails.
            }
        }

        private static void DisposeObject(object instance)
        {
            IDisposable disposable = instance as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }
    }

    internal sealed class BatteryHistorySample
    {
        public DateTime Timestamp { get; private set; }
        public int Percentage { get; private set; }
        public bool IsCharging { get; private set; }

        public BatteryHistorySample(
            DateTime timestamp,
            int percentage,
            bool isCharging)
        {
            Timestamp = timestamp;
            Percentage = percentage;
            IsCharging = isCharging;
        }
    }

    internal sealed class BatteryHistoryStore
    {
        private static readonly TimeSpan RecordInterval =
            TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Retention =
            TimeSpan.FromDays(30);

        private readonly List<BatteryHistorySample> _samples =
            new List<BatteryHistorySample>();
        private readonly string _historyPath;

        public BatteryHistoryStore()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MadCatzRat9BatteryTray");
            _historyPath = Path.Combine(directory, "history-v1.txt");
            Load();
        }

        public IList<BatteryHistorySample> GetRecentSamples(
            TimeSpan period)
        {
            DateTime threshold = DateTime.Now - period;
            List<BatteryHistorySample> result =
                new List<BatteryHistorySample>();

            foreach (BatteryHistorySample sample in _samples)
            {
                if (sample.Timestamp >= threshold)
                {
                    result.Add(sample);
                }
            }

            return result;
        }

        public void Record(BatteryReading reading)
        {
            if (reading == null ||
                !reading.HasPercentage ||
                (reading.Status != BatteryStatus.Ok &&
                 reading.Status != BatteryStatus.Empty))
            {
                return;
            }

            DateTime timestamp = reading.MeasuredAt;
            if (_samples.Count > 0)
            {
                BatteryHistorySample previous =
                    _samples[_samples.Count - 1];
                bool sameReading =
                    previous.Percentage == reading.Percentage.Value &&
                    previous.IsCharging == reading.IsCharging;
                if (sameReading &&
                    timestamp - previous.Timestamp < RecordInterval)
                {
                    return;
                }
            }

            BatteryHistorySample sample = new BatteryHistorySample(
                timestamp,
                reading.Percentage.Value,
                reading.IsCharging);
            _samples.Add(sample);
            TrimOldSamples();
            Append(sample);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_historyPath))
                {
                    return;
                }

                foreach (string line in File.ReadLines(_historyPath))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 3)
                    {
                        continue;
                    }

                    long ticks;
                    int percentage;
                    if (!long.TryParse(
                            parts[0],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out ticks) ||
                        !int.TryParse(
                            parts[1],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out percentage) ||
                        percentage < 0 ||
                        percentage > 100)
                    {
                        continue;
                    }

                    _samples.Add(new BatteryHistorySample(
                        new DateTime(ticks, DateTimeKind.Local),
                        percentage,
                        parts[2] == "1"));
                }

                TrimOldSamples();
            }
            catch
            {
                // Battery history is an enhancement; polling still works
                // when the local history file cannot be read.
            }
        }

        private void Append(BatteryHistorySample sample)
        {
            try
            {
                string directory = Path.GetDirectoryName(_historyPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|{1}|{2}",
                    sample.Timestamp.Ticks,
                    sample.Percentage,
                    sample.IsCharging ? 1 : 0);
                File.AppendAllText(
                    _historyPath,
                    line + Environment.NewLine);
            }
            catch
            {
                // Keep the in-memory history even if disk writes fail.
            }
        }

        private void TrimOldSamples()
        {
            DateTime threshold = DateTime.Now - Retention;
            int removeCount = 0;
            while (removeCount < _samples.Count &&
                   _samples[removeCount].Timestamp < threshold)
            {
                removeCount++;
            }

            if (removeCount > 0)
            {
                _samples.RemoveRange(0, removeCount);
            }
        }
    }

    internal sealed class BatteryTimeEstimate
    {
        public int? RemainingMinutes { get; internal set; }
        public int? LowMinutes { get; internal set; }
        public int? HighMinutes { get; internal set; }
        public int? EffectiveFullRuntimeMinutes { get; internal set; }
        public int? CapacityPercent { get; internal set; }
        public int ConfidencePercent { get; internal set; }
        public bool UsesMeasuredDischarge { get; internal set; }

        public string SourceText
        {
            get
            {
                return UsesMeasuredDischarge
                    ? "anpassad efter din användning"
                    : "utgår från 9 h specifikation";
            }
        }

        public string FormatShort()
        {
            if (!LowMinutes.HasValue || !HighMinutes.HasValue)
            {
                return string.Empty;
            }

            return FormatRange(
                LowMinutes.Value,
                HighMinutes.Value,
                false);
        }

        public string FormatDetailed()
        {
            if (!LowMinutes.HasValue || !HighMinutes.HasValue)
            {
                return string.Empty;
            }

            return FormatRange(
                LowMinutes.Value,
                HighMinutes.Value,
                true);
        }

        public string Format()
        {
            return FormatDetailed();
        }

        private static string FormatRange(
            int lowMinutes,
            int highMinutes,
            bool detailed)
        {
            lowMinutes = RoundToFive(lowMinutes);
            highMinutes = RoundToFive(highMinutes);

            if (highMinutes <= 5)
            {
                return "mindre än 5 min kvar";
            }

            if (!detailed &&
                lowMinutes >= 60 &&
                highMinutes >= 60)
            {
                int lowHours = lowMinutes / 60;
                int highHours = highMinutes / 60;
                if (lowHours != highHours)
                {
                    return "ca " + lowHours + "–" +
                        highHours + " t kvar";
                }
            }

            if (!detailed && highMinutes < 60)
            {
                return "ca " + lowMinutes + "–" +
                    highMinutes + " min kvar";
            }

            string low = FormatDuration(lowMinutes);
            string high = FormatDuration(highMinutes);
            if (low == high)
            {
                return "ca " + low + " kvar";
            }

            return "ca " + low + " – " + high + " kvar";
        }

        private static string FormatDuration(int minutes)
        {
            int hours = minutes / 60;
            int remainder = minutes % 60;
            if (hours > 0 && remainder == 0)
            {
                return hours + " t";
            }

            if (hours > 0)
            {
                return hours + " t " + remainder + " min";
            }

            return minutes + " min";
        }

        private static int RoundToFive(int minutes)
        {
            return (int)(Math.Round(minutes / 5.0) * 5);
        }
    }

    internal sealed class BatteryTimeEstimator
    {
        private const double RatedFullRuntimeHours = 9.0;
        private const double MinimumCalibrationHours = 0.33;
        private const int MinimumCalibrationDrop = 3;
        private const double MinimumPlausibleRuntimeHours = 2.0;
        private const double MaximumPlausibleRuntimeHours = 12.0;
        private static readonly TimeSpan SampleRetention =
            TimeSpan.FromDays(14);
        private static readonly TimeSpan MaximumDischargeGap =
            TimeSpan.FromHours(6);
        private static readonly TimeSpan PointInterval =
            TimeSpan.FromMinutes(5);

        private readonly List<BatteryHistorySample> _history =
            new List<BatteryHistorySample>();

        public BatteryTimeEstimator(
            IList<BatteryHistorySample> history)
        {
            if (history != null)
            {
                DateTime threshold = DateTime.Now - SampleRetention;
                foreach (BatteryHistorySample sample in history)
                {
                    if (sample.Timestamp >= threshold)
                    {
                        _history.Add(sample);
                    }
                }
            }
        }

        public BatteryTimeEstimate Calculate(BatteryReading reading)
        {
            if (reading == null ||
                !reading.HasPercentage ||
                reading.Percentage.Value <= 0)
            {
                return new BatteryTimeEstimate();
            }

            AppendCurrentReading(reading);
            TrimOldSamples();

            int percentage = reading.Percentage.Value;
            if (reading.IsCharging)
            {
                return new BatteryTimeEstimate();
            }

            List<BatteryHistorySample> segment =
                GetCurrentDischargeSegment(
                    reading.MeasuredAt,
                    percentage);

            double remainingMinutes =
                RatedFullRuntimeHours * 60.0 * percentage / 100.0;
            double effectiveFullRuntimeHours =
                RatedFullRuntimeHours;
            int confidence = 28;
            bool calibrated = false;
            int observedDrop = 0;
            double observedHours = 0;

            if (segment.Count >= 2)
            {
                BatteryHistorySample earliest = segment[0];
                observedHours =
                    (reading.MeasuredAt - earliest.Timestamp).TotalHours;
                observedDrop = earliest.Percentage - percentage;

                if (observedHours >= MinimumCalibrationHours &&
                    observedDrop >= MinimumCalibrationDrop)
                {
                    double candidateRuntimeHours =
                        observedHours * 100.0 / observedDrop;

                    if (candidateRuntimeHours >=
                            MinimumPlausibleRuntimeHours &&
                        candidateRuntimeHours <=
                            MaximumPlausibleRuntimeHours)
                    {
                        effectiveFullRuntimeHours =
                            candidateRuntimeHours;
                        remainingMinutes =
                            effectiveFullRuntimeHours *
                            60.0 *
                            percentage /
                            100.0;
                        calibrated = true;
                        confidence = Math.Min(
                            92,
                            38 +
                            observedDrop * 4 +
                            (int)Math.Round(observedHours * 10));
                    }
                }
            }

            double uncertainty = calibrated
                ? (confidence < 60 ? 0.18 :
                   confidence < 80 ? 0.12 : 0.08)
                : 0.22;
            int? capacityPercent = null;
            if (calibrated &&
                observedDrop >= 10 &&
                observedHours >= 1.0)
            {
                capacityPercent = (int)Math.Round(
                    effectiveFullRuntimeHours /
                    RatedFullRuntimeHours *
                    100.0);
                capacityPercent = Math.Max(
                    20,
                    Math.Min(120, capacityPercent.Value));
            }

            return new BatteryTimeEstimate
            {
                RemainingMinutes = Math.Max(
                    0,
                    (int)Math.Round(remainingMinutes)),
                LowMinutes = Math.Max(
                    0,
                    (int)Math.Round(
                        remainingMinutes *
                        (1.0 - uncertainty))),
                HighMinutes = Math.Max(
                    0,
                    (int)Math.Round(
                        remainingMinutes *
                        (1.0 + uncertainty))),
                EffectiveFullRuntimeMinutes = Math.Max(
                    0,
                    (int)Math.Round(
                        effectiveFullRuntimeHours * 60.0)),
                CapacityPercent = capacityPercent,
                ConfidencePercent = confidence,
                UsesMeasuredDischarge = calibrated
            };
        }

        private void AppendCurrentReading(BatteryReading reading)
        {
            DateTime timestamp = reading.MeasuredAt;
            int percentage = reading.Percentage.Value;

            if (_history.Count > 0)
            {
                BatteryHistorySample previous =
                    _history[_history.Count - 1];
                bool sameReading =
                    previous.Percentage == percentage &&
                    previous.IsCharging == reading.IsCharging;
                if (sameReading &&
                    timestamp - previous.Timestamp < PointInterval)
                {
                    return;
                }
            }

            _history.Add(new BatteryHistorySample(
                timestamp,
                percentage,
                reading.IsCharging));
        }

        private List<BatteryHistorySample> GetCurrentDischargeSegment(
            DateTime now,
            int currentPercentage)
        {
            List<BatteryHistorySample> segment =
                new List<BatteryHistorySample>();
            BatteryHistorySample latest = _history.Count > 0
                ? _history[_history.Count - 1]
                : null;

            if (latest == null ||
                latest.Percentage != currentPercentage)
            {
                latest = new BatteryHistorySample(
                    now,
                    currentPercentage,
                    false);
            }

            segment.Add(latest);
            for (int index = _history.Count - 1;
                 index >= 0;
                 index--)
            {
                BatteryHistorySample candidate = _history[index];
                if (candidate.Timestamp >= latest.Timestamp &&
                    candidate.Percentage == latest.Percentage)
                {
                    continue;
                }

                if (candidate.IsCharging ||
                    candidate.Percentage < latest.Percentage ||
                    latest.Timestamp - candidate.Timestamp >
                        MaximumDischargeGap)
                {
                    break;
                }

                segment.Add(candidate);
                latest = candidate;
            }

            segment.Reverse();
            return segment;
        }

        private void TrimOldSamples()
        {
            DateTime threshold = DateTime.Now - SampleRetention;
            int removeCount = 0;
            while (removeCount < _history.Count &&
                   _history[removeCount].Timestamp < threshold)
            {
                removeCount++;
            }

            if (removeCount > 0)
            {
                _history.RemoveRange(0, removeCount);
            }
        }
    }
}
