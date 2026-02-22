using System;
using System.Collections;
using System.IO;
using System.Linq;
using MiniLab.Core.Rhythm;
using UnityEngine;
using UnityEngine.Networking;

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
        public string audioPath = "";
        public float bpm = 120f;
        public float offsetSec = 0f;
        public float durationSec = 0f;
        public float startTrimSec = 0f;
        public string levelPath = "";

        public string GetAudioReferencePath()
        {
            return string.IsNullOrWhiteSpace(audioPath) ? filePath : audioPath;
        }
    }

    public static class ZebraDashPaths
    {
        public static string GameRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        public static string ContentRoot => Path.Combine(GameRoot, "Content");

        public static string EditorLevelsRoot => Path.Combine(ContentRoot, "Levels");

        public static string EditorCatalogPath => Path.Combine(EditorLevelsRoot, "music_catalog.json");

        public static string EditorAudioRoot => Path.Combine(GameRoot, "AudioLocal");

        public static bool PreferStreamingAssets => !Application.isEditor;

        public static string StreamingRoot => CombineRuntimePath(Application.streamingAssetsPath, "ZebraDash");

        public static string StreamingLevelsRoot => CombineRuntimePath(StreamingRoot, "Levels");

        public static string StreamingAudioRoot => CombineRuntimePath(StreamingRoot, "Audio");

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

        public static string ResolveCatalogPathForRead()
        {
            if (PreferStreamingAssets)
            {
                return CombineRuntimePath(StreamingLevelsRoot, "music_catalog.json");
            }

            return EditorCatalogPath;
        }

        public static string ResolveLevelPathForRead(string relativeOrAbsolute)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
            {
                return string.Empty;
            }

            if (PreferStreamingAssets)
            {
                string fileName = Path.GetFileName(relativeOrAbsolute);
                return CombineRuntimePath(StreamingLevelsRoot, fileName);
            }

            return ResolvePath(relativeOrAbsolute);
        }

        public static string ResolveAudioPathForRead(string relativeOrAbsolute)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
            {
                return string.Empty;
            }

            if (PreferStreamingAssets)
            {
                string fileName = Path.GetFileName(relativeOrAbsolute);
                return CombineRuntimePath(StreamingAudioRoot, fileName);
            }

            return ResolvePath(relativeOrAbsolute);
        }

        public static bool IsUrlLike(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return path.Contains("://", StringComparison.Ordinal) ||
                   path.StartsWith("jar:file:", StringComparison.OrdinalIgnoreCase);
        }

        public static string ToUnityWebRequestUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (IsUrlLike(path))
            {
                return path;
            }

            string normalized = path.Replace("\\", "/");
            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                return "file://" + normalized;
            }

            return "file:///" + normalized;
        }

        private static string CombineRuntimePath(string root, params string[] segments)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            bool urlLike = IsUrlLike(root);
            string combined = urlLike
                ? root.TrimEnd('/')
                : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (string rawSegment in segments)
            {
                if (string.IsNullOrWhiteSpace(rawSegment))
                {
                    continue;
                }

                string segment = rawSegment.Trim().Trim('/', '\\');
                if (urlLike)
                {
                    combined = combined + "/" + segment;
                }
                else
                {
                    combined = Path.Combine(combined, segment);
                }
            }

            return combined;
        }
    }

    public static class ZebraDashCatalogIo
    {
        public static MusicCatalog LoadCatalog()
        {
            string path = ZebraDashPaths.EditorCatalogPath;
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
            string path = ZebraDashPaths.EditorCatalogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ZebraDashPaths.EditorLevelsRoot);
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

        public static IEnumerator LoadCatalogAsync(Action<MusicCatalog, string> onComplete)
        {
            string path = ZebraDashPaths.ResolveCatalogPathForRead();
            yield return ReadTextAsync(path, (json, error) =>
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    onComplete?.Invoke(new MusicCatalog(), error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    onComplete?.Invoke(new MusicCatalog(), "Catalog is empty.");
                    return;
                }

                MusicCatalog catalog = JsonUtility.FromJson<MusicCatalog>(json);
                onComplete?.Invoke(catalog ?? new MusicCatalog(), string.Empty);
            });
        }

        public static IEnumerator LoadBeatMapAsync(MusicTrackEntry entry, Action<BeatMap, string> onComplete)
        {
            if (entry == null)
            {
                onComplete?.Invoke(new BeatMap(), "Track entry is null.");
                yield break;
            }

            string levelPath = ZebraDashPaths.ResolveLevelPathForRead(entry.levelPath);
            yield return ReadTextAsync(levelPath, (json, error) =>
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    onComplete?.Invoke(new BeatMap
                    {
                        trackId = entry.trackId,
                        bpm = entry.bpm,
                        offsetSec = entry.offsetSec
                    }, error);
                    return;
                }

                BeatMap map = BeatMapJson.FromJson(json);
                if (string.IsNullOrWhiteSpace(map.trackId))
                {
                    map.trackId = entry.trackId;
                }

                if (map.bpm <= 0.001f)
                {
                    map.bpm = entry.bpm;
                }

                onComplete?.Invoke(map, string.Empty);
            });
        }

        public static IEnumerator LoadAudioClipAsync(MusicTrackEntry entry, Action<AudioClip, string> onComplete)
        {
            if (entry == null)
            {
                onComplete?.Invoke(null, "Track entry is null.");
                yield break;
            }

            string path = ZebraDashPaths.ResolveAudioPathForRead(entry.GetAudioReferencePath());
            if (string.IsNullOrWhiteSpace(path))
            {
                onComplete?.Invoke(null, "Audio path is empty.");
                yield break;
            }

            if (!ZebraDashPaths.IsUrlLike(path) && !File.Exists(path))
            {
                onComplete?.Invoke(null, $"Audio file not found: {path}");
                yield break;
            }

            string url = ZebraDashPaths.ToUnityWebRequestUrl(path);
            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, $"Audio load failed: {request.error}");
                yield break;
            }

            onComplete?.Invoke(DownloadHandlerAudioClip.GetContent(request), string.Empty);
        }

        private static IEnumerator ReadTextAsync(string path, Action<string, string> onComplete)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                onComplete?.Invoke(string.Empty, "Path is empty.");
                yield break;
            }

            if (!ZebraDashPaths.IsUrlLike(path))
            {
                if (!File.Exists(path))
                {
                    onComplete?.Invoke(string.Empty, $"File not found: {path}");
                    yield break;
                }

                string text = File.ReadAllText(path);
                onComplete?.Invoke(text, string.Empty);
                yield break;
            }

            string url = ZebraDashPaths.ToUnityWebRequestUrl(path);
            using UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(string.Empty, $"Read failed: {request.error} ({url})");
                yield break;
            }

            onComplete?.Invoke(request.downloadHandler.text, string.Empty);
        }
    }
}
