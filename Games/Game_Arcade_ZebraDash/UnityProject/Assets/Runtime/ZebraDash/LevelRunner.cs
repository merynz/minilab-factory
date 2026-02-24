using System;
using System.Collections.Generic;
using System.Linq;
using MiniLab.Core.Rhythm;
using UnityEngine;
using ZebraDash.LevelDesign;

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
        private const string DeviceOffsetPrefsKey = "minilab.rhythm.device_offset_sec";
        private const float PixelSnapPpu = 16f;

        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Transform worldRoot;
        [SerializeField] private BeatClock beatClock;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private ObstacleSpawner obstacleSpawner;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private ParallaxSystem parallaxSystem;
        [SerializeField, Range(2, 4)] private int countdownBeats = 3;
        [SerializeField] private float collisionWindowSec = 0.074f;
        [SerializeField] private float introCollisionGraceSec = 2.20f;
        [SerializeField] private float worldScrollUnitsPerSec = 8.0f;
        [SerializeField] private float hitLineOffsetX = 1.75f;
        [SerializeField] private bool useJumpOnlyLevelDesign = true;
        [SerializeField] private bool useGlideMechanic = false;
        [SerializeField] private bool enableModeSwitching = false;
        [SerializeField] private bool forceMazeGlideMode = false;
        [SerializeField] private float modeSwitchBlendLeadSec = 0.10f;
        [SerializeField] private float hazardHitToleranceY = 0.22f;
        [SerializeField] private float holdHazardToleranceY = 0.32f;
        [SerializeField] private bool mazeCollisionEnabled = true;
        [SerializeField] private float mazeProbeOffsetX = 0.18f;
        [SerializeField] private float groundProbeOffsetX = 0.20f;
        [SerializeField] private float mazeWallMarginY = 0.08f;
        [SerializeField] private float mazeCollisionArmDelaySec = 4.8f;
        [SerializeField] private float mazeOutOfBoundsGraceSec = 0.42f;
        [SerializeField] private float mazeHardOvershootY = 0.34f;
        [SerializeField] private float groundFloorInsetY = 0.16f;
        [SerializeField] private float groundCeilingInsetY = 0.18f;

        private float lowerLaneY = -1.2f;
        private float upperLaneY = 1.2f;

        private RunState state = RunState.Idle;
        private float countdownRemaining;
        private float levelDurationSec;
        private string activeTrackId = "";
        private BeatMap activeBeatMap;
        private BeatMap runtimeBeatMap;
        private GameplayPattern activePattern = new GameplayPattern();
        private float[] accentPulseHitTimes = Array.Empty<float>();
        private float activeOffsetSec;
        private float timelineStartSec;
        private float audioStartSec;
        private string failReason = "";
        private bool restartRequested;
        private bool pauseRequested;
        private float worldShiftX;
        private Vector3 baseWorldPosition;
        private Vector3 baseCameraPosition;
        private float previousSongTimeSec;
        private bool hasSongTimeSample;

        private BeatEvent[] canonicalEvents = Array.Empty<BeatEvent>();
        private BeatEvent[] tapJudgeEvents = Array.Empty<BeatEvent>();
        private GameplayTapScheduleEvent[] tapScheduleEntries = Array.Empty<GameplayTapScheduleEvent>();
        private GameplayPatternEvent[] cameraShiftEvents = Array.Empty<GameplayPatternEvent>();
        private MazeBeatCue[] mazeBeatCues = Array.Empty<MazeBeatCue>();
        private int nextMazeCueIndex;
        private readonly HashSet<int> consumedTapIndices = new HashSet<int>();
        private readonly HashSet<int> missedTapIndices = new HashSet<int>();
        private readonly HashSet<int> resolvedHazards = new HashSet<int>();
        private float currentStrain;
        private float targetStrain;
        private string currentPresetId = "";
        private string lastEmptyTapDecision = "None";
        private float lastTapOffsetMs;
        private bool hazardsArmed;
        private float hazardsAutoArmSec;
        private float mazeCollisionArmSec;
        private float mazeOutOfBoundsAccumSec;
        private readonly BeatGrid beatGrid = new BeatGrid(120f, 4, 4);
        private LevelOrchestration levelOrchestration;
        private SectionPlan activeSectionPlan;
        private MovementProfile activeMovementProfile;
        private int activeSectionIndex = -1;
        private float currentScrollUnitsPerSec;
        private float accumulatedWorldScrollPos;
        private float mazeSyncAvgCueOffsetMs;
        private float mazeSyncMaxCueOffsetMs;
        private int mazeSyncOffGridCueCount;
        private int mazeSyncExpectedTapCount;
        private float mazeSyncAvgTapGapSec;
        private float mazeSyncMaxTapGapSec;
        private float mazeSyncFirstExpectedTapSec;
        private int mazeSyncUnalignedSectionCount;
        private bool mazeSyncAnalyzed;

        private readonly struct HazardEnvelope
        {
            public HazardEnvelope(float centerY, float halfHeight)
            {
                CenterY = centerY;
                HalfHeight = Mathf.Max(0.05f, halfHeight);
            }

            public float CenterY { get; }
            public float HalfHeight { get; }
        }

        private readonly InputJudge inputJudge = new InputJudge(new JudgeWindows
        {
            perfectMs = 45f,
            goodMs = 90f
        });

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
        public float HitLineX => playerTransform != null ? playerTransform.position.x + hitLineOffsetX : -3.28f;
        public float OffsetMs => activeOffsetSec * 1000f;
        public float DeviceOffsetMs => inputJudge.DeviceOffsetSec * 1000f;
        public float SessionPhaseMs => inputJudge.SessionPhaseMs;
        public float BeatMs => beatClock != null ? beatClock.BeatMs : 0f;
        public float LastTapOffsetMs => lastTapOffsetMs;
        public string LastEmptyTapDecision => lastEmptyTapDecision;
        public float Progress01 => levelDurationSec > 0f ? Mathf.Clamp01(SongTimeSec / levelDurationSec) : 0f;
        public int CurrentBeat => beatClock != null ? beatClock.BeatIndex : 0;
        public int CurrentBar => beatGrid.BarIndex(SongTimeSec);
        public float BeatSec => beatGrid.BeatSec;
        public float BarSec => beatGrid.BarSec;
        public float PhaseBeat => beatGrid.GetPhaseBeat(SongTimeSec);
        public float PhaseBar => beatGrid.GetPhaseBar(SongTimeSec);
        public float WorldScrollUnitsPerSec => Mathf.Max(0.1f, currentScrollUnitsPerSec > 0.01f ? currentScrollUnitsPerSec : worldScrollUnitsPerSec);
        public float WorldScrollPos => accumulatedWorldScrollPos;
        public float PlayerX => playerTransform != null ? playerTransform.position.x : 0f;
        public string HazardMaskBar => BuildCurrentBarHazardMask();
        public string GridDebugLine => BuildCurrentGridDebugLine();
        public string TwoBarPlanDebug => BuildTwoBarPlanDebug();
        public string NextHazardTimingDebug => BuildNextHazardTimingDebugLine();
        public string CurrentSectionState => currentSectionState;
        public float CurrentStrain => currentStrain;
        public float TargetStrain => targetStrain;
        public string CurrentPresetId => currentPresetId;
        public int PatternSeed => activePattern != null ? activePattern.seed : 0;
        public string ActiveTrackId => activeTrackId ?? string.Empty;
        public IReadOnlyList<float> AccentPulseHitTimes => accentPulseHitTimes;
        public string NextHazardsDebug => BuildNextHazardsDebugLine();
        public string MazeSyncDebug => BuildMazeSyncDebugLine();
        public int ActiveObstacleCount => obstacleSpawner != null ? obstacleSpawner.ActiveCount : 0;
        public int PooledObstacleCount => obstacleSpawner != null ? obstacleSpawner.PoolCount : 0;
        public int CreatedObstacleCount => obstacleSpawner != null ? obstacleSpawner.CreatedCount : 0;
        public Transform WorldRoot => worldRoot;

        public bool TryGetTelegraph(out int lane, out float leadSec, out float lead01)
        {
            return TryGetTelegraph(SongTimeSec, out lane, out leadSec, out lead01);
        }

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
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            PlacePlayerAtLeftThird();
            CacheBasePositions();
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
                float previousSongTime = hasSongTimeSample ? previousSongTimeSec : songTime;
                float songDt = Mathf.Max(0f, songTime - previousSongTime);
                ApplySectionProfile(songTime, false);
                accumulatedWorldScrollPos += WorldScrollUnitsPerSec * songDt;
                playerController?.SetTimingContext(songTime, beatGrid.BeatSec);
                obstacleSpawner?.Tick(songTime);
                TickSectionState(songTime);
                UpdateMovementProfile(songTime, false);
                TickHazards(previousSongTime, songTime);
                TickMazeCollision(previousSongTime, songTime);
                TickAutoMiss(songTime);
                TickCameraShift(songTime);

                if (levelDurationSec > 0f && songTime >= levelDurationSec)
                {
                    CompleteRun();
                }

                previousSongTimeSec = songTime;
                hasSongTimeSample = true;
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
            CacheBasePositions();
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

        public void ConfigureParallax(ParallaxSystem system)
        {
            parallaxSystem = system;
        }

        public void StartRun(BeatMap beatMap, MusicTrackEntry trackEntry, float offsetSec, int trackIndex = -1)
        {
            if (beatMap == null || audioSource == null || beatClock == null || obstacleSpawner == null || playerController == null)
            {
                Debug.LogError("LevelRunner dependencies are missing.");
                return;
            }

            activeBeatMap = beatMap;
            activeTrackId = trackEntry?.trackId ?? "unknown_track";
            activeOffsetSec = offsetSec;
            timelineStartSec = ResolveTimelineStartSec(activeBeatMap, trackEntry);
            audioStartSec = timelineStartSec;
            runtimeBeatMap = BuildRuntimeBeatMap(activeBeatMap, timelineStartSec);
            float explicitDurationSec = trackEntry != null && trackEntry.durationSec > 0f
                ? Mathf.Max(0f, trackEntry.durationSec - timelineStartSec)
                : 0f;
            levelDurationSec = explicitDurationSec > 0f
                ? explicitDurationSec
                : EstimateLevelDuration(runtimeBeatMap);
            beatGrid.Configure(runtimeBeatMap != null ? runtimeBeatMap.bpm : activeBeatMap.bpm, 4, 4);
            levelOrchestration = LevelOrchestrator.Build(runtimeBeatMap, levelDurationSec, activeTrackId);
            activeSectionPlan = levelOrchestration.ResolveSectionAt(0f);
            activeSectionIndex = activeSectionPlan != null ? activeSectionPlan.sectionIndex : -1;
            activeMovementProfile = levelOrchestration.ResolveProfile(activeSectionPlan != null ? activeSectionPlan.movementProfileRef : string.Empty);
            currentScrollUnitsPerSec = activeMovementProfile != null ? activeMovementProfile.ScrollUnitsPerSec : worldScrollUnitsPerSec;
            accumulatedWorldScrollPos = 0f;

            canonicalEvents = BeatMapEventUtils.GetCanonicalEvents(runtimeBeatMap);
            activePattern = GameplayPatternGenerator.Build(runtimeBeatMap, activeTrackId, 1f);
            obstacleSpawner.ConfigureThemeContext(
                trackId: activeTrackId,
                trackIndex: trackIndex,
                explicitThemeCode: trackEntry != null ? trackEntry.themeCode : string.Empty,
                themeSeed: activePattern != null ? activePattern.seed : 0);
            tapScheduleEntries = (activePattern.tapSchedule ?? Array.Empty<GameplayTapScheduleEvent>())
                .OrderBy(e => e.timeSec)
                .ThenBy(e => e.beatIndex)
                .ToArray();
            tapJudgeEvents = BuildJudgeEvents(activePattern);
            cameraShiftEvents = activePattern.events
                .Where(e => e != null && string.Equals(e.kind, GameplayPatternKinds.CameraShift, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.hitTimeSec)
                .ToArray();
            accentPulseHitTimes = activePattern.events
                .Where(e => e != null && string.Equals(e.kind, GameplayPatternKinds.AccentPulse, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.hitTimeSec)
                .OrderBy(t => t)
                .ToArray();

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
            currentStrain = 0f;
            targetStrain = 0f;
            currentPresetId = "";
            lastEmptyTapDecision = "None";
            lastTapOffsetMs = 0f;
            worldShiftX = 0f;
            hazardsArmed = !useJumpOnlyLevelDesign;
            mazeOutOfBoundsAccumSec = 0f;
            ResetWorldShift();
            hasSongTimeSample = false;
            previousSongTimeSec = 0f;

            if (useJumpOnlyLevelDesign)
            {
                obstacleSpawner.ConfigureStaticLevel(levelOrchestration, levelDurationSec, activePattern.seed);
                mazeBeatCues = obstacleSpawner.BeatCues
                    .Where(c => c != null && c.expectedTap)
                    .OrderBy(c => c.timeSec)
                    .ToArray();
            }
            else
            {
                obstacleSpawner.Configure(activePattern);
                mazeBeatCues = Array.Empty<MazeBeatCue>();
            }
            obstacleSpawner.ResetAll();
            nextMazeCueIndex = 0;
            AnalyzeMazeFlowSync();

            inputJudge.SetDeviceOffset(LoadDeviceOffsetSec());
            inputJudge.ConfigureForBpm(runtimeBeatMap != null ? runtimeBeatMap.bpm : activeBeatMap.bpm);
            inputJudge.ResetSessionPhase();
            float secondsPerBeat = 60f / Mathf.Max(1f, runtimeBeatMap != null ? runtimeBeatMap.bpm : activeBeatMap.bpm);
            if (useJumpOnlyLevelDesign)
            {
                introCollisionGraceSec = Mathf.Max(1.1f, secondsPerBeat * 2.5f);
                mazeCollisionArmSec = introCollisionGraceSec + 0.25f;
                hazardsAutoArmSec = introCollisionGraceSec + 0.45f;
            }
            else
            {
                introCollisionGraceSec = Mathf.Max(5.6f, secondsPerBeat * 10f);
                mazeCollisionArmSec = Mathf.Max(mazeCollisionArmDelaySec, introCollisionGraceSec + 0.65f);
                hazardsAutoArmSec = Mathf.Max(7.0f, introCollisionGraceSec + 1.80f);
            }
            countdownRemaining = Mathf.Max(1f, countdownBeats * secondsPerBeat);
            hazardHitToleranceY = Mathf.Clamp(hazardHitToleranceY, 0.12f, 0.18f);
            holdHazardToleranceY = Mathf.Clamp(holdHazardToleranceY, 0.20f, 0.30f);

            if (useJumpOnlyLevelDesign)
            {
                lowerLaneY = -2.0f;
                upperLaneY = 2.0f;
            }
            else
            {
                lowerLaneY = -1.2f;
                upperLaneY = 1.2f;
            }

            playerController.InitializeLanes(lowerLaneY, upperLaneY);
            if (activeMovementProfile != null)
            {
                playerController.ApplyMovementProfile(activeMovementProfile);
            }

            playerController.SetMovementMode(useJumpOnlyLevelDesign
                ? PlayerController.MovementMode.JumpOnly
                : ResolveMovementModeForSection(0f));
            playerController.ConfigureGlideCorridor(-2.30f, 2.30f);
            playerController.SetLane(ResolveInitialLane(activePattern));
            UpdateMovementProfile(0f, true);
            playerController.SetInputEnabled(false);
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

            float failSongSec = beatClock != null ? beatClock.NowSeconds : 0f;
            failReason = string.IsNullOrWhiteSpace(reason) ? "Collision" : reason;
            playerController.SetInputEnabled(false);
            beatClock.StopClock();
            audioSource.Stop();
            Debug.Log($"[ZebraDash] FailRun reason={failReason} song={failSongSec:F3}s lane={(playerController != null ? playerController.LaneIndex : -1)}");
            TransitionTo(RunState.Failed);
        }

        private void BeginPlayback()
        {
            countdownRemaining = 0f;
            if (audioSource != null && audioSource.clip != null && audioStartSec > 0.01f)
            {
                float maxStartSec = Mathf.Max(0f, audioSource.clip.length - 0.05f);
                audioSource.time = Mathf.Clamp(audioStartSec, 0f, maxStartSec);
            }

            float bpm = runtimeBeatMap != null ? runtimeBeatMap.bpm : activeBeatMap.bpm;
            beatClock.StartClock(audioSource, bpm, activeOffsetSec, activeTrackId);
            playerController?.SetTimingContext(0f, beatGrid.BeatSec);
            ApplySectionProfile(0f, true);
            UpdateMovementProfile(0f, true);

            playerController.SetInputEnabled(true);
            hasSongTimeSample = false;
            previousSongTimeSec = 0f;
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
            if (useJumpOnlyLevelDesign)
            {
                float beatMs = beatGrid.BeatSec * 1000f;
                float perfectMs = Mathf.Clamp(0.10f * beatMs, 30f, 46f);
                float goodMs = Mathf.Clamp(0.22f * beatMs, 68f, 98f);
                if (TryMatchMazeCueTap(now, goodMs, out float deltaMs))
                {
                    lastTapOffsetMs = deltaMs;
                    float absDelta = Mathf.Abs(deltaMs);
                    if (absDelta <= perfectMs)
                    {
                        perfectCount++;
                        combo++;
                        score += 120;
                        lastJudge = JudgeResult.Perfect.ToString();
                    }
                    else
                    {
                        goodCount++;
                        combo++;
                        score += 80;
                        lastJudge = JudgeResult.Good.ToString();
                    }

                    lastEmptyTapDecision = EmptyTapDecision.Matched.ToString();
                }
                else
                {
                    // Jump-only mode: taps outside schedule do not fail.
                    lastTapOffsetMs = 0f;
                    lastJudge = "NoScore";
                    lastEmptyTapDecision = EmptyTapDecision.Ignored.ToString();
                }

                maxCombo = Mathf.Max(maxCombo, combo);
                return;
            }

            if (playerController != null && playerController.Mode == PlayerController.MovementMode.LaneSwitch)
            {
                QueueQuantizedLaneSwitch(now);
            }
            JudgeOutcome outcome = inputJudge.EvaluateNearest(
                tapJudgeEvents,
                consumedTapIndices,
                now,
                e => e != null && e.IsKind(BeatKinds.Tap));
            bool matched = outcome.EventIndex >= 0
                && (outcome.Result == JudgeResult.Perfect || outcome.Result == JudgeResult.Good);
            lastTapOffsetMs = outcome.EventIndex >= 0 ? outcome.DeltaMs : inputJudge.LastTapOffsetMs;
            lastEmptyTapDecision = matched ? EmptyTapDecision.Matched.ToString() : EmptyTapDecision.Ignored.ToString();

            if (matched)
            {
                hazardsArmed = true;
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
                return;
            }

            // Survival mode: non-schedule taps only move lane; they do not fail or score.
            lastJudge = "NoScore";
        }

        private void QueueQuantizedLaneSwitch(float nowSec)
        {
            if (playerController == null || playerController.Mode != PlayerController.MovementMode.LaneSwitch)
            {
                return;
            }

            float beatSec = beatGrid.BeatSec;
            float quantizedStartSec = nowSec;
            int targetLane = 1 - Mathf.Clamp(playerController.PlannedLaneIndex, 0, 1);
            float switchSec = Mathf.Clamp(0.18f * beatSec, 0.055f, 0.12f);
            playerController.QueueLaneSwitch(targetLane, quantizedStartSec, switchSec);
        }

        private void TickAutoMiss(float now)
        {
            if (forceMazeGlideMode)
            {
                return;
            }

            if (useJumpOnlyLevelDesign)
            {
                TickJumpCueMisses(now);
                return;
            }

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

        private void TickJumpCueMisses(float now)
        {
            if (mazeBeatCues == null || mazeBeatCues.Length == 0)
            {
                return;
            }

            float missWindow = Mathf.Max(0.06f, inputJudge.MissWindowSec);
            while (nextMazeCueIndex < mazeBeatCues.Length)
            {
                MazeBeatCue cue = mazeBeatCues[nextMazeCueIndex];
                if (cue == null || !cue.expectedTap)
                {
                    nextMazeCueIndex++;
                    continue;
                }

                if (now <= cue.timeSec + missWindow)
                {
                    break;
                }

                missCount++;
                combo = 0;
                lastJudge = JudgeResult.Miss.ToString();
                lastEmptyTapDecision = EmptyTapDecision.Miss.ToString();
                nextMazeCueIndex++;
            }
        }

        private bool TryMatchMazeCueTap(float now, float goodMs, out float deltaMs)
        {
            deltaMs = 0f;
            if (mazeBeatCues == null || mazeBeatCues.Length == 0)
            {
                return false;
            }

            int startIndex = Mathf.Clamp(nextMazeCueIndex - 1, 0, mazeBeatCues.Length - 1);
            int endIndex = Mathf.Clamp(nextMazeCueIndex + 4, 0, mazeBeatCues.Length - 1);
            float goodSec = Mathf.Max(0.03f, goodMs / 1000f);
            float bestAbsDeltaSec = goodSec + 0.0001f;
            int bestIndex = -1;
            float bestDeltaSec = 0f;

            for (int i = startIndex; i <= endIndex; i++)
            {
                MazeBeatCue cue = mazeBeatCues[i];
                if (cue == null || !cue.expectedTap)
                {
                    continue;
                }

                float deltaSec = now - cue.timeSec;
                float absSec = Mathf.Abs(deltaSec);
                if (absSec <= goodSec && absSec < bestAbsDeltaSec)
                {
                    bestAbsDeltaSec = absSec;
                    bestDeltaSec = deltaSec;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return false;
            }

            nextMazeCueIndex = Mathf.Max(nextMazeCueIndex, bestIndex + 1);
            deltaMs = bestDeltaSec * 1000f;
            return true;
        }

        private void TickHazards(float previousNow, float now)
        {
            if (useJumpOnlyLevelDesign)
            {
                return;
            }

            if (!hazardsArmed)
            {
                if (now < hazardsAutoArmSec)
                {
                    return;
                }

                hazardsArmed = true;
            }

            if (now < introCollisionGraceSec)
            {
                return;
            }

            IReadOnlyList<ObstacleSpawner.SpawnDirective> hazards = obstacleSpawner.Directives;
            for (int i = 0; i < hazards.Count; i++)
            {
                if (resolvedHazards.Contains(i))
                {
                    continue;
                }

                ObstacleSpawner.SpawnDirective hazard = hazards[i];
                GameplayPatternEvent evt = hazard.PatternEvent;
                if (evt == null)
                {
                    resolvedHazards.Add(i);
                    continue;
                }

                if (!evt.isHazard)
                {
                    if (now > hazard.EndTimeSec + collisionWindowSec)
                    {
                        resolvedHazards.Add(i);
                    }
                    continue;
                }

                if (string.Equals(evt.kind, GameplayPatternKinds.HoldSlide, StringComparison.OrdinalIgnoreCase))
                {
                    float start = hazard.HitTimeSec;
                    float end = hazard.EndTimeSec + collisionWindowSec;
                    if (now < start)
                    {
                        continue;
                    }

                    if (IntersectsWindow(previousNow, now, start, end))
                    {
                        HazardEnvelope envelope = ResolveHazardEnvelope(evt, hazard, now);
                        bool inDangerBand = IsPlayerInsideEnvelope(envelope, holdHazardToleranceY);
                        if (inDangerBand)
                        {
                            string reason = string.IsNullOrWhiteSpace(evt.archetype) ? "Hold collision" : $"{evt.archetype} collision";
                            FailRun(reason);
                            return;
                        }
                    }

                    if (now > end)
                    {
                        resolvedHazards.Add(i);
                    }
                    continue;
                }

                float hitEnd = hazard.HitTimeSec + collisionWindowSec;
                if (now < hazard.HitTimeSec)
                {
                    continue;
                }

                if (IntersectsWindow(previousNow, now, hazard.HitTimeSec, hitEnd))
                {
                    HazardEnvelope envelope = ResolveHazardEnvelope(evt, hazard, hazard.HitTimeSec);
                    if (IsPlayerInsideEnvelope(envelope, hazardHitToleranceY))
                    {
                        FailRun(string.Equals(evt.kind, GameplayPatternKinds.Fakeout, StringComparison.OrdinalIgnoreCase)
                            ? "Fakeout collision"
                            : "Jump collision");
                        return;
                    }
                }

                if (now > hitEnd)
                {
                    resolvedHazards.Add(i);
                }
            }
        }

        private void UpdateMovementProfile(float songTimeSec, bool forceSnap)
        {
            if (playerController == null)
            {
                return;
            }

            PlayerController.MovementMode targetMode = ResolveMovementModeForSection(songTimeSec + Mathf.Max(0f, modeSwitchBlendLeadSec));
            if (playerController.Mode != targetMode)
            {
                playerController.SetMovementMode(targetMode);
            }

            float probeOffset = (targetMode == PlayerController.MovementMode.GroundRunner || targetMode == PlayerController.MovementMode.JumpOnly)
                ? groundProbeOffsetX
                : mazeProbeOffsetX;
            float probeX = (playerTransform != null ? playerTransform.position.x : HitLineX) + probeOffset;
            float scrollPos = accumulatedWorldScrollPos;
            float gapBottom = 0f;
            float gapTop = 0f;
            bool sampled = obstacleSpawner != null
                && obstacleSpawner.TrySampleMazeGap(probeX, out gapBottom, out gapTop, out _);
            if (!sampled && parallaxSystem != null)
            {
                sampled = parallaxSystem.TrySampleMazeGap(probeX, songTimeSec, scrollPos, out gapBottom, out gapTop, out _);
            }

            if (!sampled)
            {
                return;
            }

            float margin = Mathf.Max(0.04f, mazeWallMarginY);
            float safeBottom = gapBottom + margin;
            float safeTop = gapTop - margin;
            if (safeTop <= safeBottom)
            {
                float center = (gapTop + gapBottom) * 0.5f;
                safeBottom = center - 0.90f;
                safeTop = center + 0.90f;
            }

            if (targetMode == PlayerController.MovementMode.Glide)
            {
                playerController.ConfigureGlideCorridor(safeBottom + 0.02f, safeTop - 0.02f);
                if (forceSnap)
                {
                    AlignPlayerToMazeGap(songTimeSec, true);
                }
                else
                {
                    float y = Mathf.Clamp(playerController.CurrentY, safeBottom + 0.05f, safeTop - 0.05f);
                    playerController.SetGlideY(y);
                }

                return;
            }

            if (targetMode == PlayerController.MovementMode.GroundRunner || targetMode == PlayerController.MovementMode.JumpOnly)
            {
                float floor = safeBottom + Mathf.Max(0.08f, groundFloorInsetY);
                float ceiling = safeTop - Mathf.Max(0.10f, groundCeilingInsetY);
                if (ceiling - floor < 0.95f)
                {
                    float center = (ceiling + floor) * 0.5f;
                    floor = center - 1.00f;
                    ceiling = center + 1.00f;
                }

                playerController.ConfigureGroundBounds(floor, ceiling);
                if (forceSnap)
                {
                    playerController.SetGroundY(Mathf.Clamp(floor + 0.04f, floor, ceiling));
                }

                return;
            }

            playerController.ConfigureGlideCorridor(safeBottom, safeTop);
        }

        private PlayerController.MovementMode ResolveMovementModeForSection(float songTimeSec)
        {
            if (useJumpOnlyLevelDesign)
            {
                return PlayerController.MovementMode.JumpOnly;
            }

            if (forceMazeGlideMode)
            {
                return PlayerController.MovementMode.Glide;
            }

            if (!enableModeSwitching)
            {
                return useGlideMechanic ? PlayerController.MovementMode.Glide : PlayerController.MovementMode.LaneSwitch;
            }

            string sectionType = ResolveSectionTypeAt(songTimeSec);
            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                return PlayerController.MovementMode.Glide;
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                return PlayerController.MovementMode.GroundRunner;
            }

            if (targetStrain >= 0.68f)
            {
                return PlayerController.MovementMode.Glide;
            }

            return PlayerController.MovementMode.GroundRunner;
        }

        private string ResolveSectionTypeAt(float songTimeSec)
        {
            GameplayPatternSectionInfo[] sections = activePattern?.sections ?? Array.Empty<GameplayPatternSectionInfo>();
            for (int i = 0; i < sections.Length; i++)
            {
                GameplayPatternSectionInfo section = sections[i];
                if (section == null)
                {
                    continue;
                }

                if (songTimeSec >= section.startSec && songTimeSec <= section.endSec)
                {
                    return section.sectionType;
                }
            }

            RestSectionEvent[] rests = activePattern?.restSections ?? Array.Empty<RestSectionEvent>();
            for (int i = 0; i < rests.Length; i++)
            {
                RestSectionEvent rest = rests[i];
                if (rest == null)
                {
                    continue;
                }

                if (songTimeSec >= rest.startSec && songTimeSec <= rest.endSec)
                {
                    return GameplaySectionTypes.Rest;
                }
            }

            return currentSectionState;
        }

        private void TickMazeCollision(float previousNow, float now)
        {
            bool jumpMode = playerController != null
                && (playerController.Mode == PlayerController.MovementMode.JumpOnly
                    || playerController.Mode == PlayerController.MovementMode.GroundRunner
                    || playerController.Mode == PlayerController.MovementMode.Glide);
            if (!mazeCollisionEnabled || !jumpMode || playerController == null || state != RunState.Playing)
            {
                return;
            }

            float dt = Mathf.Clamp(now - previousNow, 0f, 0.20f);
            if (now < mazeCollisionArmSec)
            {
                return;
            }

            float probeX = (playerTransform != null ? playerTransform.position.x : HitLineX) + mazeProbeOffsetX;
            float gapBottom = 0f;
            float gapTop = 0f;
            bool sampled = obstacleSpawner != null
                && obstacleSpawner.TrySampleMazeGap(probeX, out gapBottom, out gapTop, out _);
            if (!sampled && parallaxSystem != null)
            {
                float scrollPos = accumulatedWorldScrollPos;
                sampled = parallaxSystem.TrySampleMazeGap(probeX, now, scrollPos, out gapBottom, out gapTop, out _);
            }

            if (!sampled)
            {
                mazeOutOfBoundsAccumSec = 0f;
                return;
            }

            float y = playerController.CurrentY;
            if (obstacleSpawner != null && obstacleSpawner.CheckDangerCollision(probeX, y, 0.14f, out string dangerReason))
            {
                FailRun(dangerReason);
                return;
            }

            float margin = Mathf.Max(0.04f, mazeWallMarginY);
            float safeBottom = gapBottom + margin;
            float safeTop = gapTop - margin;
            if (safeTop <= safeBottom)
            {
                float center = (gapTop + gapBottom) * 0.5f;
                safeBottom = center - 0.90f;
                safeTop = center + 0.90f;
            }

            if (y < safeBottom || y > safeTop)
            {
                float overshoot = y < safeBottom ? (safeBottom - y) : (y - safeTop);
                float hardOvershoot = Mathf.Max(0.12f, mazeHardOvershootY);
                float pressure01 = Mathf.InverseLerp(0.02f, hardOvershoot, overshoot);
                float pressure = Mathf.Lerp(0.65f, 2.2f, pressure01);
                mazeOutOfBoundsAccumSec += dt * pressure;

                float grace = Mathf.Max(0.06f, mazeOutOfBoundsGraceSec);
                bool hardBreach = overshoot >= hardOvershoot && mazeOutOfBoundsAccumSec >= grace * 0.55f;
                if (hardBreach || mazeOutOfBoundsAccumSec >= grace)
                {
                    string boundary = y < safeBottom ? "floor" : "ceiling";
                    FailRun($"Maze {boundary} collision ({overshoot:F2}u)");
                }
                return;
            }

            mazeOutOfBoundsAccumSec = Mathf.Max(0f, mazeOutOfBoundsAccumSec - (dt * 3.1f));
        }

        private void AlignPlayerToMazeGap(float songTimeSec, bool forceCenter)
        {
            if (playerController == null || playerController.Mode != PlayerController.MovementMode.Glide)
            {
                return;
            }

            float probeX = (playerTransform != null ? playerTransform.position.x : HitLineX) + mazeProbeOffsetX;
            float gapBottom = 0f;
            float gapTop = 0f;
            bool sampled = obstacleSpawner != null
                && obstacleSpawner.TrySampleMazeGap(probeX, out gapBottom, out gapTop, out _);
            if (!sampled && parallaxSystem != null)
            {
                float scrollPos = accumulatedWorldScrollPos;
                sampled = parallaxSystem.TrySampleMazeGap(probeX, songTimeSec, scrollPos, out gapBottom, out gapTop, out _);
            }

            if (!sampled)
            {
                return;
            }

            float margin = Mathf.Max(0.04f, mazeWallMarginY);
            float safeBottom = gapBottom + margin;
            float safeTop = gapTop - margin;
            if (safeTop <= safeBottom)
            {
                float center = (gapTop + gapBottom) * 0.5f;
                safeBottom = center - 0.95f;
                safeTop = center + 0.95f;
            }

            float centerY = (safeBottom + safeTop) * 0.5f;
            float targetY = forceCenter
                ? centerY
                : Mathf.Clamp(playerController.CurrentY, safeBottom + 0.06f, safeTop - 0.06f);
            targetY = Mathf.Clamp(targetY, safeBottom + 0.04f, safeTop - 0.04f);
            playerController.SetGlideY(targetY);
        }

        private static bool IntersectsWindow(float t0, float t1, float start, float end)
        {
            float a = Mathf.Min(t0, t1);
            float b = Mathf.Max(t0, t1);
            return b >= start && a <= end;
        }

        private HazardEnvelope ResolveHazardEnvelope(GameplayPatternEvent evt, ObstacleSpawner.SpawnDirective hazard, float nowSec)
        {
            int lane = Mathf.Clamp(evt != null ? evt.lane : 0, 0, 1);
            float laneCenter = ResolveLaneY(lane);
            float centerY = laneCenter;
            float halfHeight = 0.22f;
            string archetype = evt != null && !string.IsNullOrWhiteSpace(evt.archetype)
                ? evt.archetype
                : GameplayArchetypes.LaneBlock;
            float travel = Mathf.Max(0.08f, hazard.TravelTimeSec);
            float approach01 = Mathf.Clamp01((nowSec - hazard.SpawnTimeSec) / travel);

            if (string.Equals(archetype, GameplayArchetypes.AccentCrusher, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.28f;
            }
            else if (string.Equals(archetype, GameplayArchetypes.AlternatorPair, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.24f;
                centerY += Mathf.Sin((nowSec - hazard.SpawnTimeSec) * 8.5f) * Mathf.Lerp(0.16f, 0.04f, approach01);
            }
            else if (string.Equals(archetype, GameplayArchetypes.StreakBreaker, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.26f;
                centerY += Mathf.Sin((nowSec * 5.2f) + (hazard.Seed * 0.021f)) * Mathf.Lerp(0.14f, 0.03f, approach01);
            }
            else if (string.Equals(archetype, GameplayArchetypes.CrossGate, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.30f;
                float laneSign = lane == 0 ? -1f : 1f;
                centerY += laneSign * Mathf.Sin((1f - approach01) * Mathf.PI) * 0.22f;
            }
            else if (string.Equals(archetype, GameplayArchetypes.HoldLaneLock, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(archetype, GameplayArchetypes.HoldReleaseGate, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.34f;
            }
            else if (string.Equals(archetype, GameplayArchetypes.OffbeatSnap, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.20f;
            }
            else if (string.Equals(archetype, GameplayArchetypes.SpinnerSentinel, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.32f;
                centerY += Mathf.Sin((nowSec * 5.4f) + (hazard.Seed * 0.019f)) * Mathf.Lerp(0.20f, 0.05f, approach01);
            }
            else if (string.Equals(archetype, GameplayArchetypes.RisingWall, StringComparison.OrdinalIgnoreCase))
            {
                halfHeight = 0.36f;
                centerY = laneCenter - Mathf.Lerp(0.58f, 0.0f, approach01);
            }

            return new HazardEnvelope(centerY, halfHeight);
        }

        private bool IsPlayerInsideEnvelope(HazardEnvelope envelope, float extraToleranceY)
        {
            if (playerController == null)
            {
                return false;
            }

            if (playerController.Mode == PlayerController.MovementMode.LaneSwitch)
            {
                float laneY = ResolveLaneY(Mathf.Clamp(playerController.LaneIndex, 0, 1));
                return Mathf.Abs(laneY - envelope.CenterY) <= envelope.HalfHeight + (extraToleranceY * 0.25f);
            }

            float y = playerController.CurrentY;
            float tolerance = envelope.HalfHeight + Mathf.Max(0.04f, extraToleranceY);
            return Mathf.Abs(y - envelope.CenterY) <= tolerance;
        }

        private float ResolveLaneY(int lane)
        {
            return lane <= 0 ? lowerLaneY : upperLaneY;
        }

        private void ApplySectionProfile(float songTimeSec, bool force)
        {
            if (!useJumpOnlyLevelDesign || levelOrchestration == null)
            {
                return;
            }

            SectionPlan section = levelOrchestration.ResolveSectionAt(songTimeSec);
            if (section == null)
            {
                return;
            }

            bool sectionChanged = force || section.sectionIndex != activeSectionIndex;
            activeSectionPlan = section;
            if (!sectionChanged)
            {
                return;
            }

            activeSectionIndex = section.sectionIndex;
            MovementProfile profile = levelOrchestration.ResolveProfile(section.movementProfileRef);
            if (profile == null)
            {
                return;
            }

            activeMovementProfile = profile;
            currentScrollUnitsPerSec = Mathf.Max(2f, profile.ScrollUnitsPerSec);
            playerController?.ApplyMovementProfile(profile);
        }

        private void TickSectionState(float now)
        {
            if (useJumpOnlyLevelDesign && levelOrchestration != null)
            {
                SectionPlan section = levelOrchestration.ResolveSectionAt(now);
                if (section != null)
                {
                    currentSectionState = LevelOrchestrator.ToLegacySectionType(section.sectionType);
                    currentStrain = Mathf.Clamp01(section.densityTarget);
                    targetStrain = Mathf.Clamp01(section.difficultyRamp);
                    currentPresetId = section.movementProfileRef;
                    return;
                }
            }

            currentSectionState = "Active";
            currentStrain = 0f;
            targetStrain = 0f;
            currentPresetId = "";

            GameplayPatternSectionInfo[] sections = activePattern.sections ?? Array.Empty<GameplayPatternSectionInfo>();
            for (int i = 0; i < sections.Length; i++)
            {
                GameplayPatternSectionInfo section = sections[i];
                if (section == null || section.endSec < section.startSec)
                {
                    continue;
                }

                if (now >= section.startSec && now <= section.endSec)
                {
                    currentSectionState = section.sectionType;
                    currentStrain = section.currentStrain;
                    targetStrain = section.targetStrain;
                    currentPresetId = section.presetId;
                    return;
                }
            }

            RestSectionEvent[] restSections = activePattern.restSections ?? Array.Empty<RestSectionEvent>();
            for (int i = 0; i < restSections.Length; i++)
            {
                if (now >= restSections[i].startSec && now <= restSections[i].endSec)
                {
                    currentSectionState = "Rest";
                    currentStrain = 0.12f;
                    targetStrain = 0.15f;
                    currentPresetId = "REST_RESET_2TO4S";
                    break;
                }
            }
        }

        private void TickCameraShift(float now)
        {
            float shift = 0f;
            for (int i = 0; i < cameraShiftEvents.Length; i++)
            {
                GameplayPatternEvent evt = cameraShiftEvents[i];
                float start = evt.hitTimeSec;
                float duration = Mathf.Max(0.14f, evt.endTimeSec - evt.hitTimeSec);
                float end = start + duration;
                if (now < start || now > end)
                {
                    continue;
                }

                float u = Mathf.Clamp01((now - start) / duration);
                float envelope = Mathf.Sin(u * Mathf.PI);
                shift += envelope * Mathf.Lerp(0.14f, 0.42f, evt.intensity);
            }

            worldShiftX = shift;
            ApplyWorldShift();
        }

        private void ApplyWorldShift()
        {
            if (worldRoot != null)
            {
                worldRoot.localPosition = new Vector3(
                    SnapToPixel(baseWorldPosition.x + worldShiftX),
                    SnapToPixel(baseWorldPosition.y),
                    baseWorldPosition.z);
            }

            if (targetCamera != null)
            {
                targetCamera.transform.position = new Vector3(
                    SnapToPixel(baseCameraPosition.x + (worldShiftX * 0.08f)),
                    SnapToPixel(baseCameraPosition.y),
                    baseCameraPosition.z);
            }
        }

        private void ResetWorldShift()
        {
            worldShiftX = 0f;
            ApplyWorldShift();
        }

        private void TransitionTo(RunState newState)
        {
            state = newState;
            if (newState != RunState.Playing)
            {
                ResetWorldShift();
            }
            RunStateChanged?.Invoke(newState);
        }

        private void PlacePlayerAtLeftThird()
        {
            if (targetCamera == null || playerTransform == null)
            {
                return;
            }

            targetCamera.orthographic = true;
            targetCamera.allowMSAA = false;
            targetCamera.allowHDR = false;
            if (targetCamera.orthographicSize <= 0.01f)
            {
                targetCamera.orthographicSize = 5.8f;
            }

            if (QualitySettings.antiAliasing != 0)
            {
                QualitySettings.antiAliasing = 0;
            }

            Vector3 viewport = new Vector3(0.33f, 0.5f, Mathf.Abs(targetCamera.transform.position.z));
            Vector3 position = targetCamera.ViewportToWorldPoint(viewport);
            playerTransform.position = new Vector3(
                SnapToPixel(position.x),
                SnapToPixel(playerTransform.position.y),
                0f);
        }

        private void CacheBasePositions()
        {
            baseWorldPosition = worldRoot != null ? worldRoot.localPosition : Vector3.zero;
            baseCameraPosition = targetCamera != null ? targetCamera.transform.position : Vector3.zero;
            baseWorldPosition.x = SnapToPixel(baseWorldPosition.x);
            baseWorldPosition.y = SnapToPixel(baseWorldPosition.y);
            baseCameraPosition.x = SnapToPixel(baseCameraPosition.x);
            baseCameraPosition.y = SnapToPixel(baseCameraPosition.y);
        }

        private static float SnapToPixel(float value)
        {
            float ppu = Mathf.Max(1f, PixelSnapPpu);
            return Mathf.Round(value * ppu) / ppu;
        }

        private static float ResolveTimelineStartSec(BeatMap source, MusicTrackEntry entry)
        {
            float startSec = 0f;
            if (entry != null && entry.startTrimSec > 0.01f)
            {
                startSec = entry.startTrimSec;
            }
            else if (source != null && source.offsetSec > 0.01f)
            {
                startSec = source.offsetSec;
            }

            if (source?.events != null && source.events.Length > 0)
            {
                float firstEventSec = float.MaxValue;
                for (int i = 0; i < source.events.Length; i++)
                {
                    BeatEvent evt = source.events[i];
                    if (evt == null)
                    {
                        continue;
                    }

                    firstEventSec = Mathf.Min(firstEventSec, evt.timeSec);
                }

                if (firstEventSec < float.MaxValue)
                {
                    if (startSec <= 0.01f)
                    {
                        startSec = firstEventSec;
                    }
                    else
                    {
                        startSec = Mathf.Min(startSec, firstEventSec);
                    }
                }
            }

            return Mathf.Max(0f, startSec);
        }

        private static BeatMap BuildRuntimeBeatMap(BeatMap source, float timelineStartSec)
        {
            if (source == null)
            {
                return new BeatMap();
            }

            BeatEvent[] canonical = BeatMapEventUtils.GetCanonicalEvents(source);
            RestSectionEvent[] restSections = BeatMapEventUtils.GetRestSections(source);

            return new BeatMap
            {
                schemaVersion = source.schemaVersion,
                trackId = source.trackId,
                bpm = source.bpm,
                offsetSec = 0f,
                seed = source.seed,
                sections = ShiftSections(source.sections, timelineStartSec),
                restSections = ShiftRestSections(restSections, timelineStartSec),
                events = ShiftEvents(canonical, timelineStartSec)
            };
        }

        private static BeatSection[] ShiftSections(BeatSection[] sections, float shiftSec)
        {
            if (sections == null || sections.Length == 0)
            {
                return Array.Empty<BeatSection>();
            }

            var output = new List<BeatSection>(sections.Length);
            for (int i = 0; i < sections.Length; i++)
            {
                BeatSection section = sections[i];
                if (section == null)
                {
                    continue;
                }

                float start = section.startSec - shiftSec;
                float end = section.endSec - shiftSec;
                if (end <= 0f)
                {
                    continue;
                }

                output.Add(new BeatSection
                {
                    type = section.type,
                    startSec = Mathf.Max(0f, start),
                    endSec = Mathf.Max(0f, end),
                    density = section.density,
                    intensity = section.intensity
                });
            }

            return output.ToArray();
        }

        private static RestSectionEvent[] ShiftRestSections(RestSectionEvent[] sections, float shiftSec)
        {
            if (sections == null || sections.Length == 0)
            {
                return Array.Empty<RestSectionEvent>();
            }

            var output = new List<RestSectionEvent>(sections.Length);
            for (int i = 0; i < sections.Length; i++)
            {
                RestSectionEvent section = sections[i];
                if (section == null)
                {
                    continue;
                }

                float start = section.startSec - shiftSec;
                float end = section.endSec - shiftSec;
                if (end <= 0f)
                {
                    continue;
                }

                output.Add(new RestSectionEvent
                {
                    startSec = Mathf.Max(0f, start),
                    endSec = Mathf.Max(0f, end)
                });
            }

            return output.ToArray();
        }

        private static BeatEvent[] ShiftEvents(BeatEvent[] events, float shiftSec)
        {
            if (events == null || events.Length == 0)
            {
                return Array.Empty<BeatEvent>();
            }

            var output = new List<BeatEvent>(events.Length);
            for (int i = 0; i < events.Length; i++)
            {
                BeatEvent evt = events[i];
                if (evt == null)
                {
                    continue;
                }

                float start = evt.timeSec - shiftSec;
                float end = evt.GetEndTimeSec() - shiftSec;
                if (end <= -0.001f)
                {
                    continue;
                }

                float clampedStart = Mathf.Max(0f, start);
                float clampedEnd = Mathf.Max(clampedStart, end);
                output.Add(new BeatEvent
                {
                    timeSec = clampedStart,
                    endTimeSec = clampedEnd,
                    durationSec = Mathf.Max(0f, clampedEnd - clampedStart),
                    lane = evt.lane,
                    laneTo = evt.laneTo,
                    kind = evt.kind,
                    intensity = evt.intensity,
                    prefabId = evt.prefabId,
                    motion = evt.motion
                });
            }

            return output
                .OrderBy(e => e.timeSec)
                .ToArray();
        }

        private static BeatEvent[] BuildJudgeEvents(GameplayPattern pattern)
        {
            GameplayTapScheduleEvent[] schedule = pattern?.tapSchedule;
            if (schedule != null && schedule.Length > 0)
            {
                List<BeatEvent> scheduleEvents = new List<BeatEvent>(schedule.Length);
                GameplayTapScheduleEvent[] ordered = schedule
                    .Where(e => e != null)
                    .OrderBy(e => e.timeSec)
                    .ToArray();
                for (int i = 0; i < ordered.Length; i++)
                {
                    GameplayTapScheduleEvent entry = ordered[i];
                    scheduleEvents.Add(new BeatEvent
                    {
                        timeSec = entry.timeSec,
                        endTimeSec = entry.timeSec,
                        lane = Mathf.Clamp(entry.laneTo, 0, 1),
                        laneTo = Mathf.Clamp(entry.laneTo, 0, 1),
                        kind = BeatKinds.Tap,
                        intensity = 1f,
                        prefabId = "switch_tap"
                    });
                }

                return scheduleEvents.ToArray();
            }

            if (pattern?.events == null)
            {
                return Array.Empty<BeatEvent>();
            }

            List<BeatEvent> judgeEvents = new List<BeatEvent>();
            GameplayPatternEvent[] sorted = pattern.events
                .Where(e =>
                    e != null
                    && e.isHazard
                    && string.Equals(e.kind, GameplayPatternKinds.Jump, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(e.sourceKind, BeatKinds.Tap, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.sourceKind, BeatKinds.Accent, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(e => e.hitTimeSec)
                .ToArray();

            for (int i = 0; i < sorted.Length; i++)
            {
                GameplayPatternEvent evt = sorted[i];
                judgeEvents.Add(new BeatEvent
                {
                    timeSec = evt.hitTimeSec,
                    endTimeSec = evt.hitTimeSec,
                    lane = evt.lane,
                    kind = string.Equals(evt.sourceKind, BeatKinds.Accent, StringComparison.OrdinalIgnoreCase)
                        ? BeatKinds.Accent
                        : BeatKinds.Tap,
                    intensity = evt.intensity,
                    prefabId = "tap_basic"
                });
            }

            return judgeEvents.ToArray();
        }

        private float? GetNextTapHazardHitTime(float now)
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

                if (evt.timeSec >= now - 0.01f)
                {
                    return evt.timeSec;
                }
            }

            return null;
        }

        private bool TryGetTelegraph(float now, out int lane, out float leadSec, out float lead01)
        {
            lane = -1;
            leadSec = float.PositiveInfinity;
            lead01 = 0f;
            float leadWindowSec = 0f;
            if (obstacleSpawner == null)
            {
                return false;
            }

            IReadOnlyList<ObstacleSpawner.SpawnDirective> directives = obstacleSpawner.Directives;
            for (int i = 0; i < directives.Count; i++)
            {
                if (resolvedHazards.Contains(i))
                {
                    continue;
                }

                ObstacleSpawner.SpawnDirective directive = directives[i];
                GameplayPatternEvent evt = directive.PatternEvent;
                if (evt == null || !evt.isHazard)
                {
                    continue;
                }

                if (directive.HitTimeSec < now - 0.01f)
                {
                    continue;
                }

                float lead = directive.HitTimeSec - now;
                if (lead < leadSec)
                {
                    leadSec = lead;
                    lane = Mathf.Clamp(evt.lane, 0, 1);
                    leadWindowSec = directive.TelegraphLeadSec > 0f
                        ? directive.TelegraphLeadSec
                        : ObstacleSpecs.ResolveTelegraphLeadSec(evt, beatGrid.BeatSec);
                }
            }

            if (lane < 0)
            {
                return false;
            }

            leadWindowSec = Mathf.Max(0.01f, leadWindowSec);
            lead01 = 1f - Mathf.Clamp01(leadSec / leadWindowSec);
            return leadSec <= leadWindowSec;
        }

        private string BuildNextHazardsDebugLine()
        {
            IReadOnlyList<ObstacleSpawner.SpawnDirective> directives = obstacleSpawner != null
                ? obstacleSpawner.Directives
                : Array.Empty<ObstacleSpawner.SpawnDirective>();
            int added = 0;
            var preview = new List<string>(3);
            for (int i = 0; i < directives.Count && added < 3; i++)
            {
                if (resolvedHazards.Contains(i))
                {
                    continue;
                }

                ObstacleSpawner.SpawnDirective directive = directives[i];
                GameplayPatternEvent evt = directive.PatternEvent;
                if (evt == null || !evt.isHazard || directive.HitTimeSec < SongTimeSec - 0.05f)
                {
                    continue;
                }

                string archetype = string.IsNullOrWhiteSpace(evt.archetype) ? GameplayArchetypes.LaneBlock : evt.archetype;
                string approach = ObstacleSpecs.ResolvePresentation(evt);
                float teleLead = directive.TelegraphLeadSec > 0f
                    ? directive.TelegraphLeadSec
                    : ObstacleSpecs.ResolveTelegraphLeadSec(evt, beatGrid.BeatSec);
                preview.Add($"{directive.HitTimeSec:F2}s L{evt.lane} {archetype} tr:{directive.TravelTimeSec:F2} tel:{teleLead:F2} st:{approach}");
                added++;
            }

            return preview.Count == 0 ? "-" : string.Join(" | ", preview);
        }

        private void AnalyzeMazeFlowSync()
        {
            mazeSyncAvgCueOffsetMs = 0f;
            mazeSyncMaxCueOffsetMs = 0f;
            mazeSyncOffGridCueCount = 0;
            mazeSyncExpectedTapCount = 0;
            mazeSyncAvgTapGapSec = 0f;
            mazeSyncMaxTapGapSec = 0f;
            mazeSyncFirstExpectedTapSec = 0f;
            mazeSyncUnalignedSectionCount = 0;
            mazeSyncAnalyzed = false;

            if (!useJumpOnlyLevelDesign || mazeBeatCues == null || mazeBeatCues.Length == 0)
            {
                return;
            }

            float beatSec = Mathf.Max(0.0001f, beatGrid.BeatSec);
            float sumCueOffsetMs = 0f;
            int cueCount = 0;
            float sumTapGapSec = 0f;
            int tapGapCount = 0;
            float lastExpectedTapSec = -1f;
            float firstExpectedTapSec = -1f;

            for (int i = 0; i < mazeBeatCues.Length; i++)
            {
                MazeBeatCue cue = mazeBeatCues[i];
                if (cue == null)
                {
                    continue;
                }

                cueCount++;
                float nearestBeatSec = Mathf.Round(cue.timeSec / beatSec) * beatSec;
                float offsetMs = Mathf.Abs((cue.timeSec - nearestBeatSec) * 1000f);
                sumCueOffsetMs += offsetMs;
                mazeSyncMaxCueOffsetMs = Mathf.Max(mazeSyncMaxCueOffsetMs, offsetMs);
                if (offsetMs > 6f)
                {
                    mazeSyncOffGridCueCount++;
                }

                if (!cue.expectedTap)
                {
                    continue;
                }

                mazeSyncExpectedTapCount++;
                if (firstExpectedTapSec < 0f)
                {
                    firstExpectedTapSec = cue.timeSec;
                }

                if (lastExpectedTapSec >= 0f)
                {
                    float gapSec = cue.timeSec - lastExpectedTapSec;
                    sumTapGapSec += gapSec;
                    tapGapCount++;
                    mazeSyncMaxTapGapSec = Mathf.Max(mazeSyncMaxTapGapSec, gapSec);
                }

                lastExpectedTapSec = cue.timeSec;
            }

            mazeSyncAvgCueOffsetMs = cueCount > 0 ? sumCueOffsetMs / cueCount : 0f;
            mazeSyncAvgTapGapSec = tapGapCount > 0 ? sumTapGapSec / tapGapCount : 0f;
            mazeSyncFirstExpectedTapSec = firstExpectedTapSec >= 0f ? firstExpectedTapSec : 0f;

            SectionPlan[] sections = levelOrchestration != null ? levelOrchestration.sections : null;
            if (sections != null && sections.Length > 0)
            {
                for (int i = 0; i < sections.Length; i++)
                {
                    SectionPlan section = sections[i];
                    if (section == null)
                    {
                        continue;
                    }

                    float startBeat = section.startSec / beatSec;
                    float endBeat = section.endSec / beatSec;
                    if (Mathf.Abs(startBeat - Mathf.Round(startBeat)) > 0.05f
                        || Mathf.Abs(endBeat - Mathf.Round(endBeat)) > 0.05f)
                    {
                        mazeSyncUnalignedSectionCount++;
                    }
                }
            }

            mazeSyncAnalyzed = true;
            Debug.Log(
                $"[ZebraDash][Sync] track={activeTrackId} cues={cueCount} expectedTap={mazeSyncExpectedTapCount} " +
                $"cueOffsetMs(avg/max)={mazeSyncAvgCueOffsetMs:F2}/{mazeSyncMaxCueOffsetMs:F2} offGrid>6ms={mazeSyncOffGridCueCount} " +
                $"tapGapSec(avg/max)={mazeSyncAvgTapGapSec:F2}/{mazeSyncMaxTapGapSec:F2} firstTap={mazeSyncFirstExpectedTapSec:F2}s " +
                $"sectionBeatBoundaryIssues={mazeSyncUnalignedSectionCount}");
        }

        private string BuildMazeSyncDebugLine()
        {
            if (!mazeSyncAnalyzed)
            {
                return "Sync: pending";
            }

            if (!useJumpOnlyLevelDesign)
            {
                return "Sync: dynamic-pattern mode";
            }

            return $"Sync maze cueOff(avg/max):{mazeSyncAvgCueOffsetMs:F1}/{mazeSyncMaxCueOffsetMs:F1}ms " +
                   $"offGrid:{mazeSyncOffGridCueCount} taps:{mazeSyncExpectedTapCount} " +
                   $"tapGap(avg/max):{mazeSyncAvgTapGapSec:F2}/{mazeSyncMaxTapGapSec:F2}s firstTap:{mazeSyncFirstExpectedTapSec:F2}s " +
                   $"sectionBeatIssues:{mazeSyncUnalignedSectionCount}";
        }

        private string BuildCurrentBarHazardMask()
        {
            if (obstacleSpawner == null || beatGrid.BeatSec <= 0.0001f)
            {
                return "0000000000000000";
            }

            float now = SongTimeSec;
            int barIndex = beatGrid.BarIndex(now);
            float barStart = barIndex * beatGrid.BarSec;
            float subSec = beatGrid.SubSec;
            int mask = 0;
            IReadOnlyList<ObstacleSpawner.SpawnDirective> directives = obstacleSpawner.Directives;
            for (int i = 0; i < directives.Count; i++)
            {
                GameplayPatternEvent evt = directives[i].PatternEvent;
                if (evt == null || !evt.isHazard)
                {
                    continue;
                }

                float hit = directives[i].HitTimeSec;
                if (hit < barStart || hit >= barStart + beatGrid.BarSec)
                {
                    continue;
                }

                int slot = Mathf.Clamp(Mathf.FloorToInt((hit - barStart) / Mathf.Max(0.0001f, subSec)), 0, 15);
                mask |= (1 << slot);
            }

            char[] bits = new char[16];
            for (int i = 0; i < 16; i++)
            {
                bits[15 - i] = (mask & (1 << i)) != 0 ? '1' : '0';
            }

            return new string(bits);
        }

        private string BuildCurrentGridDebugLine()
        {
            GameplayGridDebugBar[] bars = activePattern?.gridDebugBars;
            if (bars == null || bars.Length == 0)
            {
                return "Grid: -";
            }

            int currentBar = CurrentBar;
            GameplayGridDebugBar chosen = null;
            for (int i = 0; i < bars.Length; i++)
            {
                GameplayGridDebugBar bar = bars[i];
                if (bar == null)
                {
                    continue;
                }

                if (bar.barIndex == currentBar)
                {
                    chosen = bar;
                    break;
                }

                if (bar.barIndex <= currentBar && (chosen == null || bar.barIndex > chosen.barIndex))
                {
                    chosen = bar;
                }
            }

            if (chosen == null)
            {
                chosen = bars[0];
            }

            return $"Grid b{chosen.barIndex} {chosen.sectionType} mask:{chosen.hazardMask16} lanes:{chosen.lanePlan} " +
                   $"k:{chosen.hazardTarget} sw:{chosen.switchTarget} E:{chosen.averageEnergy:F2} target:{chosen.targetStrain:F2} fb:{chosen.fallbackStep} {chosen.presetId}";
        }

        private string BuildTwoBarPlanDebug()
        {
            int currentBar = CurrentBar;
            string first = BuildBarPlanDebug(currentBar);
            string second = BuildBarPlanDebug(currentBar + 1);
            return $"{first} || {second}";
        }

        private string BuildBarPlanDebug(int barIndex)
        {
            GameplayGridDebugBar grid = FindGridDebugBar(barIndex);
            string lanePlan = grid != null ? grid.lanePlan : "----";
            string hazardMask = grid != null ? grid.hazardMask16 : "0000000000000000";
            string tapMask = BuildTapMask4(barIndex);
            string hazardLane = BuildHazardLaneMask4(barIndex);
            string fallback = grid != null ? grid.fallbackStep : "-";
            string preset = grid != null ? grid.presetId : "-";
            int beatStart = barIndex * 4;
            float t0 = barIndex * beatGrid.BarSec;
            return $"b{barIndex} beat[{beatStart}-{beatStart + 3}] t0:{t0:F2} lanes:{lanePlan} taps:{tapMask} haz:{hazardMask} hLane:{hazardLane} fb:{fallback} {preset}";
        }

        private string BuildNextHazardTimingDebugLine()
        {
            IReadOnlyList<ObstacleSpawner.SpawnDirective> directives = obstacleSpawner != null
                ? obstacleSpawner.Directives
                : Array.Empty<ObstacleSpawner.SpawnDirective>();
            float beatSec = Mathf.Max(0.0001f, beatGrid.BeatSec);
            float minVisibleSec = Mathf.Clamp(0.90f * beatSec, 0.45f, 0.85f);
            var preview = new List<string>(3);
            for (int i = 0; i < directives.Count && preview.Count < 3; i++)
            {
                if (resolvedHazards.Contains(i))
                {
                    continue;
                }

                ObstacleSpawner.SpawnDirective directive = directives[i];
                GameplayPatternEvent evt = directive.PatternEvent;
                if (evt == null || !evt.isHazard || directive.HitTimeSec < SongTimeSec - 0.05f)
                {
                    continue;
                }

                bool minOk = directive.TravelTimeSec >= minVisibleSec - 0.0001f;
                float teleLead = directive.TelegraphLeadSec > 0f
                    ? directive.TelegraphLeadSec
                    : ObstacleSpecs.ResolveTelegraphLeadSec(evt, beatSec);
                ObstacleSpec spec = ObstacleSpecs.Resolve(evt.archetype);
                string approach = string.IsNullOrWhiteSpace(evt.presentation) ? GameplayPresentationKinds.Straight : evt.presentation;
                preview.Add($"t:{directive.HitTimeSec:F2} L{evt.lane} tr:{directive.TravelTimeSec:F2} tel:{teleLead:F2} min:{(minOk ? "ok" : "low")} app:{approach} rule:{spec.GameplayRule}");
            }

            return preview.Count == 0 ? "-" : string.Join(" | ", preview);
        }

        private GameplayGridDebugBar FindGridDebugBar(int barIndex)
        {
            GameplayGridDebugBar[] bars = activePattern?.gridDebugBars;
            if (bars == null || bars.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < bars.Length; i++)
            {
                if (bars[i] != null && bars[i].barIndex == barIndex)
                {
                    return bars[i];
                }
            }

            return null;
        }

        private string BuildTapMask4(int barIndex)
        {
            char[] mask = { '0', '0', '0', '0' };
            for (int i = 0; i < tapScheduleEntries.Length; i++)
            {
                GameplayTapScheduleEvent entry = tapScheduleEntries[i];
                if (entry == null || entry.barIndex != barIndex)
                {
                    continue;
                }

                int beat = Mathf.Clamp(entry.beatInBar, 0, 3);
                mask[beat] = '1';
            }

            return new string(mask);
        }

        private string BuildHazardLaneMask4(int barIndex)
        {
            char[] mask = { '-', '-', '-', '-' };
            IReadOnlyList<ObstacleSpawner.SpawnDirective> directives = obstacleSpawner != null
                ? obstacleSpawner.Directives
                : Array.Empty<ObstacleSpawner.SpawnDirective>();
            float barStart = barIndex * beatGrid.BarSec;
            float barEnd = barStart + beatGrid.BarSec;
            float beatSec = Mathf.Max(0.0001f, beatGrid.BeatSec);
            for (int i = 0; i < directives.Count; i++)
            {
                GameplayPatternEvent evt = directives[i].PatternEvent;
                if (evt == null || !evt.isHazard)
                {
                    continue;
                }

                float hit = directives[i].HitTimeSec;
                if (hit < barStart || hit >= barEnd)
                {
                    continue;
                }

                int beat = Mathf.Clamp(Mathf.FloorToInt((hit - barStart) / beatSec), 0, 3);
                char lane = evt.lane <= 0 ? '0' : '1';
                if (mask[beat] == '-')
                {
                    mask[beat] = lane;
                }
                else if (mask[beat] != lane)
                {
                    mask[beat] = '*';
                }
            }

            return new string(mask);
        }

        private int ResolveInitialLane(GameplayPattern pattern)
        {
            if (pattern?.events == null || pattern.events.Length == 0)
            {
                return 0;
            }

            float safeWindowSec = Mathf.Max(1.20f, introCollisionGraceSec + 0.15f);
            for (int i = 0; i < pattern.events.Length; i++)
            {
                GameplayPatternEvent evt = pattern.events[i];
                if (evt == null || !evt.isHazard)
                {
                    continue;
                }

                if (evt.hitTimeSec < 0f)
                {
                    continue;
                }

                int hazardLane = Mathf.Clamp(evt.lane, 0, 1);
                if (evt.hitTimeSec <= safeWindowSec)
                {
                    return 1 - hazardLane;
                }

                break;
            }

            return 0;
        }

        private static bool IsPunishSection(string sectionType)
        {
            return string.Equals(sectionType, GameplaySectionTypes.Active, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase);
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

        private static float LoadDeviceOffsetSec()
        {
            return PlayerPrefs.GetFloat(DeviceOffsetPrefsKey, 0f);
        }
    }
}
