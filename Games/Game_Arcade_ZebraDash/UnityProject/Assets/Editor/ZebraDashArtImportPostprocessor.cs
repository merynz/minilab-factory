#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZebraDash.EditorTools
{
    internal sealed class ZebraDashArtImportPostprocessor : AssetPostprocessor
    {
        private const string LegacyArtRoot = "Assets/Resources/ZebraDashArtLocal/";
        private const string IndustrialRoot = "Assets/Resources/ZebraDashArtLocal/Industrial/";
        private const int IndustrialCellSize = 16;

        private void OnPreprocessTexture()
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            string normalized = assetPath.Replace('\\', '/');
            bool isLegacyArt = normalized.Contains(LegacyArtRoot);
            bool isIndustrialArt = normalized.Contains(IndustrialRoot);
            if (!isLegacyArt)
            {
                return;
            }

            if (!(assetImporter is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = isIndustrialArt ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = isIndustrialArt ? FilterMode.Point : FilterMode.Bilinear;
            importer.wrapMode = normalized.Contains("/Backgrounds/")
                ? TextureWrapMode.Repeat
                : TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = isIndustrialArt ? 16f : 100f;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            if (!isIndustrialArt)
            {
                return;
            }

            if (!TryGetTextureSize(importer, out int width, out int height))
            {
                return;
            }

            int cols = Mathf.Max(1, width / IndustrialCellSize);
            int rows = Mathf.Max(1, height / IndustrialCellSize);
            string spriteBaseName = Path.GetFileNameWithoutExtension(normalized);
            var sheet = new List<SpriteMetaData>(cols * rows);
            int idx = 0;
            for (int y = rows - 1; y >= 0; y--)
            {
                for (int x = 0; x < cols; x++)
                {
                    var meta = new SpriteMetaData
                    {
                        name = $"{spriteBaseName}_{idx:000}",
                        alignment = (int)SpriteAlignment.Center,
                        border = Vector4.zero,
                        pivot = new Vector2(0.5f, 0.5f),
                        rect = new Rect(
                            x * IndustrialCellSize,
                            y * IndustrialCellSize,
                            IndustrialCellSize,
                            IndustrialCellSize)
                    };
                    sheet.Add(meta);
                    idx++;
                }
            }

            importer.spritesheet = sheet.ToArray();
        }

        private static bool TryGetTextureSize(TextureImporter importer, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (importer == null)
            {
                return false;
            }

            try
            {
                importer.GetSourceTextureWidthAndHeight(out width, out height);
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
