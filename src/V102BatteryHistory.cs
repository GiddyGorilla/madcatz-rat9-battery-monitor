using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Rat9Battery
{
    internal sealed class V102BatterySample
    {
        public V102BatterySample(
            DateTime timestampUtc,
            int percentage,
            bool isCharging)
        {
            TimestampUtc = timestampUtc;
            Percentage = percentage;
            IsCharging = isCharging;
        }

        public DateTime TimestampUtc { get; private set; }
        public int Percentage { get; private set; }
        public bool IsCharging { get; private set; }
    }

    internal sealed class V102BatteryHistoryStore
    {
        private static readonly TimeSpan RecordInterval =
            TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Retention =
            TimeSpan.FromDays(30);

        private readonly List<V102BatterySample> _samples =
            new List<V102BatterySample>();
        private readonly string _historyPath;

        public V102BatteryHistoryStore()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MadCatzRat9BatteryTray");
            _historyPath = Path.Combine(
                directory,
                "v102-history-v1.txt");
            Load();
        }

        public IList<V102BatterySample> GetRecentSamples(
            TimeSpan period)
        {
            DateTime threshold = DateTime.UtcNow - period;
            List<V102BatterySample> result =
                new List<V102BatterySample>();
            foreach (V102BatterySample sample in _samples)
            {
                if (sample.TimestampUtc >= threshold)
                {
                    result.Add(sample);
                }
            }

            return result;
        }

        public void Record(DeviceBatteryReading reading)
        {
            if (reading == null ||
                !reading.HasPercentage ||
                reading.BatteryState ==
                    DeviceBatteryState.Unavailable ||
                reading.BatteryState ==
                    DeviceBatteryState.Error)
            {
                return;
            }

            DateTime timestamp = reading.MeasuredAtUtc.ToUniversalTime();
            if (_samples.Count > 0)
            {
                V102BatterySample previous =
                    _samples[_samples.Count - 1];
                if (previous.Percentage ==
                        reading.Percentage.Value &&
                    previous.IsCharging ==
                        reading.IsCharging &&
                    timestamp - previous.TimestampUtc <
                        RecordInterval)
                {
                    return;
                }
            }

            V102BatterySample sample = new V102BatterySample(
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

                    _samples.Add(new V102BatterySample(
                        new DateTime(ticks, DateTimeKind.Utc),
                        percentage,
                        parts[2] == "1"));
                }

                TrimOldSamples();
            }
            catch
            {
                // History is optional and must not prevent polling.
            }
        }

        private void Append(V102BatterySample sample)
        {
            try
            {
                string directory =
                    Path.GetDirectoryName(_historyPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|{1}|{2}",
                    sample.TimestampUtc.Ticks,
                    sample.Percentage,
                    sample.IsCharging ? 1 : 0);
                File.AppendAllText(
                    _historyPath,
                    line + Environment.NewLine);
            }
            catch
            {
                // Keep the in-memory history when disk writes fail.
            }
        }

        private void TrimOldSamples()
        {
            DateTime threshold = DateTime.UtcNow - Retention;
            int removeCount = 0;
            while (removeCount < _samples.Count &&
                _samples[removeCount].TimestampUtc < threshold)
            {
                removeCount++;
            }

            if (removeCount > 0)
            {
                _samples.RemoveRange(0, removeCount);
            }
        }
    }

    internal sealed class V102TimeEstimate
    {
        public bool HasEstimate { get; set; }
        public int LowMinutes { get; set; }
        public int HighMinutes { get; set; }
        public int ConfidencePercent { get; set; }

        public string FormatShort()
        {
            return HasEstimate
                ? V102TimeEstimator.FormatRange(
                    LowMinutes,
                    HighMinutes,
                    false)
                : "samlar mätdata";
        }

        public string FormatDetailed()
        {
            return HasEstimate
                ? V102TimeEstimator.FormatRange(
                    LowMinutes,
                    HighMinutes,
                    true)
                : "samlar mätdata";
        }
    }

    internal static class V102TimeEstimator
    {
        private const double MinimumObservedHours = 1.0 / 3.0;
        private const int MinimumDrop = 3;
        private const double MinimumPlausibleHours = 1.0;
        private const double MaximumPlausibleHours = 200.0;
        private static readonly TimeSpan MaximumGap =
            TimeSpan.FromHours(6);

        public static V102TimeEstimate Calculate(
            DeviceBatteryReading reading,
            IList<V102BatterySample> samples)
        {
            if (reading == null ||
                !reading.HasPercentage ||
                reading.IsCharging ||
                reading.Percentage.Value <= 0 ||
                samples == null)
            {
                return new V102TimeEstimate();
            }

            V102BatterySample earliest = null;
            V102BatterySample previous = null;
            foreach (V102BatterySample sample in samples)
            {
                if (sample.IsCharging ||
                    sample.Percentage < reading.Percentage.Value)
                {
                    earliest = null;
                    previous = sample;
                    continue;
                }

                if (previous != null &&
                    sample.TimestampUtc - previous.TimestampUtc >
                        MaximumGap)
                {
                    earliest = sample;
                }
                else if (earliest == null)
                {
                    earliest = sample;
                }

                previous = sample;
            }

            if (earliest == null)
            {
                return new V102TimeEstimate();
            }

            double observedHours =
                (reading.MeasuredAtUtc -
                    earliest.TimestampUtc).TotalHours;
            int observedDrop =
                earliest.Percentage -
                reading.Percentage.Value;
            if (observedHours < MinimumObservedHours ||
                observedDrop < MinimumDrop)
            {
                return new V102TimeEstimate();
            }

            double fullRuntimeHours =
                observedHours * 100.0 / observedDrop;
            if (fullRuntimeHours < MinimumPlausibleHours ||
                fullRuntimeHours > MaximumPlausibleHours)
            {
                return new V102TimeEstimate();
            }

            double remainingMinutes =
                fullRuntimeHours * 60.0 *
                reading.Percentage.Value / 100.0;
            int confidence = Math.Min(
                90,
                35 +
                observedDrop * 3 +
                (int)Math.Round(observedHours * 8));
            double uncertainty = confidence < 60
                ? 0.22
                : confidence < 78
                    ? 0.16
                    : 0.11;

            return new V102TimeEstimate
            {
                HasEstimate = true,
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
                ConfidencePercent = confidence
            };
        }

        internal static string FormatRange(
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

            string low = FormatDuration(lowMinutes);
            string high = FormatDuration(highMinutes);
            if (low == high)
            {
                return "ca " + low + " kvar";
            }

            return detailed
                ? "ca " + low + " – " + high + " kvar"
                : "ca " + low + "–" + high + " kvar";
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
}
