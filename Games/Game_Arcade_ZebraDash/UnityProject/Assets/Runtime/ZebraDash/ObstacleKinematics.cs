using UnityEngine;

namespace ZebraDash
{
    public sealed class ObstacleKinematics : MonoBehaviour
    {
        private enum LifecycleState
        {
            Spawned = 0,
            Active = 1,
            PostHit = 2,
            Despawned = 3
        }

        private float spawnTimeSec;
        private float hitTimeSec;
        private float endTimeSec;
        private float beatSec;
        private float spawnX;
        private float hitX;
        private float laneY;
        private float travelTimeSec;
        private float telegraphLeadSec;
        private float intensity;
        private float wobbleSeed;
        private int visualSeed;
        private string kind = GameplayPatternKinds.Jump;
        private string archetype = GameplayArchetypes.LaneBlock;
        private string presentation = GameplayPresentationKinds.Straight;
        private float presentationYOffset;
        private float diagonalDxMax;
        private float tetherSwayAmp;
        private float tetherSwayFreq;
        private float holdWidth;
        private float baseScaleY = 1f;
        private float shapeScaleX = 1f;
        private float shapeScaleY = 1f;
        private float shapeBaseRotationDeg;
        private float styleMotionAmp = 1f;
        private float styleSpinRateMul = 1f;
        private float travelSpeedX;
        private float despawnGraceSec;
        private float postHitSec;
        private float despawnAtSec;
        private bool hitLineVerified;
        private Transform holdTelegraph;
        private Renderer holdTelegraphRenderer;
        private Transform tetherTelegraph;
        private Renderer tetherTelegraphRenderer;
        private Transform floorTelegraph;
        private Renderer floorTelegraphRenderer;
        private Transform glyphPrimary;
        private Renderer glyphPrimaryRenderer;
        private Transform glyphSecondary;
        private Renderer glyphSecondaryRenderer;
        private Transform glyphRing;
        private Renderer glyphRingRenderer;
        private SpriteRenderer spriteRenderer;
        private Material spriteMaterial;
        [SerializeField] private bool preferGeneratedSprites = true;
        private Color baseColor = Color.white;
        private LifecycleState lifecycleState = LifecycleState.Despawned;
        private bool initialized;

        public bool IsActiveVisual => initialized;
        public float HitTimeSec => hitTimeSec;
        public float EndTimeSec => endTimeSec;
        public int Lane { get; private set; }
        public string Kind => kind;

        public void Configure(
            string eventKind,
            string archetypeName,
            string presentationKind,
            int lane,
            float spawnTime,
            float hitTime,
            float endTime,
            float startX,
            float targetX,
            float travelTime,
            float telegraphLeadSec,
            float beatSec,
            float targetLaneY,
            float eventIntensity,
            int seed)
        {
            kind = string.IsNullOrWhiteSpace(eventKind) ? GameplayPatternKinds.Jump : eventKind;
            archetype = string.IsNullOrWhiteSpace(archetypeName) ? GameplayArchetypes.LaneBlock : archetypeName;
            ObstacleSpec spec = ObstacleSpecs.Resolve(archetype);
            presentation = string.IsNullOrWhiteSpace(presentationKind) ? spec.DefaultStyle : presentationKind;
            Lane = lane;
            spawnTimeSec = spawnTime;
            hitTimeSec = hitTime;
            endTimeSec = Mathf.Max(hitTime, endTime);
            spawnX = startX;
            hitX = targetX;
            laneY = targetLaneY;
            this.beatSec = Mathf.Max(0.0001f, beatSec);
            travelTimeSec = Mathf.Max(0.1f, travelTime);
            this.telegraphLeadSec = Mathf.Max(0.08f, telegraphLeadSec);
            travelSpeedX = Mathf.Abs(spawnX - hitX) / travelTimeSec;
            intensity = Mathf.Clamp01(eventIntensity <= 0f ? 0.6f : eventIntensity);
            wobbleSeed = seed * 0.173f;
            visualSeed = seed;
            diagonalDxMax = spec.DiagonalDxMax * 1.18f;
            tetherSwayAmp = spec.TetherSwayAmp * 1.25f;
            tetherSwayFreq = spec.TetherSwayFreq;
            presentationYOffset = ResolvePresentationYOffset(presentation, intensity, seed);
            hitLineVerified = false;
            initialized = true;
            postHitSec = ObstacleSpecs.ResolvePostHitSec(archetype, this.beatSec);
            despawnAtSec = Mathf.Max(endTimeSec, hitTimeSec) + postHitSec;
            lifecycleState = LifecycleState.Spawned;

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                spriteMaterial = spriteRenderer.material;
                if (spriteMaterial != null)
                {
                    baseColor = spriteMaterial.color;
                }
            }

            EnsurePresentationTelegraphs();
            EnsureArchetypeGlyphs();
            ConfigureArchetypeVisualProfile();
            ApplyArchetypeSprites(seed);

