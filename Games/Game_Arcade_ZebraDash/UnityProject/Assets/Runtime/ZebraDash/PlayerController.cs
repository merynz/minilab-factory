using System;
using UnityEngine;

namespace ZebraDash
{
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private float lowerLaneY = -1.2f;
        [SerializeField] private float upperLaneY = 1.2f;
        [SerializeField] private float lerpSpeed = 18f;

        private bool inputEnabled;
        private int laneIndex;

        public event Action TapPerformed;

        public int LaneIndex => laneIndex;
        public float LaneY => laneIndex == 0 ? lowerLaneY : upperLaneY;

        public void InitializeLanes(float lowerY, float upperY)
        {
            lowerLaneY = lowerY;
            upperLaneY = upperY;
            laneIndex = 0;
            SnapToLane();
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        public void SetLane(int index)
        {
            laneIndex = Mathf.Clamp(index, 0, 1);
            SnapToLane();
        }

        public void ToggleLane()
        {
            laneIndex = 1 - laneIndex;
        }

        private void Update()
        {
            if (inputEnabled)
            {
                bool tapped = Input.GetMouseButtonDown(0);
                if (!tapped && Input.touchCount > 0)
                {
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        if (Input.GetTouch(i).phase == TouchPhase.Began)
                        {
                            tapped = true;
                            break;
                        }
                    }
                }

                if (tapped)
                {
                    ToggleLane();
                    TapPerformed?.Invoke();
                }
            }

            Vector3 target = transform.position;
            target.y = LaneY;
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * lerpSpeed);
        }

        private void SnapToLane()
        {
            Vector3 position = transform.position;
            position.y = LaneY;
            transform.position = position;
        }
    }
}
