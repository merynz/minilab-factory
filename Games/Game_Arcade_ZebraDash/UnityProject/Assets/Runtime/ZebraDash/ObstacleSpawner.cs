
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using ZebraDash.LevelDesign;

namespace ZebraDash
{
    public sealed class ObstacleSpawner : MonoBehaviour
    {
        private const float PixelSnapPpu = 16f;

        [SerializeField] private LevelRunner levelRunner;
        [SerializeField] private bool useStaticMazeDesign = true;
        [SerializeField] private bool useTilemapRendering = true;
        [SerializeField] private bool enableAnimatedHazardOverlays = true;
        [SerializeField] private float visibleMinX = -20f;
        [SerializeField] private float visibleMaxX = 24f;
        [SerializeField] private float farParallaxRatio = 0.15f;
        [SerializeField] private float midParallaxRatio = 0.35f;
        [SerializeField] private float foregroundParallaxRatio = 1.00f;

        private readonly List<PropInstance> props = new List<PropInstance>(4096);
        private readonly List<AnimatedHazardInstance> animatedHazards = new List<AnimatedHazardInstance>(512);
        private readonly List<MazeGapColumn> gapColumns = new List<MazeGapColumn>(2048);
        private readonly List<MazeBeatCue> beatCues = new List<MazeBeatCue>(1024);
        private readonly List<MazeTileSpec> dangerCollisionSpecs = new List<MazeTileSpec>(4096);
        private readonly Dictionary<Sprite, TileBase> tileCache = new Dictionary<Sprite, TileBase>();
        private static readonly SpawnDirective[] EmptyDirectives = Array.Empty<SpawnDirective>();
        private int activeCount;
        private int createdCount;
        private int foregroundCellCount;
        private string activeThemeCode = "BASE";
        private int activeThemeSeed;

        private Transform tilemapRoot;
        private Grid tilemapGrid;
        private Tilemap tilemapBgFar;
        private Tilemap tilemapBgMid;
        private Tilemap tilemapSolid;
        private Tilemap tilemapHazard;
        private Tilemap tilemapDeco;
        private Transform hazardsRoot;
        private TilemapCollider2D solidCollider;
        private CompositeCollider2D solidComposite;
        private Rigidbody2D solidBody;
        private TilemapCollider2D hazardCollider;
        private CompositeCollider2D hazardComposite;
        private Rigidbody2D hazardBody;

        [Serializable]
        public struct SpawnDirective
        {
            public GameplayPatternEvent PatternEvent;
            public float SpawnTimeSec;
            public float TravelTimeSec;
            public float TelegraphLeadSec;
            public float HitTimeSec;
            public float EndTimeSec;
            public int Seed;
        }

        private sealed class PropInstance
        {
            public MazeTileSpec Spec;
            public Transform Transform;
            public Renderer Renderer;
            public float BaseRotationZ;
            public Vector3 BaseScale;
            public Color BaseColor;
        }

        private sealed class AnimatedHazardInstance
        {
            public MazeTileSpec Spec;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Sprite[] Frames;
            public float Fps;
            public float Phase;
            public Vector3 BaseScale;
            public float BaseRotationZ;
            public float SpinRateDegPerSec;
            public float MotionAmpX;
            public float MotionAmpY;
            public float MotionFreq;
            public Color BaseColor;
        }

        public IReadOnlyList<SpawnDirective> Directives => EmptyDirectives;
        public IReadOnlyList<MazeBeatCue> BeatCues => beatCues;
        public GameplayPattern Pattern => new GameplayPattern();
        public int ActiveCount => activeCount;
        public int PoolCount => 0;
        public int CreatedCount => createdCount;

        public void ConfigureThemeContext(string trackId, int trackIndex, string explicitThemeCode, int themeSeed)
        {
            activeThemeCode = IndustrialArtCatalog.ResolveThemeCode(trackId, trackIndex, explicitThemeCode);
            activeThemeSeed = themeSeed;
        }

        public void Configure(GameplayPattern sourcePattern)
        {
            if (useStaticMazeDesign)
            {
                // Static maze mode is configured by ConfigureStaticLevel.
                return;
            }
        }

        public void ConfigureStaticLevel(LevelOrchestration orchestration, float durationSec, int seed)
        {
            if (levelRunner == null)
            {
                levelRunner = GetComponent<LevelRunner>();
            }

            if (activeThemeSeed == 0)
            {
                activeThemeSeed = seed;
            }

            ClearAllTiles();
            gapColumns.Clear();
            beatCues.Clear();
            dangerCollisionSpecs.Clear();
            activeCount = 0;
            createdCount = 0;
            foregroundCellCount = 0;

            if (orchestration == null)
            {
                return;
            }

            MazeLayout layout = MazeGridBuilder.Build(orchestration, durationSec, seed);
            if (layout == null)
            {
                return;
            }

            if (layout.columns != null)
            {
                gapColumns.AddRange(layout.columns);
            }

            if (layout.beatCues != null)
            {
                beatCues.AddRange(layout.beatCues);
            }

            if (layout.tiles == null || layout.tiles.Length == 0)
            {
                return;
            }

            Transform parent = levelRunner != null && levelRunner.WorldRoot != null
                ? levelRunner.WorldRoot
                : transform;
            EnsureTilemapHierarchy(parent);
            PopulateBackgroundTilemaps(layout, durationSec);

            for (int i = 0; i < layout.tiles.Length; i++)
            {
                MazeTileSpec spec = layout.tiles[i];
                if (spec == null)
                {
                    continue;
                }

                if (spec.isDanger)
                {
                    dangerCollisionSpecs.Add(spec);
                }

                if (useTilemapRendering && TryPlaceOnTilemap(spec))
                {
                    createdCount++;
                    continue;
                }

                CreatePropInstance(spec, i);
                createdCount++;
            }

            RefreshTilemapColliders();
        }

