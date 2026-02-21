using UnityEngine;

namespace ZebraDash
{
    public sealed class MovingObstacle : MonoBehaviour
    {
        private string motion = "Slide";
        private float amplitude;
        private float frequency;
        private float startY;
        private float endY;
        private float duration;
        private float startedAt;

        public void ConfigureHold(string motionName, float yFrom, float yTo, float holdDuration)
        {
            motion = motionName;
            startY = yFrom;
            endY = yTo;
            duration = Mathf.Max(0.01f, holdDuration);
            startedAt = Time.time;
            amplitude = Mathf.Abs(yTo - yFrom) * 0.5f + 0.3f;
            frequency = 2f;
            Vector3 pos = transform.position;
            pos.y = yFrom;
            transform.position = pos;
        }

        private void Update()
        {
            if (duration <= 0f)
            {
                return;
            }

            float t = Mathf.Clamp01((Time.time - startedAt) / duration);
            Vector3 pos = transform.position;

            if (motion == "Drop")
            {
                pos.y = Mathf.Lerp(startY + 1.2f, endY, t);
            }
            else if (motion == "Oscillate")
            {
                pos.y = Mathf.Lerp(startY, endY, t) + Mathf.Sin(t * Mathf.PI * frequency) * amplitude * 0.35f;
            }
            else
            {
                pos.y = Mathf.Lerp(startY, endY, t);
            }

            transform.position = pos;
        }
    }
}
