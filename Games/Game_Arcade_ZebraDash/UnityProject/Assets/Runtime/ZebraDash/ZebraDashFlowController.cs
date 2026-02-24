using System;
using System.Collections;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ZebraDash.Vfx;

namespace ZebraDash
{
    [Preserve]
    public sealed class ZebraDashFlowController : MonoBehaviour
    {
        private const string SceneBootstrapLegacy = "Bootstrap";
        private const string SceneBoot = "Boot";
        private const string SceneMainMenu = "MainMenu";
        private const string SceneLevelSelect = "LevelSelect";
        private const string SceneGameplay = "Gameplay";
        private const string SceneResults = "Results";
        private const string SceneWorkbench = "BeatmapWorkbench";

        private static ZebraDashFlowController instance;

        private MusicCatalog catalog = new MusicCatalog();
        private bool catalogLoaded;
        private string catalogError = "";

        private int selectedTrackIndex;
        private MusicTrackEntry selectedTrack;
        private BeatMap activeBeatMap;

        private Canvas canvas;
        private RectTransform uiRoot;
        private RectTransform safeAreaRoot;
        private Text titleText;
        private Text statusText;
        private Text hudText;
        private Text countdownText;
        private GameObject pausePanel;
        private Image pulseOverlay;
        private Image warpOverlay;
        private Rect lastSafeArea = new Rect(0f, 0f, -1f, -1f);
        private Vector2Int lastScreenSize = Vector2Int.zero;
        private bool safeAreaFallbackLogged;

        private GameObject runtimeRoot;
        private GameObject playerObject;
        private LevelRunner runner;
        private BeatClock beatClock;
        private AudioSource audioSource;
        private ParallaxSystem parallaxSystem;
        private Renderer safeZoneRenderer;
        private Renderer laneTelegraphLowerRenderer;
        private Renderer laneTelegraphUpperRenderer;
        private Renderer laneLowerRenderer;
        private Renderer laneUpperRenderer;
        private Renderer hitLineRenderer;

        private float screenPulse;
        private float warpPulse;
        private float resultsDelay;
        private bool resultSceneQueued;
        private readonly List<float> accentHitTimes = new List<float>();
        private int nextAccentIndex;
        private int lastSubVisualIndex = int.MinValue;
        private int lastBeatVisualIndex = int.MinValue;
        private int lastBarVisualIndex = int.MinValue;
        private int lastPhraseVisualIndex = int.MinValue;
        private bool debugOverlayVisible = true;

        private ResultSnapshot lastResult;

        [Serializable]
        private struct ResultSnapshot
        {
            public string TrackId;
            public bool Success;
            public string FailReason;
            public int Score;
            public int MaxCombo;
            public int Perfect;
            public int Good;
            public int Miss;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureFlowController()
        {
            Scene scene = SceneManager.GetActiveScene();
            string name = scene.name;
            bool managedScene = string.Equals(name, SceneBoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SceneBootstrapLegacy, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SceneMainMenu, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SceneLevelSelect, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SceneGameplay, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SceneResults, StringComparison.OrdinalIgnoreCase);

            if (!managedScene)
            {
                return;
            }

            if (FindObjectOfType<ZebraDashFlowController>() != null)
            {
                return;
            }

            GameObject go = new GameObject("ZebraDashFlowController");
            go.AddComponent<ZebraDashFlowController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            debugOverlayVisible = Application.isEditor;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            StartCoroutine(LoadCatalogIfNeeded());
            // sceneLoaded is not fired for the initial scene at app launch.
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            Debug.Log("[ZebraDashFlow] Start called. Initial scene processed.");
        }

        private void Update()
        {
            ApplySafeArea();

            TickPulseOverlay();

            if (Input.GetKeyDown(KeyCode.F3))
            {
                debugOverlayVisible = !debugOverlayVisible;
            }

            if (runner == null)
            {
                return;
            }

            TickLaneRailPulse();

            if (runner.State == RunState.Playing)
            {
                TickBeatGridVisuals();
                TickAccentPulse();
            }

            if (runner.State == RunState.Playing || runner.State == RunState.Paused)
            {
                if (runner.ConsumePauseRequest())
                {
                    TogglePause();
                }
            }

            if (hudText != null)
            {
                hudText.text = BuildHudLine();
                hudText.enabled = debugOverlayVisible;
            }

            if (countdownText != null)
            {
                countdownText.text = runner.State == RunState.Countdown
                    ? $"{Mathf.CeilToInt(runner.CountdownRemaining)}"
                    : string.Empty;
            }

            if (!resultSceneQueued && (runner.State == RunState.Failed || runner.State == RunState.Completed))
            {
                resultSceneQueued = true;
                resultsDelay = 0.35f;
                lastResult = new ResultSnapshot
                {
                    TrackId = selectedTrack != null ? selectedTrack.trackId : "unknown",
                    Success = runner.State == RunState.Completed,
                    FailReason = runner.State == RunState.Completed ? string.Empty : runner.FailReason,
                    Score = runner.Score,
                    MaxCombo = runner.MaxCombo,
                    Perfect = runner.PerfectCount,
                    Good = runner.GoodCount,
                    Miss = runner.MissCount
                };
            }

            if (resultSceneQueued)
            {
                resultsDelay -= Time.unscaledDeltaTime;
                if (resultsDelay <= 0f)
                {
                    resultSceneQueued = false;
                    LoadScene(SceneResults);
                }
            }
        }

