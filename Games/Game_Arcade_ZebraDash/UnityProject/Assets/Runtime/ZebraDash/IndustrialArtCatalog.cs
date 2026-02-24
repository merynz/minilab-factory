using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ZebraDash
{
    public static class IndustrialArtCatalog
    {
        private const string ResourcesRoot = "ZebraDashArtLocal/Industrial";
        private const string TilesetsPath = ResourcesRoot + "/Tilesets";
        private const string BackgroundsPath = ResourcesRoot + "/Backgrounds";
        private const string HazardsPath = ResourcesRoot + "/Hazards";

        private static readonly string[] ThemeCycle = { "B", "I", "C", "D", "E", "F", "H", "BASE" };
        private static readonly string[] ThemeCodes = { "BASE", "B", "C", "D", "E", "F", "H", "I" };
        private static readonly HashSet<string> ThemeCodeSet = new HashSet<string>(ThemeCodes, StringComparer.OrdinalIgnoreCase);
        private static readonly string[] VariantTokens = { "B", "C", "D", "E", "F", "H", "I" };
        private static readonly Regex FrameNumberRegex = new Regex(@"(\d+)$", RegexOptions.Compiled);

        private static readonly int[] SolidTopBank =
        {
            0, 1, 2, 3, 8, 9, 10, 11,
            24, 25, 26, 27, 32, 33, 34, 35
        };

        // Keep bottom wall tiles in the same atlas family as top wall (pages 1/2).
        private static readonly int[] SolidBottomBank =
        {
            12, 13, 14, 15, 18, 19, 20, 21,
            36, 37, 38, 39, 42, 43, 44, 45
        };

        private static readonly int[] SolidInnerBank =
        {
            4, 5, 6, 7, 16, 17, 22, 23,
            28, 29, 30, 31, 40, 41, 46, 47
        };

        // Hazard atlas content lives primarily on page 3 (48..71 for 3x 96x64 sheets).
        private static readonly int[] DangerBank =
        {
            48, 49, 50, 51, 52, 53, 54, 55,
            56, 57, 58, 59, 60, 61, 62, 63,
            64, 65, 66, 67, 68, 69, 70, 71
        };

        private static readonly int[] DecoBank =
        {
            6, 7, 16, 17, 22, 23,
            30, 31, 40, 41, 46, 47,
            56, 57, 58, 59
        };

        private static readonly int[] ModifierBank =
        {
            32, 33, 34, 35, 36, 37, 38, 39,
            40, 41, 42, 43, 44, 45, 46, 47
        };

        private static bool loaded;
        private static Sprite[] allTiles = Array.Empty<Sprite>();
        private static Sprite[] allBackgrounds = Array.Empty<Sprite>();
        private static Sprite[] allHazards = Array.Empty<Sprite>();
        private static readonly Dictionary<string, Sprite[]> tilesByTheme = new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite[]> backgroundsByTheme = new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite[]> hazardsByType = new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);

        public static bool HasIndustrialArtwork
        {
            get
            {
                EnsureLoaded();
                return allTiles.Length + allBackgrounds.Length + allHazards.Length > 0;
            }
        }

        public static string ResolveThemeCode(string trackId, int trackIndex, string explicitThemeCode)
        {
            if (!string.IsNullOrWhiteSpace(explicitThemeCode))
            {
                string normalized = NormalizeThemeCode(explicitThemeCode);
                if (ThemeCodeSet.Contains(normalized))
                {
                    return normalized;
                }
            }

            if (trackIndex >= 0)
            {
                return ThemeCycle[Mathf.Abs(trackIndex) % ThemeCycle.Length];
            }

            int seed = string.IsNullOrWhiteSpace(trackId) ? 0 : trackId.GetHashCode();
            return ThemeCycle[Mathf.Abs(seed) % ThemeCycle.Length];
        }

        public static Sprite ResolveBackgroundSprite(string themeCode, bool farLayer, int seed)
        {
            EnsureLoaded();
            Sprite[] pool = GetThemeBackgrounds(themeCode);
            if (pool.Length == 0)
            {
                return null;
            }

            Sprite[] filtered = pool
                .Where(s =>
                {
                    if (s == null || s.texture == null)
                    {
                        return false;
                    }

                    string upper = s.texture.name.ToUpperInvariant();
                    bool isFar = upper.Contains("FAR");
                    return farLayer ? isFar : !isFar;
                })
                .ToArray();
            Sprite[] source = filtered.Length > 0 ? filtered : pool;
            return source[StableIndex(seed, source.Length)];
        }

        public static Sprite ResolveTileSprite(
            string themeCode,
            string visualKind,
            bool isDanger,
            bool isSolid,
            bool isDecor,
            int variant,
            int xCell,
            int yCell,
            int seed)
        {
            EnsureLoaded();
            Sprite[] pool = GetThemeTiles(themeCode);
            if (pool.Length == 0)
            {
                return null;
            }

            string kind = visualKind ?? string.Empty;
            int hash = StableHash(seed, xCell, yCell, variant, kind.GetHashCode());
            int[] bank = ResolveBank(kind, isDanger, isSolid, isDecor);
            if (bank.Length > 0)
            {
                int bankPick = bank[StableIndex(hash, bank.Length)];
                if (bankPick >= 0 && bankPick < pool.Length)
                {
                    return pool[bankPick];
                }
            }

            return pool[StableIndex(hash, pool.Length)];
        }

        public static Sprite[] ResolveHazardFrames(string hazardType, int variant = 0, int seed = 0)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(hazardType))
            {
                return Array.Empty<Sprite>();
            }

            string key = hazardType.Trim().ToLowerInvariant();
            if (string.Equals(key, "spike", StringComparison.Ordinal))
            {
                // Alternate between both yellow spike sets for visible variation.
                string setKey = ((Mathf.Abs(variant) + Mathf.Abs(seed)) & 1) == 0
                    ? "spike_yellow_1"
                    : "spike_yellow_2";
                if (hazardsByType.TryGetValue(setKey, out Sprite[] spikeFrames) && spikeFrames != null && spikeFrames.Length > 0)
                {
                    return spikeFrames;
                }
            }

            if (hazardsByType.TryGetValue(key, out Sprite[] frames) && frames != null && frames.Length > 0)
            {
                return frames;
            }

            if (string.Equals(key, "sheet", StringComparison.Ordinal)
                && hazardsByType.TryGetValue("sheet_green_1", out Sprite[] sheetFrames) && sheetFrames != null && sheetFrames.Length > 0)
            {
                return sheetFrames;
            }

            if (string.Equals(key, "piston", StringComparison.Ordinal)
                && hazardsByType.TryGetValue("piston_blue_1", out Sprite[] pistonFrames) && pistonFrames != null && pistonFrames.Length > 0)
            {
                return pistonFrames;
            }

            if (string.Equals(key, "circular", StringComparison.Ordinal)
                && hazardsByType.TryGetValue("circular_brown_1", out Sprite[] circularFrames) && circularFrames != null && circularFrames.Length > 0)
            {
                return circularFrames;
            }

            return Array.Empty<Sprite>();
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            allTiles = LoadAndSort(TilesetsPath);
            allBackgrounds = LoadAndSort(BackgroundsPath);
            allHazards = LoadAndSort(HazardsPath);

            tilesByTheme.Clear();
            backgroundsByTheme.Clear();
            hazardsByType.Clear();

            for (int i = 0; i < ThemeCodes.Length; i++)
            {
                string code = ThemeCodes[i];
                tilesByTheme[code] = FilterByTheme(allTiles, code);
                backgroundsByTheme[code] = FilterByTheme(allBackgrounds, code);
            }

            Sprite[] spike1 = FilterHazards("YELLOWSPIKE1");
            Sprite[] spike2 = FilterHazards("YELLOWSPIKE2");
            Sprite[] spikeGeneric = FilterHazards("YELLOWSPIKE");
            Sprite[] sheet1 = FilterHazards("GREENSHEETSPIKE1");
            Sprite[] sheetGeneric = FilterHazards("GREENSHEETSPIKE");
            Sprite[] piston1 = FilterHazards("BLUEPISTON1");
            Sprite[] pistonGeneric = FilterHazards("BLUEPISTON");
            Sprite[] circular1 = FilterHazards("BROWNCIRCULAR1");
            Sprite[] circularGeneric = FilterHazards("BROWNCIRCULAR");

            hazardsByType["spike_yellow_1"] = spike1;
            hazardsByType["spike_yellow_2"] = spike2;
            hazardsByType["sheet_green_1"] = sheet1;
            hazardsByType["piston_blue_1"] = piston1;
            hazardsByType["circular_brown_1"] = circular1;

            hazardsByType["spike"] = MergeFrames(spike1, spike2, spikeGeneric);
            hazardsByType["sheet"] = MergeFrames(sheet1, sheetGeneric);
            hazardsByType["piston"] = MergeFrames(piston1, pistonGeneric);
            hazardsByType["circular"] = MergeFrames(circular1, circularGeneric);

            loaded = true;
        }

        private static Sprite[] LoadAndSort(string resourcesPath)
        {
            Sprite[] loadedSprites = Resources.LoadAll<Sprite>(resourcesPath);
            if (loadedSprites == null || loadedSprites.Length == 0)
            {
                return Array.Empty<Sprite>();
            }

            return loadedSprites
                .Where(s => s != null)
                .OrderBy(s => s.texture != null ? s.texture.name : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => ResolveFrameNumber(s.name))
                .ThenBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static Sprite[] FilterByTheme(Sprite[] source, string themeCode)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<Sprite>();
            }

            string normalized = NormalizeThemeCode(themeCode);
            List<Sprite> filtered = new List<Sprite>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                Sprite sprite = source[i];
                if (sprite == null || sprite.texture == null)
                {
                    continue;
                }

                if (MatchesTheme(sprite.texture.name, normalized))
                {
                    filtered.Add(sprite);
                }
            }

            return filtered.Count > 0 ? filtered.ToArray() : source;
        }

        private static bool MatchesTheme(string rawName, string themeCode)
        {
            string upper = (rawName ?? string.Empty).ToUpperInvariant();
            if (themeCode == "BASE")
            {
                if (upper.Contains("TILES_BASE_") || upper.Contains("BG_BASE_"))
                {
                    return true;
                }

                for (int i = 0; i < VariantTokens.Length; i++)
                {
                    if (HasVariantToken(upper, VariantTokens[i]))
                    {
                        return false;
                    }
                }

                return upper.Contains("INDUSTRIAL_TILESET");
            }

            if (upper.Contains($"TILES_{themeCode}_") || upper.Contains($"BG_{themeCode}_"))
            {
                return true;
            }

            return HasVariantToken(upper, themeCode);
        }

        private static bool HasVariantToken(string upperName, string token)
        {
            if (string.IsNullOrWhiteSpace(upperName) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            // Examples: 1B / 2B / 3B, _1H_, _3I_, etc.
            string pattern = $@"(?:^|[_\-])(?:[123]){Regex.Escape(token)}(?:[_\-]|$)";
            return Regex.IsMatch(upperName, pattern);
        }

        private static Sprite[] FilterHazards(string token)
        {
            if (allHazards.Length == 0)
            {
                return Array.Empty<Sprite>();
            }

            Sprite[] allMatches = allHazards
                .Where(s => s != null && s.texture != null && s.texture.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => ResolveFrameNumber(s.name))
                .ThenBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (allMatches.Length == 0)
            {
                return Array.Empty<Sprite>();
            }

            // Prefer explicit frame strips over packed spritesheet slices when both exist.
            Sprite[] frameMatches = allMatches
                .Where(s => s.texture != null && s.texture.name.IndexOf("_frames_", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            Sprite[] filtered = frameMatches.Length > 0 ? frameMatches : allMatches;

            return filtered.Length > 0 ? filtered : Array.Empty<Sprite>();
        }

        private static Sprite[] MergeFrames(params Sprite[][] groups)
        {
            if (groups == null || groups.Length == 0)
            {
                return Array.Empty<Sprite>();
            }

            var set = new HashSet<Sprite>();
            var merged = new List<Sprite>(64);
            for (int i = 0; i < groups.Length; i++)
            {
                Sprite[] group = groups[i];
                if (group == null || group.Length == 0)
                {
                    continue;
                }

                for (int j = 0; j < group.Length; j++)
                {
                    Sprite sprite = group[j];
                    if (sprite == null || !set.Add(sprite))
                    {
                        continue;
                    }

                    merged.Add(sprite);
                }
            }

            return merged.Count > 0 ? merged.ToArray() : Array.Empty<Sprite>();
        }

        private static string NormalizeThemeCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "BASE";
            }

            string upper = raw.Trim().ToUpperInvariant();
            return upper switch
            {
                "A" => "BASE",
                "DEFAULT" => "BASE",
                _ => upper
            };
        }

        private static int[] ResolveBank(string visualKind, bool isDanger, bool isSolid, bool isDecor)
        {
            if (visualKind.StartsWith("wall_top", StringComparison.OrdinalIgnoreCase))
            {
                return SolidTopBank;
            }

            if (visualKind.StartsWith("wall_bottom", StringComparison.OrdinalIgnoreCase))
            {
                return SolidBottomBank;
            }

            if (visualKind.StartsWith("speed_pad", StringComparison.OrdinalIgnoreCase)
                || visualKind.StartsWith("gravity_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(visualKind, "spring_pad", StringComparison.OrdinalIgnoreCase)
                || string.Equals(visualKind, "jump_ring", StringComparison.OrdinalIgnoreCase))
            {
                return ModifierBank;
            }

            if (isDanger)
            {
                return DangerBank;
            }

            if (isSolid)
            {
                return SolidInnerBank;
            }

            if (isDecor)
            {
                return DecoBank;
            }

            return Array.Empty<int>();
        }

        private static int ResolveFrameNumber(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return 0;
            }

            Match match = FrameNumberRegex.Match(name);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
            {
                return value;
            }

            return 0;
        }

        private static int StableHash(int a, int b, int c, int d, int e)
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) + a;
                h = (h * 31) + b;
                h = (h * 31) + c;
                h = (h * 31) + d;
                h = (h * 31) + e;
                return h;
            }
        }

        private static int StableIndex(int seed, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int idx = seed % count;
            if (idx < 0)
            {
                idx += count;
            }

            return idx;
        }

        private static Sprite[] GetThemeTiles(string themeCode)
        {
            string normalized = NormalizeThemeCode(themeCode);
            if (tilesByTheme.TryGetValue(normalized, out Sprite[] sprites) && sprites != null && sprites.Length > 0)
            {
                return sprites;
            }

            return allTiles;
        }

        private static Sprite[] GetThemeBackgrounds(string themeCode)
        {
            string normalized = NormalizeThemeCode(themeCode);
            if (backgroundsByTheme.TryGetValue(normalized, out Sprite[] sprites) && sprites != null && sprites.Length > 0)
            {
                return sprites;
            }

            return allBackgrounds;
        }
    }
}
