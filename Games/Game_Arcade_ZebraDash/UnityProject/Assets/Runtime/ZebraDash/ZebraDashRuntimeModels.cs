using System;
using System.IO;
using System.Linq;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    [Serializable]
    public sealed class MusicCatalog
    {
        public MusicTrackEntry[] tracks = Array.Empty<MusicTrackEntry>();

        public MusicTrackEntry FindTrack(string trackId)
        {
            return tracks.FirstOrDefault(t => string.Equals(t.trackId, trackId, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Serializable]
    public sealed class MusicTrackEntry
    {
        public string trackId = "";
        public string filePath = "";
        public float bpm = 120f;
        public float offsetSec = 0f;
        public float durationSec = 0f;
        public float startTrimSec = 0f;
        public string levelPath = "";
    }

    public static class ZebraDashPaths
    {
        public static string GameRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        public static string ContentRoot => Path.Combine(GameRoot, "Content");

        public static string CatalogPath => Path.Combine(ContentRoot, "Levels", "music_catalog.json");

        public static string ResolvePath(string relativeOrAbsolute)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(relativeOrAbsolute))
            {
                return Path.GetFullPath(relativeOrAbsolute);
            }

            return Path.GetFullPath(Path.Combine(GameRoot, relativeOrAbsolute));
        }
    }

    public static class ZebraDashCatalogIo
    {
        public static MusicCatalog LoadCatalog()
        {
            string path = ZebraDashPaths.CatalogPath;
            if (!File.Exists(path))
            {
                return new MusicCatalog();
            }

            string json = File.ReadAllText(path);
            MusicCatalog catalog = JsonUtility.FromJson<MusicCatalog>(json);
            return catalog ?? new MusicCatalog();
        }

        public static void SaveCatalog(MusicCatalog catalog)
        {
            string path = ZebraDashPaths.CatalogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ZebraDashPaths.ContentRoot);
            File.WriteAllText(path, JsonUtility.ToJson(catalog, true));
        }

        public static BeatMap LoadBeatMap(MusicTrackEntry entry)
        {
            if (entry == null)
            {
                return new BeatMap();
            }

            string levelPath = ZebraDashPaths.ResolvePath(entry.levelPath);
            if (!File.Exists(levelPath))
            {
                return new BeatMap
                {
                    trackId = entry.trackId,
                    bpm = entry.bpm,
                    offsetSec = entry.offsetSec
                };
            }

            BeatMap map = BeatMapJson.LoadFromFile(levelPath);
            if (string.IsNullOrWhiteSpace(map.trackId))
            {
                map.trackId = entry.trackId;
            }

            if (map.bpm <= 0.001f)
            {
                map.bpm = entry.bpm;
            }

            return map;
        }
    }
}