        private void LateUpdate()
        {
            if (parallaxSystem == null)
            {
                return;
            }

            float songTime = runner != null ? runner.SongTimeSec : 0f;
            bool isPlaying = runner != null && runner.State == RunState.Playing;
            if (runner != null)
            {
                parallaxSystem.SetSectionMood(runner.CurrentSectionState, runner.CurrentStrain, runner.TargetStrain);
            }

            float phaseBeat = runner != null ? runner.PhaseBeat : 0f;
            float phaseBar = runner != null ? runner.PhaseBar : 0f;
            float worldScrollPos = runner != null ? runner.WorldScrollPos : 0f;
            float beatSec = runner != null ? runner.BeatSec : 0.5f;
            if (runner != null)
            {
                float energy = ResolveSectionEnergy(runner.CurrentSectionState, runner.CurrentStrain, runner.TargetStrain);
                float tension = ResolveSectionTension(runner.CurrentSectionState, runner.CurrentStrain, runner.TargetStrain);
                float brightness = ResolveSectionBrightness(runner.CurrentSectionState, energy, tension);
                parallaxSystem.SetOrchestrationState(
                    trackId: runner.ActiveTrackId,
                    patternSeed: runner.PatternSeed,
                    sectionType: runner.CurrentSectionState,
                    songTimeSec: songTime,
                    beatSec: beatSec,
                    energy: energy,
                    tension: tension,
                    brightness: brightness);
            }

            parallaxSystem.Tick(songTime, isPlaying, phaseBeat, phaseBar, worldScrollPos, beatSec);
        }

        private IEnumerator LoadCatalogIfNeeded()
        {
            if (catalogLoaded)
            {
                yield break;
            }

            MusicCatalog loaded = null;
            string error = "";
            yield return ZebraDashCatalogIo.LoadCatalogAsync((value, err) =>
            {
                loaded = value;
                error = err ?? "";
            });

            catalog = loaded ?? new MusicCatalog();
            catalogError = error;
            catalogLoaded = true;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[ZebraDashFlow] OnSceneLoaded: {scene.name} ({mode})");
            CleanupRuntimeSceneObjects();
            EnsureCanvasAndEventSystem();

            string sceneName = scene.name;
            if (string.Equals(sceneName, SceneBoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sceneName, SceneBootstrapLegacy, StringComparison.OrdinalIgnoreCase))
            {
                LoadScene(SceneMainMenu);
                return;
            }

            if (string.Equals(sceneName, SceneMainMenu, StringComparison.OrdinalIgnoreCase))
            {
                BuildMainMenu();
                return;
            }

            if (string.Equals(sceneName, SceneLevelSelect, StringComparison.OrdinalIgnoreCase))
            {
                BuildLevelSelect();
                return;
            }

            if (string.Equals(sceneName, SceneGameplay, StringComparison.OrdinalIgnoreCase))
            {
                BuildGameplay();
                return;
            }

            if (string.Equals(sceneName, SceneResults, StringComparison.OrdinalIgnoreCase))
            {
                BuildResults();
                return;
            }

            if (string.Equals(sceneName, SceneWorkbench, StringComparison.OrdinalIgnoreCase))
            {
                // Workbench owns itself.
                SetStatus("Workbench loaded.");
            }
        }

        private void BuildMainMenu()
        {
            SetBackdrop(new Color(0.06f, 0.08f, 0.14f, 1f));
            titleText = CreateLabel("ZebraDash", new Vector2(0.5f, 0.78f), 56, TextAnchor.MiddleCenter);
            CreateLabel("LANDSCAPE Beat Runner", new Vector2(0.5f, 0.70f), 24, TextAnchor.MiddleCenter);

            GameObject panel = CreatePanel(
                new Vector2(0.5f, 0.39f),
                new Vector2(920f, 420f),
                new Color(0.04f, 0.10f, 0.20f, 0.88f),
                SpaceMazeArtCatalog.ResolveUiPanelTile(11),
                tiled: true,
                name: "MainMenuPanel");
            RectTransform panelRoot = panel.transform as RectTransform;

            CreateButton("Play", panelRoot, new Vector2(0.5f, 0.78f), () => LoadScene(SceneLevelSelect), new Vector2(620f, 74f));
            CreateButton("Workbench", panelRoot, new Vector2(0.5f, 0.58f), () => LoadScene(SceneWorkbench), new Vector2(620f, 74f));
            CreateButton("Settings (Tap->Sync)", panelRoot, new Vector2(0.5f, 0.38f), () => LoadScene(SceneWorkbench), new Vector2(620f, 74f));
            CreateButton("Quit", panelRoot, new Vector2(0.5f, 0.18f), QuitApp, new Vector2(620f, 74f));

            SetStatus(string.IsNullOrWhiteSpace(catalogError) ? "Ready." : catalogError);
        }

        private void BuildLevelSelect()
        {
            SetBackdrop(new Color(0.08f, 0.10f, 0.16f, 1f));
            CreateLabel("Level Select", new Vector2(0.5f, 0.83f), 44, TextAnchor.MiddleCenter);

            if (!catalogLoaded)
            {
                StartCoroutine(LoadCatalogIfNeeded());
            }

            if (catalog.tracks == null || catalog.tracks.Length == 0)
            {
                CreateLabel("Track bulunamadi. tools/analyze-audio.ps1 calistirin.", new Vector2(0.5f, 0.55f), 22, TextAnchor.MiddleCenter);
                CreateButton("Back", new Vector2(0.5f, 0.25f), () => LoadScene(SceneMainMenu));
                return;
            }

            GameObject panel = CreatePanel(
                new Vector2(0.5f, 0.43f),
                new Vector2(980f, 500f),
                new Color(0.04f, 0.10f, 0.18f, 0.88f),
                SpaceMazeArtCatalog.ResolveUiPanelTile(21),
                tiled: true,
                name: "LevelSelectPanel");
            RectTransform panelRoot = panel.transform as RectTransform;

            for (int i = 0; i < catalog.tracks.Length; i++)
            {
                int index = i;
                MusicTrackEntry track = catalog.tracks[i];
                string label = $"Level {i + 1:00} - {track.trackId}";
                CreateButton(label, panelRoot, new Vector2(0.5f, 0.84f - (i * 0.18f)), () =>
                {
                    selectedTrackIndex = index;
                    selectedTrack = catalog.tracks[index];
                    LoadScene(SceneGameplay);
                }, new Vector2(700f, 72f));
            }

            CreateButton("Back", panelRoot, new Vector2(0.5f, 0.12f), () => LoadScene(SceneMainMenu), new Vector2(700f, 72f));
        }

        private void BuildGameplay()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camera = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
                camGo.transform.position = new Vector3(0f, 0f, -10f);
            }

