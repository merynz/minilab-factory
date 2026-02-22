using System;
using System.Collections;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ZebraDash
{
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
        private Text titleText;
        private Text statusText;
        private Text hudText;
        private Text countdownText;
        private GameObject pausePanel;
        private Image pulseOverlay;

        private readonly List<RectTransform> parallaxLayers = new List<RectTransform>();
        private readonly List<float> parallaxSpeeds = new List<float>();

        private GameObject runtimeRoot;
        private GameObject playerObject;
        private LevelRunner runner;
        private BeatClock beatClock;
        private AudioSource audioSource;

        private float screenPulse;
        private float resultsDelay;
        private bool resultSceneQueued;
        private readonly List<float> accentHitTimes = new List<float>();
        private int nextAccentIndex;

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
        }

        private void Update()
        {
            TickParallax();
            TickPulseOverlay();

            if (runner == null)
            {
                return;
            }

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
            SetBackdrop(new Color(0.05f, 0.07f, 0.11f, 1f));
            BuildParallaxBackground();

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

            GameObject worldRoot = new GameObject("WorldRoot");
            playerObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playerObject.name = "Player";
            playerObject.transform.position = new Vector3(-4f, -1.2f, 0f);
            playerObject.transform.localScale = new Vector3(0.7f, 0.7f, 1f);

            runtimeRoot = new GameObject("ZebraDashRuntimeRoot");
            audioSource = runtimeRoot.AddComponent<AudioSource>();
            beatClock = runtimeRoot.AddComponent<BeatClock>();
            ObstacleSpawner spawner = runtimeRoot.AddComponent<ObstacleSpawner>();
            PlayerController player = playerObject.AddComponent<PlayerController>();
            runner = runtimeRoot.AddComponent<LevelRunner>();

            runner.ConfigureScene(camera, playerObject.transform, worldRoot.transform);
            runner.ConfigureDependencies(beatClock, audioSource, spawner, player);

            hudText = CreateLabel("", new Vector2(0.02f, 0.96f), 20, TextAnchor.UpperLeft);
            hudText.rectTransform.anchorMin = new Vector2(0.02f, 0.96f);
            hudText.rectTransform.anchorMax = new Vector2(0.98f, 0.96f);
            hudText.rectTransform.pivot = new Vector2(0f, 1f);
            hudText.rectTransform.sizeDelta = new Vector2(0f, 120f);

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

            accentHitTimes.Clear();
            BeatEvent[] canonical = BeatMapEventUtils.GetCanonicalEvents(activeBeatMap);
            for (int i = 0; i < canonical.Length; i++)
            {
                if (canonical[i] != null && canonical[i].IsKind(BeatKinds.Accent))
                {
                    accentHitTimes.Add(canonical[i].timeSec);
                }
            }
            accentHitTimes.Sort();
            nextAccentIndex = 0;

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

            float offsetSec = BeatClock.LoadTrackOffsetSec(selectedTrack.trackId, selectedTrack.offsetSec);
            runner.StartRun(activeBeatMap, selectedTrack, offsetSec);
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

        private void TickParallax()
        {
            if (parallaxLayers.Count == 0)
            {
                return;
            }

            bool playing = runner != null && runner.State == RunState.Playing;
            if (!playing)
            {
                return;
            }

            for (int i = 0; i < parallaxLayers.Count; i++)
            {
                RectTransform rt = parallaxLayers[i];
                Vector2 pos = rt.anchoredPosition;
                pos.x -= parallaxSpeeds[i] * Time.deltaTime;
                if (pos.x < -140f)
                {
                    pos.x += 280f;
                }

                rt.anchoredPosition = pos;
            }
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
                $"Beat: {runner.CurrentBeat}   " +
                $"Offset: {runner.OffsetMs:F1} ms\n" +
                $"Judge: {runner.LastJudge}   Combo: {runner.Combo}   Score: {runner.Score}   " +
                $"P/G/M: {runner.PerfectCount}/{runner.GoodCount}/{runner.MissCount}   Section: {runner.CurrentSectionState}";
        }

        private void LoadScene(string sceneName)
        {
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
            canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGo = new GameObject("ZebraDashCanvas");
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<StandaloneInputModule>();
            }

            foreach (Transform child in canvas.transform)
            {
                Destroy(child.gameObject);
            }

            titleText = null;
            statusText = null;
            hudText = null;
            countdownText = null;
            pausePanel = null;
            pulseOverlay = null;
            parallaxLayers.Clear();
            parallaxSpeeds.Clear();
        }

        private void CleanupRuntimeSceneObjects()
        {
            runner = null;
            beatClock = null;
            audioSource = null;

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

        private void SetBackdrop(Color color)
        {
            if (canvas == null)
            {
                return;
            }

            CreateImage(new Vector2(0.5f, 0.5f), new Vector2(5000f, 5000f), color);
        }

        private void BuildParallaxBackground()
        {
            Color[] colors =
            {
                new Color(0.08f, 0.12f, 0.18f, 0.85f),
                new Color(0.11f, 0.17f, 0.24f, 0.75f),
                new Color(0.18f, 0.24f, 0.31f, 0.65f)
            };

            for (int i = 0; i < colors.Length; i++)
            {
                Image layer = CreateImage(new Vector2(0.5f, 0.2f + (i * 0.22f)), new Vector2(2800f, 180f), colors[i]);
                RectTransform rt = layer.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 120f + (i * 120f));
                parallaxLayers.Add(rt);
                parallaxSpeeds.Add(12f + (i * 8f));
            }
        }

        private Text CreateLabel(string text, Vector2 anchor, int fontSize, TextAnchor align)
        {
            RectTransform root = canvas.transform as RectTransform;
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
            RectTransform root = canvas.transform as RectTransform;
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
            go.transform.SetParent(canvas.transform, false);
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
            RectTransform root = canvas.transform as RectTransform;
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
    }
}
