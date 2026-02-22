using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniLab.Build
{
    public static class BuildPipelineEntry
    {
        public static void BuildAndroidAab()
        {
            try
            {
                Debug.Log("[MiniLab] BuildAndroidAab START");
                string outputDir = GetOutputDir(
                    "MINILAB_ANDROID_ARTIFACT_DIR",
                    Path.Combine(Directory.GetCurrentDirectory(), "BuildArtifacts", "Android"));
                Directory.CreateDirectory(outputDir);

                string outputFileName = Environment.GetEnvironmentVariable("MINILAB_ANDROID_AAB_NAME");
                if (string.IsNullOrWhiteSpace(outputFileName))
                {
                    outputFileName = $"{SanitizeFileName(PlayerSettings.productName)}.aab";
                }

                string buildNumberRaw = Environment.GetEnvironmentVariable("MINILAB_ANDROID_BUILD_NUMBER");
                if (!string.IsNullOrWhiteSpace(buildNumberRaw) && int.TryParse(buildNumberRaw, out int buildNumber))
                {
                    PlayerSettings.Android.bundleVersionCode = buildNumber;
                }

                AndroidStoreConfig storeConfig = LoadAndroidStoreConfig();
                ApplyAndroidApplicationId(storeConfig.ApplicationId, storeConfig.SourcePath, storeConfig.IsPlaceholder);
                ApplyLandscapeOrientation();
                ValidateKeystoreConfiguration();

                string[] scenes = EnsureEnabledScenesWithBootstrap();
                string outputPath = Path.Combine(outputDir, outputFileName);
                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    target = BuildTarget.Android,
                    locationPathName = outputPath
                };

                EditorUserBuildSettings.buildAppBundle = true;
                BuildReportWithGuard(options, outputPath);
                Debug.Log("[MiniLab] BuildAndroidAab DONE");
            }
            catch (Exception ex)
            {
                Debug.LogError($"MiniLab Android build failed: {ex.Message}");
                Debug.LogException(ex);
                throw;
            }
        }

        public static void BuildAndroidApk()
        {
            try
            {
                Debug.Log("[MiniLab] BuildAndroidApk START");
                string outputDir = GetOutputDir(
                    "MINILAB_ANDROID_ARTIFACT_DIR",
                    Path.Combine(Directory.GetCurrentDirectory(), "BuildArtifacts"));
                Directory.CreateDirectory(outputDir);

                string outputFileName = Environment.GetEnvironmentVariable("MINILAB_ANDROID_APK_NAME");
                if (string.IsNullOrWhiteSpace(outputFileName))
                {
                    outputFileName = "zebradash-dev.apk";
                }

                string buildNumberRaw = Environment.GetEnvironmentVariable("MINILAB_ANDROID_BUILD_NUMBER");
                if (!string.IsNullOrWhiteSpace(buildNumberRaw) && int.TryParse(buildNumberRaw, out int buildNumber))
                {
                    PlayerSettings.Android.bundleVersionCode = buildNumber;
                }

                AndroidStoreConfig storeConfig = LoadAndroidStoreConfig();
                ApplyAndroidApplicationId(storeConfig.ApplicationId, storeConfig.SourcePath, storeConfig.IsPlaceholder);
                ApplyLandscapeOrientation();
                ValidateKeystoreConfiguration();

                string[] scenes = EnsureEnabledScenesWithBootstrap();
                string outputPath = Path.Combine(outputDir, outputFileName);
                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    target = BuildTarget.Android,
                    locationPathName = outputPath
                };

                EditorUserBuildSettings.buildAppBundle = false;
                BuildReportWithGuard(options, outputPath);
                Debug.Log("[MiniLab] BuildAndroidApk DONE");
            }
            catch (Exception ex)
            {
                Debug.LogError($"MiniLab Android APK build failed: {ex.Message}");
                Debug.LogException(ex);
                throw;
            }
        }

        public static void BuildiOSXcodeProject()
        {
            try
            {
                string outputDir = GetOutputDir(
                    "MINILAB_IOS_EXPORT_DIR",
                    Path.Combine(Directory.GetCurrentDirectory(), "BuildArtifacts", "iOS", "XcodeProject"));
                Directory.CreateDirectory(outputDir);

                string buildNumberRaw = Environment.GetEnvironmentVariable("MINILAB_IOS_BUILD_NUMBER");
                if (!string.IsNullOrWhiteSpace(buildNumberRaw))
                {
                    PlayerSettings.iOS.buildNumber = buildNumberRaw;
                }

                string iosBundleId = LoadIosBundleId();
                ApplyIosBundleId(iosBundleId);
                ApplyLandscapeOrientation();

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = EnsureEnabledScenesWithBootstrap(),
                    target = BuildTarget.iOS,
                    locationPathName = outputDir
                };

                BuildReportWithGuard(options, outputDir);
            }
            catch (Exception ex)
            {
                Debug.LogError($"MiniLab iOS build failed: {ex.Message}");
                Debug.LogException(ex);
                throw;
            }
        }

        private static string[] EnsureEnabledScenesWithBootstrap()
        {
            string[] scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes);
            if (scenes != null && scenes.Length > 0)
            {
                return scenes;
            }

            string[] discoveredScenes = AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct()
                .ToArray();

            if (discoveredScenes.Length > 0)
            {
                Debug.Log("No enabled scenes found. Falling back to discovered scene list.");
                return discoveredScenes;
            }

            string bootstrapScenePath = "Assets/MiniLab/Scenes/Bootstrap.unity";
            EnsureBootstrapSceneExists(bootstrapScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(bootstrapScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("No scene found. Bootstrap scene auto-created and added to Build Settings.");
            return new[] { bootstrapScenePath };
        }

        private static void EnsureBootstrapSceneExists(string scenePath)
        {
            if (File.Exists(scenePath))
            {
                return;
            }

            string absoluteSceneDir = Path.GetDirectoryName(Path.Combine(Directory.GetCurrentDirectory(), scenePath));
            if (string.IsNullOrWhiteSpace(absoluteSceneDir))
            {
                throw new InvalidOperationException("Cannot resolve bootstrap scene directory.");
            }

            Directory.CreateDirectory(absoluteSceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("MiniLabBootstrapRoot");
            root.AddComponent<MiniLab.Core.Boot.MiniLabBootstrap>();
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException("Failed to save auto-generated Bootstrap scene.");
            }
        }

        private static AndroidStoreConfig LoadAndroidStoreConfig()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string[] candidates =
            {
                Path.Combine(projectRoot, "store.yaml"),
                Path.Combine(projectRoot, "..", "store.yaml")
            };

            string storePath = candidates.FirstOrDefault(File.Exists);
            bool allowPlaceholder = string.Equals(
                Environment.GetEnvironmentVariable("MINILAB_ALLOW_PLACEHOLDER_CONFIG"),
                "1",
                StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(storePath))
            {
                if (allowPlaceholder)
                {
                    Debug.LogWarning("store.yaml not found. Placeholder Android applicationId will be used.");
                    return AndroidStoreConfig.Placeholder("store.yaml missing");
                }

                throw new FileNotFoundException(
                    "store.yaml bulunamadi. Beklenen lokasyonlar: <ProjectPath>/store.yaml veya <ProjectPath>/../store.yaml");
            }

            string raw = File.ReadAllText(storePath);
            Match match = Regex.Match(
                raw,
                @"(?m)^\s*application_id_android\s*:\s*[""']?([A-Za-z0-9._]+)[""']?\s*$");

            if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                if (allowPlaceholder)
                {
                    Debug.LogWarning($"application_id_android bulunamadi ({storePath}). Placeholder kullanılacak.");
                    return AndroidStoreConfig.Placeholder(storePath);
                }

                throw new InvalidOperationException($"store.yaml içinde application_id_android bulunamadi: {storePath}");
            }

            return new AndroidStoreConfig(match.Groups[1].Value, storePath, false);
        }

        private static void ApplyAndroidApplicationId(string applicationId, string source, bool isPlaceholder)
        {
            if (!Regex.IsMatch(applicationId, @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$"))
            {
                throw new InvalidOperationException(
                    $"Android applicationId gecersiz: '{applicationId}'. Kaynak: {source}");
            }

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, applicationId);
            string appliedId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);

            if (!string.Equals(appliedId, applicationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Android applicationId set edilemedi. Beklenen: {applicationId}, Alinan: {appliedId}");
            }

            if (isPlaceholder)
            {
                Debug.LogWarning($"Placeholder Android applicationId kullaniliyor: {applicationId}");
            }
            else
            {
                Debug.Log($"Android applicationId set from store config ({source}): {applicationId}");
            }
        }

        private static void ValidateKeystoreConfiguration()
        {
            if (!PlayerSettings.Android.useCustomKeystore)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(PlayerSettings.Android.keystoreName))
            {
                throw new InvalidOperationException("Custom keystore aktif ama keystore path bos.");
            }

            string keystorePath = PlayerSettings.Android.keystoreName;
            if (!Path.IsPathRooted(keystorePath))
            {
                keystorePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), keystorePath));
            }

            if (!File.Exists(keystorePath))
            {
                throw new FileNotFoundException($"Custom keystore dosyasi bulunamadi: {keystorePath}");
            }
        }

        private static string LoadIosBundleId()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string[] candidates =
            {
                Path.Combine(projectRoot, "store.yaml"),
                Path.Combine(projectRoot, "..", "store.yaml")
            };

            string storePath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(storePath))
            {
                throw new FileNotFoundException(
                    "store.yaml bulunamadi. iOS bundle id icin <ProjectPath>/store.yaml veya <ProjectPath>/../store.yaml gerekli.");
            }

            string raw = File.ReadAllText(storePath);
            Match match = Regex.Match(
                raw,
                @"(?m)^\s*bundle_id_ios\s*:\s*[""']?([A-Za-z0-9._]+)[""']?\s*$");

            if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                throw new InvalidOperationException($"store.yaml içinde bundle_id_ios bulunamadi: {storePath}");
            }

            return match.Groups[1].Value.Trim();
        }

        private static void ApplyIosBundleId(string bundleId)
        {
            if (!Regex.IsMatch(bundleId, @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$"))
            {
                throw new InvalidOperationException($"iOS bundle id gecersiz: {bundleId}");
            }

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, bundleId);
            string applied = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS);
            if (!string.Equals(applied, bundleId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"iOS bundle id set edilemedi. Beklenen: {bundleId}, Alinan: {applied}");
            }
        }

        private static void BuildReportWithGuard(BuildPlayerOptions options, string outputPath)
        {
            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Build failed: {report.summary.result}");
            }

            Debug.Log($"Build completed: {outputPath}");
        }

        private static string GetOutputDir(string envName, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static void ApplyLandscapeOrientation()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }

        private sealed class AndroidStoreConfig
        {
            public AndroidStoreConfig(string applicationId, string sourcePath, bool isPlaceholder)
            {
                ApplicationId = applicationId;
                SourcePath = sourcePath;
                IsPlaceholder = isPlaceholder;
            }

            public string ApplicationId { get; }
            public string SourcePath { get; }
            public bool IsPlaceholder { get; }

            public static AndroidStoreConfig Placeholder(string sourcePath)
            {
                return new AndroidStoreConfig("com.zebratank.minilab.placeholder", sourcePath, true);
            }
        }
    }
}
