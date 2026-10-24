using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Rat9Battery
{
    internal static class BatteryWorkerHost
    {
        public static void Run()
        {
            Rat9BatteryReader reader = new Rat9BatteryReader();

            while (true)
            {
                try
                {
                    Console.WriteLine(
                        "READING|" + DateTime.UtcNow.Ticks);
                    Console.Out.Flush();

                    BatteryReading reading = reader.Read();
                    int waitMilliseconds =
                        reading.IsCharging ||
                        (reading.HasPercentage &&
                         reading.Percentage.Value <= 15)
                            ? 5000
                            : 12000;

                    string percentage = reading.Percentage.HasValue
                        ? reading.Percentage.Value.ToString(
                            CultureInfo.InvariantCulture)
                        : string.Empty;
                    string message = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(
                            reading.Message ?? string.Empty));
                    string data = string.Join(
                        "|",
                        "DATA",
                        ((int)reading.Status).ToString(
                            CultureInfo.InvariantCulture),
                        percentage,
                        reading.RawValue.ToString(
                            CultureInfo.InvariantCulture),
                        reading.IsCharging ? "1" : "0",
                        reading.MeasuredAt.Ticks.ToString(
                            CultureInfo.InvariantCulture),
                        message);
                    Console.WriteLine(data);
                    Console.WriteLine(
                        "WAIT|" + waitMilliseconds.ToString(
                            CultureInfo.InvariantCulture));
                    Console.Out.Flush();
                    Thread.Sleep(waitMilliseconds);
                }
                catch (Exception exception)
                {
                    string message = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(
                            exception.Message ?? "worker error"));
                    try
                    {
                        Console.WriteLine("FATAL|" + message);
                        Console.Out.Flush();
                    }
                    catch
                    {
                        return;
                    }

                    Thread.Sleep(2000);
                }
            }
        }
    }
}
