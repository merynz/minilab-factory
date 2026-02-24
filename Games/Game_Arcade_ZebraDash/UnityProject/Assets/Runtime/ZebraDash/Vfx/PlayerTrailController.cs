using UnityEngine;

namespace ZebraDash.Vfx
{
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class PlayerTrailController : MonoBehaviour
    {
        [SerializeField] private LevelRunner runner;
        [SerializeField] private float minTime = 0.03f;
        [SerializeField] private float maxTime = 0.14f;
        [SerializeField] private float baseSpeed = 6f;
        [SerializeField] private Color startColor = new Color(0.30f, 1f, 0.96f, 0.58f);
        [SerializeField] private Color endColor = new Color(0.98f, 0.48f, 1f, 0f);

        private TrailRenderer trail;

        private void Awake()
        {
            trail = GetComponent<TrailRenderer>();
            if (trail == null)
            {
                return;
            }

            trail.alignment = LineAlignment.View;
            trail.minVertexDistance = 0.08f;
            trail.startWidth = 0.09f;
            trail.endWidth = 0.01f;
            trail.startColor = startColor;
            trail.endColor = endColor;
            trail.time = minTime;
        }

        private void LateUpdate()
        {
            if (trail == null)
            {
                return;
            }

            float speed = runner != null ? runner.WorldScrollUnitsPerSec : baseSpeed;
            float normalized = Mathf.Clamp01(speed / Mathf.Max(0.1f, baseSpeed));
            trail.time = Mathf.Lerp(minTime, maxTime, normalized);
        }

        public void SetRunner(LevelRunner value)
        {
            runner = value;
        }
    }
}
