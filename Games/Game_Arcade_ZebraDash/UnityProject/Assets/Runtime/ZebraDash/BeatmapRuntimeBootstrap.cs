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
            if (!string.Equals(scene.name, "BeatmapWorkbench", System.StringComparison.OrdinalIgnoreCase))
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
