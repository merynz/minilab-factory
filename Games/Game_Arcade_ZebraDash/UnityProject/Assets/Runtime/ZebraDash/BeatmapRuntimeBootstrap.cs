using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace ZebraDash
{
    [Preserve]
    public static class BeatmapRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeControllers()
        {
            Scene scene = SceneManager.GetActiveScene();
            string sceneName = scene.name;

            bool managedFlowScene = string.Equals(sceneName, "Boot", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(sceneName, "Bootstrap", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(sceneName, "MainMenu", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(sceneName, "LevelSelect", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(sceneName, "Gameplay", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(sceneName, "Results", System.StringComparison.OrdinalIgnoreCase);

            if (managedFlowScene && Object.FindObjectOfType<ZebraDashFlowController>() == null)
            {
                GameObject flow = new GameObject("ZebraDashFlowController");
                flow.AddComponent<ZebraDashFlowController>();
            }

            bool isWorkbench = string.Equals(sceneName, "BeatmapWorkbench", System.StringComparison.OrdinalIgnoreCase);
            if (!isWorkbench)
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
