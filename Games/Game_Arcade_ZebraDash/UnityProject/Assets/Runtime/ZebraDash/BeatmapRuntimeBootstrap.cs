using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZebraDash
{
    public static class BeatmapRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureWorkbenchController()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool isWorkbench = string.Equals(scene.name, "BeatmapWorkbench", System.StringComparison.OrdinalIgnoreCase);
            bool isBootstrap = string.Equals(scene.name, "Bootstrap", System.StringComparison.OrdinalIgnoreCase);
            if (!isWorkbench && !isBootstrap)
            {
                return;
            }

            if (Object.FindObjectOfType<BeatmapWorkbenchController>() != null)
            {
                return;
            }

            GameObject root = new GameObject("BeatmapWorkbenchRuntime");
            root.AddComponent<AudioSource>();
            root.AddComponent<MiniLab.Core.Rhythm.BeatClock>();
            root.AddComponent<BeatmapWorkbenchController>();
        }
    }
}
