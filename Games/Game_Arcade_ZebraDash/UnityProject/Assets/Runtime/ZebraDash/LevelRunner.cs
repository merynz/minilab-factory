using System;
using System.Collections.Generic;
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
        Completed = 4,
        Paused = 5
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
        [SerializeField] private float countdownDurationSec = 3f;
        [SerializeField] private float collisionWindowSec = 0.07f;

        private RunState state = RunState.Idle;
        private float countdownRemaining;
        private float levelDurationSec;
        private string activeTrackId = "";
        private BeatMap activeBeatMap;
        private float activeOffsetSec;
        private string failReason = "";
        private bool restartRequested;
        private bool pauseRequested;

        private BeatEvent[] canonicalEvents = Array.Empty<BeatEvent>();
        private BeatEvent[] tapJudgeEvents = Array.Empty<BeatEvent>();
        private readonly HashSet<int> consumedTapIndices = new HashSet<int>();
        private readonly HashSet<int> missedTapIndices = new HashSet<int>();
        private readonly HashSet<int> resolvedHazards = new HashSet<int>();

        private InputJudge inputJudge = new InputJudge(new JudgeWindows());

        private int combo;
        private int maxCombo;
        private int score;
        private int perfectCount;
        private int goodCount;
        private int missCount;
        private string lastJudge = "None";
        private string currentSectionState = "Active";

        public event Action<RunState> RunStateChanged;

        public RunState State => state;
        public string LastJudge => lastJudge;
        public int Combo => combo;
        public int MaxCombo => maxCombo;
        public int Score => score;
        public int PerfectCount => perfectCount;
        public int GoodCount => goodCount;
        public int MissCount => missCount;
        public string FailReason => failReason;
        public float CountdownRemaining => Mathf.Max(0f, countdownRemaining);
        public float SongTimeSec => beatClock != null ? beatClock.NowSeconds : 0f;
        public float HitLineX => playerTransform != null ? playerTransform.position.x : -4f;
        public float OffsetMs => activeOffsetSec * 1000f;
        public int CurrentBeat => beatClock != null ? beatClock.BeatIndex : 0;
        public string CurrentSectionState => currentSectionState;
        public Transform WorldRoot => worldRoot;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
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

            if (playerController != null)
            {
                playerController.TapPerformed -= OnPlayerTap;
                playerController.TapPerformed += OnPlayerTap;
            }

            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            PlacePlayerAtLeftThird();
        }

        private void OnDestroy()
        {
            if (playerController != null)
            {
                playerController.TapPerformed -= OnPlayerTap;
            }
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
                return;
            }

            if (state == RunState.Playing)
            {
                float songTime = SongTimeSec;
                obstacleSpawner?.Tick(songTime);
                TickSectionState(songTime);
                TickHazards(songTime);
                TickAutoMiss(songTime);

                if (levelDurationSec > 0f && songTime >= levelDurationSec)
                {
                    CompleteRun();
                }
            }

            if (state == RunState.Playing || state == RunState.Paused)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
                {
                    pauseRequested = true;
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

            if (playerController != null)
            {
                playerController.TapPerformed -= OnPlayerTap;
            }

            playerController = player;
            if (playerController != null)
            {
                playerController.TapPerformed -= OnPlayerTap;
                playerController.TapPerformed += OnPlayerTap;
            }
        }

        public void StartRun(BeatMap beatMap, MusicTrackEntry trackEntry, float offsetSec)
        {
            if (beatMap == null || audioSource == null || beatClock == null || obstacleSpawner == null || playerController == null)
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

            canonicalEvents = BeatMapEventUtils.GetCanonicalEvents(activeBeatMap);
            tapJudgeEvents = BeatMapEventUtils.GetTapAccentEvents(activeBeatMap);

            consumedTapIndices.Clear();
            missedTapIndices.Clear();
            resolvedHazards.Clear();
            combo = 0;
            maxCombo = 0;
            score = 0;
            perfectCount = 0;
            goodCount = 0;
            missCount = 0;
            lastJudge = "None";
            failReason = "";
            restartRequested = false;
            pauseRequested = false;
            currentSectionState = "Active";

            obstacleSpawner.Configure(activeBeatMap);
            obstacleSpawner.ResetAll();

            inputJudge.SetDeviceOffset(0f);
            playerController.InitializeLanes(-1.2f, 1.2f);
            playerController.SetLane(0);
            playerController.SetInputEnabled(false);

            countdownRemaining = countdownDurationSec;
            TransitionTo(RunState.Countdown);
        }

        public bool ConsumeRestartRequest()
        {
            bool value = restartRequested;
            restartRequested = false;
            return value;
        }

        public bool ConsumePauseRequest()
        {
            bool value = pauseRequested;
            pauseRequested = false;
            return value;
        }

        public void PauseRun()
        {
            if (state != RunState.Playing)
            {
                return;
            }

            beatClock.PauseClock();
            playerController.SetInputEnabled(false);
            TransitionTo(RunState.Paused);
        }

        public void ResumeRun()
        {
            if (state != RunState.Paused)
            {
                return;
            }

            beatClock.ResumeClock();
            playerController.SetInputEnabled(true);
            TransitionTo(RunState.Playing);
        }

        public void FailRun(string reason)
        {
            if (state != RunState.Playing)
            {
                return;
            }

            failReason = string.IsNullOrWhiteSpace(reason) ? "Collision" : reason;
            playerController.SetInputEnabled(false);
            beatClock.StopClock();
            audioSource.Stop();
            TransitionTo(RunState.Failed);
        }

        private void BeginPlayback()
        {
            countdownRemaining = 0f;
            beatClock.StartClock(audioSource, activeBeatMap.bpm, activeOffsetSec, activeTrackId);
            playerController.SetInputEnabled(true);
            TransitionTo(RunState.Playing);
        }

        private void CompleteRun()
        {
            if (state != RunState.Playing)
            {
                return;
            }

            playerController.SetInputEnabled(false);
            beatClock.StopClock();
            audioSource.Stop();
            TransitionTo(RunState.Completed);
        }

        private void OnPlayerTap()
        {
            if (state != RunState.Playing)
            {
                return;
            }

            float now = SongTimeSec;
            JudgeOutcome outcome = inputJudge.EvaluateNearest(
                tapJudgeEvents,
                consumedTapIndices,
                now,
                e => e != null && (e.IsKind(BeatKinds.Tap) || e.IsKind(BeatKinds.Accent)));

            if (outcome.EventIndex < 0 || outcome.Result == JudgeResult.Miss)
            {
                missCount++;
                combo = 0;
                lastJudge = JudgeResult.Miss.ToString();
                return;
            }

            consumedTapIndices.Add(outcome.EventIndex);
            if (outcome.Result == JudgeResult.Perfect)
            {
                perfectCount++;
                score += 120;
            }
            else
            {
                goodCount++;
                score += 80;
            }

            combo++;
            maxCombo = Mathf.Max(maxCombo, combo);
            lastJudge = outcome.Result.ToString();
        }

        private void TickAutoMiss(float now)
        {
            for (int i = 0; i < tapJudgeEvents.Length; i++)
            {
                if (consumedTapIndices.Contains(i) || missedTapIndices.Contains(i))
                {
                    continue;
                }

                BeatEvent evt = tapJudgeEvents[i];
                if (evt == null)
                {
                    continue;
                }

                if (now > evt.timeSec + inputJudge.MissWindowSec)
                {
                    missedTapIndices.Add(i);
                    combo = 0;
                    missCount++;
                    lastJudge = JudgeResult.Miss.ToString();
                }
                else
                {
                    break;
                }
            }
        }

        private void TickHazards(float now)
        {
            IReadOnlyList<ObstacleSpawner.SpawnDirective> hazards = obstacleSpawner.Directives;
            for (int i = 0; i < hazards.Count; i++)
            {
                if (resolvedHazards.Contains(i))
                {
                    continue;
                }

                ObstacleSpawner.SpawnDirective hazard = hazards[i];
                BeatEvent evt = hazard.Event;
                if (evt == null)
                {
                    resolvedHazards.Add(i);
                    continue;
                }

                int lane = Mathf.Clamp(evt.lane, 0, 1);

                if (evt.IsKind(BeatKinds.Long))
                {
                    float start = hazard.HitTimeSec - collisionWindowSec;
                    float end = hazard.EndTimeSec + collisionWindowSec;
                    if (now < start)
                    {
                        break;
                    }

                    if (now >= start && now <= end)
                    {
                        if (playerController.LaneIndex == lane)
                        {
                            FailRun("Long lane collision");
                            return;
                        }
                    }

                    if (now > end)
                    {
                        resolvedHazards.Add(i);
                    }

                    continue;
                }

                float hitStart = hazard.HitTimeSec - collisionWindowSec;
                float hitEnd = hazard.HitTimeSec + collisionWindowSec;
                if (now < hitStart)
                {
                    break;
                }

                if (now >= hitStart && now <= hitEnd)
                {
                    if (playerController.LaneIndex == lane)
                    {
                        FailRun(evt.IsKind(BeatKinds.Accent) ? "Accent collision" : "Tap collision");
                        return;
                    }
                }

                if (now > hitEnd)
                {
                    resolvedHazards.Add(i);
                }
            }
        }

        private void TickSectionState(float now)
        {
            currentSectionState = "Active";
            RestSectionEvent[] restSections = BeatMapEventUtils.GetRestSections(activeBeatMap);
            for (int i = 0; i < restSections.Length; i++)
            {
                if (now >= restSections[i].startSec && now <= restSections[i].endSec)
                {
                    currentSectionState = "Rest";
                    break;
                }
            }
        }

        private void TransitionTo(RunState newState)
        {
            state = newState;
            RunStateChanged?.Invoke(newState);
        }

        private void PlacePlayerAtLeftThird()
        {
            if (targetCamera == null || playerTransform == null)
            {
                return;
            }

            targetCamera.orthographic = true;
            targetCamera.orthographicSize = 5f;
            Vector3 viewport = new Vector3(0.33f, 0.5f, Mathf.Abs(targetCamera.transform.position.z));
            Vector3 position = targetCamera.ViewportToWorldPoint(viewport);
            playerTransform.position = new Vector3(position.x, playerTransform.position.y, 0f);
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

                    duration = Mathf.Max(duration, evt.GetEndTimeSec());
                }
            }

            if (duration <= 0f && map?.sections != null)
            {
                for (int i = 0; i < map.sections.Length; i++)
                {
                    duration = Mathf.Max(duration, map.sections[i].endSec);
                }
            }

            return Mathf.Max(duration + 1f, 20f);
        }
    }
}
