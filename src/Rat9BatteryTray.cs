// R.A.T. 9 tray and detail-popup application.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Rat9Battery
{
    internal static class Program
    {
        private const string SingleInstanceName =
            @"Local\MadCatzRat9BatteryTray";
        private const string ActivateEventName =
            @"Local\MadCatzRat9BatteryTrayActivate";
        internal const string BatteryWorkerArgument =
            "--battery-worker";

        [STAThread]
        private static void Main(string[] args)
        {
            SaitekAssemblyResolver.Install();

            if (args != null &&
                args.Length > 0 &&
                string.Equals(
                    args[0],
                    BatteryWorkerArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                BatteryWorkerHost.Run();
                return;
            }

            EnableDpiAwareness();

            using (Mutex instanceMutex = new Mutex(
                true,
                SingleInstanceName,
                out bool isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    try
                    {
                        using (EventWaitHandle activateEvent =
                            EventWaitHandle.OpenExisting(
                                ActivateEventName))
                        {
                            activateEvent.Set();
                        }
                    }
                    catch
                    {
                        // The first instance may still be starting.
                    }

                    return;
                }

                using (EventWaitHandle activateEvent =
                    new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        ActivateEventName))
                {
                    Application.ThreadException +=
                        delegate(object sender,
                            System.Threading.ThreadExceptionEventArgs args)
                        {
                            LogException(args.Exception);
                        };
                    AppDomain.CurrentDomain.UnhandledException +=
                        delegate(object sender,
                            UnhandledExceptionEventArgs args)
                        {
                            LogException(
                                args.ExceptionObject as Exception);
                        };
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(
                        new BatteryTrayContext(activateEvent));
                }
            }
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                NativeMethods.SetProcessDpiAwarenessContext(
                    new IntPtr(-4));
            }
            catch
            {
                NativeMethods.SetProcessDPIAware();
            }
        }

        internal static void LogWorkerException(Exception exception)
        {
            LogException(exception);
        }

        private static void LogException(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MadCatzRat9BatteryTray");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    Environment.NewLine +
                    exception +
                    Environment.NewLine +
                    Environment.NewLine);
            }
            catch
            {
                // Diagnostics must not trigger another failure.
            }
        }
    }

    internal sealed class BatteryTrayContext : ApplicationContext
    {
        private const string RunKey =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "MadCatzRat9BatteryTray";
        private const string SettingsKey =
            @"Software\MadCatzRat9BatteryTray";

        private readonly BatteryHistoryStore _historyStore;
        private readonly BatteryTimeEstimator _timeEstimator;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly Control _uiInvoker;
        private BatteryDetailForm _details;
        private readonly Thread _activationThread;
        private readonly System.Windows.Forms.Timer _workerWatchdog;
        private readonly EventWaitHandle _activateEvent;
        private readonly ToolStripMenuItem _showDetailsItem;
        private readonly ToolStripMenuItem _pinItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly HashSet<int> _alertedThresholds =
            new HashSet<int>();

        private Icon _currentIcon;
        private Process _batteryWorker;
        private BatteryReading _lastReading;
        private BatteryTimeEstimate _lastEstimate;
        private BatteryStatus? _previousStatus;
        private volatile bool _workerQueryInFlight;
        private long _workerQueryStartedUtcTicks;
        private bool _workerRestartScheduled;
        private bool _stopping;
        private bool _fullChargeNotified;

        public BatteryTrayContext(EventWaitHandle activateEvent)
        {
            _activateEvent = activateEvent;
            _historyStore = new BatteryHistoryStore();
            _timeEstimator = new BatteryTimeEstimator(
                _historyStore.GetRecentSamples(TimeSpan.FromDays(14)));

            _uiInvoker = new Control();
            _uiInvoker.CreateControl();

            _menu = new ContextMenuStrip();
            _showDetailsItem = new ToolStripMenuItem(
                "Öppna batteridetaljer",
                null,
                delegate { ToggleDetails(); });

            ToolStripMenuItem refreshItem = new ToolStripMenuItem(
                "Uppdatera nu",
                null,
                delegate { ScheduleWorkerRestart(0, false); });

            ToolStripMenuItem reconnectItem = new ToolStripMenuItem(
                "Återanslut musen",
                null,
                delegate { ScheduleWorkerRestart(0, true); });

            _pinItem = new ToolStripMenuItem(
                "Fäst detaljpanelen på skrivbordet",
                null,
                delegate { TogglePinned(); });
            _pinItem.Checked = IsPinned();

            _startupItem = new ToolStripMenuItem(
                "Starta med Windows",
                null,
                delegate { ToggleStartup(); });
            _startupItem.Checked = IsStartupEnabled();

            ToolStripMenuItem exitItem = new ToolStripMenuItem(
                "Avsluta",
                null,
                delegate { ExitApplication(); });

            _menu.Items.Add(_showDetailsItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(refreshItem);
            _menu.Items.Add(reconnectItem);
            _menu.Items.Add(_pinItem);
            _menu.Items.Add(_startupItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            EnsureDetails();

            _currentIcon = CreateBatteryIcon(null, false);
            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = _menu,
                Icon = _currentIcon,
                Text = "R.A.T. 9: läser batterinivå...",
                Visible = true
            };
            _notifyIcon.MouseClick += NotifyIconMouseClick;

            if (_pinItem.Checked)
            {
                _details.ShowPinned();
            }

            _workerWatchdog = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            _workerWatchdog.Tick += WorkerWatchdogTick;
            _workerWatchdog.Start();
            StartBatteryWorker();

            _activationThread = new Thread(ActivationLoop);
            _activationThread.IsBackground = true;
            _activationThread.Name = "R.A.T. 9 activation listener";
            _activationThread.Start();
        }

        private void WorkerWatchdogTick(object sender, EventArgs args)
        {
            if (_stopping || !_workerQueryInFlight)
            {
                return;
            }

            DateTime started = new DateTime(
                Interlocked.Read(ref _workerQueryStartedUtcTicks),
                DateTimeKind.Utc);
            if (DateTime.UtcNow - started < TimeSpan.FromSeconds(8))
            {
                return;
            }

            _workerQueryInFlight = false;
            ApplyReading(BatteryReading.Create(
                BatteryStatus.Unknown,
                null,
                -1,
                false,
                "återansluter musen"));
            ShowBalloon(
                "Batteriläsningen fastnade vid batteribytet. " +
                "Musanslutningen återställs automatiskt.",
                ToolTipIcon.Warning);
            ScheduleWorkerRestart(250, false);
        }

        private void StartBatteryWorker()
        {
            if (_stopping ||
                (_batteryWorker != null && !_batteryWorker.HasExited))
            {
                return;
            }

            Process process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = Program.BatteryWorkerArgument,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(
                object sender,
                DataReceivedEventArgs args)
            {
                HandleWorkerOutput(process, args.Data);
            };
            process.ErrorDataReceived += delegate(
                object sender,
                DataReceivedEventArgs args)
            {
                HandleWorkerError(process, args.Data);
            };
            process.Exited += delegate
            {
                HandleWorkerExited(process);
            };

            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    ScheduleWorkerRestart(1000, false);
                    return;
                }

                _batteryWorker = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                process.Dispose();
                Program.LogWorkerException(exception);
                ScheduleWorkerRestart(2000, false);
            }
        }

        private void HandleWorkerOutput(Process process, string line)
        {
            if (string.IsNullOrEmpty(line) ||
                !object.ReferenceEquals(_batteryWorker, process))
            {
                return;
            }

            if (line.StartsWith("READING|", StringComparison.Ordinal))
            {
                Interlocked.Exchange(
                    ref _workerQueryStartedUtcTicks,
                    DateTime.UtcNow.Ticks);
                _workerQueryInFlight = true;
                return;
            }

            if (line.StartsWith("WAIT|", StringComparison.Ordinal))
            {
                _workerQueryInFlight = false;
                return;
            }

            if (line.StartsWith("FATAL|", StringComparison.Ordinal))
            {
                _workerQueryInFlight = false;
                return;
            }

            if (!line.StartsWith("DATA|", StringComparison.Ordinal))
            {
                return;
            }

            _workerQueryInFlight = false;
            BatteryReading reading = ParseWorkerReading(line);
            if (reading == null)
            {
                return;
            }

            try
            {
                _uiInvoker.BeginInvoke(
                    new Action<BatteryReading>(ApplyReading),
                    reading);
            }
            catch (InvalidOperationException)
            {
                // The tray application is shutting down.
            }
        }

        private void HandleWorkerError(Process process, string line)
        {
            if (string.IsNullOrWhiteSpace(line) ||
                !object.ReferenceEquals(_batteryWorker, process))
            {
                return;
            }

            Program.LogWorkerException(
                new InvalidOperationException(line));
        }

        private void HandleWorkerExited(Process process)
        {
            if (!object.ReferenceEquals(_batteryWorker, process))
            {
                return;
            }

            _batteryWorker = null;
            _workerQueryInFlight = false;
            process.Dispose();

            if (!_stopping && !_workerRestartScheduled)
            {
                ScheduleWorkerRestart(500, false);
            }
        }

        private static BatteryReading ParseWorkerReading(string line)
        {
            string[] parts = line.Split(new[] { '|' }, 7);
            if (parts.Length != 7)
            {
                return null;
            }

            int statusValue;
            int rawValue;
            bool charging;
            long ticks;
            if (!int.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out statusValue) ||
                !int.TryParse(
                    parts[3],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out rawValue) ||
                !bool.TryParse(
                    parts[4] == "1" ? "true" : "false",
                    out charging) ||
                !long.TryParse(
                    parts[5],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out ticks))
            {
                return null;
            }

            int? percentage = null;
            if (!string.IsNullOrEmpty(parts[2]))
            {
                int parsedPercentage;
                if (!int.TryParse(
                        parts[2],
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out parsedPercentage))
                {
                    return null;
                }

                percentage = parsedPercentage;
            }

            string message;
            try
            {
                message = Encoding.UTF8.GetString(
                    Convert.FromBase64String(parts[6]));
            }
            catch (FormatException)
            {
                message = "kunde inte läsas";
            }

            return BatteryReading.Create(
                (BatteryStatus)statusValue,
                percentage,
                rawValue,
                charging,
                message,
                new DateTime(ticks, DateTimeKind.Local));
        }

        private void ScheduleWorkerRestart(
            int delayMilliseconds,
            bool notify)
        {
            if (_stopping || _workerRestartScheduled)
            {
                return;
            }

            _workerRestartScheduled = true;
            StopBatteryWorker();
            if (notify)
            {
                ShowBalloon(
                    "Startar om musanslutningen...",
                    ToolTipIcon.Info);
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                if (delayMilliseconds > 0)
                {
                    Thread.Sleep(delayMilliseconds);
                }

                if (_stopping)
                {
                    return;
                }

                try
                {
                    _uiInvoker.BeginInvoke(new Action(delegate
                    {
                        _workerRestartScheduled = false;
                        StartBatteryWorker();
                    }));
                }
                catch
                {
                    // The tray application is shutting down.
                }
            });
        }

        private void StopBatteryWorker()
        {
            Process process = _batteryWorker;
            _batteryWorker = null;
            _workerQueryInFlight = false;
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch
            {
                // The process may already have exited.
            }
            finally
            {
                process.Dispose();
            }
        }

        private void ActivationLoop()
        {
            while (!_stopping)
            {
                if (!_activateEvent.WaitOne(1000))
                {
                    continue;
                }

                try
                {
                    _uiInvoker.BeginInvoke(
                        new Action(ShowDetails));
                }
                catch
                {
                    return;
                }
            }
        }

        private void ApplyReading(BatteryReading reading)
        {
            if (_stopping || reading == null)
            {
                return;
            }

            _historyStore.Record(reading);
            _lastReading = reading;
            _lastEstimate = _timeEstimator.Calculate(reading);

            _details.UpdateReading(
                reading,
                _lastEstimate,
                _historyStore.GetRecentSamples(TimeSpan.FromHours(24)));

            Icon newIcon = CreateBatteryIcon(
                reading.Percentage,
                reading.IsCharging);
            _notifyIcon.Icon = newIcon;

            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
            }

            _currentIcon = newIcon;
            _notifyIcon.Text = TrimTooltip(BuildTooltip(
                reading,
                _lastEstimate));

            UpdateSmartAlerts(reading, _lastEstimate);
        }

        private static string BuildTooltip(
            BatteryReading reading,
            BatteryTimeEstimate estimate)
        {
            string tooltip = reading.ToDisplayText();
            string remaining = estimate != null
                ? estimate.FormatShort()
                : string.Empty;
            if (!string.IsNullOrEmpty(remaining))
            {
                tooltip += " · " + remaining;
            }

            return tooltip;
        }

        private void UpdateSmartAlerts(
            BatteryReading reading,
            BatteryTimeEstimate estimate)
        {
            bool disconnected =
                reading.Status == BatteryStatus.Disconnected ||
                reading.Status == BatteryStatus.Error;

            if (disconnected &&
                _previousStatus.HasValue &&
                _previousStatus.Value != BatteryStatus.Disconnected &&
                _previousStatus.Value != BatteryStatus.Error)
            {
                ShowBalloon(
                    "R.A.T. 9 tappade anslutningen.",
                    ToolTipIcon.Warning);
            }
            else if (!disconnected &&
                     _previousStatus.HasValue &&
                     (_previousStatus.Value == BatteryStatus.Disconnected ||
                      _previousStatus.Value == BatteryStatus.Error))
            {
                ShowBalloon(
                    "R.A.T. 9 är ansluten igen.",
                    ToolTipIcon.Info);
            }

            _previousStatus = reading.Status;

            if (!reading.HasPercentage)
            {
                return;
            }

            int percentage = reading.Percentage.Value;
            if (reading.IsCharging)
            {
                _alertedThresholds.Clear();
                if (percentage >= 99 && !_fullChargeNotified)
                {
                    _fullChargeNotified = true;
                    ShowBalloon(
                        "R.A.T. 9 är fulladdad.",
                        ToolTipIcon.Info);
                }
                else if (percentage < 97)
                {
                    _fullChargeNotified = false;
                }

                return;
            }

            _fullChargeNotified = false;
            int[] thresholds = { 20, 10, 5 };
            foreach (int threshold in thresholds)
            {
                if (percentage > threshold + 3)
                {
                    _alertedThresholds.Remove(threshold);
                }

                if (percentage <= threshold &&
                    !_alertedThresholds.Contains(threshold))
                {
                    _alertedThresholds.Add(threshold);
                    string remaining = estimate != null
                        ? estimate.FormatDetailed()
                        : string.Empty;
                    string text = "R.A.T. 9: " +
                        percentage + "% batteri kvar.";
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        text += "\r\n" + remaining;
                    }

                    ShowBalloon(
                        text,
                        threshold <= 10
                            ? ToolTipIcon.Warning
                            : ToolTipIcon.Info);
                }
            }
        }

        private void NotifyIconMouseClick(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left)
            {
                ToggleDetails();
            }
        }

        private void ToggleDetails()
        {
            if (_details.Visible)
            {
                _details.Hide();
            }
            else
            {
                ShowDetails();
            }
        }

        private void ShowDetails()
        {
            EnsureDetails();
            if (_lastReading != null)
            {
                _details.UpdateReading(
                    _lastReading,
                    _lastEstimate,
                    _historyStore.GetRecentSamples(
                        TimeSpan.FromHours(24)));
            }

            _details.ShowNearCursor();
            _showDetailsItem.Checked = true;
        }

        private void EnsureDetails()
        {
            if (_details != null && !_details.IsDisposed)
            {
                return;
            }

            _details = new BatteryDetailForm(_menu, _pinItem.Checked);
            _details.VisibilityChanged += DetailVisibilityChanged;
        }

        private void DetailVisibilityChanged(bool visible)
        {
            _showDetailsItem.Checked = visible;
        }

        private void TogglePinned()
        {
            bool pinned = !_pinItem.Checked;
            _pinItem.Checked = pinned;
            _details.SetPinned(pinned);
            SavePinned(pinned);

            if (pinned && !_details.Visible)
            {
                ShowDetails();
            }
        }

        private void ShowBalloon(string text, ToolTipIcon icon)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(
                    4000,
                    "Mad Catz R.A.T. 9",
                    text,
                    icon);
            }
            catch
            {
                // Notifications can be disabled by Windows policy.
            }
        }

        private static string TrimTooltip(string value)
        {
            const int maxLength = 63;
            if (value == null)
            {
                return string.Empty;
            }

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    RunKey,
                    true))
                {
                    if (_startupItem.Checked)
                    {
                        key.DeleteValue(RunValueName, false);
                        _startupItem.Checked = false;
                    }
                    else
                    {
                        key.SetValue(
                            RunValueName,
                            "\"" + Application.ExecutablePath + "\"",
                            RegistryValueKind.String);
                        _startupItem.Checked = true;
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Kunde inte ändra autostart:\r\n" + exception.Message,
                    "R.A.T. 9 batteri",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RunKey,
                    false))
                {
                    return key != null &&
                        key.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPinned()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    SettingsKey,
                    false))
                {
                    return key != null &&
                        Convert.ToInt32(
                            key.GetValue("DetailPinned", 0)) != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SavePinned(bool pinned)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    SettingsKey))
                {
                    key.SetValue(
                        "DetailPinned",
                        pinned ? 1 : 0,
                        RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Pinning is optional.
            }
        }

        private static Icon CreateBatteryIcon(
            int? percentage,
            bool isCharging)
        {
            int size = Math.Max(
                16,
                SystemInformation.SmallIconSize.Width);
            Bitmap bitmap = new Bitmap(
                size,
                size,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            float fontSize = size <= 16
                ? (percentage.HasValue && percentage.Value >= 100
                    ? 5.8f
                    : 7.7f)
                : (percentage.HasValue && percentage.Value >= 100
                    ? 8.7f
                    : 11.5f);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush background = new SolidBrush(
                GetIconBackground(percentage, isCharging)))
            using (Pen border = new Pen(Color.FromArgb(185, 255, 255, 255), 1))
            using (Font font = new Font(
                "Segoe UI",
                fontSize,
                FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (StringFormat format = new StringFormat())
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint =
                    TextRenderingHint.AntiAliasGridFit;

                Rectangle bounds = new Rectangle(
                    0,
                    0,
                    size - 1,
                    size - 1);
                graphics.FillEllipse(background, bounds);
                graphics.DrawEllipse(border, bounds);

                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                string text = percentage.HasValue
                    ? percentage.Value.ToString()
                    : "?";
                graphics.DrawString(
                    text,
                    font,
                    textBrush,
                    new RectangleF(0, 0, size, size),
                    format);

                if (isCharging)
                {
                    using (SolidBrush boltBrush = new SolidBrush(Color.White))
                    {
                        float scale = size / 32f;
                        PointF[] bolt =
                        {
                            new PointF(24 * scale, 3 * scale),
                            new PointF(19 * scale, 13 * scale),
                            new PointF(23 * scale, 13 * scale),
                            new PointF(18 * scale, 27 * scale),
                            new PointF(27 * scale, 11 * scale),
                            new PointF(23 * scale, 11 * scale)
                        };
                        graphics.FillPolygon(boltBrush, bolt);
                    }
                }
            }

            IntPtr iconHandle = bitmap.GetHicon();
            try
            {
                using (Icon temporaryIcon = Icon.FromHandle(iconHandle))
                {
                    return (Icon)temporaryIcon.Clone();
                }
            }
            finally
            {
                NativeMethods.DestroyIcon(iconHandle);
                bitmap.Dispose();
            }
        }

        private static Color GetIconBackground(
            int? percentage,
            bool isCharging)
        {
            if (isCharging)
            {
                return Color.FromArgb(25, 139, 92);
            }

            if (!percentage.HasValue)
            {
                return Color.FromArgb(100, 109, 122);
            }

            if (percentage.Value <= 15)
            {
                return Color.FromArgb(205, 67, 67);
            }

            if (percentage.Value <= 35)
            {
                return Color.FromArgb(211, 139, 35);
            }

            return Color.FromArgb(35, 110, 190);
        }

        private void ExitApplication()
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            _workerWatchdog.Stop();
            _workerWatchdog.Dispose();
            StopBatteryWorker();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
                _currentIcon = null;
            }

            if (_uiInvoker != null)
            {
                _uiInvoker.Dispose();
            }

            if (_details != null)
            {
                _details.Dispose();
            }
            _menu.Dispose();
            base.ExitThread();
        }
    }

    internal sealed class BatteryDetailForm : Form
    {
        private readonly ContextMenuStrip _menu;
        private BatteryReading _reading;
        private BatteryTimeEstimate _estimate;
        private IList<BatteryHistorySample> _history =
            new List<BatteryHistorySample>();
        private bool _pinned;
        private bool _dragging;
        private Point _dragStart;

        public event Action<bool> VisibilityChanged;

        public BatteryDetailForm(
            ContextMenuStrip menu,
            bool initiallyPinned)
        {
            _menu = menu;
            _pinned = initiallyPinned;

            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(24, 29, 37);
            ClientSize = new Size(390, 430);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Mad Catz R.A.T. 9 batteri";
            TopMost = true;

            SetRoundedRegion();
            Paint += DetailPaint;
            MouseDown += DetailMouseDown;
            MouseMove += DetailMouseMove;
            MouseUp += DetailMouseUp;
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape && !_pinned)
                {
                    Hide();
                }
            };
        }

        public void UpdateReading(
            BatteryReading reading,
            BatteryTimeEstimate estimate,
            IList<BatteryHistorySample> history)
        {
            _reading = reading;
            _estimate = estimate;
            _history = history ?? new List<BatteryHistorySample>();
            Invalidate();
        }

        public void SetPinned(bool pinned)
        {
            _pinned = pinned;
            Invalidate();
        }

        public void ShowNearCursor()
        {
            if (_pinned &&
                IsLocationOnScreen(Location, Size))
            {
                Show();
                BringToFront();
                return;
            }

            Point cursor = Cursor.Position;
            Rectangle workingArea = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X - Width / 2;
            int y = workingArea.Bottom - Height - 16;
            x = Math.Max(
                workingArea.Left + 8,
                Math.Min(workingArea.Right - Width - 8, x));
            y = Math.Max(
                workingArea.Top + 8,
                Math.Min(workingArea.Bottom - Height - 8, y));
            Location = new Point(x, y);
            Show();
            BringToFront();
            Activate();
        }

        public void ShowPinned()
        {
            if (!IsLocationOnScreen(Location, Size))
            {
                Rectangle workingArea =
                    Screen.PrimaryScreen.WorkingArea;
                Location = new Point(
                    workingArea.Right - Width - 24,
                    workingArea.Top + 24);
            }

            Show();
            BringToFront();
        }

        protected override void OnVisibleChanged(EventArgs args)
        {
            base.OnVisibleChanged(args);
            Action<bool> handler = VisibilityChanged;
            if (handler != null)
            {
                handler(Visible);
            }
        }

        protected override void OnDeactivate(EventArgs args)
        {
            base.OnDeactivate(args);
            if (!_pinned && Visible && !Focused)
            {
                Hide();
            }
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            SetRoundedRegion();
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (!_pinned && args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                Hide();
                return;
            }

            SavePosition();
            base.OnFormClosing(args);
        }

        private void SetRoundedRegion()
        {
            using (GraphicsPath path = CreateRoundedRectangle(
                new Rectangle(0, 0, Width, Height),
                20))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
        }

        private void DetailPaint(object sender, PaintEventArgs eventArgs)
        {
            Graphics graphics = eventArgs.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (LinearGradientBrush background =
                new LinearGradientBrush(
                    ClientRectangle,
                    Color.FromArgb(34, 41, 52),
                    Color.FromArgb(18, 22, 29),
                    55f))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            using (GraphicsPath borderPath = CreateRoundedRectangle(
                new Rectangle(0, 0, Width - 1, Height - 1),
                20))
            using (Pen edge = new Pen(
                Color.FromArgb(72, 255, 255, 255),
                1))
            {
                graphics.DrawPath(edge, borderPath);
            }

            Color accent = GetAccentColor(_reading);
            using (SolidBrush accentBrush = new SolidBrush(accent))
            {
                graphics.FillRectangle(accentBrush, 0, 20, 4, Height - 40);
            }

            DrawHeader(graphics, accent);
            DrawBatterySummary(graphics, accent);
            DrawStatistics(graphics);
            DrawHistory(graphics, accent);

            using (Font footerFont = new Font(
                "Segoe UI",
                8f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (SolidBrush footerBrush = new SolidBrush(
                Color.FromArgb(133, 146, 163)))
            {
                string updated = _reading != null
                    ? "Uppdaterad " +
                      _reading.MeasuredAt.ToString("HH:mm:ss")
                    : "Läser batteridata...";
                graphics.DrawString(
                    updated,
                    footerFont,
                    footerBrush,
                    24,
                    Height - 31);
                graphics.DrawString(
                    _pinned ? "FÄST" : "klick utanför stänger",
                    footerFont,
                    footerBrush,
                    Width - 124,
                    Height - 31);
            }
        }

        private void DrawHeader(Graphics graphics, Color accent)
        {
            using (Font titleFont = new Font(
                "Segoe UI",
                9f,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (SolidBrush titleBrush = new SolidBrush(
                Color.FromArgb(187, 197, 210)))
            using (SolidBrush dotBrush = new SolidBrush(accent))
            using (SolidBrush closeBrush = new SolidBrush(
                Color.FromArgb(160, 173, 190)))
            using (Font closeFont = new Font(
                "Segoe UI",
                13f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            {
                graphics.DrawString(
                    "MAD CATZ  R.A.T. 9",
                    titleFont,
                    titleBrush,
                    24,
                    19);
                graphics.FillEllipse(dotBrush, 198, 21, 8, 8);
                graphics.DrawString(
                    "×",
                    closeFont,
                    closeBrush,
                    Width - 36,
                    11);
            }
        }

        private void DrawBatterySummary(Graphics graphics, Color accent)
        {
            string percentageText = _reading != null &&
                _reading.HasPercentage
                ? _reading.Percentage.Value + "%"
                : "--";
            string status = GetStatusText(_reading);
            string remaining = _estimate != null
                ? _estimate.FormatShort()
                : string.Empty;

            using (Font percentageFont = new Font(
                "Segoe UI",
                32f,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (Font statusFont = new Font(
                "Segoe UI",
                10f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (Font remainingFont = new Font(
                "Segoe UI",
                9f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (SolidBrush valueBrush = new SolidBrush(
                Color.FromArgb(246, 248, 251)))
            using (SolidBrush accentBrush = new SolidBrush(accent))
            using (SolidBrush mutedBrush = new SolidBrush(
                Color.FromArgb(190, 201, 216)))
            {
                graphics.DrawString(
                    percentageText,
                    percentageFont,
                    valueBrush,
                    20,
                    43);
                graphics.DrawString(
                    status,
                    statusFont,
                    accentBrush,
                    148,
                    55);
                if (!string.IsNullOrEmpty(remaining))
                {
                    graphics.DrawString(
                        remaining,
                        remainingFont,
                        mutedBrush,
                        148,
                        79);
                }
            }

            int left = 24;
            int top = 113;
            int width = Width - 48;
            int height = 8;

            using (GraphicsPath trackPath = CreateRoundedRectangle(
                new Rectangle(left, top, width, height),
                height))
            using (SolidBrush trackBrush = new SolidBrush(
                Color.FromArgb(58, 67, 81)))
            {
                graphics.FillPath(trackBrush, trackPath);
            }

            int percentage = _reading != null &&
                _reading.HasPercentage
                ? Math.Max(0, Math.Min(
                    100,
                    _reading.Percentage.Value))
                : 0;
            int fillWidth = (int)Math.Round(width * percentage / 100.0);
            if (fillWidth > 0)
            {
                using (GraphicsPath fillPath = CreateRoundedRectangle(
                    new Rectangle(
                        left,
                        top,
                        Math.Max(height, fillWidth),
                        height),
                    height))
                using (SolidBrush fillBrush = new SolidBrush(accent))
                {
                    graphics.FillPath(fillBrush, fillPath);
                }
            }
        }

        private void DrawStatistics(Graphics graphics)
        {
            using (Pen separator = new Pen(
                Color.FromArgb(44, 255, 255, 255),
                1))
            {
                graphics.DrawLine(separator, 24, 139, Width - 24, 139);
            }

            string timeText = _estimate != null
                ? _estimate.FormatDetailed()
                : "väntar på batteridata";
            string fullRuntime = _estimate != null &&
                _estimate.EffectiveFullRuntimeMinutes.HasValue
                ? FormatMinutes(
                    _estimate.EffectiveFullRuntimeMinutes.Value)
                : "9 timmar (specifikation)";
            string capacity = _estimate != null &&
                _estimate.CapacityPercent.HasValue
                ? "ca " + _estimate.CapacityPercent.Value +
                  "% av specifikationen"
                : "samlar mätdata";
            string source = _estimate != null
                ? _estimate.SourceText
                : "startar";

            DrawStatRow(graphics, 154, "Tid kvar", timeText);
            DrawStatRow(graphics, 178, "Full uthållighet", fullRuntime);
            DrawStatRow(graphics, 202, "Beräknad kapacitet", capacity);
            DrawStatRow(graphics, 226, "Underlag", source);
        }

        private static void DrawStatRow(
            Graphics graphics,
            int y,
            string label,
            string value)
        {
            using (Font labelFont = new Font(
                "Segoe UI",
                8.5f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (Font valueFont = new Font(
                "Segoe UI",
                8.5f,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (SolidBrush labelBrush = new SolidBrush(
                Color.FromArgb(142, 155, 172)))
            using (SolidBrush valueBrush = new SolidBrush(
                Color.FromArgb(224, 230, 238)))
            {
                graphics.DrawString(label, labelFont, labelBrush, 24, y);
                graphics.DrawString(value, valueFont, valueBrush, 150, y);
            }
        }

        private void DrawHistory(Graphics graphics, Color accent)
        {
            using (Font historyTitleFont = new Font(
                "Segoe UI",
                7.5f,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (SolidBrush mutedBrush = new SolidBrush(
                Color.FromArgb(129, 143, 161)))
            {
                graphics.DrawString(
                    "BATTERIHISTORIK  ·  24 TIM",
                    historyTitleFont,
                    mutedBrush,
                    24,
                    252);
            }

            Rectangle chart = new Rectangle(
                24,
                273,
                Width - 48,
                108);

            using (Pen gridPen = new Pen(
                Color.FromArgb(34, 255, 255, 255),
                1))
            {
                gridPen.DashStyle = DashStyle.Dot;
                graphics.DrawLine(
                    gridPen,
                    chart.Left,
                    chart.Top,
                    chart.Right,
                    chart.Top);
                graphics.DrawLine(
                    gridPen,
                    chart.Left,
                    chart.Top + chart.Height / 2,
                    chart.Right,
                    chart.Top + chart.Height / 2);
                graphics.DrawLine(
                    gridPen,
                    chart.Left,
                    chart.Bottom,
                    chart.Right,
                    chart.Bottom);
            }

            DateTime end = _reading != null
                ? _reading.MeasuredAt
                : DateTime.Now;
            DateTime start = end - TimeSpan.FromHours(24);
            List<BatteryHistorySample> samples =
                new List<BatteryHistorySample>();
            foreach (BatteryHistorySample sample in _history)
            {
                if (sample.Timestamp >= start &&
                    sample.Timestamp <= end)
                {
                    samples.Add(sample);
                }
            }

            if (samples.Count == 0 && _reading != null &&
                _reading.HasPercentage)
            {
                samples.Add(new BatteryHistorySample(
                    end,
                    _reading.Percentage.Value,
                    _reading.IsCharging));
            }

            if (samples.Count < 2)
            {
                using (Font emptyFont = new Font(
                    "Segoe UI",
                    9f,
                    FontStyle.Regular,
                    GraphicsUnit.Point))
                using (SolidBrush emptyBrush = new SolidBrush(
                    Color.FromArgb(151, 164, 181)))
                {
                    graphics.DrawString(
                        "Historiken börjar fyllas automatiskt.",
                        emptyFont,
                        emptyBrush,
                        chart.Left + 12,
                        chart.Top + 40);
                }
            }
            else
            {
                double totalSeconds =
                    Math.Max(1, (end - start).TotalSeconds);
                Point previousPoint = Point.Empty;
                bool hasPrevious = false;

                foreach (BatteryHistorySample sample in samples)
                {
                    double xRatio =
                        (sample.Timestamp - start).TotalSeconds /
                        totalSeconds;
                    int x = chart.Left + (int)Math.Round(
                        chart.Width * xRatio);
                    int y = chart.Bottom - (int)Math.Round(
                        chart.Height * sample.Percentage / 100.0);
                    Point currentPoint = new Point(x, y);

                    if (hasPrevious)
                    {
                        Color lineColor = sample.IsCharging
                            ? Color.FromArgb(75, 203, 139)
                            : accent;
                        using (Pen dataPen = new Pen(lineColor, 2f))
                        {
                            dataPen.LineJoin = LineJoin.Round;
                            graphics.DrawLine(
                                dataPen,
                                previousPoint,
                                currentPoint);
                        }
                    }

                    previousPoint = currentPoint;
                    hasPrevious = true;
                }

                using (SolidBrush pointBrush = new SolidBrush(accent))
                {
                    graphics.FillEllipse(
                        pointBrush,
                        previousPoint.X - 3,
                        previousPoint.Y - 3,
                        6,
                        6);
                }
            }

            using (Font axisFont = new Font(
                "Segoe UI",
                7.5f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (SolidBrush axisBrush = new SolidBrush(
                Color.FromArgb(121, 135, 153)))
            {
                graphics.DrawString("24 t sedan", axisFont, axisBrush, 24, 385);
                graphics.DrawString("nu", axisFont, axisBrush, Width - 46, 385);
            }
        }

        private static string GetStatusText(BatteryReading reading)
        {
            if (reading == null)
            {
                return "läser...";
            }

            if (reading.IsCharging)
            {
                return "laddar";
            }

            if (reading.Status == BatteryStatus.Empty)
            {
                return "tomt batteri";
            }

            if (reading.Status == BatteryStatus.Ok)
            {
                return "batteri";
            }

            if (reading.Status == BatteryStatus.Disconnected)
            {
                return "hittades inte";
            }

            if (reading.Status == BatteryStatus.Unknown)
            {
                return "ingen batteridata";
            }

            return "kunde inte läsa";
        }

        private static Color GetAccentColor(BatteryReading reading)
        {
            if (reading != null && reading.IsCharging)
            {
                return Color.FromArgb(70, 211, 144);
            }

            if (reading == null || !reading.HasPercentage)
            {
                return Color.FromArgb(141, 153, 170);
            }

            if (reading.Percentage.Value <= 15)
            {
                return Color.FromArgb(238, 105, 105);
            }

            if (reading.Percentage.Value <= 35)
            {
                return Color.FromArgb(233, 171, 71);
            }

            return Color.FromArgb(84, 169, 238);
        }

        private static string FormatMinutes(int minutes)
        {
            int rounded = (int)(Math.Round(minutes / 5.0) * 5);
            int hours = rounded / 60;
            int remainder = rounded % 60;
            if (hours > 0 && remainder == 0)
            {
                return hours + " timmar";
            }

            if (hours > 0)
            {
                return hours + " t " + remainder + " min";
            }

            return rounded + " minuter";
        }

        private void DetailMouseDown(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left)
            {
                return;
            }

            if (new Rectangle(Width - 46, 8, 40, 34).Contains(args.Location))
            {
                Hide();
                return;
            }

            if (_pinned && args.Y < 42)
            {
                _dragging = true;
                _dragStart = args.Location;
                Cursor = Cursors.SizeAll;
            }
        }

        private void DetailMouseMove(object sender, MouseEventArgs args)
        {
            if (!_dragging)
            {
                return;
            }

            Point screenPoint = PointToScreen(args.Location);
            Location = new Point(
                screenPoint.X - _dragStart.X,
                screenPoint.Y - _dragStart.Y);
        }

        private void DetailMouseUp(object sender, MouseEventArgs args)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Cursor = Cursors.Default;
            SavePosition();
        }

        private void SavePosition()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"Software\MadCatzRat9BatteryTray"))
                {
                    key.SetValue(
                        "DetailX",
                        Location.X,
                        RegistryValueKind.DWord);
                    key.SetValue(
                        "DetailY",
                        Location.Y,
                        RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Saving the location is optional.
            }
        }

        private static bool IsLocationOnScreen(Point point, Size size)
        {
            Rectangle candidate = new Rectangle(point, size);
            if (candidate.X == 0 && candidate.Y == 0)
            {
                return false;
            }

            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle intersection = Rectangle.Intersect(
                    screen.WorkingArea,
                    candidate);
                if (intersection.Width >= 100 &&
                    intersection.Height >= 100)
                {
                    return true;
                }
            }

            return false;
        }

        private static GraphicsPath CreateRoundedRectangle(
            Rectangle bounds,
            int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(
                bounds.Width,
                bounds.Height)));
            Rectangle arc = new Rectangle(
                bounds.Location,
                new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDpiAwarenessContext(
            IntPtr dpiContext);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr handle);
    }
}