        public void Tick(float nowSec)
        {
            if (!useStaticMazeDesign)
            {
                return;
            }

            float scrollPos = levelRunner != null ? levelRunner.WorldScrollPos : 0f;
            float playerX = levelRunner != null ? levelRunner.PlayerX : 0f;
            float baseX = playerX - scrollPos;
            activeCount = 0;

            if (useTilemapRendering)
            {
                UpdateTilemapParallax(baseX);
            }

            for (int i = 0; i < props.Count; i++)
            {
                PropInstance prop = props[i];
                if (prop == null || prop.Transform == null || prop.Spec == null)
                {
                    continue;
                }

                float x = playerX + (prop.Spec.designX - scrollPos);
                bool visible = x >= visibleMinX && x <= visibleMaxX;
                if (prop.Transform.gameObject.activeSelf != visible)
                {
                    prop.Transform.gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                prop.Transform.localPosition = new Vector3(SnapToPixel(x), SnapToPixel(prop.Spec.centerY), ResolveZ(prop.Spec));
                TickPropAnimation(prop, nowSec);
                activeCount++;
            }

            for (int i = 0; i < animatedHazards.Count; i++)
            {
                AnimatedHazardInstance hazard = animatedHazards[i];
                if (hazard == null || hazard.Transform == null || hazard.Spec == null)
                {
                    continue;
                }

                float x = playerX + (hazard.Spec.designX - scrollPos);
                bool visible = x >= visibleMinX && x <= visibleMaxX;
                if (hazard.Transform.gameObject.activeSelf != visible)
                {
                    hazard.Transform.gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                hazard.Transform.localPosition = new Vector3(SnapToPixel(x), SnapToPixel(hazard.Spec.centerY), -0.82f);
                TickAnimatedHazard(hazard, nowSec);
                activeCount++;
            }

            activeCount += foregroundCellCount;
        }

        public void ResetAll()
        {
            activeCount = 0;
        }

        public bool TrySampleMazeGap(float worldX, out float gapBottom, out float gapTop, out int sectionIndex)
        {
            gapBottom = -2.2f;
            gapTop = 2.2f;
            sectionIndex = -1;
            if (gapColumns.Count == 0)
            {
                return false;
            }

            float designX = WorldToDesignX(worldX);
            int index = FindClosestGapColumnIndex(designX);
            if (index < 0 || index >= gapColumns.Count)
            {
                return false;
            }

            MazeGapColumn column = gapColumns[index];
            gapBottom = column.gapBottom;
            gapTop = column.gapTop;
            sectionIndex = column.sectionIndex;
            return true;
        }

        public bool CheckDangerCollision(float worldX, float worldY, float radius, out string reason)
        {
            reason = string.Empty;
            if (!useStaticMazeDesign || dangerCollisionSpecs.Count == 0)
            {
                return false;
            }

            float designX = WorldToDesignX(worldX);
            float xToleranceExtra = Mathf.Max(0.01f, radius * 0.35f);
            for (int i = 0; i < dangerCollisionSpecs.Count; i++)
            {
                MazeTileSpec spec = dangerCollisionSpecs[i];
                if (spec == null || !spec.isDanger)
                {
                    continue;
                }

                float halfW = Mathf.Max(0.04f, spec.width * 0.5f);
                float halfH = Mathf.Max(0.04f, spec.height * 0.5f);
                float scaleX = ResolveDangerHitScaleX(spec.visualKind);
                float scaleY = ResolveDangerHitScaleY(spec.visualKind);
                halfW *= scaleX;
                halfH *= scaleY;
                if (Mathf.Abs(designX - spec.designX) > halfW + xToleranceExtra)
                {
                    continue;
                }

                if (Mathf.Abs(worldY - spec.centerY) > halfH + radius)
                {
                    continue;
                }

                reason = $"{spec.visualKind} collision";
                return true;
            }

            return false;
        }

        private void EnsureTilemapHierarchy(Transform parent)
        {
            if (!useTilemapRendering)
            {
                return;
            }

            if (tilemapRoot == null)
            {
                GameObject rootGo = new GameObject("Grid");
                tilemapRoot = rootGo.transform;
                tilemapRoot.SetParent(parent, false);
                tilemapRoot.localPosition = new Vector3(SnapToPixel(-0.5f), SnapToPixel(-0.5f), 0f);
                tilemapGrid = rootGo.AddComponent<Grid>();
                tilemapGrid.cellSize = Vector3.one;
                tilemapGrid.cellGap = Vector3.zero;

                tilemapBgFar = CreateTilemapLayer(rootGo.transform, "Tilemap_BG_Far", 1);
                tilemapBgMid = CreateTilemapLayer(rootGo.transform, "Tilemap_BG_Mid", 5);
                tilemapSolid = CreateTilemapLayer(rootGo.transform, "Tilemap_Solid", 16);
                tilemapHazard = CreateTilemapLayer(rootGo.transform, "Tilemap_Hazard", 26);
                tilemapDeco = CreateTilemapLayer(rootGo.transform, "Tilemap_Deco_Front", 22);

                ConfigureSolidColliders(tilemapSolid.gameObject);
                ConfigureHazardColliders(tilemapHazard.gameObject);

                GameObject hazardsGo = new GameObject("HazardsRoot");
                hazardsRoot = hazardsGo.transform;
                hazardsRoot.SetParent(parent, false);
            }
            else
            {
                tilemapRoot.SetParent(parent, false);
                if (hazardsRoot != null)
                {
                    hazardsRoot.SetParent(parent, false);
                }
            }
        }

        private static Tilemap CreateTilemapLayer(Transform parent, string name, int sortingOrder)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Tilemap tilemap = go.AddComponent<Tilemap>();
            tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0f);
            TilemapRenderer renderer = go.AddComponent<TilemapRenderer>();
            // Individual mode reduces visible seam artifacts in pixel art tilemaps.
            renderer.mode = TilemapRenderer.Mode.Individual;
            renderer.sortingOrder = sortingOrder;
            return tilemap;
        }

