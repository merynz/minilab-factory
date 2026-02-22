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
        private bool initialized;

        public bool IsActiveVisual => initialized;
        public float HitTimeSec => hitTimeSec;
        public float EndTimeSec => endTimeSec;
        public int Lane { get; private set; }
        public string Kind => kind;

        public void Configure(
            string eventKind,
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
            initialized = true;

            if (IsHold())
            {
                float span = Mathf.Max(0.2f, endTimeSec - hitTimeSec);
                float width = Mathf.Clamp((span / travelTimeSec) * Mathf.Abs(spawnX - hitX), 1.2f, 9f);
                transform.localScale = new Vector3(width, 1.05f, 1f);
            }
            else if (IsFakeout())
            {
                transform.localScale = new Vector3(0.95f, 0.95f, 1f);
            }
            else
            {
                float accentScale = Mathf.Lerp(1f, 1.25f, intensity);
                transform.localScale = new Vector3(accentScale, accentScale, 1f);
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
            float y = laneY;

            if (IsHold())
            {
                y += 0.14f * Mathf.Sin((nowSec * 2.3f) + wobbleSeed);
                x -= transform.localScale.x * 0.45f;
            }
            else if (IsFakeout())
            {
                y += 0.06f * Mathf.Sin((nowSec * 4.4f) + wobbleSeed);
                transform.localScale = new Vector3(0.95f, Mathf.Lerp(0.78f, 1.05f, t), 1f);
            }
            else
            {
                y += 0.08f * Mathf.Sin((nowSec * 8f) + wobbleSeed);
                float pulse = 1f + (0.22f * (1f - Mathf.Clamp01(Mathf.Abs(nowSec - hitTimeSec) / 0.24f)) * intensity);
                transform.localScale = new Vector3(pulse, pulse, 1f);
            }

            transform.position = new Vector3(x, y, 0f);
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
