using System;
using System.Collections;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        private Rect lastSafeArea = new Rect(0f, 0f, -1f, -1f);
        private Vector2Int lastScreenSize = Vector2Int.zero;
        private bool safeAreaFallbackLogged;
        private static Material cachedPlayerMaterial;

        private GameObject runtimeRoot;
        private GameObject playerObject;
        private LevelRunner runner;
        private BeatClock beatClock;
        private AudioSource audioSource;
        private ParallaxSystem parallaxSystem;
        private Renderer laneLowerRenderer;
        private Renderer laneUpperRenderer;
        private Renderer hitLineRenderer;

        private float screenPulse;
        private float resultsDelay;
        private bool resultSceneQueued;
        private readonly List<float> accentHitTimes = new List<float>();
        private int nextAccentIndex;
        private bool debugOverlayVisible = true;

        private ResultSnapshot lastResult;

        [Serializable]
        private struct ResultSnapshot
        {
            public string TrackId;
            public bool Success;
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
            parallaxSystem.Tick(songTime, isPlaying, phaseBeat, phaseBar, worldScrollPos);
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

            CreateButton("Play", new Vector2(0.5f, 0.54f), () => LoadScene(SceneLevelSelect));
            CreateButton("Workbench", new Vector2(0.5f, 0.44f), () => LoadScene(SceneWorkbench));
            CreateButton("Settings (Tap->Sync)", new Vector2(0.5f, 0.34f), () => LoadScene(SceneWorkbench));
            CreateButton("Quit", new Vector2(0.5f, 0.24f), QuitApp);

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

            float startY = 0.62f;
            for (int i = 0; i < catalog.tracks.Length; i++)
            {
                int index = i;
                MusicTrackEntry track = catalog.tracks[i];
                string label = $"Level {i + 1:00} - {track.trackId}";
                CreateButton(label, new Vector2(0.5f, startY - (i * 0.11f)), () =>
                {
                    selectedTrackIndex = index;
                    selectedTrack = catalog.tracks[index];
                    LoadScene(SceneGameplay);
                });
            }

            CreateButton("Back", new Vector2(0.5f, 0.16f), () => LoadScene(SceneMainMenu));
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
            camera.orthographicSize = 5f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.10f, 0.17f, 1f);

            GameObject worldRoot = new GameObject("WorldRoot");
            playerObject = RuntimeSpriteFactory.Create(
                "Player",
                worldRoot.transform,
                new Vector3(-4f, -1.2f, 0f),
                new Vector3(0.9f, 0.9f, 1f),
                sortingOrder: 26);
            Renderer playerRenderer = playerObject.GetComponent<Renderer>();
            if (playerRenderer != null)
            {
                playerRenderer.sharedMaterial = GetPlayerMaterial();
            }

            runtimeRoot = new GameObject("ZebraDashRuntimeRoot");
            audioSource = runtimeRoot.AddComponent<AudioSource>();
            beatClock = runtimeRoot.AddComponent<BeatClock>();
            ObstacleSpawner spawner = runtimeRoot.AddComponent<ObstacleSpawner>();
            PlayerController player = playerObject.AddComponent<PlayerController>();
            runner = runtimeRoot.AddComponent<LevelRunner>();
            parallaxSystem = runtimeRoot.AddComponent<ParallaxSystem>();
            parallaxSystem.Initialize(worldRoot.transform);

            runner.ConfigureScene(camera, playerObject.transform, worldRoot.transform);
            runner.ConfigureDependencies(beatClock, audioSource, spawner, player);
            BuildLaneGuides(worldRoot.transform, playerObject.transform.position.x);

            hudText = CreateLabel("", new Vector2(0.02f, 0.96f), 20, TextAnchor.UpperLeft);
            hudText.rectTransform.anchorMin = new Vector2(0.02f, 0.96f);
            hudText.rectTransform.anchorMax = new Vector2(0.98f, 0.96f);
            hudText.rectTransform.pivot = new Vector2(0f, 1f);
            hudText.rectTransform.sizeDelta = new Vector2(0f, 196f);

            countdownText = CreateLabel("", new Vector2(0.5f, 0.55f), 88, TextAnchor.MiddleCenter);

            CreateButton("Pause", new Vector2(0.92f, 0.94f), TogglePause, new Vector2(160f, 52f));
            CreateButton("Menu", new Vector2(0.80f, 0.94f), () => LoadScene(SceneMainMenu), new Vector2(160f, 52f));

            pausePanel = CreatePanel(new Vector2(0.5f, 0.5f), new Vector2(460f, 310f), new Color(0f, 0f, 0f, 0.75f));
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

            resultSceneQueued = false;
            nextAccentIndex = 0;
            accentHitTimes.Clear();
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
            runner.StartRun(activeBeatMap, selectedTrack, offsetSec);
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
            CreateLabel(body, new Vector2(0.5f, 0.58f), 28, TextAnchor.MiddleCenter);

            CreateButton("Restart", new Vector2(0.5f, 0.34f), () => LoadScene(SceneGameplay));
            CreateButton("Next", new Vector2(0.5f, 0.24f), LoadNextLevel);
            CreateButton("Menu", new Vector2(0.5f, 0.14f), () => LoadScene(SceneMainMenu));
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
                parallaxSystem?.PushAccent(0.9f);
                nextAccentIndex++;
            }
        }

        private void TickPulseOverlay()
        {
            if (pulseOverlay == null)
            {
                return;
            }

            screenPulse = Mathf.MoveTowards(screenPulse, 0f, Time.unscaledDeltaTime * 1.8f);
            Color c = pulseOverlay.color;
            c.a = screenPulse * 0.24f;
            pulseOverlay.color = c;
        }

        private string BuildHudLine()
        {
            if (runner == null || beatClock == null)
            {
                return string.Empty;
            }

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
                $"Parallax x{(parallaxSystem != null ? parallaxSystem.SpeedPulseMultiplier : 1f):F2} emx{(parallaxSystem != null ? parallaxSystem.EmissivePulseMultiplier : 1f):F2}   " +
                $"Next: {runner.NextHazardsDebug}\n" +
                $"{runner.GridDebugLine}\n" +
                $"{runner.TwoBarPlanDebug}\n" +
                $"HazardTiming: {runner.NextHazardTimingDebug}\n" +
                $"Obs active/pool/created: {runner.ActiveObstacleCount}/{runner.PooledObstacleCount}/{runner.CreatedObstacleCount}";
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
            laneLowerRenderer = null;
            laneUpperRenderer = null;
            hitLineRenderer = null;

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

            laneLowerRenderer = CreateWorldQuad(
                "LaneLower",
                worldRoot,
                new Vector3(0f, -1.2f, -1.6f),
                new Vector3(36f, 0.10f, 1f),
                new Color(0.32f, 0.56f, 0.82f, 0.62f),
                transparent: true,
                sortingOrder: 12);
            laneUpperRenderer = CreateWorldQuad(
                "LaneUpper",
                worldRoot,
                new Vector3(0f, 1.2f, -1.6f),
                new Vector3(36f, 0.10f, 1f),
                new Color(0.32f, 0.56f, 0.82f, 0.62f),
                transparent: true,
                sortingOrder: 12);
            hitLineRenderer = CreateWorldQuad(
                "HitLine",
                worldRoot,
                new Vector3(hitX, 0f, 0.6f),
                new Vector3(0.10f, 3.4f, 1f),
                new Color(0.98f, 0.99f, 1f, 0.84f),
                transparent: true,
                sortingOrder: 18);
        }

        private static Renderer CreateWorldQuad(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            bool transparent,
            int sortingOrder)
        {
            GameObject quad = RuntimeSpriteFactory.Create(
                name,
                parent,
                localPosition,
                localScale,
                sortingOrder: sortingOrder);

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

            ApplyPulseColor(laneLowerRenderer, new Color(0.32f, 0.56f, 0.82f, 0.50f), beatPulse, accentScale, 0.18f);
            ApplyPulseColor(laneUpperRenderer, new Color(0.32f, 0.56f, 0.82f, 0.50f), beatPulse, accentScale, 0.18f);
            ApplyPulseColor(hitLineRenderer, new Color(0.98f, 0.99f, 1f, 0.74f), beatPulse, accentScale, 0.28f);
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

            CreateImage(new Vector2(0.5f, 0.5f), new Vector2(5000f, 5000f), color);
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

        private GameObject CreatePanel(Vector2 anchor, Vector2 size, Color color)
        {
            RectTransform root = uiRoot;
            GameObject go = new GameObject("Panel");
            go.transform.SetParent(root, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            Image image = go.AddComponent<Image>();
            image.color = color;
            return go;
        }

        private Image CreateImage(Vector2 anchor, Vector2 size, Color color)
        {
            GameObject go = new GameObject("Image");
            go.transform.SetParent(uiRoot, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            Image image = go.AddComponent<Image>();
            image.color = color;
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
            image.color = new Color(0.17f, 0.24f, 0.35f, 0.95f);

            Button button = go.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.17f, 0.24f, 0.35f, 0.95f);
            colors.highlightedColor = new Color(0.22f, 0.32f, 0.44f, 1f);
            colors.pressedColor = new Color(0.12f, 0.18f, 0.27f, 1f);
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

        private static Material GetPlayerMaterial()
        {
            if (cachedPlayerMaterial != null)
            {
                return cachedPlayerMaterial;
            }

            cachedPlayerMaterial = RenderMaterialUtils.CreateSolidMaterial(new Color(0.95f, 0.96f, 0.98f, 1f));
            return cachedPlayerMaterial;
        }
    }
}