            camera.orthographic = true;
            camera.orthographicSize = 5.8f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.10f, 0.17f, 1f);

            GameObject worldRoot = new GameObject("WorldRoot");
            playerObject = RuntimeSpriteFactory.Create(
                "Player",
                worldRoot.transform,
                new Vector3(-4f, -2.0f, 0f),
                new Vector3(0.9f, 0.9f, 1f),
                sprite: SpaceMazeArtCatalog.ResolvePlayerSprite(11),
                sortingOrder: 26);
            SpriteRenderer playerSpriteRenderer = playerObject.GetComponent<SpriteRenderer>();
            if (playerSpriteRenderer != null)
            {
                playerSpriteRenderer.drawMode = SpriteDrawMode.Simple;
                Sprite sprite = playerSpriteRenderer.sprite;
                if (sprite != null)
                {
                    Vector2 size = sprite.rect.size;
                    float aspect = size.x / Mathf.Max(1f, size.y);
                    float height = 1.00f;
                    float width = Mathf.Clamp(height * aspect, 0.72f, 1.72f);
                    playerObject.transform.localScale = new Vector3(width, height, 1f);
                }
            }

            runtimeRoot = new GameObject("ZebraDashRuntimeRoot");
            audioSource = runtimeRoot.AddComponent<AudioSource>();
            beatClock = runtimeRoot.AddComponent<BeatClock>();
            ObstacleSpawner spawner = runtimeRoot.AddComponent<ObstacleSpawner>();
            PlayerController player = playerObject.AddComponent<PlayerController>();
            PlayerTrailController trail = playerObject.AddComponent<PlayerTrailController>();
            runner = runtimeRoot.AddComponent<LevelRunner>();
            parallaxSystem = runtimeRoot.AddComponent<ParallaxSystem>();
            parallaxSystem.Initialize(worldRoot.transform);

            runner.ConfigureScene(camera, playerObject.transform, worldRoot.transform);
            runner.ConfigureDependencies(beatClock, audioSource, spawner, player);
            runner.ConfigureParallax(parallaxSystem);
            trail.SetRunner(runner);
            BuildLaneGuides(worldRoot.transform, runner.HitLineX);

            hudText = CreateLabel("", new Vector2(0.02f, 0.96f), 20, TextAnchor.UpperLeft);
            hudText.rectTransform.anchorMin = new Vector2(0.02f, 0.96f);
            hudText.rectTransform.anchorMax = new Vector2(0.98f, 0.96f);
            hudText.rectTransform.pivot = new Vector2(0f, 1f);
            hudText.rectTransform.sizeDelta = new Vector2(0f, 196f);

            countdownText = CreateLabel("", new Vector2(0.5f, 0.55f), 88, TextAnchor.MiddleCenter);

            CreateButton("Pause", new Vector2(0.92f, 0.94f), TogglePause, new Vector2(160f, 52f));
            CreateButton("Menu", new Vector2(0.80f, 0.94f), () => LoadScene(SceneMainMenu), new Vector2(160f, 52f));

            pausePanel = CreatePanel(
                new Vector2(0.5f, 0.5f),
                new Vector2(560f, 360f),
                new Color(0.00f, 0.04f, 0.10f, 0.86f),
                SpaceMazeArtCatalog.ResolveUiPanelTile(31),
                tiled: true,
                name: "PausePanel");
            CreateLabel("Paused", pausePanel.transform as RectTransform, new Vector2(0.5f, 0.82f), 36, TextAnchor.MiddleCenter);
            CreateButton("Resume", pausePanel.transform as RectTransform, new Vector2(0.5f, 0.58f), () =>
            {
                pausePanel.SetActive(false);
                runner.ResumeRun();
            });
            CreateButton("Restart", pausePanel.transform as RectTransform, new Vector2(0.5f, 0.40f), () => StartCoroutine(StartSelectedTrack()));
            CreateButton("Exit", pausePanel.transform as RectTransform, new Vector2(0.5f, 0.22f), () => LoadScene(SceneMainMenu));
            pausePanel.SetActive(false);

            pulseOverlay = CreateImage(new Vector2(0.5f, 0.5f), new Vector2(3000f, 3000f), new Color(1f, 1f, 1f, 0f));
            warpOverlay = CreateImage(new Vector2(0.5f, 0.5f), new Vector2(3000f, 3000f), new Color(0.32f, 0.72f, 1f, 0f));

