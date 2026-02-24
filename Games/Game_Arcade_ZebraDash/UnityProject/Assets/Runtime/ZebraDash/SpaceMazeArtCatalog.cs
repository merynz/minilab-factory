using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZebraDash
{
    public static class SpaceMazeArtCatalog
    {
        private const string ResourcesRoot = "ZebraDashArtLocal";
        // Keep external artwork fully disabled until final art integration pass.
        public static bool UseExternalArtwork = false;
        // UI/player overrides are only meaningful when external artwork is enabled.
        public static bool UseUiAndPlayerOverrides = false;
        private static bool loaded;
        private static Sprite[] backgroundPool = Array.Empty<Sprite>();
        private static Sprite[] obstaclePool = Array.Empty<Sprite>();
        private static Sprite[] decorPool = Array.Empty<Sprite>();
        private static Sprite[] vfxPool = Array.Empty<Sprite>();
        private static Sprite[] tilesetPool = Array.Empty<Sprite>();
        private static Sprite[] uiPool = Array.Empty<Sprite>();
        private static Sprite[] playerPool = Array.Empty<Sprite>();
        private static readonly Dictionary<string, Sprite> exactNameLookup = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly int[][] WallTopBanks =
        {
            new[] { 0, 1, 2, 3, 24, 25, 26, 27, 36, 37, 38, 39 },
            new[] { 8, 9, 10, 11, 32, 33, 34, 35, 72, 73, 74, 75 },
            new[] { 12, 13, 14, 15, 40, 41, 42, 43, 76, 77, 78, 79 },
            new[] { 4, 5, 6, 7, 28, 29, 30, 31, 68, 69, 70, 71 }
        };

        private static readonly int[][] WallBottomBanks =
        {
            new[] { 48, 49, 50, 51, 80, 81, 82, 83, 88, 89, 90, 91 },
            new[] { 52, 53, 54, 55, 84, 85, 86, 87, 92, 93, 94, 95 },
            new[] { 60, 61, 62, 63, 72, 73, 74, 75, 44, 45, 46, 47 },
            new[] { 56, 57, 58, 59, 64, 65, 66, 67, 40, 41, 42, 43 }
        };

        private static readonly int[] LaneRailTileIndicesLower = { 60, 61, 62, 63, 64, 65 };
        private static readonly int[] LaneRailTileIndicesUpper = { 68, 69, 70, 71, 72, 73 };
        private static readonly int[] UiPanelTileIndices = { 24, 25, 26, 27, 32, 33, 34, 35 };
        private static readonly int[] UiButtonTileIndices = { 36, 37, 38, 39, 40, 41, 42, 43 };
        private static readonly int[] UiBackdropTileIndices = { 0, 1, 8, 9, 48, 49, 52, 53 };

        public static bool HasArtwork
        {
            get
            {
                EnsureLoaded();
                return backgroundPool.Length + obstaclePool.Length + decorPool.Length + vfxPool.Length + tilesetPool.Length + uiPool.Length + playerPool.Length > 0;
            }
        }

        public static Sprite ResolvePlayerSprite(int seed = 0)
        {
            EnsureLoaded();
            if (UseUiAndPlayerOverrides)
            {
                Sprite custom = PickByPriority(
                    seed,
                    playerPool,
                    new[] { "player_custom" },
                    new[] { "ship_main_icon" },
                    new[] { "ship" });
                if (custom != null)
                {
                    return custom;
                }
            }

            return PickByPriority(
                seed,
                obstaclePool,
                new[] { "laser_turret" },
                new[] { "electric_turret" },
                new[] { "control_panel_idle" });
        }

        public static Sprite ResolveLayerSprite(int layerIndex, string motifId, int seed)
        {
            EnsureLoaded();
            if (backgroundPool.Length == 0)
            {
                return null;
            }

            if (layerIndex <= 0 || layerIndex >= 3)
            {
                return PickByPriority(
                    seed + layerIndex,
                    backgroundPool,
                    new[] { "starfield" },
                    new[] { "blue", "nebula" },
                    new[] { "nebula" });
            }

            string nebulaColor = ResolveNebulaColorToken(motifId);
            if (layerIndex == 1)
            {
                return PickByPriority(
                    seed + 17,
                    backgroundPool,
                    new[] { nebulaColor, "nebula" },
                    new[] { "blue", "nebula" },
                    new[] { "nebula" });
            }

            return PickByPriority(
                seed + 41,
                backgroundPool,
                new[] { nebulaColor, "nebula" },
                new[] { "purple", "nebula" },
                new[] { "green", "nebula" },
                new[] { "nebula" });
        }

        public static Sprite ResolveRibSprite(string motifId, int ribIndex, int seed)
        {
            EnsureLoaded();
            if (tilesetPool.Length > 0)
            {
                int motifBias = StableIndex((motifId != null ? motifId.GetHashCode() : 0) + (seed * 13), tilesetPool.Length);
                int stride = 5 + Mathf.Abs(ribIndex % 7);
                return tilesetPool[StableIndex(motifBias + (ribIndex * stride), tilesetPool.Length)];
            }

            Sprite fromDecor = PickByPriority(
                seed + ribIndex * 11,
                decorPool,
                new[] { "platform", "moving" },
                new[] { "tile" },
                new[] { "decor" },
                new[] { "barrier", "idle" });
            if (fromDecor != null)
            {
                return fromDecor;
            }

            return ResolveLayerSprite(2, motifId, seed + ribIndex);
        }

        public static Sprite ResolveMazeWallTile(string motifId, int segmentIndex, bool upperBand, int seed)
        {
            EnsureLoaded();
            if (tilesetPool.Length == 0)
            {
                return ResolveRibSprite(motifId, segmentIndex, seed);
            }

            int motifHash = motifId != null ? motifId.GetHashCode() : 0;
            int bankIndex = StableIndex(motifHash + (seed * 5), WallTopBanks.Length);
            int[] bank = upperBand ? WallTopBanks[bankIndex] : WallBottomBanks[bankIndex];
            if (bank == null || bank.Length == 0)
            {
                return tilesetPool[StableIndex(seed + segmentIndex, tilesetPool.Length)];
            }

            int tileIndex = bank[StableIndex(segmentIndex + seed, bank.Length)];
            Sprite selected = ResolveTileByIndex(tileIndex);
            return selected ?? tilesetPool[StableIndex(seed + segmentIndex, tilesetPool.Length)];
        }

        public static Sprite ResolveLaneRailTile(int lane, int seed)
        {
            EnsureLoaded();
            if (tilesetPool.Length == 0)
            {
                return null;
            }

            int[] bank = lane <= 0 ? LaneRailTileIndicesLower : LaneRailTileIndicesUpper;
            int tileIndex = bank[StableIndex(seed, bank.Length)];
            Sprite selected = ResolveTileByIndex(tileIndex);
            return selected ?? tilesetPool[StableIndex(seed, tilesetPool.Length)];
        }

        public static Sprite ResolveUiPanelTile(int seed)
        {
            EnsureLoaded();
            if (UseUiAndPlayerOverrides)
            {
                Sprite uiPanel = PickByPriority(
                    seed,
                    uiPool,
                    new[] { "Level_Menu_Window" },
                    new[] { "Pause_Window" },
                    new[] { "Main_Menu_BG" },
                    new[] { "Level_Menu_Table" },
                    new[] { "Pause_Table" });
                if (uiPanel != null)
                {
                    return uiPanel;
                }
            }

            if (tilesetPool.Length == 0)
            {
                return null;
            }

            int tileIndex = UiPanelTileIndices[StableIndex(seed, UiPanelTileIndices.Length)];
            return ResolveTileByIndex(tileIndex) ?? tilesetPool[StableIndex(seed, tilesetPool.Length)];
        }

        public static Sprite ResolveUiButtonTile(int seed)
        {
            return ResolveUiButtonTile("default", seed);
        }

        public static Sprite ResolveUiButtonTile(string label, int seed)
        {
            EnsureLoaded();
            if (UseUiAndPlayerOverrides && uiPool.Length > 0)
            {
                string labelKey = label ?? string.Empty;
                Sprite uiButton = null;
                if (labelKey.IndexOf("play", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    uiButton = PickByPriority(seed, uiPool, new[] { "BTN_Play" }, new[] { "Level_Menu_Play_BTN" }, new[] { "Main_Menu_Start_BTN" });
                }
                else if (labelKey.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("resume", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    uiButton = PickByPriority(seed, uiPool, new[] { "BTN_Pause" }, new[] { "Pause_Ok_BTN" });
                }
                else if (labelKey.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("quit", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("exit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    uiButton = PickByPriority(seed, uiPool, new[] { "BTN_Menu" }, new[] { "Pause_Menu_BTN" }, new[] { "Main_Menu_Exit_BTN" });
                }
                else if (labelKey.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("sync", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    uiButton = PickByPriority(seed, uiPool, new[] { "BTN_Settings" }, new[] { "Main_Menu_Settings_BTN" });
                }
                else if (labelKey.IndexOf("restart", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("retry", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("next", StringComparison.OrdinalIgnoreCase) >= 0 || labelKey.IndexOf("replay", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    uiButton = PickByPriority(seed, uiPool, new[] { "BTN_Replay" }, new[] { "BTN_Ok" });
                }
                else
                {
                    uiButton = PickByPriority(seed, uiPool, new[] { "BTN_Ok" }, new[] { "BTN_Close" });
                }

                if (uiButton != null)
                {
                    return uiButton;
                }
            }

            if (tilesetPool.Length == 0)
            {
                return null;
            }

            int tileIndex = UiButtonTileIndices[StableIndex(seed, UiButtonTileIndices.Length)];
            return ResolveTileByIndex(tileIndex) ?? tilesetPool[StableIndex(seed, tilesetPool.Length)];
        }

        public static Sprite ResolveUiBackdropTile(int seed)
        {
            EnsureLoaded();
            if (UseUiAndPlayerOverrides)
            {
                Sprite uiBg = PickByPriority(seed, uiPool, new[] { "Main_Menu_BG" }, new[] { "Main_UI_BG" });
                if (uiBg != null)
                {
                    return uiBg;
                }
            }

            if (tilesetPool.Length == 0)
            {
                return ResolveLayerSprite(0, "MOTIF_HYPERLANE_NEON", seed);
            }

            int tileIndex = UiBackdropTileIndices[StableIndex(seed, UiBackdropTileIndices.Length)];
            return ResolveTileByIndex(tileIndex) ?? tilesetPool[StableIndex(seed, tilesetPool.Length)];
        }

        public static Sprite ResolveDustSprite(int index, int seed)
        {
            EnsureLoaded();
            return PickByPriority(
                seed + index * 31,
                vfxPool,
                new[] { "trace" },
                new[] { "spark" },
                new[] { "star" },
                new[] { "smoke" },
                new[] { "twirl" });
        }

        public static Sprite ResolveTelegraphSprite(string channel, string archetype, int seed)
        {
            EnsureLoaded();
            if (string.Equals(channel, "tether", StringComparison.OrdinalIgnoreCase))
            {
                return PickByPriority(
                    seed,
                    vfxPool,
                    new[] { "trace" },
                    new[] { "slash" },
                    new[] { "light" });
            }

            if (string.Equals(channel, "floor", StringComparison.OrdinalIgnoreCase))
            {
                return PickByPriority(
                    seed,
                    vfxPool,
                    new[] { "flare" },
                    new[] { "smoke" },
                    new[] { "light" });
            }

            if (string.Equals(channel, "hold", StringComparison.OrdinalIgnoreCase))
            {
                return PickByPriority(
                    seed,
                    vfxPool,
                    new[] { "light" },
                    new[] { "window" },
                    new[] { "circle" });
            }

            if (string.Equals(channel, "glyph_primary", StringComparison.OrdinalIgnoreCase))
            {
                return PickByPriority(
                    seed,
                    vfxPool,
                    new[] { "symbol_01" },
                    new[] { "slash" },
                    new[] { "window" });
            }

            if (string.Equals(channel, "glyph_secondary", StringComparison.OrdinalIgnoreCase))
            {
                return PickByPriority(
                    seed,
                    vfxPool,
                    new[] { "symbol_02" },
                    new[] { "trace" },
                    new[] { "light" });
            }

            if (string.Equals(channel, "glyph_ring", StringComparison.OrdinalIgnoreCase))
            {
                return PickByPriority(
                    seed,
                    vfxPool,
                    new[] { "circle" },
                    new[] { "window" },
                    new[] { "twirl" });
            }

            return null;
        }

        public static Sprite ResolveObstacleSprite(string archetype, int lane, float intensity, int seed)
        {
            EnsureLoaded();
            Sprite sprite = null;
            if (string.Equals(archetype, GameplayArchetypes.LaneBlock, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "laser", "idle" }, new[] { "barrier", "idle" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.AccentCrusher, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "laser_spikes", "activate" }, new[] { "laser", "activate" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.AlternatorPair, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "laser_turret" }, new[] { "laser", "idle" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.StreakBreaker, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "wall_blade" }, new[] { "saw", "idle" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.HoldLaneLock, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "barrier", "idle" }, new[] { "platform", "moving" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.HoldReleaseGate, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "barrier", "deactivate" }, new[] { "barrier", "idle" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.CrossGate, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "laser_spikes", "idle" }, new[] { "wall_blade" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.OffbeatSnap, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "electric_turret" }, new[] { "saw", "activate" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.SpinnerSentinel, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "saw", "idle" }, new[] { "saw", "activate" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.RisingWall, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "platform", "moving" }, new[] { "wall_blade" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.FakeoutGhost, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, obstaclePool, new[] { "laser", "deactivate" }, new[] { "laser_spikes", "deactivate" });
            }
            else if (string.Equals(archetype, GameplayArchetypes.RestPulse, StringComparison.OrdinalIgnoreCase))
            {
                sprite = PickByPriority(seed, vfxPool, new[] { "star" }, new[] { "circle" });
            }

            if (sprite != null)
            {
                return sprite;
            }

            if (obstaclePool.Length > 0)
            {
                return obstaclePool[StableIndex(seed + lane + Mathf.RoundToInt(intensity * 10f), obstaclePool.Length)];
            }

            if (decorPool.Length > 0)
            {
                return decorPool[StableIndex(seed + 13, decorPool.Length)];
            }

            return null;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            if (!UseExternalArtwork)
            {
                backgroundPool = Array.Empty<Sprite>();
                obstaclePool = Array.Empty<Sprite>();
                decorPool = Array.Empty<Sprite>();
                vfxPool = Array.Empty<Sprite>();
                tilesetPool = Array.Empty<Sprite>();
                uiPool = Array.Empty<Sprite>();
                playerPool = Array.Empty<Sprite>();
                exactNameLookup.Clear();
                loaded = true;
                return;
            }

            backgroundPool = LoadPool("Backgrounds");
            obstaclePool = LoadPool("Obstacles");
            decorPool = LoadPool("Decor");
            vfxPool = LoadPool("VFX");
            tilesetPool = LoadPool("Tileset");
            uiPool = LoadPool("UI");
            playerPool = LoadPool("Player");
            exactNameLookup.Clear();
            IndexPool(backgroundPool);
            IndexPool(obstaclePool);
            IndexPool(decorPool);
            IndexPool(vfxPool);
            IndexPool(tilesetPool);
            IndexPool(uiPool);
            IndexPool(playerPool);
            loaded = true;
        }

        private static Sprite[] LoadPool(string relativePath)
        {
            Sprite[] loadedSprites = Resources.LoadAll<Sprite>($"{ResourcesRoot}/{relativePath}");
            if (loadedSprites != null && loadedSprites.Length > 0)
            {
                return loadedSprites
                    .OrderBy(sprite => sprite != null ? sprite.name : string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return Array.Empty<Sprite>();
        }

        private static void IndexPool(Sprite[] pool)
        {
            if (pool == null)
            {
                return;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                Sprite sprite = pool[i];
                if (sprite == null || string.IsNullOrWhiteSpace(sprite.name))
                {
                    continue;
                }

                if (!exactNameLookup.ContainsKey(sprite.name))
                {
                    exactNameLookup[sprite.name] = sprite;
                }
            }
        }

        private static Sprite PickByPriority(int seed, Sprite[] pool, params string[][] tokenGroups)
        {
            if (pool == null || pool.Length == 0)
            {
                return null;
            }

            if (tokenGroups != null)
            {
                for (int i = 0; i < tokenGroups.Length; i++)
                {
                    string[] tokens = tokenGroups[i];
                    if (tokens == null || tokens.Length == 0)
                    {
                        continue;
                    }

                    Sprite sprite = PickByTokens(pool, seed + i * 7, tokens);
                    if (sprite != null)
                    {
                        return sprite;
                    }
                }
            }

            return pool[StableIndex(seed, pool.Length)];
        }

        private static Sprite PickByTokens(Sprite[] pool, int seed, params string[] tokens)
        {
            if (pool == null || pool.Length == 0 || tokens == null || tokens.Length == 0)
            {
                return null;
            }

            var matches = new List<Sprite>(pool.Length);
            for (int i = 0; i < pool.Length; i++)
            {
                Sprite sprite = pool[i];
                if (sprite == null)
                {
                    continue;
                }

                if (NameContainsAll(sprite.name, tokens))
                {
                    matches.Add(sprite);
                }
            }

            if (matches.Count == 0)
            {
                return null;
            }

            return matches[StableIndex(seed, matches.Count)];
        }

        private static bool NameContainsAll(string source, string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (source.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static int StableIndex(int seed, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int index = seed % count;
            if (index < 0)
            {
                index += count;
            }

            return index;
        }

        private static Sprite ResolveTileByIndex(int tileIndex)
        {
            string key = $"tile{Mathf.Clamp(tileIndex, 0, 999):000}";
            if (exactNameLookup.TryGetValue(key, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }

            return null;
        }

        private static string ResolveNebulaColorToken(string motifId)
        {
            if (string.IsNullOrWhiteSpace(motifId))
            {
                return "blue";
            }

            if (ContainsAny(motifId, "LASER", "MAGENTA", "VOID", "IRIS"))
            {
                return "purple";
            }

            if (ContainsAny(motifId, "EMERALD", "CONDUIT", "GREEN"))
            {
                return "green";
            }

            return "blue";
        }

        private static bool ContainsAny(string source, params string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (source.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
