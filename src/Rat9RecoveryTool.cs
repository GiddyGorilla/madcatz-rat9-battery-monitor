using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Rat9Recovery
{
    internal static class Program
    {
        private const string TaskName = "MadCatzRat9Recovery";
        private const string AppRegistryKey =
            @"Software\MadCatzRat9BatteryTray";
        private const string InstallDirectory =
            @"C:\Program Files\MadCatz RAT9 Battery Monitor";
        private const string ToolFileName = "RAT9RecoveryTool.exe";
        private const string DeviceId = @"USB\VID_0738&PID_1709";
        private const string FilterDriverName = "_hid_0738_1709";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();

            try
            {
                if (args != null &&
                    args.Length > 0 &&
                    string.Equals(
                        args[0],
                        "--install",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Install(true);
                }
                else
                {
                    Recover();
                }

                return 0;
            }
            catch (Exception exception)
            {
                Log("FATAL: " + exception);
                if (args == null ||
                    args.Length == 0 ||
                    !string.Equals(
                        args[0],
                        "--quiet",
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "Återställningen misslyckades:\r\n\r\n" +
                        exception.Message,
                        "Mad Catz R.A.T. 9 Recovery",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return 1;
            }
        }

        private static void Install(bool showConfirmation)
        {
            string toolSource = Application.ExecutablePath;
            string appPath = Path.Combine(
                Path.GetDirectoryName(toolSource),
                "RAT9BatteryTray.exe");
            if (!File.Exists(appPath))
            {
                throw new FileNotFoundException(
                    "RAT9BatteryTray.exe måste ligga bredvid recovery-verktyget.");
            }

            Directory.CreateDirectory(InstallDirectory);
            string installedTool = Path.Combine(
                InstallDirectory,
                ToolFileName);
            if (!string.Equals(
                Path.GetFullPath(toolSource),
                Path.GetFullPath(installedTool),
                StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(toolSource, installedTool, true);
            }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                AppRegistryKey,
                true))
            {
                key.SetValue(
                    "AppPath",
                    appPath,
                    RegistryValueKind.String);
            }

            string taskXmlPath = Path.Combine(
                Path.GetTempPath(),
                "MadCatzRat9Recovery-" +
                Guid.NewGuid().ToString("N") +
                ".xml");
            try
            {
                File.WriteAllText(
                    taskXmlPath,
                    BuildTaskXml(installedTool),
                    new UTF16EncodingWithBom());
                CommandResult result = RunExact(
                    "schtasks.exe",
                    "/Create /TN \"" + TaskName +
                    "\" /XML \"" + taskXmlPath + "\" /F");
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        result.CombinedOutput);
                }
            }
            finally
            {
                try
                {
                    File.Delete(taskXmlPath);
                }
                catch
                {
                    // The temp file is harmless if Windows still holds it.
                }
            }

            Log("Installed recovery task for " + appPath);
            if (showConfirmation)
            {
                MessageBox.Show(
                    "Snabb återställning är installerad.\r\n\r\n" +
                    "Nästa gång anslutningen fastnar kan appen starta om " +
                    "Mad Catz-drivrutinen utan att datorn startas om.",
                    "Mad Catz R.A.T. 9",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private static void Recover()
        {
            string appPath = string.Empty;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                AppRegistryKey,
                false))
            {
                if (key != null)
                {
                    appPath = key.GetValue(
                        "AppPath",
                        string.Empty) as string;
                }
            }

            Log("Recovery started");
            StopProcesses("RAT9BatteryTray");
            StopProcesses("ProfilerU");
            StopProcesses("SaiMfd");

            RunAndLog(
                "pnputil.exe",
                "/disable-device /deviceid \"" + DeviceId + "\"");
            Thread.Sleep(1000);

            RunAndLog(
                "sc.exe",
                "stop " + FilterDriverName);
            WaitForServiceState("STOPPED", 6000);
            RunAndLog(
                "sc.exe",
                "start " + FilterDriverName);
            Thread.Sleep(1000);

            RunAndLog(
                "pnputil.exe",
                "/enable-device /deviceid \"" + DeviceId + "\"");
            Thread.Sleep(2000);
            RunAndLog("pnputil.exe", "/scan-devices");
            Thread.Sleep(2000);

            StartSmartTechnology("ProfilerU.exe");
            StartSmartTechnology("SaiMfd.exe");

            if (string.IsNullOrWhiteSpace(appPath))
            {
                appPath = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath),
                    "RAT9BatteryTray.exe");
            }

            try
            {
                if (!IsTaskInstalled())
                {
                    Install(false);
                    Log("Recovery task installed during recovery");
                }
            }
            catch (Exception exception)
            {
                Log(
                    "Could not install recovery task: " +
                    exception.Message);
            }

            if (!string.IsNullOrWhiteSpace(appPath) &&
                File.Exists(appPath))
            {
                StartInUserShell(appPath);
            }

            Log("Recovery completed");
        }

        private static bool IsTaskInstalled()
        {
            CommandResult result = RunExact(
                "schtasks.exe",
                "/Query /TN \"" + TaskName + "\"");
            return result.ExitCode == 0;
        }

        private static string BuildTaskXml(string toolPath)
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string sid = identity.User.Value;
            string escapedToolPath = System.Security.SecurityElement.Escape(
                toolPath);

            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.4\" " +
                "xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo>\r\n" +
                "    <Description>Reset a stuck Mad Catz R.A.T. 9 receiver.</Description>\r\n" +
                "  </RegistrationInfo>\r\n" +
                "  <Triggers />\r\n" +
                "  <Principals>\r\n" +
                "    <Principal id=\"Author\">\r\n" +
                "      <UserId>" + sid + "</UserId>\r\n" +
                "      <LogonType>InteractiveToken</LogonType>\r\n" +
                "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
                "    </Principal>\r\n" +
                "  </Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                "    <IdleSettings>\r\n" +
                "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
                "      <RestartOnIdle>false</RestartOnIdle>\r\n" +
                "    </IdleSettings>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <Hidden>false</Hidden>\r\n" +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                "    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\">\r\n" +
                "    <Exec>\r\n" +
                "      <Command>" + escapedToolPath + "</Command>\r\n" +
                "    </Exec>\r\n" +
                "  </Actions>\r\n" +
                "</Task>\r\n";
        }

        private static void WaitForServiceState(
            string expectedState,
            int timeoutMilliseconds)
        {
            int elapsed = 0;
            while (elapsed < timeoutMilliseconds)
            {
                CommandResult result = RunExact(
                    "sc.exe",
                    "query " + FilterDriverName);
                if (result.CombinedOutput.IndexOf(
                        expectedState,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }

                Thread.Sleep(250);
                elapsed += 250;
            }
        }

        private static void StartSmartTechnology(string fileName)
        {
            string path = Path.Combine(
                @"C:\Program Files\SmartTechnology\Software",
                fileName);
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                StartInUserShell(path);
            }
            catch (Exception exception)
            {
                Log("Could not start " + fileName + ": " + exception.Message);
            }
        }

        private static void StartInUserShell(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                Log("Could not start through Explorer: " + exception.Message);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path),
                    UseShellExecute = true
                });
            }
        }

        private static void StopProcesses(string processName)
        {
            foreach (Process process in Process.GetProcessesByName(
                processName))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                catch (Exception exception)
                {
                    Log(
                        "Could not stop " + processName + ": " +
                        exception.Message);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void RunAndLog(string fileName, string arguments)
        {
            CommandResult result = RunExact(fileName, arguments);
            Log(
                fileName + " " + arguments + "\r\n" +
                "ExitCode=" + result.ExitCode + "\r\n" +
                result.CombinedOutput);
        }

        private static CommandResult RunExact(
            string fileName,
            string arguments)
        {
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(30000);

                return new CommandResult
                {
                    ExitCode = process.HasExited
                        ? process.ExitCode
                        : -1,
                    CombinedOutput = (output + "\r\n" + error).Trim()
                };
            }
        }

        private static void Log(string message)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData),
                    "MadCatzRat9BatteryTray");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "recovery.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    " " + message + Environment.NewLine);
            }
            catch
            {
                // Recovery must continue if logging is unavailable.
            }
        }

        private sealed class CommandResult
        {
            public int ExitCode { get; set; }
            public string CombinedOutput { get; set; }
        }

        private sealed class UTF16EncodingWithBom : UnicodeEncoding
        {
            public UTF16EncodingWithBom()
                : base(false, true)
            {
            }
        }
    }
}
