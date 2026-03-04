using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FluxOut.PCR.Editor
{
    public sealed class PCRShaderInclusionPrebuild : IPreprocessBuildWithReport
    {
        private static readonly string[] RequiredShaderNames =
        {
            "FluxOut/PCR/Trace",
            "FluxOut/PCR/SDFIcon",
            "FluxOut/PCR/Background",
            "FluxOut/PCR/ScreenFlash",
            "FluxOut/PCR/PlayerRadial"
        };

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            EnsureAlwaysIncludedShaders(logPrefix: "[PCR][Prebuild]");
        }

        [MenuItem("FluxOut/PCR/Ensure Always Included Shaders")]
        public static void EnsureAlwaysIncludedShadersMenu()
        {
            EnsureAlwaysIncludedShaders(logPrefix: "[PCR][Menu]");
        }

        private static void EnsureAlwaysIncludedShaders(string logPrefix)
        {
            Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settingsAssets == null || settingsAssets.Length == 0)
            {
                Debug.LogError($"{logPrefix} Could not load ProjectSettings/GraphicsSettings.asset");
                return;
            }

            var settingsObject = new SerializedObject(settingsAssets[0]);
            SerializedProperty alwaysIncluded = settingsObject.FindProperty("m_AlwaysIncludedShaders");
            if (alwaysIncluded == null || !alwaysIncluded.isArray)
            {
                Debug.LogError($"{logPrefix} m_AlwaysIncludedShaders property not found.");
                return;
            }

            bool changed = false;

            for (int i = 0; i < RequiredShaderNames.Length; i++)
            {
                string shaderName = RequiredShaderNames[i];
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"{logPrefix} Shader not found in project: {shaderName}");
                    continue;
                }

                if (ContainsShader(alwaysIncluded, shader))
                {
                    continue;
                }

                int index = alwaysIncluded.arraySize;
                alwaysIncluded.InsertArrayElementAtIndex(index);
                alwaysIncluded.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                changed = true;
                Debug.Log($"{logPrefix} Added Always Included Shader: {shaderName}");
            }

            if (!changed)
            {
                return;
            }

            settingsObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"{logPrefix} GraphicsSettings.alwaysIncludedShaders updated.");
        }

        private static bool ContainsShader(SerializedProperty shaderArray, Shader shader)
        {
            for (int i = 0; i < shaderArray.arraySize; i++)
            {
                if (shaderArray.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
