using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ObstacleKinematics : MonoBehaviour
    {
        private BeatClock beatClock;
        private float spawnTimeSec;
        private float hitTimeSec;
        private float endTimeSec;
        private float spawnX;
        private float hitX;
        private float laneY;
        private float travelTimeSec;
        private float intensity;
        private float wobbleSeed;
        private string kind = BeatKinds.Tap;
        private bool initialized;

        public bool IsActiveVisual => initialized;
        public float HitTimeSec => hitTimeSec;
        public float EndTimeSec => endTimeSec;
        public int Lane { get; private set; }
        public string Kind => kind;

        public void Configure(
            BeatClock clock,
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
            beatClock = clock;
            kind = string.IsNullOrWhiteSpace(eventKind) ? BeatKinds.Tap : eventKind;
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

            transform.localScale = kind == BeatKinds.Accent
                ? new Vector3(1.4f, 1.4f, 1f)
                : new Vector3(1f, 1f, 1f);

            if (kind == BeatKinds.Long)
            {
                float span = Mathf.Max(0.15f, endTimeSec - hitTimeSec);
                float width = Mathf.Clamp((span / travelTimeSec) * Mathf.Abs(spawnX - hitX), 1.2f, 8f);
                transform.localScale = new Vector3(width, 1.1f, 1f);
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

            if (kind == BeatKinds.Tap)
            {
                y += 0.08f * Mathf.Sin((nowSec * 8f) + wobbleSeed);
            }
            else if (kind == BeatKinds.Accent)
            {
                float scalePulse = 1f + (0.25f * (1f - Mathf.Clamp01(Mathf.Abs(nowSec - hitTimeSec) / 0.25f)));
                transform.localScale = new Vector3(1.35f * scalePulse, 1.35f * scalePulse, 1f);
            }
            else if (kind == BeatKinds.Long)
            {
                y += 0.16f * Mathf.Sin((nowSec * 2.4f) + wobbleSeed);
                x -= transform.localScale.x * 0.45f;
            }

            transform.position = new Vector3(x, y, 0f);
        }
    }
}