            resultSceneQueued = false;
            nextAccentIndex = 0;
            accentHitTimes.Clear();
            lastSubVisualIndex = int.MinValue;
            lastBeatVisualIndex = int.MinValue;
            lastBarVisualIndex = int.MinValue;
            lastPhraseVisualIndex = int.MinValue;
            StartCoroutine(StartSelectedTrack());
        }

        private IEnumerator StartSelectedTrack()
        {
            if (!catalogLoaded)
            {
                yield return LoadCatalogIfNeeded();
            }

            if (catalog.tracks == null || catalog.tracks.Length == 0)
            {
                SetStatus("Track list empty.");
                yield break;
            }

            if (selectedTrack == null)
            {
                selectedTrackIndex = Mathf.Clamp(selectedTrackIndex, 0, catalog.tracks.Length - 1);
                selectedTrack = catalog.tracks[selectedTrackIndex];
            }

            BeatMap loadedMap = null;
            string mapError = "";
            yield return ZebraDashCatalogIo.LoadBeatMapAsync(selectedTrack, (map, err) =>
            {
                loadedMap = map;
                mapError = err ?? "";
            });

            activeBeatMap = loadedMap ?? new BeatMap
            {
                trackId = selectedTrack.trackId,
                bpm = selectedTrack.bpm,
                offsetSec = selectedTrack.offsetSec
            };

            AudioClip loadedClip = null;
            string audioError = "";
            yield return ZebraDashCatalogIo.LoadAudioClipAsync(selectedTrack, (clip, err) =>
            {
                loadedClip = clip;
                audioError = err ?? "";
            });

            if (loadedClip != null)
            {
                audioSource.clip = loadedClip;
                audioSource.loop = false;
            }
            else
            {
                audioSource.clip = MetronomeClipFactory.Create(activeBeatMap.bpm, 24);
                audioSource.loop = true;
            }

            float offsetSec = BeatClock.LoadTrackOffsetSec(selectedTrack.trackId, 0f);
            runner.StartRun(activeBeatMap, selectedTrack, offsetSec, selectedTrackIndex);
            accentHitTimes.Clear();
            if (runner.AccentPulseHitTimes != null)
            {
                for (int i = 0; i < runner.AccentPulseHitTimes.Count; i++)
                {
                    accentHitTimes.Add(runner.AccentPulseHitTimes[i]);
                }
            }
            nextAccentIndex = 0;
            SetStatus(string.IsNullOrWhiteSpace(mapError + audioError)
                ? $"Playing {selectedTrack.trackId}"
                : $"{mapError} {audioError}".Trim());
        }

        private void BuildResults()
        {
            SetBackdrop(new Color(0.09f, 0.08f, 0.13f, 1f));
            string header = lastResult.Success ? "Stage Clear" : "Failed";
            CreateLabel(header, new Vector2(0.5f, 0.80f), 52, TextAnchor.MiddleCenter);

            string body =
                $"Track: {lastResult.TrackId}\n" +
                $"Score: {lastResult.Score}\n" +
                $"Max Combo: {lastResult.MaxCombo}\n" +
                $"Perfect/Good/Miss: {lastResult.Perfect}/{lastResult.Good}/{lastResult.Miss}";
            if (!lastResult.Success && !string.IsNullOrWhiteSpace(lastResult.FailReason))
            {
                body += $"\nReason: {lastResult.FailReason}";
            }
            CreateLabel(body, new Vector2(0.5f, 0.58f), 28, TextAnchor.MiddleCenter);

            GameObject panel = CreatePanel(
                new Vector2(0.5f, 0.25f),
                new Vector2(760f, 230f),
                new Color(0.04f, 0.08f, 0.16f, 0.86f),
                SpaceMazeArtCatalog.ResolveUiPanelTile(41),
                tiled: true,
                name: "ResultsPanel");
            RectTransform panelRoot = panel.transform as RectTransform;
            CreateButton("Restart", panelRoot, new Vector2(0.5f, 0.74f), () => LoadScene(SceneGameplay), new Vector2(520f, 68f));
            CreateButton("Next", panelRoot, new Vector2(0.5f, 0.46f), LoadNextLevel, new Vector2(520f, 68f));
            CreateButton("Menu", panelRoot, new Vector2(0.5f, 0.18f), () => LoadScene(SceneMainMenu), new Vector2(520f, 68f));
        }

        private void LoadNextLevel()
        {
            if (catalog.tracks == null || catalog.tracks.Length == 0)
            {
                LoadScene(SceneMainMenu);
                return;
            }

            selectedTrackIndex = (selectedTrackIndex + 1) % catalog.tracks.Length;
            selectedTrack = catalog.tracks[selectedTrackIndex];
            LoadScene(SceneGameplay);
        }

        private void TogglePause()
        {
            if (runner == null)
            {
                return;
            }

            if (runner.State == RunState.Paused)
            {
                pausePanel?.SetActive(false);
                runner.ResumeRun();
                return;
            }

            if (runner.State == RunState.Playing)
            {
                pausePanel?.SetActive(true);
                runner.PauseRun();
            }
        }

        private void TickAccentPulse()
        {
            if (runner == null)
            {
                return;
            }

            float songTime = runner.SongTimeSec;
            while (nextAccentIndex < accentHitTimes.Count && songTime >= accentHitTimes[nextAccentIndex])
            {
                screenPulse = Mathf.Max(screenPulse, 0.45f);
                warpPulse = Mathf.Max(warpPulse, 0.22f);
                parallaxSystem?.PushAccent(0.9f);
                nextAccentIndex++;
            }
        }

        private void TickPulseOverlay()
        {
            if (pulseOverlay == null && warpOverlay == null)
            {
                return;
            }

            screenPulse = Mathf.MoveTowards(screenPulse, 0f, Time.unscaledDeltaTime * 1.8f);
            warpPulse = Mathf.MoveTowards(warpPulse, 0f, Time.unscaledDeltaTime * 1.15f);

            if (pulseOverlay != null)
            {
                Color c = pulseOverlay.color;
                c.a = screenPulse * 0.24f;
                pulseOverlay.color = c;
            }

            if (warpOverlay != null)
            {
                float phaseBeat = runner != null ? runner.PhaseBeat : 0f;
                float beatWave = PulseEnvelope(phaseBeat, 0.11f) * 0.08f;
                float warpBoost = parallaxSystem != null ? Mathf.Max(0f, parallaxSystem.WarpPulseMultiplier - 1f) : 0f;
                Color c = warpOverlay.color;
                c.a = Mathf.Clamp01((warpPulse * 0.20f) + (warpBoost * 0.08f) + beatWave);
                warpOverlay.color = c;
            }
        }

        private string BuildHudLine()
        {
            if (runner == null || beatClock == null)
            {
                return string.Empty;
            }

            bool hasTelegraph = runner.TryGetTelegraph(out int teleLane, out float teleLeadSec, out float teleLead01);
            string telegraphText = hasTelegraph
                ? $"L{teleLane} in {teleLeadSec:F2}s ({teleLead01:P0})"
                : "-";
            string motifText = parallaxSystem != null ? parallaxSystem.CurrentMotifId : "-";
            string envText = parallaxSystem != null
                ? $"E/T/B:{parallaxSystem.Energy01:F2}/{parallaxSystem.Tension01:F2}/{parallaxSystem.Brightness01:F2}"
                : "E/T/B:-";

            return
                $"Track: {(selectedTrack != null ? selectedTrack.trackId : "-")}   " +
                $"State: {runner.State}   " +
                $"Beat/Bar: {runner.CurrentBeat}/{runner.CurrentBar}   " +
                $"Progress: {runner.Progress01 * 100f:F0}%   DSP: {beatClock.DspNow:F3}   BeatMs: {runner.BeatMs:F1}   " +
                $"Phase(b/bar): {runner.PhaseBeat:F2}/{runner.PhaseBar:F2}\n" +
                $"Offset: track {runner.OffsetMs:F1} ms / device {runner.DeviceOffsetMs:F1} ms / phase {runner.SessionPhaseMs:F2} ms   " +
                $"LastTap: {runner.LastTapOffsetMs:+0.0;-0.0;0.0} ms   TapResult: {runner.LastEmptyTapDecision}   " +
                $"Mask16: {runner.HazardMaskBar}\n" +
                $"Judge: {runner.LastJudge}   Combo: {runner.Combo}   Score: {runner.Score}   P/G/M: {runner.PerfectCount}/{runner.GoodCount}/{runner.MissCount}\n" +
                $"Section: {runner.CurrentSectionState} ({runner.CurrentStrain:F2}/{runner.TargetStrain:F2}) preset:{runner.CurrentPresetId}   " +
                $"Motif:{motifText} {envText}   Parallax x{(parallaxSystem != null ? parallaxSystem.SpeedPulseMultiplier : 1f):F2} emx{(parallaxSystem != null ? parallaxSystem.EmissivePulseMultiplier : 1f):F2} warpx{(parallaxSystem != null ? parallaxSystem.WarpPulseMultiplier : 1f):F2}   " +
                $"Scroll:{runner.WorldScrollPos:F1}@{runner.WorldScrollUnitsPerSec:F1}   Telegraph:{telegraphText}\n" +
                $"Next: {runner.NextHazardsDebug}\n" +
                $"{runner.GridDebugLine}\n" +
                $"{runner.TwoBarPlanDebug}\n" +
                $"HazardTiming: {runner.NextHazardTimingDebug}\n" +
                $"{runner.MazeSyncDebug}\n" +
                $"SafeDim:{(parallaxSystem != null ? parallaxSystem.SafeZoneDimMultiplier : 0.38f):F2} Outline:{(parallaxSystem != null ? parallaxSystem.HazardOutlineMultiplier : 1.20f):F2} Dust:{(parallaxSystem != null ? parallaxSystem.DustRateMultiplier : 1f):F2} Scan:{(parallaxSystem != null ? parallaxSystem.ScanlineIntensity : 0f):F2} Rotor:{(parallaxSystem != null ? parallaxSystem.RotorSpinRate : 0f):F2}\n" +
                $"Obs active/pool/created: {runner.ActiveObstacleCount}/{runner.PooledObstacleCount}/{runner.CreatedObstacleCount}";
        }

        private static float ResolveSectionEnergy(string sectionType, float currentStrain, float targetStrain)
        {
            float baseEnergy = Mathf.Clamp01(Mathf.Lerp(currentStrain, targetStrain, 0.55f));
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(baseEnergy * 0.35f);
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(0.62f + (baseEnergy * 0.38f));
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(0.42f + (baseEnergy * 0.28f));
            }

            return Mathf.Clamp01(0.36f + (baseEnergy * 0.40f));
        }

        private static float ResolveSectionTension(string sectionType, float currentStrain, float targetStrain)
        {
            float blend = Mathf.Clamp01(Mathf.Lerp(currentStrain, targetStrain, 0.45f));
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(blend * 0.20f);
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(0.68f + (blend * 0.30f));
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(0.45f + (blend * 0.28f));
            }

            return Mathf.Clamp01(0.30f + (blend * 0.32f));
        }

        private static float ResolveSectionBrightness(string sectionType, float energy, float tension)
        {
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(0.22f + (energy * 0.35f));
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(0.48f + (energy * 0.28f) + (tension * 0.20f));
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp01(0.36f + (energy * 0.24f) + (tension * 0.10f));
            }

            return Mathf.Clamp01(0.34f + (energy * 0.30f));
        }

        private void LoadScene(string sceneName)
        {
            Debug.Log($"[ZebraDashFlow] LoadScene request: {sceneName}");
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SceneManager.LoadScene(sceneName);
                return;
            }

            if (string.Equals(sceneName, SceneMainMenu, StringComparison.OrdinalIgnoreCase)
                && Application.CanStreamedLevelBeLoaded(SceneBootstrapLegacy))
            {
                SceneManager.LoadScene(SceneBootstrapLegacy);
            }
        }

        private void EnsureCanvasAndEventSystem()
        {
            GameObject canvasGo = GameObject.Find("ZebraDashCanvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("ZebraDashCanvas");
            }

            canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = canvasGo.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 5000;

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect != null)
            {
                canvasRect.anchorMin = Vector2.zero;
                canvasRect.anchorMax = Vector2.one;
                canvasRect.offsetMin = Vector2.zero;
                canvasRect.offsetMax = Vector2.zero;
                canvasRect.localScale = Vector3.one;
            }

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            Canvas[] canvases = FindObjectsOfType<Canvas>();
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas other = canvases[i];
                if (other == null || other == canvas)
                {
                    continue;
                }

                other.enabled = false;
            }

            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<StandaloneInputModule>();
            }

            EnsureSafeAreaRoot();
            foreach (Transform child in uiRoot)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            titleText = null;
            statusText = null;
            hudText = null;
            countdownText = null;
            pausePanel = null;
            pulseOverlay = null;
            warpOverlay = null;
        }

        private void EnsureSafeAreaRoot()
        {
            Transform existing = canvas.transform.Find("UIRoot");
            if (existing != null)
            {
                safeAreaRoot = existing as RectTransform;
            }
            else
            {
                GameObject go = new GameObject("UIRoot");
                safeAreaRoot = go.AddComponent<RectTransform>();
            }

            safeAreaRoot.SetParent(canvas.transform, false);
            safeAreaRoot.localScale = Vector3.one;
            safeAreaRoot.localRotation = Quaternion.identity;
            safeAreaRoot.anchoredPosition3D = Vector3.zero;
            safeAreaRoot.sizeDelta = Vector2.zero;
            safeAreaRoot.pivot = new Vector2(0.5f, 0.5f);
            safeAreaRoot.anchorMin = Vector2.zero;
            safeAreaRoot.anchorMax = Vector2.one;
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;

            uiRoot = safeAreaRoot;
            ApplySafeArea(force: true);
        }

        private void ApplySafeArea(bool force = false)
        {
            if (safeAreaRoot == null)
            {
                return;
            }

            Vector2Int screenSize = new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            bool fallbackToFull;
            Rect safe = ResolveStableSafeArea(screenSize, out fallbackToFull);
            if (!force && screenSize == lastScreenSize && ApproximatelyEqual(safe, lastSafeArea))
            {
                return;
            }

            if (fallbackToFull && !safeAreaFallbackLogged)
            {
                safeAreaFallbackLogged = true;
                Rect raw = Screen.safeArea;
                Debug.LogWarning($"[ZebraDashFlow] SafeArea fallback -> fullscreen. raw=({raw.x:F0},{raw.y:F0},{raw.width:F0},{raw.height:F0}) screen=({screenSize.x},{screenSize.y})");
            }

            Vector2 anchorMin = new Vector2(safe.xMin / screenSize.x, safe.yMin / screenSize.y);
            Vector2 anchorMax = new Vector2(safe.xMax / screenSize.x, safe.yMax / screenSize.y);
            safeAreaRoot.anchorMin = anchorMin;
            safeAreaRoot.anchorMax = anchorMax;
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;

            lastSafeArea = safe;
            lastScreenSize = screenSize;
        }

        private static Rect ResolveStableSafeArea(Vector2Int screenSize, out bool fallbackToFull)
        {
            Rect safe = Screen.safeArea;
            float width = Mathf.Max(1f, screenSize.x);
            float height = Mathf.Max(1f, screenSize.y);

            float coverageX = safe.width / width;
            float coverageY = safe.height / height;
            float insetLeft = Mathf.Max(0f, safe.xMin) / width;
            float insetRight = Mathf.Max(0f, width - safe.xMax) / width;
            float insetBottom = Mathf.Max(0f, safe.yMin) / height;
            float insetTop = Mathf.Max(0f, height - safe.yMax) / height;
            bool outOfBounds = safe.xMin < -1f
                || safe.yMin < -1f
                || safe.xMax > (width + 1f)
                || safe.yMax > (height + 1f);
            bool unreasonableCoverage = coverageX < 0.97f
                || coverageX > 1.01f
                || coverageY < 0.97f
                || coverageY > 1.01f;
            bool unreasonableInset = insetLeft > 0.03f
                || insetRight > 0.03f
                || insetBottom > 0.03f
                || insetTop > 0.03f;

            if (outOfBounds || unreasonableCoverage || unreasonableInset)
            {
                fallbackToFull = true;
                return new Rect(0f, 0f, width, height);
            }

            fallbackToFull = false;
            return safe;
        }

        private static bool ApproximatelyEqual(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f
                && Mathf.Abs(a.y - b.y) < 0.5f
                && Mathf.Abs(a.width - b.width) < 0.5f
                && Mathf.Abs(a.height - b.height) < 0.5f;
        }

        private void CleanupRuntimeSceneObjects()
        {
            runner = null;
            beatClock = null;
            audioSource = null;
            parallaxSystem = null;
            safeZoneRenderer = null;
            laneTelegraphLowerRenderer = null;
            laneTelegraphUpperRenderer = null;
            laneLowerRenderer = null;
            laneUpperRenderer = null;
            hitLineRenderer = null;
            lastSubVisualIndex = int.MinValue;
            lastBeatVisualIndex = int.MinValue;
            lastBarVisualIndex = int.MinValue;
            lastPhraseVisualIndex = int.MinValue;

            if (runtimeRoot != null)
            {
                Destroy(runtimeRoot);
                runtimeRoot = null;
            }

            if (playerObject != null)
            {
                Destroy(playerObject);
                playerObject = null;
            }
        }

        private void BuildLaneGuides(Transform worldRoot, float hitX)
        {
            if (worldRoot == null)
            {
                return;
            }

            CreateWorldQuad(
                "MazeCeilingOccluder",
                worldRoot,
                new Vector3(0f, 3.7f, -1.65f),
                new Vector3(38f, 2.9f, 1f),
                new Color(0.02f, 0.05f, 0.09f, 0.34f),
                transparent: true,
                sortingOrder: 11);
            CreateWorldQuad(
                "MazeFloorOccluder",
                worldRoot,
                new Vector3(0f, -3.7f, -1.65f),
                new Vector3(38f, 2.9f, 1f),
                new Color(0.02f, 0.05f, 0.09f, 0.34f),
                transparent: true,
                sortingOrder: 11);
            CreateWorldQuad(
                "MazeCoreBand",
                worldRoot,
                new Vector3(0f, 0f, -1.62f),
                new Vector3(38f, 4.9f, 1f),
                new Color(0.08f, 0.16f, 0.24f, 0.18f),
                transparent: true,
                sortingOrder: 11);
            CreateWorldQuad(
                "MazeCeilingEdge",
                worldRoot,
                new Vector3(0f, 2.55f, -1.55f),
                new Vector3(38f, 0.18f, 1f),
                new Color(0.30f, 0.80f, 0.95f, 0.46f),
                transparent: true,
                sortingOrder: 14);
            CreateWorldQuad(
                "MazeFloorEdge",
                worldRoot,
                new Vector3(0f, -2.55f, -1.55f),
                new Vector3(38f, 0.18f, 1f),
                new Color(0.30f, 0.80f, 0.95f, 0.46f),
                transparent: true,
                sortingOrder: 14);
            CreateWorldQuad(
                "MazeCeilingDanger",
                worldRoot,
                new Vector3(0f, 2.84f, -1.52f),
                new Vector3(38f, 0.22f, 1f),
                new Color(0.96f, 0.42f, 0.36f, 0.42f),
                transparent: true,
                sortingOrder: 14);
            CreateWorldQuad(
                "MazeFloorDanger",
                worldRoot,
                new Vector3(0f, -2.84f, -1.52f),
                new Vector3(38f, 0.22f, 1f),
                new Color(0.96f, 0.42f, 0.36f, 0.42f),
                transparent: true,
                sortingOrder: 14);
            CreateWorldQuad(
                "MazeCeilingInner",
                worldRoot,
                new Vector3(0f, 1.92f, -1.58f),
                new Vector3(38f, 0.42f, 1f),
                new Color(0.14f, 0.30f, 0.46f, 0.30f),
                transparent: true,
                sortingOrder: 13);
            CreateWorldQuad(
                "MazeFloorInner",
                worldRoot,
                new Vector3(0f, -1.92f, -1.58f),
                new Vector3(38f, 0.42f, 1f),
                new Color(0.14f, 0.30f, 0.46f, 0.30f),
                transparent: true,
                sortingOrder: 14);

            safeZoneRenderer = CreateWorldQuad(
                "SafeZone",
                worldRoot,
                new Vector3(hitX + 0.3f, 0f, -1.7f),
                new Vector3(4.4f, 4.6f, 1f),
                new Color(0.04f, 0.10f, 0.16f, 0.14f),
                transparent: true,
                sortingOrder: 10);
            laneTelegraphLowerRenderer = CreateWorldQuad(
                "LaneTelegraphLower",
                worldRoot,
                new Vector3(2.6f, -2.0f, -1.5f),
                new Vector3(27f, 0.48f, 1f),
                new Color(0.22f, 0.92f, 1f, 0.005f),
                transparent: true,
                sortingOrder: 13);
            laneTelegraphUpperRenderer = CreateWorldQuad(
                "LaneTelegraphUpper",
                worldRoot,
                new Vector3(2.6f, 2.0f, -1.5f),
                new Vector3(27f, 0.48f, 1f),
                new Color(1f, 0.52f, 0.88f, 0.005f),
                transparent: true,
                sortingOrder: 13);
            laneLowerRenderer = CreateWorldQuad(
                "LaneLower",
                worldRoot,
                new Vector3(0f, -2.0f, -1.6f),
                new Vector3(36f, 0.08f, 1f),
                new Color(0.14f, 0.58f, 0.92f, 0.44f),
                transparent: true,
                sortingOrder: 12);
            laneUpperRenderer = CreateWorldQuad(
                "LaneUpper",
                worldRoot,
                new Vector3(0f, 2.0f, -1.6f),
                new Vector3(36f, 0.08f, 1f),
                new Color(0.14f, 0.58f, 0.92f, 0.44f),
                transparent: true,
                sortingOrder: 12);
            hitLineRenderer = CreateWorldQuad(
                "HitLine",
                worldRoot,
                new Vector3(hitX, 0f, 0.8f),
                new Vector3(0.032f, 9.6f, 1f),
                new Color(0.90f, 0.98f, 1f, 0.56f),
                transparent: true,
                sortingOrder: 40);
        }

        private static Renderer CreateWorldQuad(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            bool transparent,
            int sortingOrder,
            Sprite sprite = null)
        {
            GameObject quad = RuntimeSpriteFactory.Create(
                name,
                parent,
                localPosition,
                localScale,
                sprite: sprite,
                sortingOrder: sortingOrder);
            SpriteRenderer spriteRenderer = quad.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.drawMode = SpriteDrawMode.Tiled;
                spriteRenderer.size = Vector2.one;
            }

            Renderer renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = RenderMaterialUtils.CreateSolidMaterial(color, transparent);
            }

            return renderer;
        }

        private void TickLaneRailPulse()
        {
            if (runner == null)
            {
                return;
            }

            float beatPulse = PulseEnvelope(runner.PhaseBeat, 0.10f);
            float accentScale = parallaxSystem != null ? (parallaxSystem.EmissivePulseMultiplier - 1f) : 0f;
            float outlineMul = parallaxSystem != null ? parallaxSystem.HazardOutlineMultiplier : 1.2f;
            float safeDim = parallaxSystem != null ? parallaxSystem.SafeZoneDimMultiplier : 0.38f;
            bool hasTelegraph = runner.TryGetTelegraph(out int telegraphLane, out _, out float telegraphLead01);
            float telegraphBoost = hasTelegraph ? Mathf.Lerp(0.14f, 0.70f, telegraphLead01) : 0f;

            Color lowerBase = new Color(0.32f, 0.56f, 0.82f, 0.62f);
            Color upperBase = new Color(0.32f, 0.56f, 0.82f, 0.62f);
            if (hasTelegraph)
            {
                lowerBase = telegraphLane == 0
                    ? new Color(0.22f, 0.92f, 1f, 0.62f)
                    : new Color(0.22f, 0.50f, 0.72f, 0.30f);
                upperBase = telegraphLane == 1
                    ? new Color(1f, 0.52f, 0.88f, 0.62f)
                    : new Color(0.45f, 0.38f, 0.58f, 0.30f);
            }

            ApplyPulseColor(laneLowerRenderer, lowerBase, beatPulse, accentScale, (0.24f + (telegraphLane == 0 ? telegraphBoost * 0.30f : 0f)) * outlineMul);
            ApplyPulseColor(laneUpperRenderer, upperBase, beatPulse, accentScale, (0.24f + (telegraphLane == 1 ? telegraphBoost * 0.30f : 0f)) * outlineMul);
            ApplyPulseColor(hitLineRenderer, new Color(0.82f, 0.96f, 1f, 0.58f), beatPulse, accentScale, (0.24f + (telegraphBoost * 0.09f)) * outlineMul);

            if (safeZoneRenderer != null && safeZoneRenderer.material != null)
            {
                float alpha = (safeDim * 0.56f) + (beatPulse * 0.02f) + (telegraphBoost * 0.05f);
                RenderMaterialUtils.ApplyColor(safeZoneRenderer.material, new Color(0.03f, 0.08f, 0.13f, Mathf.Clamp01(alpha)));
            }

            if (laneTelegraphLowerRenderer != null && laneTelegraphLowerRenderer.material != null)
            {
                float lowerAlpha = telegraphLane == 0 ? (0.10f + (telegraphBoost * 0.60f)) : 0.03f;
                RenderMaterialUtils.ApplyColor(laneTelegraphLowerRenderer.material, new Color(0.22f, 0.92f, 1f, lowerAlpha));
            }

            if (laneTelegraphUpperRenderer != null && laneTelegraphUpperRenderer.material != null)
            {
                float upperAlpha = telegraphLane == 1 ? (0.10f + (telegraphBoost * 0.60f)) : 0.03f;
                RenderMaterialUtils.ApplyColor(laneTelegraphUpperRenderer.material, new Color(1f, 0.52f, 0.88f, upperAlpha));
            }
        }

        private void TickBeatGridVisuals()
        {
            if (runner == null || runner.State != RunState.Playing)
            {
                return;
            }

            float beatSec = Mathf.Max(0.0001f, runner.BeatSec);
            float subSec = Mathf.Max(0.0125f, beatSec * 0.25f);
            int subIndex = Mathf.FloorToInt(runner.SongTimeSec / subSec);
            if (subIndex != lastSubVisualIndex)
            {
                lastSubVisualIndex = subIndex;
                parallaxSystem?.PushSubTick(0.22f);
            }

            int beatIndex = runner.CurrentBeat;
            if (beatIndex == lastBeatVisualIndex)
            {
                // Continue; bar/phrase checks can still advance when scene starts.
            }
            else
            {
                lastBeatVisualIndex = beatIndex;
                screenPulse = Mathf.Max(screenPulse, 0.12f);
                parallaxSystem?.PushBeatTick(0.30f);
                parallaxSystem?.PushAccent(0.20f);
            }

            int barIndex = runner.CurrentBar;
            if (barIndex != lastBarVisualIndex)
            {
                lastBarVisualIndex = barIndex;
                screenPulse = Mathf.Max(screenPulse, 0.16f);
                warpPulse = Mathf.Max(warpPulse, 0.18f);
                parallaxSystem?.PushBarPulse(0.52f);
            }

            int phraseIndex = Mathf.FloorToInt(barIndex / 2f);
            if (phraseIndex != lastPhraseVisualIndex)
            {
                lastPhraseVisualIndex = phraseIndex;
                warpPulse = Mathf.Max(warpPulse, 0.30f);
                parallaxSystem?.PushPhraseWarp(0.74f);
            }
        }

        private static void ApplyPulseColor(Renderer renderer, Color baseColor, float beatPulse, float accentScale, float amp)
        {
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            float t = Mathf.Clamp01((beatPulse * amp) + (accentScale * amp * 0.55f));
            Color lit = Color.Lerp(baseColor, Color.white, t);
            lit.a = Mathf.Clamp01(baseColor.a + (t * 0.22f));
            RenderMaterialUtils.ApplyColor(renderer.material, lit);
        }

        private static float PulseEnvelope(float phase, float width)
        {
            float p = Mathf.Repeat(phase, 1f);
            float dist = Mathf.Min(p, 1f - p);
            float t = Mathf.Clamp01(1f - (dist / Mathf.Max(0.001f, width)));
            return t * t * (3f - (2f * t));
        }

        private void SetBackdrop(Color color)
        {
            if (uiRoot == null)
            {
                return;
            }

            int seed = SceneManager.GetActiveScene().name.GetHashCode();
            Sprite tile = SpaceMazeArtCatalog.ResolveUiBackdropTile(seed);
            CreateImage(
                new Vector2(0.5f, 0.5f),
                new Vector2(5000f, 5000f),
                color,
                tile,
                tiled: tile != null,
                name: "Backdrop");
        }

        private Text CreateLabel(string text, Vector2 anchor, int fontSize, TextAnchor align)
        {
            RectTransform root = uiRoot;
            return CreateLabel(text, root, anchor, fontSize, align);
        }

        private Text CreateLabel(string text, RectTransform parent, Vector2 anchor, int fontSize, TextAnchor align)
        {
            GameObject go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, 80f);

            Text label = go.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = fontSize;
            label.alignment = align;
            label.text = text;
            label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private GameObject CreatePanel(Vector2 anchor, Vector2 size, Color color, Sprite sprite = null, bool tiled = false, string name = "Panel")
        {
            RectTransform root = uiRoot;
            GameObject go = new GameObject(name);
            go.transform.SetParent(root, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            Image image = go.AddComponent<Image>();
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = tiled ? Image.Type.Tiled : Image.Type.Simple;
            }
            return go;
        }

        private Image CreateImage(Vector2 anchor, Vector2 size, Color color, Sprite sprite = null, bool tiled = false, string name = "Image")
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(uiRoot, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            Image image = go.AddComponent<Image>();
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = tiled ? Image.Type.Tiled : Image.Type.Simple;
            }

            return image;
        }

        private void CreateButton(string label, Vector2 anchor, Action onClick, Vector2? size = null)
        {
            RectTransform root = uiRoot;
            CreateButton(label, root, anchor, onClick, size ?? new Vector2(420f, 72f));
        }

        private void CreateButton(string label, RectTransform parent, Vector2 anchor, Action onClick, Vector2? size = null)
        {
            GameObject go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size ?? new Vector2(380f, 66f);

            Image image = go.AddComponent<Image>();
            Sprite buttonTile = SpaceMazeArtCatalog.ResolveUiButtonTile(label, label.GetHashCode());
            Color baseColor = new Color(0.15f, 0.24f, 0.36f, 0.94f);
            image.color = baseColor;
            if (buttonTile != null)
            {
                image.sprite = buttonTile;
                image.type = Image.Type.Tiled;
            }

            Button button = go.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = new Color(0.24f, 0.36f, 0.48f, 1f);
            colors.pressedColor = new Color(0.10f, 0.16f, 0.24f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            RectTransform tr = textGo.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            Text txt = textGo.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = 28;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;
        }

        private void SetStatus(string text)
        {
            if (statusText == null)
            {
                statusText = CreateLabel("", new Vector2(0.5f, 0.08f), 20, TextAnchor.MiddleCenter);
            }

            statusText.text = text;
        }

        private static void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

    }
}
