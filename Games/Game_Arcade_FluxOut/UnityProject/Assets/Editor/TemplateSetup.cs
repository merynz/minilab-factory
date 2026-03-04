using MiniLab.Core.Boot;
using MiniLab.Core.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiniLab.TemplateBootstrap
{
    public static class TemplateSetup
    {
        public static void Run()
        {
            EnsureFolders();
            var rendererData = EnsureRendererData();
            var pipelineAsset = EnsurePipelineAsset(rendererData);
            ApplyPipelineSettings(pipelineAsset);
            var coreSettings = EnsureCoreSettings();
            EnsureBootstrapScene(coreSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Template setup completed.");
        }

        private static void EnsureFolders()
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            System.IO.Directory.CreateDirectory("Assets/Settings/Core");
            System.IO.Directory.CreateDirectory("Assets/Settings/Rendering");
        }

        private static Renderer2DData EnsureRendererData()
        {
            const string rendererPath = "Assets/Settings/Rendering/Renderer2D.asset";
            var rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(rendererPath);
            if (rendererData != null)
            {
                return rendererData;
            }

            rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
            return rendererData;
        }

        private static UniversalRenderPipelineAsset EnsurePipelineAsset(Renderer2DData rendererData)
        {
            const string pipelinePath = "Assets/Settings/Rendering/URP2D.asset";
            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipelineAsset == null)
            {
                pipelineAsset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(pipelineAsset, pipelinePath);
            }

            var so = new SerializedObject(pipelineAsset);
            var rendererDataList = so.FindProperty("m_RendererDataList");
            if (rendererDataList != null)
            {
                if (rendererDataList.arraySize == 0)
                {
                    rendererDataList.arraySize = 1;
                }

                rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            }

            var defaultRendererIndex = so.FindProperty("m_DefaultRendererIndex");
            if (defaultRendererIndex != null)
            {
                defaultRendererIndex.intValue = 0;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return pipelineAsset;
        }

        private static void ApplyPipelineSettings(UniversalRenderPipelineAsset pipelineAsset)
        {
            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
        }

        private static CoreSettings EnsureCoreSettings()
        {
            const string settingsPath = "Assets/Settings/Core/CoreSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<CoreSettings>(settingsPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<CoreSettings>();
            AssetDatabase.CreateAsset(settings, settingsPath);
            return settings;
        }

        private static void EnsureBootstrapScene(CoreSettings settings)
        {
            const string scenePath = "Assets/Scenes/Bootstrap.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var bootstrap = new GameObject("MiniLabBootstrap");
            var bootstrapComponent = bootstrap.AddComponent<MiniLabBootstrap>();
            var so = new SerializedObject(bootstrapComponent);
            var settingsProp = so.FindProperty("settings");
            if (settingsProp != null)
            {
                settingsProp.objectReferenceValue = settings;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
        }
    }
}
