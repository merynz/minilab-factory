using System;
using UnityEngine;

namespace ZebraDash
{
    public sealed class PlayerController : MonoBehaviour
    {
        public enum MovementMode
        {
            LaneSwitch = 0,
            Glide = 1,
            GroundRunner = 2,
            JumpOnly = 3
        }

        [SerializeField] private float lowerLaneY = -1.2f;
        [SerializeField] private float upperLaneY = 1.2f;
        [SerializeField] private float bobAmplitude = 0.035f;
        [SerializeField] private MovementMode movementMode = MovementMode.JumpOnly;
        [SerializeField] private float corridorMinY = -2.05f;
        [SerializeField] private float corridorMaxY = 2.05f;
        [SerializeField] private float glideGravityPerSec = 8.2f;
        [SerializeField] private float glideThrustPerSec = 16.2f;
        [SerializeField] private float glideTapImpulse = 5.8f;
        [SerializeField] private float glideMaxRiseSpeed = 8.8f;
        [SerializeField] private float glideMaxFallSpeed = 6.0f;
        [SerializeField] private float glideTapBoostPerSec = 18.0f;
        [SerializeField] private float glideTapBoostDurationSec = 0.07f;
        [SerializeField] private float groundGravityPerSec = 24.0f;
        [SerializeField] private float groundJumpImpulse = 8.6f;
        [SerializeField] private float groundMaxRiseSpeed = 10.2f;
        [SerializeField] private float groundMaxFallSpeed = 16.5f;
        [SerializeField] private float groundCoyoteSec = 0.08f;
        [SerializeField] private float groundJumpBufferSec = 0.10f;

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
        private float glideY;
        private float glideVelocity;
        private float tapBoostRemainingSec;
        private float groundFloorY = -2.2f;
        private float groundCeilingY = 2.2f;
        private float groundY;
        private float groundVelocity;
        private float groundCoyoteRemainingSec;
        private float groundJumpBufferRemainingSec;
        private bool grounded;

        public event Action TapPerformed;

        public int LaneIndex => laneIndex;
        public float LaneY => laneIndex == 0 ? lowerLaneY : upperLaneY;
        public float CurrentY => transform.position.y;
        public bool IsHolding => holdActive;
        public int PlannedLaneIndex => laneSwitchQueued ? switchToLane : laneIndex;
        public MovementMode Mode => movementMode;
        public bool IsGrounded => grounded;

        private void Awake()
        {
            defaultScale = transform.localScale;
            glideY = transform.position.y;
        }

        public void InitializeLanes(float lowerY, float upperY)
        {
            lowerLaneY = lowerY;
            upperLaneY = upperY;
            laneIndex = 0;
            laneSwitchQueued = false;
            laneSwitchStarted = false;
            glideVelocity = 0f;
            tapBoostRemainingSec = 0f;
            glideY = ResolveLaneY(laneIndex);
            groundY = glideY;
            groundVelocity = 0f;
            grounded = false;
            SnapToLane();
        }

        public void SetMovementMode(MovementMode mode)
        {
            movementMode = mode;
            laneSwitchQueued = false;
            laneSwitchStarted = false;
            glideVelocity = 0f;
            tapBoostRemainingSec = 0f;
            glideY = transform.position.y;
            groundY = glideY;
            groundVelocity = 0f;
            groundCoyoteRemainingSec = 0f;
            groundJumpBufferRemainingSec = 0f;
            if (movementMode == MovementMode.GroundRunner || movementMode == MovementMode.JumpOnly)
            {
                grounded = Mathf.Abs(groundY - groundFloorY) <= 0.03f;
            }
        }

