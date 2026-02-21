using System.Collections;
using MiniLab.Core.Config;

namespace MiniLab.Core.Policy
{
    public sealed class ConsentService
    {
        private readonly PolicyKitSettings settings;

        public ConsentService(PolicyKitSettings settings)
        {
            this.settings = settings;
            State = ConsentState.Unknown;
        }

        public ConsentState State { get; private set; }

        public bool CanRequestAds =>
            State == ConsentState.NotRequired || State == ConsentState.RequiredAndGranted;

        public IEnumerator RefreshStateOnLaunch()
        {
            // Placeholder workflow:
            // 1) UMP Update()
            // 2) If form required -> Show form
            // 3) If privacy options required -> expose entry point in UI
            // Replace this stub with concrete SDK integration in game project.
            yield return null;
            State = settings.BlockAdsUntilConsentReady
                ? ConsentState.NotRequired
                : ConsentState.RequiredAndGranted;
        }
    }
}
