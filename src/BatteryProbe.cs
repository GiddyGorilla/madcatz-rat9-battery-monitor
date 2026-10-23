using System;

namespace Rat9Battery
{
    internal static class BatteryProbe
    {
        public static int Main()
        {
            SaitekAssemblyResolver.Install();
            BatteryReading reading = new Rat9BatteryReader().Read();
            BatteryHistoryStore historyStore = new BatteryHistoryStore();
            historyStore.Record(reading);
            BatteryTimeEstimate estimate =
                new BatteryTimeEstimator(
                    historyStore.GetRecentSamples(
                        TimeSpan.FromDays(14))).Calculate(reading);
            Console.WriteLine(reading.ToDisplayText());
            Console.WriteLine("Raw=" + reading.RawValue + "; Status=" + reading.Status);
            Console.WriteLine(
                string.IsNullOrEmpty(estimate.Format())
                    ? "Tid kvar: --"
                    : "Tid kvar: " + estimate.Format());
            return reading.Status == BatteryStatus.Ok ? 0 : 1;
        }
    }
}
