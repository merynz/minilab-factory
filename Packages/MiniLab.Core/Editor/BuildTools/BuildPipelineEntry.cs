using System.IO;
using UnityEditor;

namespace MiniLab.Build
{
    public static class BuildPipelineEntry
    {
        public static void BuildAndroidAab()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
            string outputDir = Path.Combine(repoRoot, "BuildArtifacts", "Android");
            Directory.CreateDirectory(outputDir);

            string outputPath = Path.Combine(outputDir, $"{PlayerSettings.productName}.aab");
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                target = BuildTarget.Android,
                locationPathName = outputPath
            };

            EditorUserBuildSettings.buildAppBundle = true;
            BuildReportWithGuard(options, outputPath);
        }

        public static void BuildiOSXcodeProject()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
            string outputDir = Path.Combine(repoRoot, "BuildArtifacts", "iOS", "XcodeProject");
            Directory.CreateDirectory(outputDir);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                target = BuildTarget.iOS,
                locationPathName = outputDir
            };

            BuildReportWithGuard(options, outputDir);
        }

        private static string[] GetEnabledScenes()
        {
            return EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes);
        }

        private static void BuildReportWithGuard(BuildPlayerOptions options, string outputPath)
        {
            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Build failed: {report.summary.result}");
            }

            UnityEngine.Debug.Log($"Build completed: {outputPath}");
        }
    }
}