        private void ConfigureSolidColliders(GameObject solidObject)
        {
            solidCollider = solidObject.GetComponent<TilemapCollider2D>();
            if (solidCollider == null)
            {
                solidCollider = solidObject.AddComponent<TilemapCollider2D>();
            }

            solidCollider.usedByComposite = true;
            solidCollider.isTrigger = false;

            solidComposite = solidObject.GetComponent<CompositeCollider2D>();
            if (solidComposite == null)
            {
                solidComposite = solidObject.AddComponent<CompositeCollider2D>();
            }

            solidComposite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            solidBody = solidObject.GetComponent<Rigidbody2D>();
            if (solidBody == null)
            {
                solidBody = solidObject.AddComponent<Rigidbody2D>();
            }

            solidBody.bodyType = RigidbodyType2D.Static;
            solidBody.simulated = true;
        }

        private void ConfigureHazardColliders(GameObject hazardObject)
        {
            hazardCollider = hazardObject.GetComponent<TilemapCollider2D>();
            if (hazardCollider == null)
            {
                hazardCollider = hazardObject.AddComponent<TilemapCollider2D>();
            }

            hazardCollider.usedByComposite = true;
            hazardCollider.isTrigger = true;

            hazardComposite = hazardObject.GetComponent<CompositeCollider2D>();
            if (hazardComposite == null)
            {
                hazardComposite = hazardObject.AddComponent<CompositeCollider2D>();
            }

            hazardComposite.geometryType = CompositeCollider2D.GeometryType.Outlines;
            hazardComposite.isTrigger = true;

            hazardBody = hazardObject.GetComponent<Rigidbody2D>();
            if (hazardBody == null)
            {
                hazardBody = hazardObject.AddComponent<Rigidbody2D>();
            }

            hazardBody.bodyType = RigidbodyType2D.Static;
            hazardBody.simulated = true;
        }

        private void PopulateBackgroundTilemaps(MazeLayout layout, float durationSec)
        {
            if (!useTilemapRendering || tilemapBgFar == null || tilemapBgMid == null)
            {
                return;
            }

            tilemapBgFar.ClearAllTiles();
            tilemapBgMid.ClearAllTiles();
            tilemapSolid.ClearAllTiles();
            tilemapHazard.ClearAllTiles();
            tilemapDeco.ClearAllTiles();

            float maxDesignX = 0f;
            if (layout != null && layout.tiles != null)
            {
                for (int i = 0; i < layout.tiles.Length; i++)
                {
                    MazeTileSpec spec = layout.tiles[i];
                    if (spec == null)
                    {
                        continue;
                    }

                    maxDesignX = Mathf.Max(maxDesignX, spec.designX + spec.width);
                }
            }

            float projected = Mathf.Max(maxDesignX + 48f, durationSec * Mathf.Max(4f, levelRunner != null ? levelRunner.WorldScrollUnitsPerSec : 7.2f));
            int minX = -32;
            int maxX = Mathf.CeilToInt(projected);

            Sprite far = IndustrialArtCatalog.ResolveBackgroundSprite(activeThemeCode, farLayer: true, seed: activeThemeSeed + 11);
            if (far == null)
            {
                far = SpaceMazeArtCatalog.ResolveLayerSprite(0, "MOTIF_CONDUIT_MAZE", activeThemeSeed + 11);
            }

            Sprite mid = IndustrialArtCatalog.ResolveBackgroundSprite(activeThemeCode, farLayer: false, seed: activeThemeSeed + 23);
            if (mid == null)
            {
                mid = SpaceMazeArtCatalog.ResolveLayerSprite(1, "MOTIF_CONDUIT_MAZE", activeThemeSeed + 23);
            }

            FillTileRect(tilemapBgFar, far, minX, maxX, -8, 9);
            FillTileRect(tilemapBgMid, mid, minX, maxX, -7, 8);
        }

