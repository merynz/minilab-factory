using UnityEngine;

namespace FluxOut.PCR
{
    public sealed class PCRAutoBootstrap : MonoBehaviour
    {
        public static PCRAutoBootstrap Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeRoot()
        {
            if (Instance != null)
            {
                return;
            }

            // Scene already owns a controller; do not spawn a duplicate runtime root.
            if (Object.FindObjectOfType<PCRGameController>() != null)
            {
                return;
            }

            GameObject existing = GameObject.Find("PCR_RuntimeRoot");
            if (existing != null)
            {
                Instance = existing.GetComponent<PCRAutoBootstrap>();
                if (Instance == null)
                {
                    Instance = existing.AddComponent<PCRAutoBootstrap>();
                }

                return;
            }

            var root = new GameObject("PCR_RuntimeRoot");
            Instance = root.AddComponent<PCRAutoBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (Object.FindObjectOfType<PCRGameController>() == null)
            {
                gameObject.AddComponent<PCRGameController>();
            }
        }
    }
}
