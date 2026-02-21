using System;
using UnityEngine;

namespace MiniLab.Core.Config
{
    [CreateAssetMenu(fileName = "CoreSettings", menuName = "MiniLab/Core Settings")]
    public sealed class CoreSettings : ScriptableObject
    {
        public string GameCode = "GAME_CODE";
        public string DefaultLocale = "en";
        public PolicyKitSettings PolicyKit = new PolicyKitSettings();
        public RemoteConfigDefaults RemoteConfig = new RemoteConfigDefaults();
    }

    [Serializable]
    public sealed class PolicyKitSettings
    {
        public bool BlockAdsUntilConsentReady = true;
        public bool RequirePrivacyOptionsEntryPoint = true;
        public bool EnableAttPromptWhenTrackingEnabled = true;
        public bool TrackingEnabled = false;
    }

    [Serializable]
    public sealed class RemoteConfigDefaults
    {
        public int InterstitialCooldownSeconds = 90;
        public int RewardedDailyCap = 8;
        public bool EnableDebugMenu = true;
    }
}