        private void FillTileRect(Tilemap tilemap, Sprite sprite, int xMin, int xMax, int yMin, int yMax)
        {
            if (tilemap == null || sprite == null)
            {
                return;
            }

            TileBase tile = GetOrCreateTile(sprite);
            if (tile == null)
            {
                return;
            }

            for (int y = yMin; y < yMax; y++)
            {
                for (int x = xMin; x < xMax; x++)
                {
                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        private bool TryPlaceOnTilemap(MazeTileSpec spec)
        {
            if (!useTilemapRendering || tilemapSolid == null || spec == null)
            {
                return false;
            }

            if (!IsGridAligned(spec))
            {
                return false;
            }

            Tilemap target = ResolveTargetTilemap(spec);
            if (target == null)
            {
                return false;
            }

            int xCell = Mathf.RoundToInt(spec.designX);
            int yCell = Mathf.RoundToInt(spec.centerY);
            Sprite sprite = IndustrialArtCatalog.ResolveTileSprite(
                themeCode: activeThemeCode,
                visualKind: spec.visualKind,
                isDanger: spec.isDanger,
                isSolid: spec.isSolid,
                isDecor: spec.isDecor,
                variant: spec.variant,
                xCell: xCell,
                yCell: yCell,
                seed: activeThemeSeed);
            if (sprite == null)
            {
                sprite = ResolveShapeSprite(spec);
            }

            if (sprite == null)
            {
                return false;
            }

            TileBase tile = GetOrCreateTile(sprite);
            if (tile == null)
            {
                return false;
            }

            Vector3Int cell = new Vector3Int(xCell, yCell, 0);
            target.SetTile(cell, tile);
            target.SetTileFlags(cell, TileFlags.None);
            target.SetColor(cell, ResolveTileTint(spec));
            foregroundCellCount++;

            if (spec.isDanger && enableAnimatedHazardOverlays)
            {
                CreateAnimatedHazardOverlay(spec);
            }

            return true;
        }

        private static bool IsGridAligned(MazeTileSpec spec)
        {
            if (spec == null)
            {
                return false;
            }

            bool unitSize = Mathf.Abs(spec.width - 1f) <= 0.12f && Mathf.Abs(spec.height - 1f) <= 0.12f;
            bool nearCellX = Mathf.Abs(spec.designX - Mathf.Round(spec.designX)) <= 0.12f;
            bool nearCellY = Mathf.Abs(spec.centerY - Mathf.Round(spec.centerY)) <= 0.12f;
            return unitSize && nearCellX && nearCellY;
        }

        private Tilemap ResolveTargetTilemap(MazeTileSpec spec)
        {
            if (spec.isSolid)
            {
                return tilemapSolid;
            }

            if (spec.isDanger)
            {
                return tilemapHazard;
            }

            if (spec.isDecor)
            {
                return tilemapDeco;
            }

            return null;
        }

        private void CreatePropInstance(MazeTileSpec spec, int index)
        {
            Transform parent = hazardsRoot != null ? hazardsRoot : (levelRunner != null ? levelRunner.WorldRoot : transform);
            Sprite sprite = spec.isDanger ? ResolveDangerStaticSprite(spec) : null;
            if (sprite == null)
            {
                sprite = IndustrialArtCatalog.ResolveTileSprite(
                    themeCode: activeThemeCode,
                    visualKind: spec.visualKind,
                    isDanger: spec.isDanger,
                    isSolid: spec.isSolid,
                    isDecor: spec.isDecor,
                    variant: spec.variant,
                    xCell: Mathf.RoundToInt(spec.designX),
                    yCell: Mathf.RoundToInt(spec.centerY),
                    seed: activeThemeSeed + index);
            }
            if (sprite == null)
            {
                sprite = ResolveShapeSprite(spec);
            }

            GameObject go = RuntimeSpriteFactory.Create(
                $"MazeProp_{index}",
                parent,
                new Vector3(spec.designX, spec.centerY, ResolveZ(spec)),
                new Vector3(spec.width, spec.height, 1f),
                sprite: sprite,
                sortingOrder: ResolveSorting(spec),
                color: ResolveColor(spec));
            SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.drawMode = SpriteDrawMode.Simple;
            }

            Renderer renderer = go.GetComponent<Renderer>();
            props.Add(new PropInstance
            {
                Spec = spec,
                Transform = go.transform,
                Renderer = renderer,
                BaseRotationZ = ResolveInitialRotation(spec),
                BaseScale = go.transform.localScale,
                BaseColor = ResolveColor(spec)
            });
            go.transform.localRotation = Quaternion.Euler(0f, 0f, ResolveInitialRotation(spec));

            if (spec.isDanger && enableAnimatedHazardOverlays)
            {
                CreateAnimatedHazardOverlay(spec);
            }
        }

        private void CreateAnimatedHazardOverlay(MazeTileSpec spec)
        {
            string hazardType = ResolveHazardType(spec.visualKind);
            if (string.IsNullOrWhiteSpace(hazardType))
            {
                return;
            }

            Sprite[] frames = IndustrialArtCatalog.ResolveHazardFrames(hazardType, spec.variant, activeThemeSeed + spec.sectionIndex);
            if (frames == null || frames.Length == 0)
            {
                return;
            }

            Transform parent = hazardsRoot != null ? hazardsRoot : (levelRunner != null ? levelRunner.WorldRoot : transform);
            Sprite first = frames[0];
            GameObject go = RuntimeSpriteFactory.Create(
                $"HazardAnim_{animatedHazards.Count}_{hazardType}",
                parent,
                new Vector3(spec.designX, spec.centerY, -0.82f),
                new Vector3(spec.width, spec.height, 1f),
                sprite: first,
                sortingOrder: 28,
                color: Color.white);

            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                return;
            }

            float phase = Mathf.Abs((spec.variant + spec.sectionIndex) * 0.17f);
            float scaleMul = ResolveHazardScaleMultiplier(spec.visualKind, spec.variant);
            Color tint = ResolveHazardOverlayTint(spec.visualKind, spec.variant);
            renderer.color = tint;
            go.transform.localScale = go.transform.localScale * scaleMul;
            animatedHazards.Add(new AnimatedHazardInstance
            {
                Spec = spec,
                Transform = go.transform,
                Renderer = renderer,
                Frames = frames,
                Fps = ResolveHazardFps(hazardType, spec.visualKind),
                Phase = phase,
                BaseScale = go.transform.localScale,
                BaseRotationZ = ResolveInitialRotation(spec),
                SpinRateDegPerSec = ResolveHazardSpinRate(hazardType, spec.visualKind),
                MotionAmpX = ResolveHazardMotionAmpX(hazardType, spec.visualKind),
                MotionAmpY = ResolveHazardMotionAmpY(hazardType, spec.visualKind),
                MotionFreq = ResolveHazardMotionFreq(hazardType, spec.visualKind),
                BaseColor = tint
            });
        }

        private static string ResolveHazardType(string visualKind)
        {
            string kind = visualKind ?? string.Empty;
            if (kind.IndexOf("spike", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("needle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "spike";
            }

            if (kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "sheet";
            }

            if (kind.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("piston", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("ram", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "piston";
            }

            if (kind.IndexOf("saw", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("rotary", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("mine", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("electric", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("plasma", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "circular";
            }

            return string.Empty;
        }

        private static float ResolveHazardFps(string hazardType, string visualKind)
        {
            string kind = visualKind ?? string.Empty;
            if (kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 15f;
            }

            if (kind.IndexOf("piston", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 12f;
            }

            if (kind.IndexOf("saw", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("rotary", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 18f;
            }

            return hazardType switch
            {
                "sheet" => 14f,
                "piston" => 12f,
                "circular" => 16f,
                _ => 12f
            };
        }

        private static float ResolveHazardSpinRate(string hazardType, string visualKind)
        {
            string kind = visualKind ?? string.Empty;
            if (kind.IndexOf("saw", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("rotary", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 240f;
            }

            if (kind.IndexOf("mine", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("electric", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("plasma", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 170f;
            }

            if (hazardType == "circular")
            {
                return 150f;
            }

            return 0f;
        }

        private static float ResolveHazardMotionAmpX(string hazardType, string visualKind)
        {
            string kind = visualKind ?? string.Empty;
            if (kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.04f;
            }

            if (kind.IndexOf("mine", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("electric", StringComparison.OrdinalIgnoreCase) >= 0
                || hazardType == "circular")
            {
                return 0.03f;
            }

            return 0f;
        }

        private static float ResolveHazardMotionAmpY(string hazardType, string visualKind)
        {
            string kind = visualKind ?? string.Empty;
            if (kind.IndexOf("piston", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.10f;
            }

            if (kind.IndexOf("spike", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("needle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.05f;
            }

            if (kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.04f;
            }

            if (hazardType == "circular")
            {
                return 0.03f;
            }

            return 0f;
        }

        private static float ResolveHazardMotionFreq(string hazardType, string visualKind)
        {
            string kind = visualKind ?? string.Empty;
            if (kind.IndexOf("piston", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 8.2f;
            }

            if (kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 7.4f;
            }

            if (kind.IndexOf("spike", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("needle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 9.2f;
            }

            if (hazardType == "circular")
            {
                return 6.8f;
            }

            return 6.0f;
        }

        private static float ResolveHazardScaleMultiplier(string visualKind, int variant)
        {
            string kind = visualKind ?? string.Empty;
            int v = Mathf.Abs(variant);
            float variantMul = 0.94f + ((v % 4) * 0.05f);

            if (kind.IndexOf("rotary", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("saw", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return variantMul * 1.08f;
            }

            if (kind.IndexOf("needle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return variantMul * 0.92f;
            }

            if (kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return variantMul * 1.02f;
            }

            return variantMul;
        }

        private static Color ResolveHazardOverlayTint(string visualKind, int variant)
        {
            string kind = visualKind ?? string.Empty;
            int v = Mathf.Abs(variant) % 3;
            float tintStep = v * 0.06f;

            if (kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.72f - tintStep, 0.28f + (tintStep * 0.45f), 1f);
            }

            if (kind.IndexOf("piston", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.60f - (tintStep * 0.45f), 0.35f, 1f);
            }

            if (kind.IndexOf("saw", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("mine", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("electric", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("plasma", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.50f - (tintStep * 0.35f), 0.40f + (tintStep * 0.35f), 1f);
            }

            return new Color(1f, 0.56f - (tintStep * 0.40f), 0.34f + (tintStep * 0.30f), 1f);
        }

        private void TickAnimatedHazard(AnimatedHazardInstance hazard, float songSec)
        {
            if (hazard == null || hazard.Renderer == null || hazard.Frames == null || hazard.Frames.Length == 0)
            {
                return;
            }

            int frameIndex = Mathf.Abs(Mathf.FloorToInt((songSec * hazard.Fps) + hazard.Phase)) % hazard.Frames.Length;
            hazard.Renderer.sprite = hazard.Frames[frameIndex];
            string kind = hazard.Spec != null ? hazard.Spec.visualKind ?? string.Empty : string.Empty;
            float pulseFast = 0.5f + (0.5f * Mathf.Sin((songSec * Mathf.Max(2f, hazard.MotionFreq)) + hazard.Phase));
            float pulseSlow = 0.5f + (0.5f * Mathf.Sin((songSec * (Mathf.Max(2f, hazard.MotionFreq) * 0.53f)) + hazard.Phase));

            Vector3 scale = hazard.BaseScale;
            if (kind.IndexOf("piston", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Piston/Crusher: thrust feel with stronger longitudinal pulse.
                scale.y = hazard.BaseScale.y * Mathf.Lerp(0.82f, 1.22f, pulseFast);
                scale.x = hazard.BaseScale.x * Mathf.Lerp(0.92f, 1.06f, pulseSlow);
            }
            else if (kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Sheet/Laser: horizontal breathing/wave.
                scale.x = hazard.BaseScale.x * Mathf.Lerp(0.88f, 1.18f, pulseFast);
                scale.y = hazard.BaseScale.y * Mathf.Lerp(0.90f, 1.06f, pulseSlow);
            }
            else if (kind.IndexOf("spike", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("needle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Spike family: short staccato pulse.
                scale = Vector3.Lerp(hazard.BaseScale * 0.92f, hazard.BaseScale * 1.12f, pulseFast);
            }
            else
            {
                // Circular/mine/electric/plasma.
                scale = Vector3.Lerp(hazard.BaseScale * 0.90f, hazard.BaseScale * 1.14f, pulseFast);
            }

            hazard.Transform.localScale = scale;

            Vector3 p = hazard.Transform.localPosition;
            if (Mathf.Abs(hazard.MotionAmpX) > 0.0001f)
            {
                p.x = SnapToPixel(p.x + (Mathf.Sin((songSec * hazard.MotionFreq) + hazard.Phase) * hazard.MotionAmpX));
            }

            if (Mathf.Abs(hazard.MotionAmpY) > 0.0001f)
            {
                p.y = SnapToPixel(p.y + (Mathf.Sin((songSec * (hazard.MotionFreq * 1.13f)) + (hazard.Phase * 1.27f)) * hazard.MotionAmpY));
            }

            hazard.Transform.localPosition = p;

            float z = hazard.BaseRotationZ + (songSec * hazard.SpinRateDegPerSec);
            hazard.Transform.localRotation = Quaternion.Euler(0f, 0f, z);

            Color c = hazard.BaseColor;
            c.a = Mathf.Lerp(0.78f, 1f, pulseSlow);
            hazard.Renderer.color = c;
        }

        private void TickPropAnimation(PropInstance prop, float songSec)
        {
            if (prop == null || prop.Transform == null || prop.Spec == null)
            {
                return;
            }

            string kind = prop.Spec.visualKind ?? string.Empty;
            float pulse = 0.5f + (0.5f * Mathf.Sin((songSec * 6.2f) + (prop.Spec.variant * 0.85f)));

            SpriteRenderer renderer = prop.Transform.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                Color c = prop.BaseColor;
                if (kind == "beat_guide_light" || kind == "score_orb")
                {
                    c.a = Mathf.Lerp(0.30f, prop.BaseColor.a, pulse);
                }
                else if (kind == "speed_gate" || kind == "style_gate" || kind == "speed_pad_up" || kind == "speed_pad_down")
                {
                    c.a = Mathf.Lerp(0.48f, prop.BaseColor.a, pulse);
                }

                renderer.color = c;
            }

            if (kind == "saw_blade" || kind == "saw_static" || kind == "rotary_saw_large"
                || kind == "no_touch_ring" || kind == "jump_ring" || kind == "tunnel_ring"
                || kind == "plasma_orb_static" || kind == "mine_static")
            {
                bool sawLike = kind == "saw_blade" || kind == "saw_static" || kind == "rotary_saw_large";
                float z = prop.BaseRotationZ + (songSec * (sawLike ? 210f : 140f));
                prop.Transform.localRotation = Quaternion.Euler(0f, 0f, z);
            }
            else
            {
                prop.Transform.localRotation = Quaternion.Euler(0f, 0f, prop.BaseRotationZ);
            }

            if (kind == "beat_guide_light")
            {
                float s = Mathf.Lerp(0.84f, 1.16f, pulse);
                prop.Transform.localScale = new Vector3(prop.BaseScale.x * s, prop.BaseScale.y * s, prop.BaseScale.z);
            }
            else if (prop.Spec.isDanger && (kind.Contains("piston") || kind.Contains("crusher")))
            {
                float yStretch = Mathf.Lerp(0.86f, 1.18f, pulse);
                prop.Transform.localScale = new Vector3(prop.BaseScale.x, prop.BaseScale.y * yStretch, prop.BaseScale.z);
            }
            else if (prop.Spec.isDanger && (kind.Contains("laser") || kind.Contains("sheet") || kind.Contains("acid")))
            {
                float xStretch = Mathf.Lerp(0.88f, 1.14f, pulse);
                prop.Transform.localScale = new Vector3(prop.BaseScale.x * xStretch, prop.BaseScale.y, prop.BaseScale.z);
            }
            else
            {
                prop.Transform.localScale = prop.BaseScale;
            }
        }

        private void UpdateTilemapParallax(float baseX)
        {
            SetTilemapLayerX(tilemapBgFar, baseX * farParallaxRatio);
            SetTilemapLayerX(tilemapBgMid, baseX * midParallaxRatio);
            SetTilemapLayerX(tilemapSolid, baseX * foregroundParallaxRatio);
            SetTilemapLayerX(tilemapHazard, baseX * foregroundParallaxRatio);
            SetTilemapLayerX(tilemapDeco, baseX * foregroundParallaxRatio);
        }

        private static void SetTilemapLayerX(Tilemap tilemap, float x)
        {
            if (tilemap == null)
            {
                return;
            }

            Transform t = tilemap.transform;
            Vector3 p = t.localPosition;
            p.x = SnapToPixel(x);
            t.localPosition = p;
        }

        private static float SnapToPixel(float value)
        {
            float ppu = Mathf.Max(1f, PixelSnapPpu);
            return Mathf.Round(value * ppu) / ppu;
        }

        private void RefreshTilemapColliders()
        {
            // TilemapCollider2D updates automatically after tile edits.
        }

        private void ClearAllTiles()
        {
            for (int i = 0; i < props.Count; i++)
            {
                PropInstance prop = props[i];
                if (prop?.Transform != null)
                {
                    Destroy(prop.Transform.gameObject);
                }
            }

            props.Clear();

            for (int i = 0; i < animatedHazards.Count; i++)
            {
                AnimatedHazardInstance hazard = animatedHazards[i];
                if (hazard?.Transform != null)
                {
                    Destroy(hazard.Transform.gameObject);
                }
            }

            animatedHazards.Clear();
            tileCache.Clear();

            if (tilemapBgFar != null)
            {
                tilemapBgFar.ClearAllTiles();
            }

            if (tilemapBgMid != null)
            {
                tilemapBgMid.ClearAllTiles();
            }

            if (tilemapSolid != null)
            {
                tilemapSolid.ClearAllTiles();
            }

            if (tilemapHazard != null)
            {
                tilemapHazard.ClearAllTiles();
            }

            if (tilemapDeco != null)
            {
                tilemapDeco.ClearAllTiles();
            }
        }

        private TileBase GetOrCreateTile(Sprite sprite)
        {
            if (sprite == null)
            {
                return null;
            }

            if (tileCache.TryGetValue(sprite, out TileBase existing) && existing != null)
            {
                return existing;
            }

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            tileCache[sprite] = tile;
            return tile;
        }

        private float WorldToDesignX(float worldX)
        {
            float scrollPos = levelRunner != null ? levelRunner.WorldScrollPos : 0f;
            float playerX = levelRunner != null ? levelRunner.PlayerX : 0f;
            return scrollPos + (worldX - playerX);
        }

        private int FindClosestGapColumnIndex(float designX)
        {
            if (gapColumns.Count == 0)
            {
                return -1;
            }

            int low = 0;
            int high = gapColumns.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                float x = gapColumns[mid].designX;
                if (Mathf.Approximately(x, designX))
                {
                    return mid;
                }

                if (x < designX)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            int a = Mathf.Clamp(low, 0, gapColumns.Count - 1);
            int b = Mathf.Clamp(high, 0, gapColumns.Count - 1);
            return Mathf.Abs(gapColumns[a].designX - designX) < Mathf.Abs(gapColumns[b].designX - designX) ? a : b;
        }

        private static int ResolveSorting(MazeTileSpec spec)
        {
            if (spec == null)
            {
                return 10;
            }

            if (spec.isDanger)
            {
                return 28;
            }

            if (spec.isDecor)
            {
                return 22;
            }

            return 16;
        }

        private static float ResolveZ(MazeTileSpec spec)
        {
            if (spec == null)
            {
                return -1.10f;
            }

            if (spec.isDanger)
            {
                return -0.84f;
            }

            if (spec.isDecor)
            {
                return -1.00f;
            }

            return -1.10f;
        }

        private static Color ResolveColor(MazeTileSpec spec)
        {
            if (spec == null)
            {
                return Color.white;
            }

            if (spec.isDanger)
            {
                return ResolveDangerTint(spec.visualKind, spec.variant, alpha: 1f);
            }

            if (spec.isDecor)
            {
                return new Color(1f, 1f, 1f, 0.90f);
            }

            return Color.white;
        }

        private static Color ResolveTileTint(MazeTileSpec spec)
        {
            if (spec == null)
            {
                return Color.white;
            }

            if (spec.isDanger)
            {
                return ResolveDangerTint(spec.visualKind, spec.variant, alpha: 1f);
            }

            if (spec.isDecor)
            {
                return new Color(1f, 1f, 1f, 0.92f);
            }

            return Color.white;
        }

        private static Color ResolveDangerTint(string visualKind, int variant, float alpha)
        {
            string kind = visualKind ?? string.Empty;
            int v = Mathf.Abs(variant) % 3;
            float step = v * 0.06f;

            if (kind.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("acid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.68f - step, 0.24f + (step * 0.35f), alpha);
            }

            if (kind.IndexOf("piston", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("ram", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.58f - (step * 0.35f), 0.30f + (step * 0.20f), alpha);
            }

            if (kind.IndexOf("saw", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("mine", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("electric", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("plasma", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("rotary", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.46f - (step * 0.25f), 0.38f + (step * 0.40f), alpha);
            }

            return new Color(1f, 0.52f - (step * 0.30f), 0.30f + (step * 0.25f), alpha);
        }

        private static float ResolveInitialRotation(MazeTileSpec spec)
        {
            if (spec == null)
            {
                return 0f;
            }

            string kind = spec.visualKind ?? string.Empty;
            if (kind == "spike_floor" || kind == "acid_pool")
            {
                return 0f;
            }

            if (kind == "spike_ceiling")
            {
                return 180f;
            }

            if (kind == "thorn_wall" || kind == "spike_wall")
            {
                return ((spec.variant % 2) == 0) ? 90f : -90f;
            }

            if (kind == "jump_ring" || kind == "score_orb" || kind == "no_touch_ring" || kind == "tunnel_ring")
            {
                return 45f;
            }

            return 0f;
        }

        private static Sprite ResolveShapeSprite(MazeTileSpec spec)
        {
            if (spec == null)
            {
                return null;
            }

            RuntimeShapeKind shape = ResolveShapeKind(spec.visualKind, spec.isDanger, spec.isSolid, spec.isDecor);
            return RuntimeSpriteFactory.GetShapeSprite(shape, 64);
        }

        private static Sprite ResolveDangerStaticSprite(MazeTileSpec spec)
        {
            if (spec == null || !spec.isDanger)
            {
                return null;
            }

            string hazardType = ResolveHazardType(spec.visualKind);
            if (string.IsNullOrWhiteSpace(hazardType))
            {
                return null;
            }

            Sprite[] frames = IndustrialArtCatalog.ResolveHazardFrames(hazardType, spec.variant, spec.sectionIndex);
            if (frames != null && frames.Length > 0)
            {
                return frames[Mathf.Abs(spec.variant) % frames.Length];
            }

            return null;
        }

        private static RuntimeShapeKind ResolveShapeKind(string kind, bool isDanger, bool isSolid, bool isDecor)
        {
            kind ??= string.Empty;
            if (kind.StartsWith("wall_", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeShapeKind.Square;
            }

            if (isDanger)
            {
                return kind switch
                {
                    "spike_floor" => RuntimeShapeKind.Spike,
                    "spike_ceiling" => RuntimeShapeKind.Spike,
                    "spike_wall" => RuntimeShapeKind.Spike,
                    "spike_cluster_dense" => RuntimeShapeKind.Spike,
                    "spike_strip" => RuntimeShapeKind.BarHorizontal,
                    "saw_blade" => RuntimeShapeKind.Ring,
                    "saw_static" => RuntimeShapeKind.Ring,
                    "rotary_saw_large" => RuntimeShapeKind.Ring,
                    "laser_tripwire" => RuntimeShapeKind.BarHorizontal,
                    "laser_bar_static" => RuntimeShapeKind.BarHorizontal,
                    "sheet_spike_wave" => RuntimeShapeKind.BarHorizontal,
                    "electric_arc_node" => RuntimeShapeKind.Diamond,
                    "electric_arc_static" => RuntimeShapeKind.Diamond,
                    "plasma_orb_static" => RuntimeShapeKind.Diamond,
                    "acid_pool" => RuntimeShapeKind.Capsule,
                    "crusher_block" => RuntimeShapeKind.Square,
                    "crusher_pillar_static" => RuntimeShapeKind.BarVertical,
                    "piston_ram_static" => RuntimeShapeKind.BarVertical,
                    "thorn_wall" => RuntimeShapeKind.BarVertical,
                    "mine_static" => RuntimeShapeKind.Diamond,
                    "no_touch_ring" => RuntimeShapeKind.Ring,
                    "needle_gate" => RuntimeShapeKind.BarVertical,
                    "no_jump_zone" => RuntimeShapeKind.Cross,
                    _ => RuntimeShapeKind.Square
                };
            }

            if (isSolid)
            {
                return RuntimeShapeKind.Square;
            }

            if (isDecor)
            {
                return kind switch
                {
                    "jump_pad" => RuntimeShapeKind.Chevron,
                    "spring_pad" => RuntimeShapeKind.Chevron,
                    "jump_ring" => RuntimeShapeKind.Ring,
                    "tunnel_ring" => RuntimeShapeKind.Ring,
                    "score_orb" => RuntimeShapeKind.Diamond,
                    "beat_guide_light" => RuntimeShapeKind.Slash,
                    "speed_gate" => RuntimeShapeKind.BarVertical,
                    "style_gate" => RuntimeShapeKind.BarVertical,
                    "speed_pad_up" => RuntimeShapeKind.BarHorizontal,
                    "speed_pad_down" => RuntimeShapeKind.BarHorizontal,
                    "gravity_heavy_zone" => RuntimeShapeKind.Capsule,
                    "gravity_light_zone" => RuntimeShapeKind.Capsule,
                    "platform_thin" => RuntimeShapeKind.BarHorizontal,
                    "solid_block" => RuntimeShapeKind.Square,
                    "wall_trim" => RuntimeShapeKind.Capsule,
                    _ => RuntimeShapeKind.Square
                };
            }

            return RuntimeShapeKind.Square;
        }

        private static float ResolveDangerHitScaleX(string kind)
        {
            return kind switch
            {
                "laser_tripwire" => 0.75f,
                "laser_bar_static" => 0.75f,
                "sheet_spike_wave" => 0.82f,
                "no_touch_ring" => 0.70f,
                "thorn_wall" => 0.68f,
                "spike_wall" => 0.68f,
                "spike_cluster_dense" => 0.90f,
                "saw_blade" => 0.82f,
                "saw_static" => 0.82f,
                "rotary_saw_large" => 0.96f,
                "piston_ram_static" => 0.84f,
                "plasma_orb_static" => 0.88f,
                "needle_gate" => 0.60f,
                _ => 0.88f
            };
        }

        private static float ResolveDangerHitScaleY(string kind)
        {
            return kind switch
            {
                "laser_tripwire" => 0.64f,
                "laser_bar_static" => 0.64f,
                "sheet_spike_wave" => 0.72f,
                "acid_pool" => 0.80f,
                "thorn_wall" => 0.78f,
                "spike_wall" => 0.78f,
                "spike_cluster_dense" => 0.90f,
                "no_touch_ring" => 0.70f,
                "rotary_saw_large" => 0.96f,
                "piston_ram_static" => 0.94f,
                "plasma_orb_static" => 0.88f,
                "needle_gate" => 0.58f,
                _ => 0.88f
            };
        }
    }
}
