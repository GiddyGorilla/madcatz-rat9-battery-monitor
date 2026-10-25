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
using Rat9Battery.V102;

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
        private const string RecoveryTaskName = "MadCatzRat9Recovery";

        private readonly BatteryHistoryStore _historyStore;
        private readonly BatteryTimeEstimator _timeEstimator;
        private readonly V102BatteryHistoryStore _v102HistoryStore;
        private readonly V102BatterySource _v102Source;
        private readonly KeyboardProfileStore _profileStore;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly Control _uiInvoker;
        private BatteryDetailForm _details;
        private V102LightingForm _lightingForm;
        private readonly Thread _activationThread;
        private readonly System.Windows.Forms.Timer _workerWatchdog;
        private readonly System.Windows.Forms.Timer _v102PollTimer;
        private readonly System.Windows.Forms.Timer _gameProfileTimer;
        private readonly EventWaitHandle _activateEvent;
        private readonly ToolStripMenuItem _showDetailsItem;
        private readonly ToolStripMenuItem _pinItem;
        private readonly ToolStripMenuItem _swapModeItem;
        private readonly ToolStripMenuItem _recoveryItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _lightingItem;
        private readonly HashSet<int> _alertedThresholds =
            new HashSet<int>();
        private readonly HashSet<int> _v102AlertedThresholds =
            new HashSet<int>();

        private Icon _currentIcon;
        private Process _batteryWorker;
        private BatteryReading _lastReading;
        private BatteryTimeEstimate _lastEstimate;
        private DeviceBatteryReading _lastV102Reading;
        private V102TimeEstimate _lastV102Estimate;
        private BatteryStatus? _previousStatus;
        private volatile bool _workerQueryInFlight;
        private long _workerQueryStartedUtcTicks;
        private bool _workerRestartScheduled;
        private int _workerRestartGeneration;
        private bool _batterySwapMode;
        private bool _recoveryTaskStarted;
        private bool _stopping;
        private bool _fullChargeNotified;
        private bool _v102FullChargeNotified;
        private int _v102PollInFlight;
        private int _gameProfileScanInFlight;
        private int _gameProfileApplyInFlight;
        private V102ProfilesDocument _profilesDocument;
        private GameProfileMonitor _gameProfileMonitor;
        private string _lastAppliedGameProfileId;
        private string _manualProfileOverrideId;
        private DateTime _gameProfileRetryAfterUtc;

        public BatteryTrayContext(EventWaitHandle activateEvent)
        {
            _activateEvent = activateEvent;
            _historyStore = new BatteryHistoryStore();
            _timeEstimator = new BatteryTimeEstimator(
                _historyStore.GetRecentSamples(TimeSpan.FromDays(14)));
            _v102HistoryStore = new V102BatteryHistoryStore();
            _v102Source = new V102BatterySource();
            _profileStore = new KeyboardProfileStore();
            _profilesDocument = _profileStore.LoadOrCreate();
            _gameProfileMonitor = new GameProfileMonitor(
                _profilesDocument.Profiles,
                _profilesDocument.BaseProfileId);

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
                delegate
                {
                    CancelScheduledWorkerRestart();
                    _batterySwapMode = false;
                    UpdateSwapModeItem();
                    ScheduleWorkerRestart(0, true);
                });

            _swapModeItem = new ToolStripMenuItem(
                "Batteribyte: pausa läsaren",
                null,
                delegate { ToggleBatterySwapMode(); });

            _recoveryItem = new ToolStripMenuItem(
                IsRecoveryTaskInstalled()
                    ? "Återställ musanslutningen (admin)"
                    : "Installera snabb musåterställning",
                null,
                delegate { HandleRecoveryAction(); });

            _pinItem = new ToolStripMenuItem(
                "Fäst detaljpanelen på skrivbordet",
                null,
                delegate { TogglePinned(); });
            _pinItem.Checked = IsPinned();

            _lightingItem = new ToolStripMenuItem(
                "V102 RGB och spelprofiler",
                null,
                delegate { OpenLightingWindow(); });

            _startupItem = new ToolStripMenuItem(
                "Starta med Windows",
                null,
                delegate { ToggleStartup(); });
            _startupItem.Checked = IsStartupEnabled();
            EnsureStartupPathCurrent();

            ToolStripMenuItem exitItem = new ToolStripMenuItem(
                "Avsluta",
                null,
                delegate { ExitApplication(); });

            _menu.Items.Add(_showDetailsItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(refreshItem);
            _menu.Items.Add(reconnectItem);
            _menu.Items.Add(_swapModeItem);
            _menu.Items.Add(_recoveryItem);
            _menu.Items.Add(_lightingItem);
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

            _v102PollTimer = new System.Windows.Forms.Timer
            {
                Interval = 10000
            };
            _v102PollTimer.Tick += V102PollTimerTick;
            _v102PollTimer.Start();
            QueueV102Poll();

            _gameProfileTimer = new System.Windows.Forms.Timer
            {
                Interval = 2000
            };
            _gameProfileTimer.Tick += delegate
            {
                QueueGameProfileScan();
            };
            _gameProfileTimer.Start();

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
            if (!_recoveryTaskStarted &&
                IsRecoveryTaskInstalled())
            {
                _recoveryTaskStarted = true;
                ApplyReading(BatteryReading.Create(
                    BatteryStatus.Unknown,
                    null,
                    -1,
                    false,
                    "startar om musanslutningen"));
                ShowBalloon(
                    "Muslänken fastnade. Startar om Mad Catz-enheten " +
                    "utan att starta om datorn.",
                    ToolTipIcon.Warning);
                ScheduleWorkerRestart(30000, false);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(1000);
                    RunRecoveryTask();
                });
                return;
            }

            ApplyReading(BatteryReading.Create(
                BatteryStatus.Unknown,
                null,
                -1,
                false,
                "väntar på musanslutningen"));
            ShowBalloon(
                "Batteriläsningen fastnade. HID-handtaget släpps " +
                "och musen får 15 sekunder att återansluta.",
                ToolTipIcon.Warning);
            ScheduleWorkerRestart(15000, false);
        }

        private void V102PollTimerTick(
            object sender,
            EventArgs args)
        {
            QueueV102Poll();
        }

        private void QueueV102Poll()
        {
            if (_stopping ||
                Interlocked.CompareExchange(
                    ref _v102PollInFlight,
                    1,
                    0) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceBatteryReading reading = null;
                try
                {
                    reading = _v102Source.ReadBattery();
                }
                finally
                {
                    Interlocked.Exchange(
                        ref _v102PollInFlight,
                        0);
                }

                if (_stopping || reading == null)
                {
                    return;
                }

                try
                {
                    _uiInvoker.BeginInvoke(
                        new Action<DeviceBatteryReading>(
                            ApplyV102Reading),
                        reading);
                }
                catch
                {
                    // The tray is shutting down.
                }
            });
        }

        private void ApplyV102Reading(
            DeviceBatteryReading reading)
        {
            if (_stopping || reading == null)
            {
                return;
            }

            bool wasConnected =
                _lastV102Reading != null &&
                _lastV102Reading.ConnectionState ==
                    DeviceConnectionState.Connected;
            bool wasDisconnected =
                _lastV102Reading != null &&
                _lastV102Reading.ConnectionState ==
                    DeviceConnectionState.Disconnected;
            _lastV102Reading = reading;
            if (reading.ConnectionState ==
                    DeviceConnectionState.Connected &&
                !wasConnected)
            {
                _lastAppliedGameProfileId = null;
                if (wasDisconnected)
                {
                    ShowBalloon(
                        "V102 Pro är återansluten.",
                        ToolTipIcon.Info);
                }
            }
            else if (wasConnected &&
                reading.ConnectionState ==
                    DeviceConnectionState.Disconnected)
            {
                ShowBalloon(
                    "V102 Pro tappade förbindelsen. Kontrollera " +
                    "2,4 GHz-dongeln.",
                    ToolTipIcon.Warning);
            }
            _v102HistoryStore.Record(reading);
            _lastV102Estimate = V102TimeEstimator.Calculate(
                reading,
                _v102HistoryStore.GetRecentSamples(
                    TimeSpan.FromDays(14)));

            if (_details != null)
            {
                _details.UpdateV102Reading(
                    reading,
                    _lastV102Estimate,
                    _v102HistoryStore.GetRecentSamples(
                        TimeSpan.FromHours(24)));
            }

            RefreshTrayPresentation();
            UpdateV102Alerts(reading);
        }

        private void UpdateV102Alerts(
            DeviceBatteryReading reading)
        {
            if (reading == null || !reading.HasPercentage)
            {
                return;
            }

            if (reading.IsCharging || reading.IsFull)
            {
                _v102AlertedThresholds.Clear();
            }

            if (reading.IsFull && !_v102FullChargeNotified)
            {
                _v102FullChargeNotified = true;
                ShowBalloon(
                    "V102 Pro-batteriet är fullt.",
                    ToolTipIcon.Info);
            }
            else if (!reading.IsFull)
            {
                _v102FullChargeNotified = false;
            }

            if (reading.IsCharging)
            {
                return;
            }

            int percentage = reading.Percentage.Value;
            foreach (int threshold in new[] { 20, 10, 5 })
            {
                if (percentage <= threshold &&
                    _v102AlertedThresholds.Add(threshold))
                {
                    ShowBalloon(
                        "V102 Pro-batteriet är på " +
                        percentage + "%.",
                        threshold <= 10
                            ? ToolTipIcon.Error
                            : ToolTipIcon.Warning);
                }
            }
        }

        private void QueueGameProfileScan()
        {
            if (_stopping ||
                Interlocked.CompareExchange(
                    ref _gameProfileScanInFlight,
                    1,
                    0) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                IList<GameProcessInfo> processes = null;
                int? foregroundProcessId = null;
                try
                {
                    processes = RunningProcessSnapshot.Capture();
                    foregroundProcessId =
                        RunningProcessSnapshot
                            .GetForegroundProcessId();
                }
                catch
                {
                    // A process can exit while its module is enumerated.
                }
                finally
                {
                    Interlocked.Exchange(
                        ref _gameProfileScanInFlight,
                        0);
                }

                if (_stopping)
                {
                    return;
                }

                try
                {
                    _uiInvoker.BeginInvoke(
                        new Action<
                            IList<GameProcessInfo>,
                            int?>(HandleGameSnapshot),
                        processes,
                        foregroundProcessId);
                }
                catch
                {
                    // The tray is shutting down.
                }
            });
        }

        private void HandleGameSnapshot(
            IList<GameProcessInfo> processes,
            int? foregroundProcessId)
        {
            if (_stopping || _gameProfileMonitor == null)
            {
                return;
            }

            GameProfileDecision decision =
                _gameProfileMonitor.Evaluate(
                processes,
                foregroundProcessId);
            if (!string.Equals(
                decision.ActiveProfileId,
                _profilesDocument.BaseProfileId,
                StringComparison.OrdinalIgnoreCase))
            {
                _manualProfileOverrideId = null;
            }
            else if (!decision.Changed &&
                !string.IsNullOrEmpty(
                    _manualProfileOverrideId))
            {
                return;
            }

            TryApplyActiveGameProfile();
        }

        private void TryApplyActiveGameProfile()
        {
            if (_stopping ||
                _gameProfileMonitor == null ||
                _lastV102Reading == null ||
                _lastV102Reading.ConnectionState !=
                    DeviceConnectionState.Connected ||
                DateTime.UtcNow < _gameProfileRetryAfterUtc ||
                Interlocked.CompareExchange(
                    ref _gameProfileApplyInFlight,
                    1,
                    0) != 0)
            {
                return;
            }

            string profileId =
                _gameProfileMonitor.ActiveProfileId;
            V102LightingProfile profile =
                FindProfile(profileId);
            if (profile == null ||
                string.Equals(
                    _lastAppliedGameProfileId,
                    profile.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Exchange(
                    ref _gameProfileApplyInFlight,
                    0);
                return;
            }

            V102LightingProfile copy = profile.Clone();
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool success = false;
                string error = null;
                try
                {
                    _v102Source.ApplyLightingProfile(copy);
                    success = true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
                finally
                {
                    Interlocked.Exchange(
                        ref _gameProfileApplyInFlight,
                        0);
                }

                try
                {
                    _uiInvoker.BeginInvoke(
                        new Action(delegate
                        {
                            if (_stopping)
                            {
                                return;
                            }

                            if (success)
                            {
                                _lastAppliedGameProfileId =
                                    copy.Id;
                                _lightingItem.Text =
                                    "V102 RGB · aktiv: " +
                                    copy.Name;
                            }
                            else
                            {
                                _gameProfileRetryAfterUtc =
                                    DateTime.UtcNow +
                                    TimeSpan.FromSeconds(15);
                                _lightingItem.Text =
                                    "V102 RGB · väntar på anslutning";
                                Program.LogWorkerException(
                                    new V102HidException(
                                        error ?? "unknown error"));
                            }
                        }));
                }
                catch
                {
                    // The tray is shutting down.
                }
            });
        }

        private V102LightingProfile FindProfile(
            string profileId)
        {
            if (_profilesDocument == null ||
                _profilesDocument.Profiles == null)
            {
                return null;
            }

            foreach (V102LightingProfile profile in
                _profilesDocument.Profiles)
            {
                if (string.Equals(
                    profile.Id,
                    profileId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        private void HandleProfilesChanged()
        {
            _gameProfileMonitor = new GameProfileMonitor(
                _profilesDocument.Profiles,
                _profilesDocument.BaseProfileId);
            _lastAppliedGameProfileId = null;
            _manualProfileOverrideId = null;
            _gameProfileRetryAfterUtc = DateTime.MinValue;
            QueueGameProfileScan();
        }

        private void ApplyLightingManually(
            V102LightingProfile profile)
        {
            _v102Source.ApplyLightingProfile(profile);
            try
            {
                _uiInvoker.BeginInvoke(new Action(delegate
                {
                    if (_stopping)
                    {
                        return;
                    }

                    _lastAppliedGameProfileId = profile.Id;
                    _manualProfileOverrideId =
                        string.Equals(
                            profile.Id,
                            _profilesDocument.BaseProfileId,
                            StringComparison.OrdinalIgnoreCase)
                            ? null
                            : profile.Id;
                    _lightingItem.Text =
                        "V102 RGB · aktiv: " + profile.Name;
                }));
            }
            catch
            {
                // The tray is shutting down.
            }
        }

        private void OpenLightingWindow()
        {
            if (_lightingForm == null ||
                _lightingForm.IsDisposed)
            {
                _lightingForm = new V102LightingForm(
                    _profileStore,
                    _profilesDocument,
                    _v102Source.ReadKeyboardConfig,
                    ApplyLightingManually,
                    HandleProfilesChanged);
                _lightingForm.FormClosed += delegate
                {
                    _lightingForm = null;
                };
            }

            if (!_lightingForm.Visible)
            {
                _lightingForm.Show();
            }
            else
            {
                _lightingForm.Activate();
            }
        }

        private void StartBatteryWorker()
        {
            if (_stopping ||
                _batterySwapMode ||
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

            if (!_stopping &&
                !_batterySwapMode &&
                !_workerRestartScheduled)
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
            if (_stopping ||
                _batterySwapMode ||
                _workerRestartScheduled)
            {
                return;
            }

            _workerRestartScheduled = true;
            int generation = ++_workerRestartGeneration;
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
                        if (generation != _workerRestartGeneration ||
                            _stopping ||
                            _batterySwapMode)
                        {
                            return;
                        }

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

        private void CancelScheduledWorkerRestart()
        {
            _workerRestartGeneration++;
            _workerRestartScheduled = false;
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

        private void ToggleBatterySwapMode()
        {
            if (_batterySwapMode)
            {
                _batterySwapMode = false;
                UpdateSwapModeItem();
                ApplyReading(BatteryReading.Create(
                    BatteryStatus.Unknown,
                    null,
                    -1,
                    false,
                    "väntar på batteriläsaren"));
                ShowBalloon(
                    "Batteribyte slutfört. Väntar 10 sekunder " +
                    "innan musanslutningen läses igen.",
                    ToolTipIcon.Info);
                ScheduleWorkerRestart(10000, false);
                return;
            }

            CancelScheduledWorkerRestart();
            _batterySwapMode = true;
            UpdateSwapModeItem();
            StopBatteryWorker();
            ApplyReading(BatteryReading.Create(
                BatteryStatus.Unknown,
                null,
                -1,
                false,
                "läsaren är pausad inför batteribyte"));
            ShowBalloon(
                "Batteribyte-läget är aktivt. Byt batteri och välj " +
                "samma menyval igen när musen är på.",
                ToolTipIcon.Info);
        }

        private void UpdateSwapModeItem()
        {
            if (_swapModeItem == null)
            {
                return;
            }

            _swapModeItem.Checked = _batterySwapMode;
            _swapModeItem.Text = _batterySwapMode
                ? "Batteribyte: återanslut efter bytet"
                : "Batteribyte: pausa läsaren";
        }

        private void HandleRecoveryAction()
        {
            if (IsRecoveryTaskInstalled())
            {
                if (RunRecoveryTask())
                {
                    ShowBalloon(
                        "Återställer Mad Catz-enheten. Appen startas om " +
                        "automatiskt.",
                        ToolTipIcon.Info);
                }

                return;
            }

            string toolPath = Path.Combine(
                Application.StartupPath,
                "RAT9RecoveryTool.exe");
            if (!File.Exists(toolPath))
            {
                MessageBox.Show(
                    "RAT9RecoveryTool.exe saknas bredvid batteriappen.",
                    "R.A.T. 9",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = "--install",
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                ShowBalloon(
                    "Installationen avbröts.",
                    ToolTipIcon.Info);
            }
        }

        private static bool RunRecoveryTask()
        {
            try
            {
                using (Process process = Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/Run /TN \"" +
                            RecoveryTaskName + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }))
                {
                    process.WaitForExit(5000);
                    return process.HasExited &&
                        process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRecoveryTaskInstalled()
        {
            try
            {
                using (Process process = Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/Query /TN \"" +
                            RecoveryTaskName + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }))
                {
                    process.WaitForExit(5000);
                    return process.HasExited &&
                        process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
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

            RefreshTrayPresentation();

            UpdateSmartAlerts(reading, _lastEstimate);
        }

        private void RefreshTrayPresentation()
        {
            int? lowestPercentage = null;
            bool selectedIsCharging = false;

            if (_lastReading != null &&
                _lastReading.HasPercentage)
            {
                lowestPercentage = _lastReading.Percentage;
                selectedIsCharging = _lastReading.IsCharging;
            }

            if (_lastV102Reading != null &&
                _lastV102Reading.HasPercentage &&
                (!lowestPercentage.HasValue ||
                 _lastV102Reading.Percentage.Value <
                    lowestPercentage.Value))
            {
                lowestPercentage =
                    _lastV102Reading.Percentage;
                selectedIsCharging =
                    _lastV102Reading.IsCharging;
            }

            Icon newIcon = CreateBatteryIcon(
                lowestPercentage,
                selectedIsCharging);
            _notifyIcon.Icon = newIcon;
            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
            }

            _currentIcon = newIcon;
            _notifyIcon.Text = TrimTooltip(
                BuildCombinedTooltip());
        }

        private string BuildCombinedTooltip()
        {
            List<string> parts = new List<string>();
            if (_lastReading != null)
            {
                string rat9 = _lastReading.ToDisplayText();
                string rat9Time = _lastEstimate != null
                    ? _lastEstimate.FormatShort()
                    : string.Empty;
                if (!string.IsNullOrEmpty(rat9Time))
                {
                    rat9 += " · " + rat9Time;
                }

                parts.Add(rat9);
            }

            if (_lastV102Reading != null)
            {
                string v102 = FormatV102Reading(
                    _lastV102Reading);
                if (_lastV102Estimate != null &&
                    _lastV102Estimate.HasEstimate)
                {
                    v102 += " · " +
                        _lastV102Estimate.FormatShort();
                }

                parts.Add(v102);
            }

            return parts.Count == 0
                ? "Läser batteridata..."
                : string.Join(" · ", parts.ToArray());
        }

        private static string FormatV102Reading(
            DeviceBatteryReading reading)
        {
            if (reading == null || !reading.HasPercentage)
            {
                return "V102 Pro: " +
                    (reading == null || string.IsNullOrEmpty(
                        reading.Error)
                        ? "ingen batteridata"
                        : reading.Error);
            }

            return "V102 Pro: " +
                reading.Percentage.Value + "%" +
                (reading.IsCharging
                    ? " (laddar)"
                    : reading.IsFull
                        ? " (fullt)"
                        : string.Empty);
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

        private static void EnsureStartupPathCurrent()
        {
            try
            {
                using (RegistryKey key =
                    Registry.CurrentUser.OpenSubKey(
                        RunKey,
                        true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    string configured =
                        key.GetValue(
                            RunValueName,
                            string.Empty) as string;
                    if (string.IsNullOrWhiteSpace(configured))
                    {
                        return;
                    }

                    string expected =
                        "\"" + Application.ExecutablePath + "\"";
                    if (!string.Equals(
                        configured,
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue(
                            RunValueName,
                            expected,
                            RegistryValueKind.String);
                    }
                }
            }
            catch
            {
                // Autostart is optional.
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
            _v102PollTimer.Stop();
            _v102PollTimer.Dispose();
            _gameProfileTimer.Stop();
            _gameProfileTimer.Dispose();
            _v102Source.Dispose();
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
            if (_lightingForm != null)
            {
                _lightingForm.Dispose();
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
        private DeviceBatteryReading _v102Reading;
        private V102TimeEstimate _v102Estimate;
        private IList<V102BatterySample> _v102History =
            new List<V102BatterySample>();
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
            ClientSize = new Size(440, 660);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Batteriöversikt";
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

        public void UpdateV102Reading(
            DeviceBatteryReading reading,
            V102TimeEstimate estimate,
            IList<V102BatterySample> history)
        {
            _v102Reading = reading;
            _v102Estimate = estimate;
            _v102History =
                history ?? new List<V102BatterySample>();
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
            DrawV102Summary(graphics);

            using (Font footerFont = new Font(
                "Segoe UI",
                8f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (SolidBrush footerBrush = new SolidBrush(
                Color.FromArgb(133, 146, 163)))
            {
                DateTime? latest = null;
                if (_reading != null)
                {
                    latest = _reading.MeasuredAt;
                }
                if (_v102Reading != null &&
                    (!latest.HasValue ||
                     _v102Reading.MeasuredAtUtc.ToLocalTime() >
                        latest.Value))
                {
                    latest =
                        _v102Reading.MeasuredAtUtc.ToLocalTime();
                }

                string updated = latest.HasValue
                    ? "Uppdaterad " +
                      latest.Value.ToString("HH:mm:ss")
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
                    Width - 128,
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
                    "BATTERI  ·  R.A.T. 9 + V102 PRO",
                    titleFont,
                    titleBrush,
                    24,
                    19);
                graphics.FillEllipse(
                    dotBrush,
                    Width - 72,
                    21,
                    8,
                    8);
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

        private void DrawV102Summary(Graphics graphics)
        {
            Color accent = GetV102AccentColor(_v102Reading);
            using (Pen separator = new Pen(
                Color.FromArgb(54, 255, 255, 255),
                1))
            {
                graphics.DrawLine(
                    separator,
                    24,
                    410,
                    Width - 24,
                    410);
            }

            string percentageText =
                _v102Reading != null &&
                _v102Reading.HasPercentage
                    ? _v102Reading.Percentage.Value + "%"
                    : "--";
            string status = GetV102StatusText(_v102Reading);
            string remaining = _v102Estimate != null
                ? _v102Estimate.FormatShort()
                : "samlar mätdata";

            using (Font deviceFont = new Font(
                "Segoe UI",
                8f,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (Font percentageFont = new Font(
                "Segoe UI",
                25f,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (Font statusFont = new Font(
                "Segoe UI",
                9.5f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (Font mutedFont = new Font(
                "Segoe UI",
                8.5f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (SolidBrush titleBrush = new SolidBrush(
                Color.FromArgb(180, 192, 208)))
            using (SolidBrush valueBrush = new SolidBrush(
                Color.FromArgb(246, 248, 251)))
            using (SolidBrush accentBrush = new SolidBrush(accent))
            using (SolidBrush mutedBrush = new SolidBrush(
                Color.FromArgb(153, 166, 183)))
            {
                graphics.DrawString(
                    "EWEADN V102 PRO  ·  2,4 GHZ",
                    deviceFont,
                    titleBrush,
                    24,
                    424);
                graphics.DrawString(
                    percentageText,
                    percentageFont,
                    valueBrush,
                    20,
                    442);
                graphics.DrawString(
                    status,
                    statusFont,
                    accentBrush,
                    145,
                    452);
                graphics.DrawString(
                    remaining,
                    mutedFont,
                    mutedBrush,
                    145,
                    476);
            }

            int left = 24;
            int top = 500;
            int width = Width - 48;
            int height = 7;
            using (GraphicsPath trackPath = CreateRoundedRectangle(
                new Rectangle(left, top, width, height),
                height))
            using (SolidBrush trackBrush = new SolidBrush(
                Color.FromArgb(58, 67, 81)))
            {
                graphics.FillPath(trackBrush, trackPath);
            }

            int percentage = _v102Reading != null &&
                _v102Reading.HasPercentage
                    ? Math.Max(
                        0,
                        Math.Min(
                            100,
                            _v102Reading.Percentage.Value))
                    : 0;
            int fillWidth = (int)Math.Round(
                width * percentage / 100.0);
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

            using (Font historyFont = new Font(
                "Segoe UI",
                7.5f,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (SolidBrush mutedBrush = new SolidBrush(
                Color.FromArgb(129, 143, 161)))
            {
                graphics.DrawString(
                    "V102-HISTORIK  ·  24 TIM",
                    historyFont,
                    mutedBrush,
                    24,
                    521);
            }

            Rectangle chart = new Rectangle(
                24,
                542,
                Width - 48,
                58);
            using (Pen gridPen = new Pen(
                Color.FromArgb(32, 255, 255, 255),
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
                    chart.Bottom,
                    chart.Right,
                    chart.Bottom);
            }

            DateTime end = _v102Reading != null
                ? _v102Reading.MeasuredAtUtc.ToLocalTime()
                : DateTime.Now;
            DateTime start = end - TimeSpan.FromHours(24);
            List<V102BatterySample> samples =
                new List<V102BatterySample>();
            foreach (V102BatterySample sample in _v102History)
            {
                DateTime local =
                    sample.TimestampUtc.ToLocalTime();
                if (local >= start && local <= end)
                {
                    samples.Add(new V102BatterySample(
                        local,
                        sample.Percentage,
                        sample.IsCharging));
                }
            }

            if (samples.Count == 0 &&
                _v102Reading != null &&
                _v102Reading.HasPercentage)
            {
                samples.Add(new V102BatterySample(
                    end,
                    _v102Reading.Percentage.Value,
                    _v102Reading.IsCharging));
            }

            if (samples.Count < 2)
            {
                using (Font emptyFont = new Font(
                    "Segoe UI",
                    8.5f,
                    FontStyle.Regular,
                    GraphicsUnit.Point))
                using (SolidBrush emptyBrush = new SolidBrush(
                    Color.FromArgb(145, 158, 175)))
                {
                    graphics.DrawString(
                        _v102Estimate != null &&
                        _v102Estimate.HasEstimate
                            ? "Tidsuppskattningen är inlärd."
                            : "Historiken börjar fyllas automatiskt.",
                        emptyFont,
                        emptyBrush,
                        chart.Left + 8,
                        chart.Top + 18);
                }
            }
            else
            {
                double totalSeconds = Math.Max(
                    1,
                    (end - start).TotalSeconds);
                Point previousPoint = Point.Empty;
                bool hasPrevious = false;
                foreach (V102BatterySample sample in samples)
                {
                    DateTime timestamp = sample.TimestampUtc;
                    if (timestamp.Kind == DateTimeKind.Utc)
                    {
                        timestamp = timestamp.ToLocalTime();
                    }

                    int x = chart.Left + (int)Math.Round(
                        chart.Width *
                        (timestamp - start).TotalSeconds /
                        totalSeconds);
                    int y = chart.Bottom - (int)Math.Round(
                        chart.Height *
                        sample.Percentage /
                        100.0);
                    Point currentPoint = new Point(x, y);
                    if (hasPrevious)
                    {
                        Color lineColor = sample.IsCharging
                            ? Color.FromArgb(75, 203, 139)
                            : accent;
                        using (Pen dataPen =
                            new Pen(lineColor, 2f))
                        {
                            graphics.DrawLine(
                                dataPen,
                                previousPoint,
                                currentPoint);
                        }
                    }

                    previousPoint = currentPoint;
                    hasPrevious = true;
                }

                using (SolidBrush pointBrush =
                    new SolidBrush(accent))
                {
                    graphics.FillEllipse(
                        pointBrush,
                        previousPoint.X - 3,
                        previousPoint.Y - 3,
                        6,
                        6);
                }
            }
        }

        private static string GetV102StatusText(
            DeviceBatteryReading reading)
        {
            if (reading == null)
            {
                return "söker...";
            }

            if (reading.IsCharging)
            {
                return "laddar";
            }

            if (reading.IsFull)
            {
                return "fullt";
            }

            if (reading.ConnectionState ==
                DeviceConnectionState.Disconnected)
            {
                return "hittades inte";
            }

            if (reading.ConnectionState ==
                DeviceConnectionState.Error)
            {
                return "kunde inte läsa";
            }

            return "batteri";
        }

        private static Color GetV102AccentColor(
            DeviceBatteryReading reading)
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

            return Color.FromArgb(231, 139, 68);
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