            if (IsHold())
            {
                float span = Mathf.Max(0.2f, endTimeSec - hitTimeSec);
                holdWidth = Mathf.Clamp((span / travelTimeSec) * Mathf.Abs(spawnX - hitX), 1.2f, 9f);
                baseScaleY = 1.05f;
                transform.localScale = new Vector3(holdWidth * shapeScaleX, baseScaleY * shapeScaleY, 1f);
                EnsureHoldTelegraph();
                if (holdTelegraph != null)
                {
                    holdTelegraph.gameObject.SetActive(true);
                }
                SetAuxTelegraphState(showTether: false, showFloor: true);
                despawnGraceSec = postHitSec;
            }
            else if (IsFakeout())
            {
                holdWidth = 1.15f;
                baseScaleY = 1.10f;
                transform.localScale = new Vector3(holdWidth * shapeScaleX, baseScaleY * shapeScaleY, 1f);
                if (holdTelegraph != null)
                {
                    holdTelegraph.gameObject.SetActive(false);
                }
                SetAuxTelegraphState(showTether: false, showFloor: false);
                despawnGraceSec = postHitSec;
            }
            else
            {
                float accentScale = Mathf.Lerp(1.02f, 1.42f, intensity);
                holdWidth = accentScale;
                baseScaleY = accentScale;
                transform.localScale = new Vector3(holdWidth * shapeScaleX, baseScaleY * shapeScaleY, 1f);
                if (holdTelegraph != null)
                {
                    holdTelegraph.gameObject.SetActive(false);
                }
                bool diagonalLike = string.Equals(presentation, GameplayPresentationKinds.Diagonal, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(presentation, GameplayPresentationKinds.Rise, System.StringComparison.OrdinalIgnoreCase);
                bool dropLike = string.Equals(presentation, GameplayPresentationKinds.Drop, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(presentation, GameplayPresentationKinds.Pop, System.StringComparison.OrdinalIgnoreCase);
                bool showTether = diagonalLike && ShouldShowTetherForArchetype(archetype);
                bool showFloor = dropLike || string.Equals(archetype, GameplayArchetypes.RisingWall, System.StringComparison.OrdinalIgnoreCase);
                SetAuxTelegraphState(showTether: showTether, showFloor: showFloor);
                despawnGraceSec = postHitSec;
            }

            ApplyArchetypeScaleOverrides(ref holdWidth, ref baseScaleY);
            UpdateVisual(hitTimeSec - 0.01f);
        }

        public bool Tick(float nowSec)
        {
            if (!initialized)
            {
                return false;
            }

            if (nowSec < hitTimeSec)
            {
                lifecycleState = LifecycleState.Spawned;
            }
            else if (nowSec <= endTimeSec + 0.0001f)
            {
                lifecycleState = LifecycleState.Active;
            }
            else if (nowSec <= despawnAtSec)
            {
                lifecycleState = LifecycleState.PostHit;
            }
            else
            {
                lifecycleState = LifecycleState.Despawned;
                return false;
            }

            UpdateVisual(nowSec);
            float cullTime = Mathf.Max(hitTimeSec, endTimeSec) + Mathf.Max(0.08f, despawnGraceSec);
            if (nowSec > cullTime)
            {
                lifecycleState = LifecycleState.Despawned;
                return false;
            }

            if (!IsHold() && transform.position.x <= (hitX - 0.35f))
            {
                lifecycleState = LifecycleState.Despawned;
                return false;
            }

            if (nowSec > hitTimeSec + 0.02f && IsOutOfViewport())
            {
                lifecycleState = LifecycleState.Despawned;
                return false;
            }

            return true;
        }

        public void ResetVisual()
        {
            initialized = false;
            lifecycleState = LifecycleState.Despawned;
            if (holdTelegraph != null)
            {
                holdTelegraph.gameObject.SetActive(false);
            }

            if (tetherTelegraph != null)
            {
                tetherTelegraph.gameObject.SetActive(false);
            }

            if (floorTelegraph != null)
            {
                floorTelegraph.gameObject.SetActive(false);
            }

            if (glyphPrimary != null)
            {
                glyphPrimary.gameObject.SetActive(false);
            }

            if (glyphSecondary != null)
            {
                glyphSecondary.gameObject.SetActive(false);
            }

            if (glyphRing != null)
            {
                glyphRing.gameObject.SetActive(false);
            }

            if (spriteMaterial != null)
            {
                RenderMaterialUtils.ApplyColor(spriteMaterial, new Color(baseColor.r, baseColor.g, baseColor.b, 1f));
            }

            transform.localScale = Vector3.one;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(false);
        }

        private void UpdateVisual(float nowSec)
        {
            float normalized = Mathf.Clamp01((nowSec - spawnTimeSec) / travelTimeSec);
            float telegraphProgress01 = ComputeTelegraphProgress01(nowSec);
            float x = ResolveLinearX(nowSec, normalized);
            float y = ResolvePresentationY(nowSec, normalized);

            if (IsHold())
            {
                y += 0.14f * Mathf.Sin((nowSec * 2.3f) + wobbleSeed);
                x -= holdWidth * 0.45f;
                transform.localScale = new Vector3(holdWidth * shapeScaleX, baseScaleY * shapeScaleY, 1f);
                transform.localRotation = Quaternion.Euler(0f, 0f, shapeBaseRotationDeg);
                UpdateHoldTelegraph(nowSec);
            }
            else if (IsFakeout())
            {
                y += 0.04f * Mathf.Sin((nowSec * 4.4f) + wobbleSeed);
                transform.localScale = new Vector3(holdWidth * shapeScaleX, Mathf.Lerp(0.78f, 1.05f, normalized) * shapeScaleY, 1f);
                transform.localRotation = Quaternion.Euler(0f, 0f, shapeBaseRotationDeg);
            }
            else
            {
                y += 0.035f * styleMotionAmp * Mathf.Sin((nowSec * 7f) + wobbleSeed);
                float pulse = 1f + (0.18f * (1f - Mathf.Clamp01(Mathf.Abs(nowSec - hitTimeSec) / 0.24f)) * intensity);
                transform.localScale = new Vector3(holdWidth * pulse * shapeScaleX, baseScaleY * pulse * shapeScaleY, 1f);
                if (string.Equals(archetype, GameplayArchetypes.SpinnerSentinel, System.StringComparison.OrdinalIgnoreCase))
                {
                    float spin = Mathf.Lerp(260f, 520f, intensity) * styleSpinRateMul;
                    transform.localRotation = Quaternion.Euler(0f, 0f, shapeBaseRotationDeg + ((nowSec - spawnTimeSec) * spin) + (wobbleSeed * 57f));
                }
                else
                {
                    float styleTilt = 0f;
                    if (string.Equals(presentation, GameplayPresentationKinds.Diagonal, System.StringComparison.OrdinalIgnoreCase))
                    {
                        styleTilt = Mathf.Lerp(14f, 0f, normalized);
                    }
                    else if (string.Equals(presentation, GameplayPresentationKinds.Drop, System.StringComparison.OrdinalIgnoreCase))
                    {
                        styleTilt = Mathf.Lerp(-10f, 0f, normalized);
                    }
                    else if (string.Equals(presentation, GameplayPresentationKinds.Rise, System.StringComparison.OrdinalIgnoreCase))
                    {
                        styleTilt = Mathf.Lerp(10f, 0f, normalized);
                    }

                    float laneSign = Lane == 0 ? -1f : 1f;
                    if (string.Equals(archetype, GameplayArchetypes.AccentCrusher, System.StringComparison.OrdinalIgnoreCase))
                    {
                        styleTilt += Mathf.Lerp(20f * laneSign, 0f, normalized);
                    }
                    else if (string.Equals(archetype, GameplayArchetypes.CrossGate, System.StringComparison.OrdinalIgnoreCase))
                    {
                        styleTilt += Mathf.Sin((1f - normalized) * Mathf.PI) * 32f * laneSign;
                    }
                    else if (string.Equals(archetype, GameplayArchetypes.AlternatorPair, System.StringComparison.OrdinalIgnoreCase))
                    {
                        styleTilt += Mathf.Sin((1f - normalized) * Mathf.PI * 2f + wobbleSeed) * 16f;
                    }
                    else if (string.Equals(archetype, GameplayArchetypes.RisingWall, System.StringComparison.OrdinalIgnoreCase))
                    {
                        styleTilt *= 0.25f;
                    }

                    transform.localRotation = Quaternion.Euler(0f, 0f, shapeBaseRotationDeg + styleTilt);
                }
            }

            UpdateAuxTelegraphs(nowSec, normalized, x, y);
            UpdateArchetypeGlyphs(nowSec, normalized, telegraphProgress01);

            ApplyLifecycleVisual(nowSec);

            transform.position = new Vector3(x, y, 0f);

            if (!hitLineVerified && nowSec >= hitTimeSec)
            {
                hitLineVerified = true;
                float hitError = Mathf.Abs(transform.position.x - hitX);
                if (hitError > 0.09f)
                {
                    Debug.LogWarning($"[ZebraDash] Hitline drift {hitError:F3} ({kind}/{presentation}) at {hitTimeSec:F3}s");
                }
            }
        }

        private float ResolveLinearX(float nowSec, float normalized)
        {
            if (nowSec <= hitTimeSec)
            {
                float x = Mathf.Lerp(spawnX, hitX, normalized);
                if (normalized >= 0.999f)
                {
                    return hitX;
                }

                float sign = (Lane & 1) == 0 ? -1f : 1f;
                if (string.Equals(presentation, GameplayPresentationKinds.Diagonal, System.StringComparison.OrdinalIgnoreCase))
                {
                    float offset = Mathf.Lerp(sign * diagonalDxMax * 1.42f, 0f, normalized);
                    return x + offset;
                }

                if (string.Equals(presentation, GameplayPresentationKinds.Rise, System.StringComparison.OrdinalIgnoreCase))
                {
                    float offset = Mathf.Lerp(sign * diagonalDxMax * 0.98f, 0f, normalized);
                    offset += Mathf.Sin((1f - normalized) * Mathf.PI) * sign * 0.30f * styleMotionAmp;
                    return x + offset;
                }

                if (string.Equals(presentation, GameplayPresentationKinds.Drop, System.StringComparison.OrdinalIgnoreCase))
                {
                    float skid = Mathf.Sin((1f - normalized) * Mathf.PI) * sign * 0.70f * styleMotionAmp;
                    return x + skid;
                }

                if (string.Equals(presentation, GameplayPresentationKinds.Pop, System.StringComparison.OrdinalIgnoreCase))
                {
                    float popSkid = Mathf.Lerp(sign * 0.42f, 0f, normalized);
                    return x + popSkid;
                }

                if (string.Equals(archetype, GameplayArchetypes.CrossGate, System.StringComparison.OrdinalIgnoreCase))
                {
                    float arc = Mathf.Sin((1f - normalized) * Mathf.PI) * sign * 1.15f;
                    return x + arc;
                }

                if (string.Equals(archetype, GameplayArchetypes.AlternatorPair, System.StringComparison.OrdinalIgnoreCase))
                {
                    float zig = Mathf.Sin((1f - normalized) * Mathf.PI * 1.9f + (wobbleSeed * 0.75f)) * sign * 0.70f;
                    return x + zig;
                }

                if (string.Equals(archetype, GameplayArchetypes.SpinnerSentinel, System.StringComparison.OrdinalIgnoreCase))
                {
                    float orbit = Mathf.Sin((1f - normalized) * Mathf.PI * 2.2f + wobbleSeed) * 0.55f;
                    return x + orbit;
                }

                if (string.Equals(archetype, GameplayArchetypes.RisingWall, System.StringComparison.OrdinalIgnoreCase))
                {
                    float preHoldX = Mathf.Lerp(hitX + (sign * 1.10f), hitX + (sign * 0.20f), normalized);
                    return Mathf.Lerp(preHoldX, hitX, normalized);
                }

                if (string.Equals(archetype, GameplayArchetypes.HoldLaneLock, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(archetype, GameplayArchetypes.HoldReleaseGate, System.StringComparison.OrdinalIgnoreCase))
                {
                    float anchorSlide = Mathf.Lerp(sign * 0.50f, 0f, normalized);
                    float anchored = Mathf.Lerp(hitX + (sign * 0.28f), hitX, normalized);
                    return Mathf.Lerp(x + anchorSlide, anchored, 0.62f);
                }

                if (string.Equals(archetype, GameplayArchetypes.OffbeatSnap, System.StringComparison.OrdinalIgnoreCase))
                {
                    float flick = Mathf.Lerp(sign * 0.72f, 0f, normalized);
                    return x + flick;
                }

                if (string.Equals(archetype, GameplayArchetypes.AccentCrusher, System.StringComparison.OrdinalIgnoreCase))
                {
                    float crush = Mathf.Lerp(sign * 1.02f, 0f, normalized);
                    return x + crush;
                }

                if (string.Equals(archetype, GameplayArchetypes.StreakBreaker, System.StringComparison.OrdinalIgnoreCase))
                {
                    float slash = Mathf.Lerp(sign * 0.52f, 0f, normalized);
                    return x + slash;
                }

                return x;
            }

            float postHitSec = nowSec - hitTimeSec;
            float distance = postHitSec * travelSpeedX * 3.15f;
            return hitX - distance;
        }

        private void ApplyLifecycleVisual(float nowSec)
        {
            if (spriteMaterial == null)
            {
                return;
            }

            float alpha;
            if (lifecycleState == LifecycleState.Spawned)
            {
                float preHit01 = Mathf.Clamp01((nowSec - spawnTimeSec) / Mathf.Max(0.0001f, hitTimeSec - spawnTimeSec));
                alpha = Mathf.Lerp(0.65f, 1f, preHit01);
            }
            else if (lifecycleState == LifecycleState.PostHit)
            {
                float fade01 = Mathf.Clamp01((nowSec - endTimeSec) / Mathf.Max(0.0001f, postHitSec));
                fade01 = fade01 * fade01;
                alpha = Mathf.Lerp(1f, 0f, fade01);
            }
            else
            {
                alpha = 1f;
            }

            RenderMaterialUtils.ApplyColor(spriteMaterial, new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(alpha)));
        }

        private float ResolvePresentationY(float nowSec, float normalized)
        {
            float y = laneY;
            if (string.Equals(presentation, GameplayPresentationKinds.Diagonal, System.StringComparison.OrdinalIgnoreCase))
            {
                y += (presentationYOffset * 1.58f) * (1f - normalized);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Drop, System.StringComparison.OrdinalIgnoreCase))
            {
                y += (presentationYOffset * 2.35f) * (1f - normalized);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Rise, System.StringComparison.OrdinalIgnoreCase))
            {
                y -= (presentationYOffset * 1.85f) * (1f - normalized);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Pop, System.StringComparison.OrdinalIgnoreCase))
            {
                y -= (presentationYOffset * 1.45f) * (1f - normalized);
            }

