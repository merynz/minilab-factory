using MiniLab.Core.Config;

namespace MiniLab.Core.Policy
{
    public sealed class PrivacyOptionsService
    {
        private readonly PolicyKitSettings settings;

        public PrivacyOptionsService(PolicyKitSettings settings)
        {
            this.settings = settings;
        }

        public bool IsEntryPointRequired => settings.RequirePrivacyOptionsEntryPoint;

        public void OpenPrivacyOptions()
        {
            // Hook this to UMP's "show privacy options form" API.
        }
    }
}
