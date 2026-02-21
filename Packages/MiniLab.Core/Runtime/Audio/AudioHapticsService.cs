using UnityEngine;

namespace MiniLab.Core.Audio
{
    public static class AudioHapticsService
    {
        public static bool AudioEnabled { get; private set; } = true;
        public static bool HapticsEnabled { get; private set; } = true;

        public static void SetAudioEnabled(bool enabled)
        {
            AudioEnabled = enabled;
            AudioListener.volume = enabled ? 1f : 0f;
        }

        public static void SetHapticsEnabled(bool enabled)
        {
            HapticsEnabled = enabled;
        }

        public static void TriggerHapticLight()
        {
            if (!HapticsEnabled)
            {
                return;
            }

            Handheld.Vibrate();
        }
    }
}