            if (string.Equals(archetype, GameplayArchetypes.RisingWall, System.StringComparison.OrdinalIgnoreCase))
            {
                y -= Mathf.Lerp(3.10f, 0f, normalized);
            }
            else if (string.Equals(archetype, GameplayArchetypes.SpinnerSentinel, System.StringComparison.OrdinalIgnoreCase))
            {
                y += Mathf.Sin((nowSec * 5.2f) + (wobbleSeed * 2.4f)) * Mathf.Lerp(0.48f, 0.10f, normalized);
            }
            else if (string.Equals(archetype, GameplayArchetypes.CrossGate, System.StringComparison.OrdinalIgnoreCase))
            {
                float laneSign = Lane == 0 ? -1f : 1f;
                y += laneSign * Mathf.Sin((1f - normalized) * Mathf.PI) * 0.52f;
            }
            else if (string.Equals(archetype, GameplayArchetypes.HoldLaneLock, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(archetype, GameplayArchetypes.HoldReleaseGate, System.StringComparison.OrdinalIgnoreCase))
            {
                y += Mathf.Sin((nowSec * 2.4f) + wobbleSeed) * Mathf.Lerp(0.22f, 0.06f, normalized);
            }

            // Always snap the lane at hit time to keep gameplay deterministic.
            if (Mathf.Abs(nowSec - hitTimeSec) <= 0.0005f || normalized >= 0.999f)
            {
                return laneY;
            }

            return y;
        }

