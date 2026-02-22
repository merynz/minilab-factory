using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string DeviceOffsetPrefsKey = "minilab.rhythm.device_offset_sec";

        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Transform worldRoot;
        [SerializeField] private BeatClock beatClock;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private ObstacleSpawner obstacleSpawner;
        [SerializeField] private PlayerController playerController;
        [SerializeField, Range(2, 4)] private int countdownBeats = 3;
        [SerializeField] private float collisionWindowSec = 0.07f;

        private RunState state = RunState.Idle;
        private float countdownRemaining;
        private float levelDurationSec;
        private string activeTrackId = "";
        private BeatMap activeBeatMap;
        private BeatMap runtimeBeatMap;
        private GameplayPattern activePattern = new GameplayPattern();
        private float activeOffsetSec;
        private float timelineStartSec;
        private float audioStartSec;
        private string failReason = "";
        private bool restartRequested;
        private bool pauseRequested;
        private float worldShiftX;
        private Vector3 baseWorldPosition;
        private Vector3 baseCameraPosition;

        private BeatEvent[] canonicalEvents = Array.Empty<BeatEvent>();
        private BeatEvent[] tapJudgeEvents = Array.Empty<BeatEvent>();
        private GameplayPatternEvent[] cameraShiftEvents = Array.Empty<GameplayPatternEvent>();
        private readonly HashSet<int> consumedTapIndices = new HashSet<int>();
        private readonly HashSet<int> missedTapIndices = new HashSet<int>();
        private readonly HashSet<int> resolvedHazards = new HashSet<int>();

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
        public float HitLineX => playerTransform != null ? playerTransform.position.x : -4f;
        public float OffsetMs => activeOffsetSec * 1000f;
        public float DeviceOffsetMs => inputJudge.DeviceOffsetSec * 1000f;
        public float Progress01 => levelDurationSec > 0f ? Mathf.Clamp01(SongTimeSec / levelDurationSec) : 0f;
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
                obstacleSpawner?.Tick(songTime);
                TickSectionState(songTime);
                TickHazards(songTime);
                TickAutoMiss(songTime);
                TickCameraShift(songTime);

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
            timelineStartSec = ResolveTimelineStartSec(activeBeatMap, trackEntry);
            audioStartSec = timelineStartSec;
            runtimeBeatMap = BuildRuntimeBeatMap(activeBeatMap, timelineStartSec);
            float explicitDurationSec = trackEntry != null && trackEntry.durationSec > 0f
                ? Mathf.Max(0f, trackEntry.durationSec - timelineStartSec)
                : 0f;
            levelDurationSec = explicitDurationSec > 0f
                ? explicitDurationSec
                : EstimateLevelDuration(runtimeBeatMap);

            canonicalEvents = BeatMapEventUtils.GetCanonicalEvents(runtimeBeatMap);
            activePattern = GameplayPatternGenerator.Build(runtimeBeatMap, activeTrackId, 1f);
            tapJudgeEvents = BuildJudgeEvents(activePattern);
            cameraShiftEvents = activePattern.events
                .Where(e => e != null && string.Equals(e.kind, GameplayPatternKinds.CameraShift, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.hitTimeSec)
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
            worldShiftX = 0f;
            ResetWorldShift();

            obstacleSpawner.Configure(activePattern);
            obstacleSpawner.ResetAll();

            inputJudge.SetDeviceOffset(LoadDeviceOffsetSec());
            playerController.InitializeLanes(-1.2f, 1.2f);
            playerController.SetLane(0);
            playerController.SetInputEnabled(false);

            float secondsPerBeat = 60f / Mathf.Max(1f, runtimeBeatMap != null ? runtimeBeatMap.bpm : activeBeatMap.bpm);
            countdownRemaining = secondsPerBeat * Mathf.Clamp(countdownBeats, 2, 4);
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
            if (audioSource != null && audioSource.clip != null && audioStartSec > 0.01f)
            {
                float maxStartSec = Mathf.Max(0f, audioSource.clip.length - 0.05f);
                audioSource.time = Mathf.Clamp(audioStartSec, 0f, maxStartSec);
            }

            float bpm = runtimeBeatMap != null ? runtimeBeatMap.bpm : activeBeatMap.bpm;
            beatClock.StartClock(audioSource, bpm, activeOffsetSec, activeTrackId);
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
                e => e != null && e.IsKind(BeatKinds.Tap));

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

                int lane = Mathf.Clamp(evt.lane, 0, 1);
                if (string.Equals(evt.kind, GameplayPatternKinds.HoldSlide, StringComparison.OrdinalIgnoreCase))
                {
                    float start = hazard.HitTimeSec - collisionWindowSec;
                    float end = hazard.EndTimeSec + collisionWindowSec;
                    if (now < start)
                    {
                        break;
                    }

                    if (now >= start && now <= end)
                    {
                        bool sameLane = playerController.LaneIndex == lane;
                        if (sameLane && !playerController.IsHolding)
                        {
                            FailRun("Hold required");
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

        private void TickSectionState(float now)
        {
            currentSectionState = "Active";
            RestSectionEvent[] restSections = activePattern.restSections ?? Array.Empty<RestSectionEvent>();
            for (int i = 0; i < restSections.Length; i++)
            {
                if (now >= restSections[i].startSec && now <= restSections[i].endSec)
                {
                    currentSectionState = "Rest";
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
                worldRoot.localPosition = new Vector3(baseWorldPosition.x + worldShiftX, baseWorldPosition.y, baseWorldPosition.z);
            }

            if (targetCamera != null)
            {
                targetCamera.transform.position = new Vector3(
                    baseCameraPosition.x + (worldShiftX * 0.08f),
                    baseCameraPosition.y,
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
            targetCamera.orthographicSize = 5f;
            Vector3 viewport = new Vector3(0.33f, 0.5f, Mathf.Abs(targetCamera.transform.position.z));
            Vector3 position = targetCamera.ViewportToWorldPoint(viewport);
            playerTransform.position = new Vector3(position.x, playerTransform.position.y, 0f);
        }

        private void CacheBasePositions()
        {
            baseWorldPosition = worldRoot != null ? worldRoot.localPosition : Vector3.zero;
            baseCameraPosition = targetCamera != null ? targetCamera.transform.position : Vector3.zero;
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
            if (pattern?.events == null)
            {
                return Array.Empty<BeatEvent>();
            }

            return pattern.events
                .Where(e => e != null && e.isHazard && string.Equals(e.kind, GameplayPatternKinds.Jump, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.hitTimeSec)
                .Select(e => new BeatEvent
                {
                    timeSec = e.hitTimeSec,
                    endTimeSec = e.hitTimeSec,
                    lane = e.lane,
                    kind = BeatKinds.Tap,
                    intensity = e.intensity,
                    prefabId = "tap_basic"
                })
                .ToArray();
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
