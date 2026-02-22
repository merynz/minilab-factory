using System;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    public enum RunState
    {
        Idle = 0,
        Countdown = 1,
        Playing = 2,
        Failed = 3,
        Completed = 4
    }

    public sealed class LevelRunner : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Transform worldRoot;
        [SerializeField] private BeatClock beatClock;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private ObstacleSpawner obstacleSpawner;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private float worldScrollSpeed = 7f;
        [SerializeField] private float restScrollMultiplier = 0.85f;
        [SerializeField] private float accentShiftMagnitude = 0.45f;
        [SerializeField] private float accentRecoverySpeed = 2.5f;
        [SerializeField] private float countdownDurationSec = 0.5f;
        [SerializeField] private bool runScrolling = true;

        private RunState state = RunState.Idle;
        private bool isRestSection;
        private float accentOffsetX;
        private float cameraBaseX;
        private bool cameraBaseCaptured;
        private float countdownRemaining;
        private float levelDurationSec;
        private string activeTrackId = "";
        private BeatMap activeBeatMap;
        private float activeOffsetSec;
        private string failReason = "";
        private bool restartRequested;

        private int combo;
        private int maxCombo;
        private int score;
        private string lastJudge = "None";

        public RunState State => state;
        public string LastJudge => lastJudge;
        public int Combo => combo;
        public int MaxCombo => maxCombo;
        public int Score => score;
        public string FailReason => failReason;
        public float CountdownRemaining => Mathf.Max(0f, countdownRemaining);
        public float SongTimeSec => beatClock != null ? beatClock.SongTimeSec : 0f;
        public float ScrollSpeed => worldScrollSpeed;
        public float HitLineX => playerTransform != null ? playerTransform.position.x : -4f;
        public Transform WorldRoot => worldRoot;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
            if (targetCamera != null)
            {
                cameraBaseX = targetCamera.transform.position.x;
                cameraBaseCaptured = true;
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (beatClock == null)
            {
                beatClock = GetComponent<BeatClock>();
            }

            if (obstacleSpawner == null)
            {
                obstacleSpawner = GetComponent<ObstacleSpawner>();
            }

            if (playerController == null)
            {
                playerController = FindObjectOfType<PlayerController>();
            }

            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            PlacePlayerAtLeftThird();
        }

        private void Update()
        {
            if (state == RunState.Countdown)
            {
                countdownRemaining -= Time.deltaTime;
                if (countdownRemaining <= 0f)
                {
                    BeginPlayback();
                }
            }

            if (state == RunState.Playing)
            {
                TickWorldScroll();
                if (levelDurationSec > 0f && SongTimeSec >= levelDurationSec)
                {
                    CompleteRun();
                }
            }

            if (state == RunState.Failed || state == RunState.Completed)
            {
                if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(0))
                {
                    restartRequested = true;
                }
            }
        }

        public void ConfigureScene(Camera camera, Transform player, Transform world)
        {
            targetCamera = camera;
            playerTransform = player;
            worldRoot = world;
            PlacePlayerAtLeftThird();
        }

        public void ConfigureDependencies(BeatClock clock, AudioSource source, ObstacleSpawner spawner, PlayerController player)
        {
            beatClock = clock;
            audioSource = source;
            obstacleSpawner = spawner;
            playerController = player;
        }

        public void StartRun(BeatMap beatMap, MusicTrackEntry trackEntry, float offsetSec)
        {
            if (beatMap == null)
            {
                return;
            }

            if (audioSource == null || beatClock == null || obstacleSpawner == null || playerController == null)
            {
                Debug.LogError("LevelRunner dependencies are missing.");
                return;
            }

            activeBeatMap = beatMap;
            activeTrackId = trackEntry?.trackId ?? "unknown_track";
            activeOffsetSec = offsetSec;
            levelDurationSec = trackEntry != null && trackEntry.durationSec > 0f
                ? trackEntry.durationSec
                : EstimateLevelDuration(activeBeatMap);

            combo = 0;
            maxCombo = 0;
            score = 0;
            lastJudge = "None";
            failReason = "";
            restartRequested = false;

            obstacleSpawner.Configure(activeBeatMap);
            playerController.Configure(beatClock, activeBeatMap, OnJudge, OnMissedEvent);
            playerController.SetInputEnabled(false);
            SetScrolling(false);

            countdownRemaining = countdownDurationSec;
            state = RunState.Countdown;
        }

        public bool ConsumeRestartRequest()
        {
            bool value = restartRequested;
            restartRequested = false;
            return value;
        }

        public void SetScrolling(bool enabled)
        {
            runScrolling = enabled;
        }

        public void SetRestSection(bool isRest)
        {
            isRestSection = isRest;
        }

        public void TriggerAccentPulse(float strength)
        {
            float pulse = Mathf.Clamp(strength, 0.1f, 2f) * accentShiftMagnitude;
            accentOffsetX = Mathf.Clamp(accentOffsetX + pulse, -accentShiftMagnitude * 2f, accentShiftMagnitude * 2f);
        }

        public void FailRun(string reason)
        {
            if (state != RunState.Playing)
            {
                return;
            }

            failReason = string.IsNullOrWhiteSpace(reason) ? "Miss" : reason;
            state = RunState.Failed;
            SetScrolling(false);
            playerController.SetInputEnabled(false);
            beatClock.StopClock();
            if (audioSource != null)
            {
                audioSource.Stop();
            }
        }

        private void BeginPlayback()
        {
            countdownRemaining = 0f;
            state = RunState.Playing;
            SetScrolling(true);
            playerController.SetInputEnabled(true);
            beatClock.StartClock(audioSource, activeBeatMap.bpm, activeOffsetSec, activeTrackId);
        }

        private void CompleteRun()
        {
            if (state != RunState.Playing)
            {
                return;
            }

            state = RunState.Completed;
            SetScrolling(false);
            playerController.SetInputEnabled(false);
            beatClock.StopClock();
            if (audioSource != null)
            {
                audioSource.Stop();
            }
        }

        private void OnJudge(JudgeOutcome outcome)
        {
            if (state != RunState.Playing)
            {
                return;
            }

            lastJudge = outcome.Result.ToString();
            if (outcome.Result == JudgeResult.Miss)
            {
                FailRun("Miss");
                return;
            }

            combo += 1;
            maxCombo = Mathf.Max(maxCombo, combo);
            score += outcome.Result == JudgeResult.Perfect ? 100 : 70;
        }

        private void OnMissedEvent(string reason)
        {
            combo = 0;
            lastJudge = "Miss";
            FailRun(reason);
        }

        private void TickWorldScroll()
        {
            if (!runScrolling || worldRoot == null)
            {
                return;
            }

            float speedMul = isRestSection ? restScrollMultiplier : 1f;
            worldRoot.position += Vector3.left * (worldScrollSpeed * speedMul * Time.deltaTime);

            accentOffsetX = Mathf.MoveTowards(accentOffsetX, 0f, accentRecoverySpeed * Time.deltaTime);
            if (targetCamera != null)
            {
                if (!cameraBaseCaptured)
                {
                    cameraBaseX = targetCamera.transform.position.x;
                    cameraBaseCaptured = true;
                }

                Vector3 camPos = targetCamera.transform.position;
                camPos.x = cameraBaseX + accentOffsetX;
                targetCamera.transform.position = camPos;
            }
        }

        private void PlacePlayerAtLeftThird()
        {
            if (targetCamera == null || playerTransform == null)
            {
                return;
            }

            Vector3 viewport = targetCamera.WorldToViewportPoint(playerTransform.position);
            viewport.x = 0.33f;
            playerTransform.position = targetCamera.ViewportToWorldPoint(viewport);
            playerTransform.position = new Vector3(playerTransform.position.x, 0f, 0f);
        }

        private static float EstimateLevelDuration(BeatMap map)
        {
            float duration = 0f;
            if (map?.events != null)
            {
                for (int i = 0; i < map.events.Length; i++)
                {
                    BeatEvent evt = map.events[i];
                    if (evt == null)
                    {
                        continue;
                    }

                    duration = Mathf.Max(duration, evt.timeSec + Mathf.Max(0f, evt.durationSec));
                }
            }

            if (duration <= 0f && map?.sections != null)
            {
                for (int i = 0; i < map.sections.Length; i++)
                {
                    duration = Mathf.Max(duration, map.sections[i].endSec);
                }
            }

            return Mathf.Max(duration + 1f, 30f);
        }
    }
}
