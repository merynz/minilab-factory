using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ObstacleKinematics : MonoBehaviour
    {
        private BeatClock beatClock;
        private float spawnTimeSec;
        private float hitTimeSec;
        private float spawnX;
        private float hitX;
        private float laneY;
        private float speed;
        private float durationSec;
        private float wobbleAmplitude;
        private float wobbleFrequency;
        private float wobblePhase;
        private bool configured;
        private string eventKind = "Tap";

        public float HitTimeSec => hitTimeSec;
        public float HoldEndTimeSec => hitTimeSec + durationSec;
        public bool IsHold => durationSec > 0.01f;

        public void Configure(
            BeatClock clock,
            float spawnTime,
            float hitTime,
            float startX,
            float targetHitX,
            float targetLaneY,
            float scrollSpeed,
            string kind,
            float holdDurationSec,
            float wobbleAmp,
            float wobbleFreq,
            int wobbleSeed)
        {
            beatClock = clock;
            spawnTimeSec = spawnTime;
            hitTimeSec = hitTime;
            spawnX = startX;
            hitX = targetHitX;
            laneY = targetLaneY;
            speed = Mathf.Max(0.01f, scrollSpeed);
            durationSec = Mathf.Max(0f, holdDurationSec);
            wobbleAmplitude = Mathf.Max(0f, wobbleAmp);
            wobbleFrequency = Mathf.Max(0.5f, wobbleFreq);
            eventKind = string.IsNullOrWhiteSpace(kind) ? "Tap" : kind;
            wobblePhase = (wobbleSeed % 4) * Mathf.PI - (hitTimeSec * wobbleFrequency);
            configured = true;

            if (IsHold)
            {
                Vector3 scale = transform.localScale;
                scale.x = Mathf.Max(scale.x, Mathf.Clamp(durationSec * speed * 0.65f, 1.1f, 6.5f));
                transform.localScale = scale;
            }
        }

        private void Update()
        {
            if (!configured || beatClock == null || !beatClock.IsRunning)
            {
                return;
            }

            float songTime = beatClock.SongTimeSec;
            float travelTime = Mathf.Max(0.01f, (spawnX - hitX) / speed);
            float progress = (songTime - spawnTimeSec) / travelTime;

            float x;
            if (IsHold && songTime > hitTimeSec)
            {
                float holdT = Mathf.Clamp01((songTime - hitTimeSec) / Mathf.Max(0.01f, durationSec));
                x = Mathf.Lerp(hitX, hitX - (speed * durationSec * 0.5f), holdT);
            }
            else
            {
                x = spawnX + ((hitX - spawnX) * progress);
            }

            float wobble = wobbleAmplitude * Mathf.Sin((songTime * wobbleFrequency) + wobblePhase);
            float y = laneY + wobble;
            transform.position = new Vector3(x, y, transform.position.z);

            float cullAfter = hitTimeSec + Mathf.Max(durationSec, 1.2f) + 2.5f;
            if (songTime > cullAfter)
            {
                Destroy(gameObject);
            }
        }
    }
}
