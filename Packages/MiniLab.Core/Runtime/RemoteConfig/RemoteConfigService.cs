using System.Collections.Generic;
using MiniLab.Core.Config;

namespace MiniLab.Core.RemoteConfig
{
    public sealed class RemoteConfigService
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();

        public RemoteConfigService(RemoteConfigDefaults defaults)
        {
            values["interstitial_cooldown_seconds"] = defaults.InterstitialCooldownSeconds;
            values["rewarded_daily_cap"] = defaults.RewardedDailyCap;
            values["debug_menu_enabled"] = defaults.EnableDebugMenu;
        }

        public void SetOverride(string key, object value)
        {
            values[key] = value;
        }

        public T Get<T>(string key, T fallback = default)
        {
            if (values.TryGetValue(key, out object value) && value is T typed)
            {
                return typed;
            }

            return fallback;
        }
    }
}
