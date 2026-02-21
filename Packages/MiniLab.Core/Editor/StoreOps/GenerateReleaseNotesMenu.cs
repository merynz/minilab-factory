using System.IO;
using MiniLab.Core.StoreOps;
using UnityEditor;
using UnityEngine;

namespace MiniLab.Core.Editor.StoreOps
{
    public static class GenerateReleaseNotesMenu
    {
        [MenuItem("MiniLab/Store Ops/Generate Release Notes")]
        public static void Generate()
        {
            string version = PlayerSettings.bundleVersion;
            string text = ReleaseNotesBuilder.Build(version, "Auto-generated from MiniLab.Core");
            string outputPath = Path.Combine(Application.dataPath, "../BuildArtifacts/release-notes.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.WriteAllText(outputPath, text);
            Debug.Log($"Release notes generated: {outputPath}");
        }
    }
}
