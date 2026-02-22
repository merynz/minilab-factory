using System;
using System.IO;
using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    public static class BeatMapJson
    {
        public static BeatMap FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new BeatMap();
            }

            BeatMap parsed = JsonUtility.FromJson<BeatMap>(json);
            return parsed ?? new BeatMap();
        }

        public static BeatMap LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Beat map not found: {path}");
            }

            return FromJson(File.ReadAllText(path));
        }

        public static string ToJson(BeatMap beatMap, bool prettyPrint = true)
        {
            return JsonUtility.ToJson(beatMap, prettyPrint);
        }

        public static void SaveToFile(string path, BeatMap beatMap)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, ToJson(beatMap));
        }
    }
}