        private static float ResolvePresentationYOffset(string presentationKind, float eventIntensity, int seed)
        {
            float sign = ((seed & 1) == 0) ? 1f : -1f;
            float amp = Mathf.Lerp(0.55f, 1.6f, Mathf.Clamp01(eventIntensity));
            if (string.Equals(presentationKind, GameplayPresentationKinds.Pop, System.StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Abs(amp) * 0.9f;
            }

            return amp * sign;
        }

        private void EnsureHoldTelegraph()
        {
            if (holdTelegraph != null)
            {
                return;
            }

            GameObject go = RuntimeSpriteFactory.Create(
                "HoldTelegraph",
                transform,
                new Vector3(0f, 0.85f, -0.05f),
                new Vector3(holdWidth, 0.12f, 1f),
                sprite: SpaceMazeArtCatalog.ResolveTelegraphSprite("hold", archetype, visualSeed),
                sortingOrder: 16);
            go.transform.localRotation = Quaternion.identity;

            holdTelegraphRenderer = go.GetComponent<Renderer>();
            if (holdTelegraphRenderer != null)
            {
                holdTelegraphRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(1f, 0.92f, 0.35f, 0.92f), true);
            }

            holdTelegraph = go.transform;
        }

        private void EnsurePresentationTelegraphs()
        {
            if (tetherTelegraph == null)
            {
                GameObject tether = RuntimeSpriteFactory.Create(
                    "TetherTelegraph",
                    transform,
                    new Vector3(0f, 1.2f, -0.04f),
                    new Vector3(0.08f, 2.2f, 1f),
                    sprite: SpaceMazeArtCatalog.ResolveTelegraphSprite("tether", archetype, visualSeed),
                    sortingOrder: 15);
                tetherTelegraphRenderer = tether.GetComponent<Renderer>();
                if (tetherTelegraphRenderer != null)
                {
                    tetherTelegraphRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(0.40f, 0.95f, 1f, 0.30f), true);
                }

                tetherTelegraph = tether.transform;
                tetherTelegraph.gameObject.SetActive(false);
            }

            if (floorTelegraph == null)
            {
                GameObject floor = RuntimeSpriteFactory.Create(
                    "FloorTelegraph",
                    transform,
                    new Vector3(0f, -1.0f, -0.04f),
                    new Vector3(1.45f, 0.14f, 1f),
                    sprite: SpaceMazeArtCatalog.ResolveTelegraphSprite("floor", archetype, visualSeed),
                    sortingOrder: 15);
                floorTelegraphRenderer = floor.GetComponent<Renderer>();
                if (floorTelegraphRenderer != null)
                {
                    floorTelegraphRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(1f, 0.62f, 0.28f, 0.34f), true);
                }

