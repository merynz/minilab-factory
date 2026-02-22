using System;
using UnityEngine;

namespace ZebraDash
{
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private float lowerLaneY = -1.2f;
        [SerializeField] private float upperLaneY = 1.2f;
        [SerializeField] private float bobAmplitude = 0.08f;

        private bool inputEnabled;
        private int laneIndex;
        private bool holdActive;
        private Vector3 defaultScale = Vector3.one;
        private float songTimeSec;
        private float beatSec = 0.5f;
        private bool laneSwitchQueued;
        private bool laneSwitchStarted;
        private int switchFromLane;
        private int switchToLane;
        private float switchStartSec;
        private float switchDurationSec = 0.12f;

        public event Action TapPerformed;

        public int LaneIndex => laneIndex;
        public float LaneY => laneIndex == 0 ? lowerLaneY : upperLaneY;
        public bool IsHolding => holdActive;
        public int PlannedLaneIndex => laneSwitchQueued ? switchToLane : laneIndex;

        private void Awake()
        {
            defaultScale = transform.localScale;
        }

        public void InitializeLanes(float lowerY, float upperY)
        {
            lowerLaneY = lowerY;
            upperLaneY = upperY;
            laneIndex = 0;
            laneSwitchQueued = false;
            laneSwitchStarted = false;
            SnapToLane();
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        public void SetLane(int index)
        {
            laneIndex = Mathf.Clamp(index, 0, 1);
            laneSwitchQueued = false;
            laneSwitchStarted = false;
            SnapToLane();
        }

        public void SetTimingContext(float currentSongTimeSec, float currentBeatSec)
        {
            songTimeSec = currentSongTimeSec;
            beatSec = Mathf.Max(0.0001f, currentBeatSec);
        }

        public void QueueLaneSwitch(int targetLane, float startSongTimeSec, float durationSec)
        {
            switchFromLane = laneSwitchQueued ? PlannedLaneIndex : laneIndex;
            switchToLane = Mathf.Clamp(targetLane, 0, 1);
            switchStartSec = startSongTimeSec;
            switchDurationSec = Mathf.Clamp(durationSec, 0.02f, 0.40f);
            laneSwitchQueued = true;
            laneSwitchStarted = false;
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
                    TapPerformed?.Invoke();
                }
            }
            holdActive = inputEnabled && pointerHeld;

            float baseY = ResolveSwitchY();
            float beatPhase = Mathf.Repeat(songTimeSec, beatSec) / beatSec;
            float bob = bobAmplitude * Mathf.Sin(beatPhase * Mathf.PI * 2f);
            Vector3 target = transform.position;
            target.y = baseY + bob;
            transform.position = target;

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

        private float ResolveSwitchY()
        {
            if (!laneSwitchQueued)
            {
                return LaneY;
            }

            float fromY = ResolveLaneY(switchFromLane);
            float toY = ResolveLaneY(switchToLane);
            if (songTimeSec < switchStartSec)
            {
                return fromY;
            }

            if (!laneSwitchStarted)
            {
                laneSwitchStarted = true;
                laneIndex = switchToLane;
            }

            float u = Mathf.Clamp01((songTimeSec - switchStartSec) / switchDurationSec);
            float eased = u * u * (3f - (2f * u));
            if (u >= 1f)
            {
                laneSwitchQueued = false;
                laneSwitchStarted = false;
                return toY;
            }

            return Mathf.Lerp(fromY, toY, eased);
        }

        private float ResolveLaneY(int lane)
        {
            return lane <= 0 ? lowerLaneY : upperLaneY;
        }
    }
}