        public void ApplyMovementProfile(LevelDesign.MovementProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            groundGravityPerSec = Mathf.Max(4f, profile.GravityUnitsPerSec2);
            groundJumpImpulse = Mathf.Max(1.5f, profile.JumpVelocityUnitsPerSec);
            groundMaxRiseSpeed = Mathf.Max(groundJumpImpulse * 1.25f, groundJumpImpulse + 1.5f);
            groundMaxFallSpeed = Mathf.Max(groundJumpImpulse * 1.85f, groundJumpImpulse + 3.2f);
            groundJumpBufferSec = Mathf.Clamp(profile.jumpBufferSec, 0.04f, 0.18f);
            groundCoyoteSec = Mathf.Clamp(profile.coyoteSec, 0.04f, 0.16f);
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        public void ConfigureGlideCorridor(float minY, float maxY)
        {
            corridorMinY = Mathf.Min(minY, maxY);
            corridorMaxY = Mathf.Max(minY, maxY);
            glideY = Mathf.Clamp(glideY, corridorMinY, corridorMaxY);
        }

        public void ConfigureGroundBounds(float floorY, float ceilingY)
        {
            groundFloorY = Mathf.Min(floorY, ceilingY);
            groundCeilingY = Mathf.Max(floorY, ceilingY);
            if (groundCeilingY - groundFloorY < 0.60f)
            {
                float center = (groundCeilingY + groundFloorY) * 0.5f;
                groundFloorY = center - 0.30f;
                groundCeilingY = center + 0.30f;
            }

            if (movementMode == MovementMode.GroundRunner)
            {
                groundY = Mathf.Clamp(groundY <= 0f ? transform.position.y : groundY, groundFloorY, groundCeilingY);
                Vector3 pos = transform.position;
                pos.y = groundY;
                transform.position = pos;
            }
        }

        public void SetGlideY(float y)
        {
            glideY = Mathf.Clamp(y, corridorMinY, corridorMaxY);
            glideVelocity = 0f;
            Vector3 position = transform.position;
            position.y = glideY;
            transform.position = position;
            float mid = (lowerLaneY + upperLaneY) * 0.5f;
            laneIndex = glideY >= mid ? 1 : 0;
        }

        public void SetGroundY(float y)
        {
            groundY = Mathf.Clamp(y, groundFloorY, groundCeilingY);
            groundVelocity = 0f;
            grounded = Mathf.Abs(groundY - groundFloorY) <= 0.05f;
            groundCoyoteRemainingSec = groundCoyoteSec;
            groundJumpBufferRemainingSec = 0f;
            Vector3 position = transform.position;
            position.y = groundY;
            transform.position = position;
            float mid = (groundFloorY + groundCeilingY) * 0.5f;
            laneIndex = groundY >= mid ? 1 : 0;
        }

        public void SetLane(int index)
        {
            laneIndex = Mathf.Clamp(index, 0, 1);
            laneSwitchQueued = false;
            laneSwitchStarted = false;
            glideY = ResolveLaneY(laneIndex);
            glideVelocity = 0f;
            tapBoostRemainingSec = 0f;
            groundY = glideY;
            groundVelocity = 0f;
            groundCoyoteRemainingSec = 0f;
            groundJumpBufferRemainingSec = 0f;
            grounded = false;
            SnapToLane();
        }

        public void SetTimingContext(float currentSongTimeSec, float currentBeatSec)
        {
            songTimeSec = currentSongTimeSec;
            beatSec = Mathf.Max(0.0001f, currentBeatSec);
        }

        public void QueueLaneSwitch(int targetLane, float startSongTimeSec, float durationSec)
        {
            if (movementMode == MovementMode.Glide)
            {
                return;
            }

            switchFromLane = laneSwitchQueued ? PlannedLaneIndex : laneIndex;
            switchToLane = Mathf.Clamp(targetLane, 0, 1);
            switchStartSec = startSongTimeSec;
            switchDurationSec = Mathf.Clamp(durationSec, 0.02f, 0.40f);
            laneSwitchQueued = true;
            laneSwitchStarted = false;

            if (switchStartSec <= songTimeSec + 0.012f)
            {
                switchStartSec = songTimeSec;
                laneSwitchStarted = true;
                laneIndex = switchToLane;
            }
        }

        private void Update()
        {
            bool pointerHeld = false;
            bool tapped = false;
            if (inputEnabled)
            {
                tapped = Input.GetMouseButtonDown(0);
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
                    if (movementMode == MovementMode.Glide)
                    {
                        glideVelocity += glideTapImpulse;
                        tapBoostRemainingSec = Mathf.Max(tapBoostRemainingSec, glideTapBoostDurationSec);
                    }
                    else if (movementMode == MovementMode.GroundRunner || movementMode == MovementMode.JumpOnly)
                    {
                        groundJumpBufferRemainingSec = Mathf.Max(groundJumpBufferRemainingSec, groundJumpBufferSec);
                        TryConsumeGroundJump();
                    }
                }
            }

            holdActive = inputEnabled && pointerHeld && movementMode == MovementMode.Glide;

            float baseY = movementMode switch
            {
                MovementMode.Glide => ResolveGlideY(Time.deltaTime),
                MovementMode.LaneSwitch => ResolveSwitchY(),
                _ => ResolveGroundY(Time.deltaTime)
            };
            float beatPhase = Mathf.Repeat(songTimeSec, beatSec) / beatSec;
            float bobScale = movementMode == MovementMode.Glide
                ? 0.55f
                : (movementMode == MovementMode.LaneSwitch ? 1f : 0.16f);
            float bob = bobAmplitude * bobScale * Mathf.Sin(beatPhase * Mathf.PI * 2f);
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

        private float ResolveGlideY(float deltaTime)
        {
            float dt = Mathf.Max(0f, deltaTime);
            float accel = holdActive ? glideThrustPerSec : -glideGravityPerSec;
            if (tapBoostRemainingSec > 0f)
            {
                accel += glideTapBoostPerSec;
                tapBoostRemainingSec = Mathf.Max(0f, tapBoostRemainingSec - dt);
            }
            glideVelocity += accel * dt;
            glideVelocity = Mathf.Clamp(glideVelocity, -glideMaxFallSpeed, glideMaxRiseSpeed);
            glideY += glideVelocity * dt;

            if (glideY < corridorMinY)
            {
                glideY = corridorMinY;
                glideVelocity = 0f;
            }
            else if (glideY > corridorMaxY)
            {
                glideY = corridorMaxY;
                glideVelocity = 0f;
            }

            float mid = (lowerLaneY + upperLaneY) * 0.5f;
            laneIndex = glideY >= mid ? 1 : 0;
            return glideY;
        }

        private float ResolveGroundY(float deltaTime)
        {
            float dt = Mathf.Max(0f, deltaTime);
            groundJumpBufferRemainingSec = Mathf.Max(0f, groundJumpBufferRemainingSec - dt);

            if (grounded)
            {
                groundCoyoteRemainingSec = groundCoyoteSec;
            }
            else
            {
                groundCoyoteRemainingSec = Mathf.Max(0f, groundCoyoteRemainingSec - dt);
            }

            TryConsumeGroundJump();

            groundVelocity -= groundGravityPerSec * dt;
            groundVelocity = Mathf.Clamp(groundVelocity, -groundMaxFallSpeed, groundMaxRiseSpeed);
            groundY += groundVelocity * dt;

            if (groundY <= groundFloorY)
            {
                groundY = groundFloorY;
                groundVelocity = 0f;
                grounded = true;
            }
            else if (groundY >= groundCeilingY)
            {
                groundY = groundCeilingY;
                if (groundVelocity > 0f)
                {
                    groundVelocity = 0f;
                }

                grounded = false;
            }
            else
            {
                grounded = false;
            }

            float mid = (groundFloorY + groundCeilingY) * 0.5f;
            laneIndex = groundY >= mid ? 1 : 0;
            return groundY;
        }

        private float ResolveLaneY(int lane)
        {
            return lane <= 0 ? lowerLaneY : upperLaneY;
        }

        private void TryConsumeGroundJump()
        {
            if (groundJumpBufferRemainingSec <= 0f)
            {
                return;
            }

            if (!grounded && groundCoyoteRemainingSec <= 0f)
            {
                return;
            }

            groundVelocity = groundJumpImpulse;
            grounded = false;
            groundCoyoteRemainingSec = 0f;
            groundJumpBufferRemainingSec = 0f;
        }
    }
}