                floorTelegraph = floor.transform;
                floorTelegraph.gameObject.SetActive(false);
            }
        }

        private void EnsureArchetypeGlyphs()
        {
            if (glyphPrimary == null)
            {
                GameObject go = RuntimeSpriteFactory.Create(
                    "GlyphPrimary",
                    transform,
                    new Vector3(0f, 0f, -0.03f),
                    new Vector3(0.45f, 0.16f, 1f),
                    sprite: SpaceMazeArtCatalog.ResolveTelegraphSprite("glyph_primary", archetype, visualSeed),
                    sortingOrder: 23);
                glyphPrimary = go.transform;
                glyphPrimaryRenderer = go.GetComponent<Renderer>();
                if (glyphPrimaryRenderer != null)
                {
                    glyphPrimaryRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(0.96f, 0.98f, 1f, 0.96f), true);
                }
            }

            if (glyphSecondary == null)
            {
                GameObject go = RuntimeSpriteFactory.Create(
                    "GlyphSecondary",
                    transform,
                    new Vector3(0f, 0f, -0.035f),
                    new Vector3(0.16f, 0.45f, 1f),
                    sprite: SpaceMazeArtCatalog.ResolveTelegraphSprite("glyph_secondary", archetype, visualSeed),
                    sortingOrder: 23);
                glyphSecondary = go.transform;
                glyphSecondaryRenderer = go.GetComponent<Renderer>();
                if (glyphSecondaryRenderer != null)
                {
                    glyphSecondaryRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(0.26f, 0.92f, 1f, 0.92f), true);
                }
            }

            if (glyphRing == null)
            {
                GameObject go = RuntimeSpriteFactory.Create(
                    "GlyphRing",
                    transform,
                    new Vector3(0f, 0f, -0.04f),
                    new Vector3(1.12f, 1.12f, 1f),
                    sprite: SpaceMazeArtCatalog.ResolveTelegraphSprite("glyph_ring", archetype, visualSeed),
                    sortingOrder: 22);
                glyphRing = go.transform;
                glyphRingRenderer = go.GetComponent<Renderer>();
                if (glyphRingRenderer != null)
                {
                    glyphRingRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(1f, 1f, 1f, 0.18f), true);
                }
            }
        }

        private void ConfigureArchetypeVisualProfile()
        {
            shapeScaleX = 1f;
            shapeScaleY = 1f;
            shapeBaseRotationDeg = 0f;
            styleMotionAmp = 1f;
            styleSpinRateMul = 1f;
            Color laneMain = Lane <= 0 ? new Color(0.22f, 0.90f, 1f, 1f) : new Color(1f, 0.52f, 0.88f, 1f);
            Color laneAccent = Lane <= 0 ? new Color(0.86f, 0.98f, 1f, 1f) : new Color(1f, 0.86f, 0.96f, 1f);
            bool showPrimary = false;
            bool showSecondary = false;
            bool showRing = false;

            switch (archetype)
            {
                case GameplayArchetypes.LaneBlock:
                    shapeScaleX = 1.06f;
                    shapeScaleY = 0.94f;
                    showPrimary = true;
                    showRing = true;
                    showSecondary = false;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0f, -0.03f), new Vector3(0.24f, 1.00f, 1f), 0f, laneAccent);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.05f, 1.05f, 1f), 0f, new Color(laneMain.r, laneMain.g, laneMain.b, 0.16f));
                    break;

                case GameplayArchetypes.AccentCrusher:
                    shapeBaseRotationDeg = 45f;
                    shapeScaleX = 1.18f;
                    shapeScaleY = 1.18f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0f, -0.03f), new Vector3(0.22f, 1.08f, 1f), 0f, new Color(1f, 0.80f, 0.35f, 1f));
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0f, 0f, -0.035f), new Vector3(1.08f, 0.22f, 1f), 0f, laneAccent);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.22f, 1.22f, 1f), 0f, new Color(1f, 0.68f, 0.28f, 0.24f));
                    break;

                case GameplayArchetypes.AlternatorPair:
                    shapeScaleX = 1.12f;
                    shapeScaleY = 0.92f;
                    styleMotionAmp = 1.15f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0.18f, -0.03f), new Vector3(0.92f, 0.16f, 1f), 14f, laneAccent);
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0f, -0.18f, -0.035f), new Vector3(0.92f, 0.16f, 1f), -14f, laneMain);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.05f, 1.05f, 1f), 0f, new Color(laneMain.r, laneMain.g, laneMain.b, 0.18f));
                    break;

                case GameplayArchetypes.StreakBreaker:
                    shapeScaleX = 1.20f;
                    shapeScaleY = 0.88f;
                    styleMotionAmp = 1.10f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(-0.12f, 0f, -0.03f), new Vector3(0.72f, 0.16f, 1f), 18f, laneAccent);
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0.16f, 0f, -0.035f), new Vector3(0.60f, 0.16f, 1f), -18f, laneMain);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.00f, 1.00f, 1f), 0f, new Color(1f, 0.78f, 0.36f, 0.18f));
                    break;

                case GameplayArchetypes.HoldLaneLock:
                    shapeScaleX = 1.10f;
                    shapeScaleY = 1.00f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0.22f, -0.03f), new Vector3(1.00f, 0.15f, 1f), 0f, new Color(1f, 0.92f, 0.34f, 1f));
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0f, -0.20f, -0.035f), new Vector3(0.78f, 0.14f, 1f), 0f, laneAccent);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.12f, 1.02f, 1f), 0f, new Color(1f, 0.84f, 0.30f, 0.20f));
                    break;

                case GameplayArchetypes.HoldReleaseGate:
                    shapeScaleX = 1.10f;
                    shapeScaleY = 1.02f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(-0.14f, 0.22f, -0.03f), new Vector3(0.82f, 0.15f, 1f), 0f, new Color(1f, 0.92f, 0.34f, 1f));
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0.26f, 0.08f, -0.035f), new Vector3(0.16f, 0.56f, 1f), 0f, new Color(1f, 0.66f, 0.30f, 1f));
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.18f, 1.08f, 1f), 0f, new Color(1f, 0.82f, 0.30f, 0.22f));
                    break;

                case GameplayArchetypes.CrossGate:
                    shapeScaleX = 1.06f;
                    shapeScaleY = 1.06f;
                    styleMotionAmp = 1.16f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0f, -0.03f), new Vector3(1.02f, 0.18f, 1f), 40f, laneAccent);
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0f, 0f, -0.035f), new Vector3(1.02f, 0.18f, 1f), -40f, laneMain);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.18f, 1.18f, 1f), 0f, new Color(1f, 0.66f, 0.28f, 0.20f));
                    break;

                case GameplayArchetypes.OffbeatSnap:
                    shapeScaleX = 0.84f;
                    shapeScaleY = 0.84f;
                    styleMotionAmp = 1.28f;
                    showPrimary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0f, -0.03f), new Vector3(0.34f, 0.34f, 1f), 45f, laneAccent);
                    showSecondary = false;
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.35f, 1.35f, 1f), 0f, new Color(laneMain.r, laneMain.g, laneMain.b, 0.24f));
                    break;

                case GameplayArchetypes.SpinnerSentinel:
                    shapeScaleX = 1.00f;
                    shapeScaleY = 1.00f;
                    styleSpinRateMul = 1.22f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0f, -0.03f), new Vector3(1.12f, 0.18f, 1f), 0f, laneAccent);
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0f, 0f, -0.035f), new Vector3(0.18f, 1.12f, 1f), 0f, laneMain);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.26f, 1.26f, 1f), 0f, new Color(1f, 0.76f, 0.32f, 0.22f));
                    break;

                case GameplayArchetypes.RisingWall:
                    shapeScaleX = 1.18f;
                    shapeScaleY = 1.34f;
                    styleMotionAmp = 1.08f;
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, -0.14f, -0.03f), new Vector3(0.90f, 0.18f, 1f), 0f, new Color(1f, 0.74f, 0.30f, 1f));
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0f, 0.20f, -0.035f), new Vector3(0.18f, 0.56f, 1f), 0f, laneAccent);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.05f, 1.32f, 1f), 0f, new Color(1f, 0.72f, 0.30f, 0.18f));
                    break;

                case GameplayArchetypes.FakeoutGhost:
                    shapeScaleX = 1.02f;
                    shapeScaleY = 1.02f;
                    styleMotionAmp = 0.82f;
                    showPrimary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0f, -0.03f), new Vector3(0.82f, 0.14f, 1f), 0f, new Color(0.92f, 0.94f, 1f, 0.56f));
                    showSecondary = false;
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.18f, 1.18f, 1f), 0f, new Color(0.84f, 0.88f, 1f, 0.18f));
                    break;

                default:
                    showPrimary = true;
                    showSecondary = true;
                    showRing = true;
                    ConfigureGlyph(glyphPrimary, glyphPrimaryRenderer, new Vector3(0f, 0f, -0.03f), new Vector3(0.24f, 1.00f, 1f), 0f, laneAccent);
                    ConfigureGlyph(glyphSecondary, glyphSecondaryRenderer, new Vector3(0f, 0f, -0.035f), new Vector3(0.16f, 0.62f, 1f), 0f, laneMain);
                    ConfigureGlyph(glyphRing, glyphRingRenderer, new Vector3(0f, 0f, -0.04f), new Vector3(1.05f, 1.05f, 1f), 0f, new Color(laneMain.r, laneMain.g, laneMain.b, 0.16f));
                    break;
            }

            if (glyphPrimary != null)
            {
                glyphPrimary.gameObject.SetActive(showPrimary);
            }

            if (glyphSecondary != null)
            {
                glyphSecondary.gameObject.SetActive(showSecondary);
            }

            if (glyphRing != null)
            {
                glyphRing.gameObject.SetActive(showRing);
            }
        }

        private static void ConfigureGlyph(Transform glyph, Renderer glyphRenderer, Vector3 localPos, Vector3 localScale, float rotDeg, Color color)
        {
            if (glyph == null)
            {
                return;
            }

            glyph.localPosition = localPos;
            glyph.localScale = localScale;
            glyph.localRotation = Quaternion.Euler(0f, 0f, rotDeg);
            if (glyphRenderer != null && glyphRenderer.material != null)
            {
                RenderMaterialUtils.ApplyColor(glyphRenderer.material, color);
            }
        }

        private static bool ShouldShowTetherForArchetype(string archetypeName)
        {
            return string.Equals(archetypeName, GameplayArchetypes.SpinnerSentinel, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(archetypeName, GameplayArchetypes.AccentCrusher, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(archetypeName, GameplayArchetypes.CrossGate, System.StringComparison.OrdinalIgnoreCase);
        }

        private void SetAuxTelegraphState(bool showTether, bool showFloor)
        {
            if (tetherTelegraph != null)
            {
                tetherTelegraph.gameObject.SetActive(showTether);
            }

            if (floorTelegraph != null)
            {
                floorTelegraph.gameObject.SetActive(showFloor);
            }
        }

        private void UpdateHoldTelegraph(float nowSec)
        {
            if (holdTelegraph == null)
            {
                return;
            }

            float total = Mathf.Max(0.01f, endTimeSec - hitTimeSec);
            float remaining01 = Mathf.Clamp01((endTimeSec - nowSec) / total);
            float width = Mathf.Max(0.08f, holdWidth * remaining01);
            holdTelegraph.localScale = new Vector3(width, 0.12f, 1f);
            holdTelegraph.localPosition = new Vector3((-holdWidth * 0.5f) + (width * 0.5f), 0.85f, -0.05f);

            if (holdTelegraphRenderer != null && holdTelegraphRenderer.material != null)
            {
                float releasePhase = 1f - remaining01;
                float pulse = 0.65f + (0.35f * Mathf.Sin((nowSec * 18f) + wobbleSeed));
                float alpha = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(releasePhase * pulse));
                RenderMaterialUtils.ApplyColor(holdTelegraphRenderer.material, new Color(1f, 0.92f, 0.35f, alpha));
            }
        }

        private void UpdateAuxTelegraphs(float nowSec, float normalized, float x, float y)
        {
            float lead01 = ComputeTelegraphProgress01(nowSec);
            if (tetherTelegraph != null && tetherTelegraph.gameObject.activeSelf)
            {
                Vector3 localEnd = new Vector3(0f, 0f, -0.04f);
                float startX = Mathf.Lerp(-Mathf.Max(0.38f, diagonalDxMax * 1.2f), -Mathf.Max(0.16f, diagonalDxMax * 0.5f), normalized);
                float startY = Mathf.Lerp(1.8f, 0.95f, normalized);
                Vector3 localStart = new Vector3(startX, startY, -0.04f);
                Vector3 delta = localEnd - localStart;
                float len = Mathf.Max(0.02f, delta.magnitude);
                tetherTelegraph.localPosition = localStart + (delta * 0.5f);
                tetherTelegraph.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
                tetherTelegraph.localScale = new Vector3(0.05f + (0.03f * lead01), len, 1f);
                if (tetherTelegraphRenderer != null && tetherTelegraphRenderer.material != null)
                {
                    Color c = Color.Lerp(new Color(0.24f, 0.95f, 1f, 1f), new Color(1f, 0.62f, 0.30f, 1f), lead01);
                    c.a = Mathf.Clamp01(0.22f + (lead01 * 0.58f));
                    RenderMaterialUtils.ApplyColor(tetherTelegraphRenderer.material, c);
                }
            }

            if (floorTelegraph != null && floorTelegraph.gameObject.activeSelf)
            {
                float sweep = Mathf.Sin((nowSec * (10f + (tetherSwayFreq * 0.4f))) + wobbleSeed) * 0.10f;
                floorTelegraph.localPosition = new Vector3(0f, Mathf.Lerp(-1.6f, -0.1f, normalized) + sweep, -0.04f);
                floorTelegraph.localScale = new Vector3(Mathf.Lerp(1.75f, 1.05f, normalized), 0.16f, 1f);
                if (floorTelegraphRenderer != null && floorTelegraphRenderer.material != null)
                {
                    float alpha = Mathf.Clamp01(0.32f + (lead01 * 0.58f));
                    Color c = Color.Lerp(new Color(1f, 0.70f, 0.34f, alpha), new Color(1f, 0.42f, 0.28f, alpha), lead01);
                    c.a = alpha;
                    RenderMaterialUtils.ApplyColor(floorTelegraphRenderer.material, c);
                }
            }
        }

        private float ComputeTelegraphProgress01(float nowSec)
        {
            float lead = Mathf.Max(0.01f, telegraphLeadSec);
            return Mathf.Clamp01(1f - ((hitTimeSec - nowSec) / lead));
        }

        private void UpdateArchetypeGlyphs(float nowSec, float normalized, float telegraphProgress01)
        {
            float beatPulse = 0.5f + (0.5f * Mathf.Sin((nowSec * Mathf.PI * 2f / Mathf.Max(0.001f, beatSec)) + wobbleSeed));
            if (glyphRing != null && glyphRing.gameObject.activeSelf)
            {
                float ringPulse = 1f + (0.28f * telegraphProgress01) + (0.12f * beatPulse);
                glyphRing.localScale = new Vector3(ringPulse, ringPulse, 1f);
                if (glyphRingRenderer != null && glyphRingRenderer.material != null)
                {
                    Color c = glyphRingRenderer.material.color;
                    c.a = Mathf.Clamp01(Mathf.Lerp(0.10f, 0.34f, telegraphProgress01));
                    RenderMaterialUtils.ApplyColor(glyphRingRenderer.material, c);
                }
            }

            if (glyphPrimary != null && glyphPrimary.gameObject.activeSelf && string.Equals(archetype, GameplayArchetypes.RisingWall, System.StringComparison.OrdinalIgnoreCase))
            {
                glyphPrimary.localPosition = new Vector3(0f, Mathf.Lerp(-0.20f, 0.18f, normalized), glyphPrimary.localPosition.z);
            }

            if (glyphSecondary != null && glyphSecondary.gameObject.activeSelf && string.Equals(archetype, GameplayArchetypes.HoldReleaseGate, System.StringComparison.OrdinalIgnoreCase))
            {
                float releasePulse = Mathf.Clamp01((nowSec - hitTimeSec) / Mathf.Max(0.02f, endTimeSec - hitTimeSec));
                glyphSecondary.localScale = new Vector3(0.16f, Mathf.Lerp(0.22f, 0.62f, releasePulse), 1f);
            }
        }

        private void ApplyArchetypeSprites(int seed)
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                Sprite obstacleSprite = null;
                bool usingFallback = preferGeneratedSprites;
                if (!preferGeneratedSprites)
                {
                    obstacleSprite = SpaceMazeArtCatalog.ResolveObstacleSprite(archetype, Lane, intensity, seed);
                    usingFallback = obstacleSprite == null;
                }

                if (usingFallback)
                {
                    obstacleSprite = RuntimeSpriteFactory.GetShapeSprite(ResolveFallbackObstacleShape(archetype, kind), 64);
                }

                if (obstacleSprite != null)
                {
                    spriteRenderer.sprite = obstacleSprite;
                    spriteRenderer.drawMode = usingFallback ? SpriteDrawMode.Simple : SpriteDrawMode.Tiled;
                    if (!usingFallback)
                    {
                        spriteRenderer.size = Vector2.one;
                    }
                }
            }

            AssignSpriteWithFallback(
                holdTelegraphRenderer,
                SpaceMazeArtCatalog.ResolveTelegraphSprite("hold", archetype, seed + 3),
                RuntimeShapeKind.BarHorizontal,
                allowTiled: true);
            AssignSpriteWithFallback(
                tetherTelegraphRenderer,
                SpaceMazeArtCatalog.ResolveTelegraphSprite("tether", archetype, seed + 5),
                RuntimeShapeKind.BarVertical,
                allowTiled: true);
            AssignSpriteWithFallback(
                floorTelegraphRenderer,
                SpaceMazeArtCatalog.ResolveTelegraphSprite("floor", archetype, seed + 7),
                RuntimeShapeKind.BarHorizontal,
                allowTiled: true);
            AssignSpriteWithFallback(
                glyphPrimaryRenderer,
                SpaceMazeArtCatalog.ResolveTelegraphSprite("glyph_primary", archetype, seed + 11),
                ResolvePrimaryGlyphShape(archetype),
                allowTiled: false);
            AssignSpriteWithFallback(
                glyphSecondaryRenderer,
                SpaceMazeArtCatalog.ResolveTelegraphSprite("glyph_secondary", archetype, seed + 13),
                ResolveSecondaryGlyphShape(archetype),
                allowTiled: false);
            AssignSpriteWithFallback(
                glyphRingRenderer,
                SpaceMazeArtCatalog.ResolveTelegraphSprite("glyph_ring", archetype, seed + 17),
                RuntimeShapeKind.Ring,
                allowTiled: false);
        }

        private void ApplyArchetypeScaleOverrides(ref float width, ref float height)
        {
            switch (archetype)
            {
                case GameplayArchetypes.LaneBlock:
                    width *= 0.78f;
                    height *= 1.80f;
                    break;
                case GameplayArchetypes.AccentCrusher:
                    width *= 1.34f;
                    height *= 1.34f;
                    break;
                case GameplayArchetypes.AlternatorPair:
                    width *= 1.35f;
                    height *= 0.90f;
                    break;
                case GameplayArchetypes.StreakBreaker:
                    width *= 0.76f;
                    height *= 1.45f;
                    break;
                case GameplayArchetypes.HoldLaneLock:
                    width *= 1.80f;
                    height *= 1.00f;
                    break;
                case GameplayArchetypes.HoldReleaseGate:
                    width *= 1.72f;
                    height *= 1.08f;
                    break;
                case GameplayArchetypes.CrossGate:
                    width *= 1.42f;
                    height *= 1.42f;
                    break;
                case GameplayArchetypes.OffbeatSnap:
                    width *= 0.70f;
                    height *= 0.70f;
                    break;
                case GameplayArchetypes.SpinnerSentinel:
                    width *= 1.56f;
                    height *= 1.56f;
                    break;
                case GameplayArchetypes.RisingWall:
                    width *= 1.22f;
                    height *= 2.20f;
                    break;
                case GameplayArchetypes.FakeoutGhost:
                    width *= 1.08f;
                    height *= 1.08f;
                    break;
            }
        }

        private static RuntimeShapeKind ResolveFallbackObstacleShape(string archetypeName, string eventKind)
        {
            if (string.Equals(eventKind, GameplayPatternKinds.HoldSlide, System.StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeShapeKind.BarHorizontal;
            }

            return archetypeName switch
            {
                GameplayArchetypes.LaneBlock => RuntimeShapeKind.BarVertical,
                GameplayArchetypes.AccentCrusher => RuntimeShapeKind.Diamond,
                GameplayArchetypes.AlternatorPair => RuntimeShapeKind.Chevron,
                GameplayArchetypes.StreakBreaker => RuntimeShapeKind.Slash,
                GameplayArchetypes.HoldLaneLock => RuntimeShapeKind.BarHorizontal,
                GameplayArchetypes.HoldReleaseGate => RuntimeShapeKind.Capsule,
                GameplayArchetypes.CrossGate => RuntimeShapeKind.Cross,
                GameplayArchetypes.OffbeatSnap => RuntimeShapeKind.TriangleRight,
                GameplayArchetypes.SpinnerSentinel => RuntimeShapeKind.Ring,
                GameplayArchetypes.RisingWall => RuntimeShapeKind.TriangleUp,
                GameplayArchetypes.FakeoutGhost => RuntimeShapeKind.Diamond,
                _ => RuntimeShapeKind.Square
            };
        }

        private static RuntimeShapeKind ResolvePrimaryGlyphShape(string archetypeName)
        {
            return archetypeName switch
            {
                GameplayArchetypes.CrossGate => RuntimeShapeKind.Slash,
                GameplayArchetypes.AccentCrusher => RuntimeShapeKind.Cross,
                GameplayArchetypes.SpinnerSentinel => RuntimeShapeKind.Cross,
                GameplayArchetypes.HoldLaneLock => RuntimeShapeKind.BarHorizontal,
                GameplayArchetypes.HoldReleaseGate => RuntimeShapeKind.BarHorizontal,
                GameplayArchetypes.OffbeatSnap => RuntimeShapeKind.Diamond,
                GameplayArchetypes.RisingWall => RuntimeShapeKind.BarVertical,
                _ => RuntimeShapeKind.Slash
            };
        }

        private static RuntimeShapeKind ResolveSecondaryGlyphShape(string archetypeName)
        {
            return archetypeName switch
            {
                GameplayArchetypes.CrossGate => RuntimeShapeKind.Slash,
                GameplayArchetypes.AccentCrusher => RuntimeShapeKind.Cross,
                GameplayArchetypes.HoldReleaseGate => RuntimeShapeKind.BarVertical,
                GameplayArchetypes.AlternatorPair => RuntimeShapeKind.Chevron,
                GameplayArchetypes.StreakBreaker => RuntimeShapeKind.Chevron,
                GameplayArchetypes.RisingWall => RuntimeShapeKind.BarHorizontal,
                GameplayArchetypes.SpinnerSentinel => RuntimeShapeKind.BarVertical,
                _ => RuntimeShapeKind.BarVertical
            };
        }

        private static void AssignSpriteWithFallback(Renderer renderer, Sprite sprite, RuntimeShapeKind fallbackShape, bool allowTiled)
        {
            if (renderer == null)
            {
                return;
            }

            SpriteRenderer spriteRenderer = renderer as SpriteRenderer;
            if (spriteRenderer == null)
            {
                spriteRenderer = renderer.GetComponent<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                Sprite resolved = sprite ?? RuntimeSpriteFactory.GetShapeSprite(fallbackShape, 64);
                if (resolved == null)
                {
                    return;
                }

                spriteRenderer.sprite = resolved;
                spriteRenderer.drawMode = allowTiled && sprite != null ? SpriteDrawMode.Tiled : SpriteDrawMode.Simple;
                if (spriteRenderer.drawMode == SpriteDrawMode.Tiled)
                {
                    spriteRenderer.size = Vector2.one;
                }
            }
        }

        private bool IsHold()
        {
            return string.Equals(kind, GameplayPatternKinds.HoldSlide, System.StringComparison.OrdinalIgnoreCase);
        }

        private bool IsFakeout()
        {
            return string.Equals(kind, GameplayPatternKinds.Fakeout, System.StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOutOfViewport()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return false;
            }

            Vector3 viewport = cam.WorldToViewportPoint(transform.position);
            return viewport.x < -0.20f || viewport.x > 1.20f || viewport.y < -0.30f || viewport.y > 1.30f;
        }
    }
}
