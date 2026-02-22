using System.Collections;
using MiniLab.Core.Config;
using MiniLab.Core.Policy;
using MiniLab.Core.RemoteConfig;
using MiniLab.Core.Telemetry;
using UnityEngine;

namespace MiniLab.Core.Boot
{
    public sealed class MiniLabBootstrap : MonoBehaviour
    {
        [SerializeField] private CoreSettings settings;

        private ConsentService consentService;
        private RemoteConfigService remoteConfigService;

        private IEnumerator Start()
        {
            if (settings == null)
            {
                settings = Resources.Load<CoreSettings>("CoreSettings");
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<CoreSettings>();
                    settings.GameCode = "MINILAB_DEFAULT";
                    Debug.LogWarning("MiniLabBootstrap fallback CoreSettings generated in-memory.");
                }
            }

            remoteConfigService = new RemoteConfigService(settings.RemoteConfig);
            consentService = new ConsentService(settings.PolicyKit);

            yield return consentService.RefreshStateOnLaunch();

            AdsGate.SetConsentResult(consentService.CanRequestAds);
            TelemetryService.Track("first_open");
        }
    }
}
