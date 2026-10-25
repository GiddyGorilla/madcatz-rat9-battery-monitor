using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Rat9Battery
{
    internal sealed class GameProcessInfo
    {
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; }
    }

    internal sealed class GameProfileDecision
    {
        public bool Changed { get; set; }
        public string ActiveProfileId { get; set; }
        public string MatchedExecutable { get; set; }
    }

    internal sealed class GameProfileMonitor
    {
        private readonly IList<V102LightingProfile> _profiles;
        private readonly string _baseProfileId;

        public GameProfileMonitor(
            IList<V102LightingProfile> profiles,
            string baseProfileId)
        {
            _profiles =
                profiles ??
                new List<V102LightingProfile>();
            _baseProfileId = baseProfileId;
            ActiveProfileId = baseProfileId;
        }

        public string ActiveProfileId { get; private set; }

        public GameProfileDecision Evaluate(
            IList<GameProcessInfo> runningProcesses,
            int? foregroundProcessId)
        {
            List<ProfileMatch> matches =
                new List<ProfileMatch>();
            foreach (GameProcessInfo process in
                runningProcesses ??
                    new List<GameProcessInfo>())
            {
                if (process == null ||
                    string.IsNullOrWhiteSpace(
                        process.ExecutablePath))
                {
                    continue;
                }

                foreach (V102LightingProfile profile in
                    _profiles)
                {
                    if (profile == null ||
                        profile.Executables == null ||
                        string.Equals(
                            profile.Id,
                            _baseProfileId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string executable in
                        profile.Executables)
                    {
                        if (PathsEqual(
                            process.ExecutablePath,
                            executable))
                        {
                            matches.Add(new ProfileMatch
                            {
                                Profile = profile,
                                ProcessId = process.ProcessId,
                                Executable = executable
                            });
                        }
                    }
                }
            }

            ProfileMatch selected = null;
            if (foregroundProcessId.HasValue)
            {
                selected = matches.Find(
                    value =>
                        value.ProcessId ==
                        foregroundProcessId.Value);
            }

            if (selected == null && matches.Count > 0)
            {
                matches.Sort(
                    delegate(ProfileMatch left, ProfileMatch right)
                    {
                        int byProfile =
                            string.Compare(
                                left.Profile.Id,
                                right.Profile.Id,
                                StringComparison.OrdinalIgnoreCase);
                        if (byProfile != 0)
                        {
                            return byProfile;
                        }

                        return left.ProcessId.CompareTo(
                            right.ProcessId);
                    });
                selected = matches[0];
            }

            string desired = selected == null
                ? _baseProfileId
                : selected.Profile.Id;
            bool changed = !string.Equals(
                ActiveProfileId,
                desired,
                StringComparison.OrdinalIgnoreCase);
            ActiveProfileId = desired;
            return new GameProfileDecision
            {
                Changed = changed,
                ActiveProfileId = desired,
                MatchedExecutable = selected == null
                    ? null
                    : selected.Executable
            };
        }

        public void Reset(string profileId)
        {
            ActiveProfileId = profileId;
        }

        private static bool PathsEqual(
            string left,
            string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd('\\'),
                    Path.GetFullPath(right).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(
                    (left ?? string.Empty).Trim(),
                    (right ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class ProfileMatch
        {
            public V102LightingProfile Profile { get; set; }
            public int ProcessId { get; set; }
            public string Executable { get; set; }
        }
    }

    internal static class RunningProcessSnapshot
    {
        public static IList<GameProcessInfo> Capture()
        {
            List<GameProcessInfo> result =
                new List<GameProcessInfo>();
            foreach (Process process in
                Process.GetProcesses())
            {
                try
                {
                    string path =
                        process.MainModule.FileName;
                    result.Add(new GameProcessInfo
                    {
                        ProcessId = process.Id,
                        ExecutablePath = path
                    });
                }
                catch
                {
                    // Protected and short-lived processes are skipped.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return result;
        }

        public static int? GetForegroundProcessId()
        {
            IntPtr window =
                GameProfileNativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return null;
            }

            uint processId;
            GameProfileNativeMethods.GetWindowThreadProcessId(
                window,
                out processId);
            return processId == 0
                ? (int?)null
                : (int)processId;
        }
    }

    internal static class GameProfileNativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);
    }
}
