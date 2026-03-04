using System.IO;
using FluxOut.PCR;
using MiniLab.Core.Boot;
using MiniLab.Core.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FluxOut.PCR.Editor
{
    public static class PCRCreateAssetsMenu
    {
        private const string DataRoot = "Assets/PhaseCircuitRunner/Data";
        private const string SegmentRoot = "Assets/PhaseCircuitRunner/Data/Segments";
        private const string ScenePath = "Assets/PhaseCircuitRunner/Scenes/PCR_DemoLevel.unity";

        [MenuItem("FluxOut/PCR/Create Default Assets")]
        public static void CreateDefaultAssets()
        {
            EnsureFolders();
            var difficulty = CreateAsset<PCRDifficultyProfileSO>($"{DataRoot}/PCRDifficultyProfile.asset");
            difficulty.StartSpeed = 7.5f;
            difficulty.EndSpeed = 10.5f;
            difficulty.MinLaneCount = 2;
            difficulty.MaxLaneCount = 5;

            var visual = CreateAsset<PCRVisualProfileSO>($"{DataRoot}/PCRVisualProfile.asset");
            visual.BloomStrength = 0.75f;

            var pacing = CreateAsset<PCRPacingProfileSO>($"{DataRoot}/PCRPacingProfile.asset");
            pacing.SimStepSec = 0.12f;
            pacing.TargetDurationSec = 90f;
            pacing.EmptyGapThresholdSec = 2f;
            pacing.LaneShiftTargetSec = 1.2f;
            pacing.EventCadenceMinSec = 1f;
            pacing.EventCadenceMaxSec = 1.4f;
            pacing.SegmentCountMin = 12;
            pacing.SegmentCountMax = 16;

            var library = CreateAsset<PCRSegmentLibrarySO>($"{DataRoot}/PCRSegmentLibrary.asset");
            library.Templates.Clear();
            var defaults = PCRTemplateDefaults.CreateRuntimeDefaults();
            for (int i = 0; i < defaults.Count; i++)
            {
                var src = defaults[i];
                string path = $"{SegmentRoot}/{src.TemplateId}.asset";
                var templateAsset = AssetDatabase.LoadAssetAtPath<PCRSegmentTemplateSO>(path);
                if (templateAsset == null)
                {
                    templateAsset = Object.Instantiate(src);
                    AssetDatabase.CreateAsset(templateAsset, path);
                }
                else
                {
                    EditorUtility.CopySerialized(src, templateAsset);
                }

                library.Templates.Add(templateAsset);
            }

            EditorUtility.SetDirty(library);

            var levelDoc = CreateAsset<PCRLevelDoc>($"{DataRoot}/PCRLevelDoc.asset");
            levelDoc.Seed = 4242;
            levelDoc.TargetDurationSec = 90f;
            levelDoc.SegmentCount = 14;
            levelDoc.DifficultyProfile = difficulty;
            levelDoc.VisualProfile = visual;
            levelDoc.PacingProfile = pacing;
            levelDoc.SegmentLibrary = library;

            CreateDemoSceneIfMissing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("FluxOut PCR default assets created.");
        }

        [MenuItem("FluxOut/PCR/Open Demo Scene")]
        public static void OpenDemoScene()
        {
            CreateDemoSceneIfMissing();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static void CreateDemoSceneIfMissing()
        {
            if (File.Exists(ScenePath))
            {
                return;
            }

            EnsureFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 1f);

            var bootstrapRoot = new GameObject("MiniLabBootstrap");
            var bootstrap = bootstrapRoot.AddComponent<MiniLabBootstrap>();
            var so = new SerializedObject(bootstrap);
            var settingsProp = so.FindProperty("settings");
            if (settingsProp != null)
            {
                var settings = AssetDatabase.LoadAssetAtPath<CoreSettings>("Assets/Settings/Core/CoreSettings.asset");
                if (settings != null)
                {
                    settingsProp.objectReferenceValue = settings;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            var runtimeRoot = new GameObject("PCR_RuntimeRoot");
            runtimeRoot.AddComponent<PCRAutoBootstrap>();
            runtimeRoot.AddComponent<PCRGameController>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }

        private static T CreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory("Assets/PhaseCircuitRunner");
            Directory.CreateDirectory("Assets/PhaseCircuitRunner/Data");
            Directory.CreateDirectory("Assets/PhaseCircuitRunner/Data/Segments");
            Directory.CreateDirectory("Assets/PhaseCircuitRunner/Scenes");
        }
    }
}
