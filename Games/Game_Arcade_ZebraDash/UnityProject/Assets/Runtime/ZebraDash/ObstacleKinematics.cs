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
        private float intensity;
        private float wobbleSeed;
        private string kind = GameplayPatternKinds.Jump;
        private string presentation = GameplayPresentationKinds.Straight;
        private float presentationYOffset;
        private float holdWidth;
        private float baseScaleY = 1f;
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
        private SpriteRenderer spriteRenderer;
        private Material spriteMaterial;
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
            string presentationKind,
            int lane,
            float spawnTime,
            float hitTime,
            float endTime,
            float startX,
            float targetX,
            float travelTime,
            float beatSec,
            float targetLaneY,
            float eventIntensity,
            int seed)
        {
            kind = string.IsNullOrWhiteSpace(eventKind) ? GameplayPatternKinds.Jump : eventKind;
            presentation = string.IsNullOrWhiteSpace(presentationKind) ? GameplayPresentationKinds.Straight : presentationKind;
            Lane = lane;
            spawnTimeSec = spawnTime;
            hitTimeSec = hitTime;
            endTimeSec = Mathf.Max(hitTime, endTime);
            spawnX = startX;
            hitX = targetX;
            laneY = targetLaneY;
            this.beatSec = Mathf.Max(0.0001f, beatSec);
            travelTimeSec = Mathf.Max(0.1f, travelTime);
            travelSpeedX = Mathf.Abs(spawnX - hitX) / travelTimeSec;
            intensity = Mathf.Clamp01(eventIntensity <= 0f ? 0.6f : eventIntensity);
            wobbleSeed = seed * 0.173f;
            presentationYOffset = ResolvePresentationYOffset(presentation, intensity, seed);
            hitLineVerified = false;
            initialized = true;
            postHitSec = Mathf.Clamp(0.20f * this.beatSec, 0.08f, 0.18f);
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

            if (IsHold())
            {
                float span = Mathf.Max(0.2f, endTimeSec - hitTimeSec);
                holdWidth = Mathf.Clamp((span / travelTimeSec) * Mathf.Abs(spawnX - hitX), 1.2f, 9f);
                baseScaleY = 1.05f;
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
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
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
                if (holdTelegraph != null)
                {
                    holdTelegraph.gameObject.SetActive(false);
                }
                SetAuxTelegraphState(showTether: false, showFloor: false);
                despawnGraceSec = postHitSec;
            }
            else
            {
                float accentScale = Mathf.Lerp(1.15f, 1.75f, intensity);
                holdWidth = accentScale;
                baseScaleY = accentScale;
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
                if (holdTelegraph != null)
                {
                    holdTelegraph.gameObject.SetActive(false);
                }
                bool diagonalLike = string.Equals(presentation, GameplayPresentationKinds.Diagonal, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(presentation, GameplayPresentationKinds.Rise, System.StringComparison.OrdinalIgnoreCase);
                bool dropLike = string.Equals(presentation, GameplayPresentationKinds.Drop, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(presentation, GameplayPresentationKinds.Pop, System.StringComparison.OrdinalIgnoreCase);
                SetAuxTelegraphState(showTether: diagonalLike, showFloor: dropLike);
                despawnGraceSec = postHitSec;
            }

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

            if (!IsHold() && transform.position.x <= (hitX - 2.2f))
            {
                lifecycleState = LifecycleState.Despawned;
                return false;
            }

            if (nowSec > hitTimeSec && IsOutOfViewport())
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

            if (spriteMaterial != null)
            {
                RenderMaterialUtils.ApplyColor(spriteMaterial, new Color(baseColor.r, baseColor.g, baseColor.b, 1f));
            }

            transform.localScale = Vector3.one;
            transform.localPosition = Vector3.zero;
            gameObject.SetActive(false);
        }

        private void UpdateVisual(float nowSec)
        {
            float normalized = Mathf.Clamp01((nowSec - spawnTimeSec) / travelTimeSec);
            float x = ResolveLinearX(nowSec);
            float y = ResolvePresentationY(nowSec, normalized);

            if (IsHold())
            {
                y += 0.14f * Mathf.Sin((nowSec * 2.3f) + wobbleSeed);
                x -= holdWidth * 0.45f;
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
                UpdateHoldTelegraph(nowSec);
            }
            else if (IsFakeout())
            {
                y += 0.06f * Mathf.Sin((nowSec * 4.4f) + wobbleSeed);
                transform.localScale = new Vector3(holdWidth, Mathf.Lerp(0.78f, 1.05f, normalized), 1f);
            }
            else
            {
                y += 0.08f * Mathf.Sin((nowSec * 8f) + wobbleSeed);
                float pulse = 1f + (0.22f * (1f - Mathf.Clamp01(Mathf.Abs(nowSec - hitTimeSec) / 0.24f)) * intensity);
                transform.localScale = new Vector3(holdWidth * pulse, baseScaleY * pulse, 1f);
            }

            UpdateAuxTelegraphs(nowSec, normalized, x, y);

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

        private float ResolveLinearX(float nowSec)
        {
            if (nowSec <= hitTimeSec)
            {
                float u = Mathf.Clamp01((nowSec - spawnTimeSec) / Mathf.Max(0.0001f, travelTimeSec));
                return Mathf.Lerp(spawnX, hitX, u);
            }

            float postHitSec = nowSec - hitTimeSec;
            float distance = postHitSec * travelSpeedX;
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
                alpha = Mathf.Lerp(0.45f, 1f, preHit01);
            }
            else if (lifecycleState == LifecycleState.PostHit)
            {
                float fade01 = Mathf.Clamp01((nowSec - endTimeSec) / Mathf.Max(0.0001f, postHitSec));
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
                y += presentationYOffset * (1f - normalized);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Drop, System.StringComparison.OrdinalIgnoreCase))
            {
                y += presentationYOffset * (1f - normalized);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Rise, System.StringComparison.OrdinalIgnoreCase))
            {
                y -= (presentationYOffset * 0.85f) * (1f - normalized);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Pop, System.StringComparison.OrdinalIgnoreCase))
            {
                y -= (presentationYOffset * 0.55f) * (1f - normalized);
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
                    sortingOrder: 15);
                tetherTelegraphRenderer = tether.GetComponent<Renderer>();
                if (tetherTelegraphRenderer != null)
                {
                    tetherTelegraphRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(0.40f, 0.95f, 1f, 0.22f), true);
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
                    sortingOrder: 15);
                floorTelegraphRenderer = floor.GetComponent<Renderer>();
                if (floorTelegraphRenderer != null)
                {
                    floorTelegraphRenderer.material = RenderMaterialUtils.CreateSolidMaterial(new Color(1f, 0.62f, 0.28f, 0.24f), true);
                }

                floorTelegraph = floor.transform;
                floorTelegraph.gameObject.SetActive(false);
            }
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
            float lead01 = Mathf.Clamp01(1f - normalized);
            if (tetherTelegraph != null && tetherTelegraph.gameObject.activeSelf)
            {
                Vector3 localEnd = new Vector3(0f, 0f, -0.04f);
                Vector3 localStart = new Vector3(Mathf.Lerp(-1.2f, -0.4f, normalized), Mathf.Lerp(2.2f, 1.1f, normalized), -0.04f);
                Vector3 delta = localEnd - localStart;
                float len = Mathf.Max(0.02f, delta.magnitude);
                tetherTelegraph.localPosition = localStart + (delta * 0.5f);
                tetherTelegraph.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
                tetherTelegraph.localScale = new Vector3(0.07f, len, 1f);
                if (tetherTelegraphRenderer != null && tetherTelegraphRenderer.material != null)
                {
                    float alpha = Mathf.Clamp01(0.10f + (lead01 * 0.24f));
                    RenderMaterialUtils.ApplyColor(tetherTelegraphRenderer.material, new Color(0.40f, 0.95f, 1f, alpha));
                }
            }

            if (floorTelegraph != null && floorTelegraph.gameObject.activeSelf)
            {
                float sweep = Mathf.Sin((nowSec * 12f) + wobbleSeed) * 0.10f;
                floorTelegraph.localPosition = new Vector3(0f, Mathf.Lerp(-1.6f, -0.1f, normalized) + sweep, -0.04f);
                floorTelegraph.localScale = new Vector3(Mathf.Lerp(1.75f, 1.05f, normalized), 0.16f, 1f);
                if (floorTelegraphRenderer != null && floorTelegraphRenderer.material != null)
                {
                    float alpha = Mathf.Clamp01(0.08f + (lead01 * 0.35f));
                    RenderMaterialUtils.ApplyColor(floorTelegraphRenderer.material, new Color(1f, 0.62f, 0.28f, alpha));
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
