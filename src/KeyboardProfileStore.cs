using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Rat9Battery
{
    [DataContract]
    internal sealed class V102LightingProfile
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        [DataMember(Name = "executables", Order = 2)]
        public List<string> Executables { get; set; }

        [DataMember(Name = "enabled", Order = 3)]
        public bool Enabled { get; set; }

        [DataMember(Name = "effectMode", Order = 4)]
        public int EffectMode { get; set; }

        [DataMember(Name = "brightness", Order = 5)]
        public int Brightness { get; set; }

        [DataMember(Name = "speed", Order = 6)]
        public int Speed { get; set; }

        [DataMember(Name = "logoMode", Order = 7)]
        public int LogoMode { get; set; }

        [DataMember(Name = "breathColor", Order = 8)]
        public int BreathColor { get; set; }

        [DataMember(Name = "waveColor", Order = 9)]
        public int WaveColor { get; set; }

        public V102LightingProfile Clone()
        {
            return new V102LightingProfile
            {
                Id = Id,
                Name = Name,
                Executables = Executables == null
                    ? new List<string>()
                    : new List<string>(Executables),
                Enabled = Enabled,
                EffectMode = EffectMode,
                Brightness = Brightness,
                Speed = Speed,
                LogoMode = LogoMode,
                BreathColor = BreathColor,
                WaveColor = WaveColor
            };
        }
    }

    [DataContract]
    internal sealed class V102ProfilesDocument
    {
        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "baseProfileId", Order = 1)]
        public string BaseProfileId { get; set; }

        [DataMember(Name = "profiles", Order = 2)]
        public List<V102LightingProfile> Profiles { get; set; }
    }

    internal sealed class KeyboardProfileStore
    {
        private static readonly int[] ValidLogoModes =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 9
        };

        private readonly DataContractJsonSerializer _serializer =
            new DataContractJsonSerializer(
                typeof(V102ProfilesDocument));
        private readonly string _path;

        public KeyboardProfileStore()
            : this(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MadCatzRat9BatteryTray",
                "v102-profiles-v1.json"))
        {
        }

        internal KeyboardProfileStore(string path)
        {
            _path = path;
        }

        public string FilePath
        {
            get { return _path; }
        }

        public V102ProfilesDocument LoadOrCreate()
        {
            try
            {
                if (File.Exists(_path))
                {
                    V102ProfilesDocument document;
                    using (FileStream stream = File.OpenRead(_path))
                    {
                        document =
                            _serializer.ReadObject(stream) as
                                V102ProfilesDocument;
                    }
                    if (document != null)
                    {
                        Normalize(document);
                        try
                        {
                            Save(document);
                        }
                        catch
                        {
                            // A read-only disk must not block startup.
                        }
                        return document;
                    }
                }
            }
            catch
            {
                // Invalid profile data is replaced by a safe default.
            }

            V102ProfilesDocument created = CreateDefault();
            Save(created);
            return created;
        }

        public void Save(V102ProfilesDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            Normalize(document);
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = _path + "." +
                Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = File.Create(temporary))
                {
                    _serializer.WriteObject(stream, document);
                }
                if (File.Exists(_path))
                {
                    string backup = _path + ".bak";
                    File.Replace(
                        temporary,
                        _path,
                        backup);
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }
                }
                else
                {
                    File.Move(temporary, _path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        public static V102LightingProfile CreateDefaultProfile()
        {
            return new V102LightingProfile
            {
                Id = "base",
                Name = "Standard",
                Executables = new List<string>(),
                Enabled = true,
                EffectMode = 4,
                Brightness = 5,
                Speed = 4,
                LogoMode = 0,
                BreathColor = 0,
                WaveColor = 0
            };
        }

        private static V102ProfilesDocument CreateDefault()
        {
            return new V102ProfilesDocument
            {
                Version = 1,
                BaseProfileId = "base",
                Profiles = new List<V102LightingProfile>
                {
                    CreateDefaultProfile()
                }
            };
        }

        private static void Normalize(
            V102ProfilesDocument document)
        {
            document.Version = 1;
            if (document.Profiles == null)
            {
                document.Profiles =
                    new List<V102LightingProfile>();
            }

            HashSet<string> ids =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            List<V102LightingProfile> valid =
                new List<V102LightingProfile>();
            foreach (V102LightingProfile profile in document.Profiles)
            {
                if (!IsValid(profile) || !ids.Add(profile.Id))
                {
                    continue;
                }

                if (profile.Executables == null)
                {
                    profile.Executables = new List<string>();
                }

                profile.Executables.RemoveAll(
                    value => string.IsNullOrWhiteSpace(value));
                valid.Add(profile);
            }

            if (valid.Count == 0)
            {
                valid.Add(CreateDefaultProfile());
                ids.Add("base");
            }

            document.Profiles = valid;
            if (string.IsNullOrWhiteSpace(
                    document.BaseProfileId) ||
                !ids.Contains(document.BaseProfileId))
            {
                document.BaseProfileId = valid[0].Id;
            }
        }

        private static bool IsValid(
            V102LightingProfile profile)
        {
            if (profile == null ||
                string.IsNullOrWhiteSpace(profile.Id) ||
                string.IsNullOrWhiteSpace(profile.Name) ||
                profile.Brightness < 1 ||
                profile.Brightness > 5 ||
                profile.Speed < 0 ||
                profile.Speed > 4 ||
                !IsValidEffect(profile.EffectMode) ||
                Array.IndexOf(
                    ValidLogoModes,
                    profile.LogoMode) < 0 ||
                profile.BreathColor < 0 ||
                profile.BreathColor > 7 ||
                profile.WaveColor < 0 ||
                profile.WaveColor > 7)
            {
                return false;
            }

            return true;
        }

        private static bool IsValidEffect(int mode)
        {
            return (mode >= 0 && mode <= 7) ||
                (mode >= 16 && mode <= 24);
        }
    }
}
