using UnityEngine;

namespace ZebraDash
{
    public sealed class ObstacleKinematics : MonoBehaviour
    {
        private float spawnTimeSec;
        private float hitTimeSec;
        private float endTimeSec;
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
            travelTimeSec = Mathf.Max(0.1f, travelTime);
            intensity = Mathf.Clamp01(eventIntensity <= 0f ? 0.6f : eventIntensity);
            wobbleSeed = seed * 0.173f;
            presentationYOffset = ResolvePresentationYOffset(presentation, intensity, seed);
            initialized = true;

            if (IsHold())
            {
                float span = Mathf.Max(0.2f, endTimeSec - hitTimeSec);
                holdWidth = Mathf.Clamp((span / travelTimeSec) * Mathf.Abs(spawnX - hitX), 1.2f, 9f);
                baseScaleY = 1.05f;
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
            }
            else if (IsFakeout())
            {
                holdWidth = 0.95f;
                baseScaleY = 0.95f;
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
            }
            else
            {
                float accentScale = Mathf.Lerp(1f, 1.25f, intensity);
                holdWidth = accentScale;
                baseScaleY = accentScale;
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
            }

            UpdateVisual(hitTimeSec - 0.01f);
        }

        public bool Tick(float nowSec)
        {
            if (!initialized)
            {
                return false;
            }

            UpdateVisual(nowSec);
            float cullTime = endTimeSec + 2f;
            return nowSec <= cullTime;
        }

        public void ResetVisual()
        {
            initialized = false;
            gameObject.SetActive(false);
        }

        private void UpdateVisual(float nowSec)
        {
            float t = Mathf.Clamp01((nowSec - spawnTimeSec) / travelTimeSec);
            float x = Mathf.Lerp(spawnX, hitX, t);
            float y = ResolvePresentationY(nowSec, t);

            if (IsHold())
            {
                y += 0.14f * Mathf.Sin((nowSec * 2.3f) + wobbleSeed);
                x -= holdWidth * 0.45f;
                transform.localScale = new Vector3(holdWidth, baseScaleY, 1f);
            }
            else if (IsFakeout())
            {
                y += 0.06f * Mathf.Sin((nowSec * 4.4f) + wobbleSeed);
                transform.localScale = new Vector3(holdWidth, Mathf.Lerp(0.78f, 1.05f, t), 1f);
            }
            else
            {
                y += 0.08f * Mathf.Sin((nowSec * 8f) + wobbleSeed);
                float pulse = 1f + (0.22f * (1f - Mathf.Clamp01(Mathf.Abs(nowSec - hitTimeSec) / 0.24f)) * intensity);
                transform.localScale = new Vector3(holdWidth * pulse, baseScaleY * pulse, 1f);
            }

            transform.position = new Vector3(x, y, 0f);
        }

        private float ResolvePresentationY(float nowSec, float t)
        {
            float y = laneY;
            if (string.Equals(presentation, GameplayPresentationKinds.Diagonal, System.StringComparison.OrdinalIgnoreCase))
            {
                y += Mathf.Lerp(presentationYOffset, 0f, t);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Drop, System.StringComparison.OrdinalIgnoreCase))
            {
                float eased = 1f - Mathf.Pow(1f - t, 2f);
                y += Mathf.Lerp(presentationYOffset, 0f, eased);
            }
            else if (string.Equals(presentation, GameplayPresentationKinds.Pop, System.StringComparison.OrdinalIgnoreCase))
            {
                y -= Mathf.Lerp(presentationYOffset * 0.55f, 0f, t);
            }

            // Always snap the lane at hit time to keep gameplay deterministic.
            if (Mathf.Abs(nowSec - hitTimeSec) <= 0.0005f || t >= 0.999f)
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

        private bool IsHold()
        {
            return string.Equals(kind, GameplayPatternKinds.HoldSlide, System.StringComparison.OrdinalIgnoreCase);
        }

        private bool IsFakeout()
        {
            return string.Equals(kind, GameplayPatternKinds.Fakeout, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
