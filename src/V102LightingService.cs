using System;

namespace Rat9Battery
{
    internal static class V102LightingService
    {
        public static V102KeyboardConfig Merge(
            V102KeyboardConfig current,
            V102LightingProfile profile)
        {
            if (current == null)
            {
                throw new ArgumentNullException("current");
            }

            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            V102KeyboardConfig result = new V102KeyboardConfig
            {
                SleepEnabled = current.SleepEnabled,
                SleepTimer = current.SleepTimer,
                LightingEnabled = profile.Enabled,
                IdleTime = current.IdleTime,
                WinKeyEnabled = current.WinKeyEnabled,
                OsType = current.OsType,
                FnFree = current.FnFree,
                UsbLightingSet = profile.EffectMode,
                ProfileLightingSet = profile.EffectMode,
                BleLightingSet = profile.EffectMode,
                SpeedAndBrightness =
                    new byte[9],
                BreatheMonoWaveOption =
                    ((profile.BreathColor & 0x07) << 4) |
                    (profile.WaveColor & 0x07),
                LogoColorType = profile.LogoMode,
                WinKeyMode = current.WinKeyMode
            };

            if (current.SpeedAndBrightness != null)
            {
                Array.Copy(
                    current.SpeedAndBrightness,
                    result.SpeedAndBrightness,
                    Math.Min(
                        current.SpeedAndBrightness.Length,
                        result.SpeedAndBrightness.Length));
            }

            int brightness = Math.Max(
                1,
                Math.Min(5, profile.Brightness));
            int speed = Math.Max(
                0,
                Math.Min(4, profile.Speed));
            if (profile.EffectMode >= 0 &&
                profile.EffectMode <= 7)
            {
                result.SpeedAndBrightness[
                    profile.EffectMode + 1] =
                    (byte)((speed << 4) | brightness);
            }
            else if (profile.EffectMode >= 16 &&
                profile.EffectMode <= 24)
            {
                result.SpeedAndBrightness[0] =
                    (byte)brightness;
            }

            return result;
        }

        public static V102LightingProfile FromCurrent(
            V102KeyboardConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            int effect = config.ProfileLightingSet;
            if (!IsValidEffect(effect))
            {
                effect = config.UsbLightingSet;
            }

            if (!IsValidEffect(effect))
            {
                effect = 4;
            }

            int brightness = 5;
            int speed = 4;
            if (effect >= 0 && effect <= 7 &&
                config.SpeedAndBrightness != null &&
                config.SpeedAndBrightness.Length > effect + 1)
            {
                byte value =
                    config.SpeedAndBrightness[effect + 1];
                brightness = Math.Max(1, Math.Min(5, value & 0x0F));
                speed = Math.Max(0, Math.Min(4, value >> 4));
            }
            else if (effect >= 16 && effect <= 24 &&
                config.SpeedAndBrightness != null &&
                config.SpeedAndBrightness.Length > 0)
            {
                brightness = Math.Max(
                    1,
                    Math.Min(
                        5,
                        config.SpeedAndBrightness[0] & 0x0F));
                speed = 4;
            }

            return new V102LightingProfile
            {
                Id = "current",
                Name = "Nuvarande",
                Executables = new System.Collections.Generic.List<string>(),
                Enabled = config.LightingEnabled,
                EffectMode = effect,
                Brightness = brightness,
                Speed = speed,
                LogoMode = config.LogoColorType,
                BreathColor =
                    (config.BreatheMonoWaveOption >> 4) & 0x07,
                WaveColor =
                    config.BreatheMonoWaveOption & 0x07
            };
        }

        private static bool IsValidEffect(int effect)
        {
            return (effect >= 0 && effect <= 7) ||
                (effect >= 16 && effect <= 24);
        }
    }
}
