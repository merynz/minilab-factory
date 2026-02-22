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
        private bool holdActive;
        private Vector3 defaultScale = Vector3.one;

        public event Action TapPerformed;

        public int LaneIndex => laneIndex;
        public float LaneY => laneIndex == 0 ? lowerLaneY : upperLaneY;
        public bool IsHolding => holdActive;

        private void Awake()
        {
            defaultScale = transform.localScale;
        }

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
            bool pointerHeld = false;
            if (inputEnabled)
            {
                bool tapped = Input.GetMouseButtonDown(0);
                pointerHeld = Input.GetMouseButton(0);
                if (!tapped && Input.touchCount > 0)
                {
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        Touch touch = Input.GetTouch(i);
                        if (touch.phase == TouchPhase.Began)
                        {
                            tapped = true;
                        }

                        if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled)
                        {
                            pointerHeld = true;
                        }
                    }
                }

                if (tapped)
                {
                    ToggleLane();
                    TapPerformed?.Invoke();
                }
            }
            holdActive = inputEnabled && pointerHeld;

            Vector3 target = transform.position;
            target.y = LaneY;
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * lerpSpeed);

            float targetScaleY = holdActive ? defaultScale.y * 0.58f : defaultScale.y;
            Vector3 scale = transform.localScale;
            scale.x = defaultScale.x;
            scale.z = defaultScale.z;
            scale.y = Mathf.Lerp(scale.y, targetScaleY, Time.deltaTime * 20f);
            transform.localScale = scale;
        }

        private void SnapToLane()
        {
            Vector3 position = transform.position;
            position.y = LaneY;
            transform.position = position;
        }
    }
}
