using System;
using System.Collections;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    [DisallowMultipleComponent]
    public sealed class BeatmapWorkbenchController : MonoBehaviour
    {
        [SerializeField] private string defaultTrackId = "electro_dance_mania";
        [SerializeField] private bool previewRunEnabled = true;
        [SerializeField] private Camera sceneCamera;

        private MusicCatalog catalog;
        private MusicTrackEntry activeTrack;
        private int activeTrackIndex = -1;
        private BeatMap beatMap;
        private BeatEvent[] canonicalEvents = Array.Empty<BeatEvent>();

        private AudioSource audioSource;
        private BeatClock beatClock;
        private InputJudge inputJudge;

        private LevelRunner levelRunner;
        private ObstacleSpawner obstacleSpawner;
        private PlayerController playerController;

        private readonly List<float> syncDeltas = new List<float>(16);
        private float offsetSliderSec;
        private string lastTapJudge = "-";
        private bool loaded;
        private bool loadingTrack;
        private string sectionState = "Unknown";
        private string statusLine = "";

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            beatClock = GetComponent<BeatClock>();
            if (beatClock == null)
            {
                beatClock = gameObject.AddComponent<BeatClock>();
            }

            inputJudge = new InputJudge(new JudgeWindows());
            if (sceneCamera == null)
            {
                sceneCamera = Camera.main;
            }
        }

        private IEnumerator Start()
        {
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            statusLine = "music_catalog yukleniyor...";
            MusicCatalog loadedCatalog = null;
            string catalogError = "";
            yield return ZebraDashCatalogIo.LoadCatalogAsync((loaded, error) =>
            {
                loadedCatalog = loaded;
                catalogError = error ?? "";
            });

            catalog = loadedCatalog ?? new MusicCatalog();
            if (catalog?.tracks == null || catalog.tracks.Length == 0)
            {
                statusLine = "Track yok. tools/analyze-audio.ps1 ve tools/sync-zebradash-content.ps1 calistirin.";
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(catalogError))
            {
                statusLine = $"Catalog warning: {catalogError}";
            }

            activeTrackIndex = ResolveTrackIndex(defaultTrackId);
            yield return LoadTrackAndRun(activeTrackIndex);
        }

        private int ResolveTrackIndex(string preferredTrackId)
        {
            if (catalog?.tracks == null || catalog.tracks.Length == 0)
            {
                return -1;
            }

            if (!string.IsNullOrWhiteSpace(preferredTrackId))
            {
                for (int i = 0; i < catalog.tracks.Length; i++)
                {
                    if (string.Equals(catalog.tracks[i].trackId, preferredTrackId, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        private IEnumerator LoadTrackAndRun(int trackIndex)
        {
            if (loadingTrack)
            {
                yield break;
            }

            if (catalog?.tracks == null || catalog.tracks.Length == 0)
            {
                yield break;
            }

            loadingTrack = true;
            loaded = false;
            statusLine = "Track yukleniyor...";

            activeTrackIndex = Mathf.Clamp(trackIndex, 0, catalog.tracks.Length - 1);
            activeTrack = catalog.tracks[activeTrackIndex];
            BeatMap loadedMap = null;
            string mapError = "";
            yield return ZebraDashCatalogIo.LoadBeatMapAsync(activeTrack, (map, error) =>
            {
                loadedMap = map;
                mapError = error ?? "";
            });

            beatMap = loadedMap ?? new BeatMap
            {
                trackId = activeTrack.trackId,
                bpm = activeTrack.bpm,
                offsetSec = activeTrack.offsetSec
            };
            canonicalEvents = BeatMapEventUtils.GetCanonicalEvents(beatMap);

            offsetSliderSec = BeatClock.LoadTrackOffsetSec(activeTrack.trackId, activeTrack.offsetSec);
            beatMap.offsetSec = offsetSliderSec;

            yield return LoadAudioOrMetronome();
            if (!string.IsNullOrWhiteSpace(mapError))
            {
                statusLine = $"Beatmap warning: {mapError}";
            }

            if (previewRunEnabled)
            {
                SetupPreviewRun();
            }

            StartPlayableLoop();
            statusLine = "Hazir";
            loaded = true;
            loadingTrack = false;
        }

        private IEnumerator LoadAudioOrMetronome()
        {
            audioSource.Stop();
            audioSource.clip = null;

            AudioClip loadedClip = null;
            string audioError = "";
            yield return ZebraDashCatalogIo.LoadAudioClipAsync(activeTrack, (clip, error) =>
            {
                loadedClip = clip;
                audioError = error ?? "";
            });

            if (loadedClip != null)
            {
                audioSource.clip = loadedClip;
                audioSource.loop = false;
                yield break;
            }

            audioSource.clip = MetronomeClipFactory.Create(beatMap.bpm, 16);
            audioSource.loop = true;
            statusLine = string.IsNullOrWhiteSpace(audioError)
                ? "Audio yok -> metronom fallback."
                : $"Audio yok -> metronom fallback. ({audioError})";
        }

        private void SetupPreviewRun()
        {
            GameObject worldRoot = GameObject.Find("WorldRoot");
            if (worldRoot == null)
            {
                worldRoot = new GameObject("WorldRoot");
            }

            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.name = "Player";
                player.transform.position = new Vector3(-4f, 0f, 0f);
            }

            levelRunner = GetComponent<LevelRunner>();
            if (levelRunner == null)
            {
                levelRunner = gameObject.AddComponent<LevelRunner>();
            }

            obstacleSpawner = GetComponent<ObstacleSpawner>();
            if (obstacleSpawner == null)
            {
                obstacleSpawner = gameObject.AddComponent<ObstacleSpawner>();
            }

            playerController = player.GetComponent<PlayerController>();
            if (playerController == null)
            {
                playerController = player.AddComponent<PlayerController>();
            }

            levelRunner.ConfigureScene(sceneCamera, player.transform, worldRoot.transform);
            levelRunner.ConfigureDependencies(beatClock, audioSource, obstacleSpawner, playerController);
        }

        private void StartPlayableLoop()
        {
            if (levelRunner == null)
            {
                return;
            }

            beatClock.UpdateOffset(offsetSliderSec);
            levelRunner.StartRun(beatMap, activeTrack, offsetSliderSec);
        }

        private void Update()
        {
            if (!loaded || beatMap == null || levelRunner == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                RegisterSyncTap();
            }

            if (levelRunner.ConsumeRestartRequest())
            {
                StartPlayableLoop();
            }

            if (levelRunner.State == RunState.Completed && Input.GetKeyDown(KeyCode.N))
            {
                StartCoroutine(LoadTrackAndRun((activeTrackIndex + 1) % catalog.tracks.Length));
            }

            sectionState = ResolveSectionState(levelRunner.SongTimeSec);
        }

        private void RegisterSyncTap()
        {
            if (!beatClock.IsRunning)
            {
                return;
            }

            float songTime = beatClock.SongTimeSec;
            float secondsPerBeat = beatClock.SecondsPerBeat;
            int nearestBeat = Mathf.RoundToInt(songTime / Mathf.Max(0.0001f, secondsPerBeat));
            float nearestBeatTime = nearestBeat * secondsPerBeat;
            float delta = songTime - nearestBeatTime;
            syncDeltas.Add(delta);
            if (syncDeltas.Count > 16)
            {
                syncDeltas.RemoveAt(0);
            }

            JudgeResult judge = inputJudge.Evaluate(nearestBeatTime, songTime);
            lastTapJudge = judge.ToString();
        }

        private void ApplyTapSyncOffset()
        {
            if (syncDeltas.Count == 0)
            {
                return;
            }

            float sum = 0f;
            for (int i = 0; i < syncDeltas.Count; i++)
            {
                sum += syncDeltas[i];
            }

            float avgDelta = sum / syncDeltas.Count;
            offsetSliderSec -= avgDelta;
            syncDeltas.Clear();
            beatClock.UpdateOffset(offsetSliderSec);
            SaveOffset();
            statusLine = $"Tap sync uygulandi: {offsetSliderSec * 1000f:F1} ms";
        }

        private void SaveOffset()
        {
            if (activeTrack == null)
            {
                return;
            }

            activeTrack.offsetSec = offsetSliderSec;
            beatMap.offsetSec = offsetSliderSec;
            BeatClock.SaveTrackOffsetSec(activeTrack.trackId, offsetSliderSec);
            beatClock.UpdateOffset(offsetSliderSec);

            if (!Application.isEditor)
            {
                statusLine = "Offset cihazda lokal kaydedildi (PlayerPrefs).";
                return;
            }

            ZebraDashCatalogIo.SaveCatalog(catalog);

            string levelPath = ZebraDashPaths.ResolveLevelPathForRead(activeTrack.levelPath);
            if (!string.IsNullOrWhiteSpace(levelPath) && System.IO.File.Exists(levelPath))
            {
                BeatMapJson.SaveToFile(levelPath, beatMap);
            }
        }

        private string ResolveSectionState(float timeSec)
        {
            if (beatMap?.sections == null || beatMap.sections.Length == 0)
            {
                return "Unknown";
            }

            for (int i = 0; i < beatMap.sections.Length; i++)
            {
                BeatSection section = beatMap.sections[i];
                if (timeSec >= section.startSec && timeSec < section.endSec)
                {
                    return section.IsRest ? "Rest" : "Active";
                }
            }

            return "Active";
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, 760, 300), "ZebraDash Beatmap Workbench");
            if (activeTrack == null || beatMap == null || levelRunner == null)
            {
                GUI.Label(new Rect(20, 40, 730, 20), "Track yok. tools/analyze-audio.ps1 ve tools/sync-zebradash-content.ps1 calistirin.");
                if (!string.IsNullOrWhiteSpace(statusLine))
                {
                    GUI.Label(new Rect(20, 60, 730, 20), statusLine);
                }
                return;
            }

            GUI.Label(new Rect(20, 40, 730, 20), $"Track: {activeTrack.trackId} | State: {levelRunner.State} | Section: {sectionState}");
            GUI.Label(new Rect(20, 60, 730, 20), $"BPM: {beatMap.bpm:F2} | Combo: {levelRunner.Combo} | MaxCombo: {levelRunner.MaxCombo} | Score: {levelRunner.Score}");
            GUI.Label(new Rect(20, 80, 730, 20), $"dspNow: {beatClock.DspNow:F3} | dspStart: {beatClock.DspStartTime:F3}");
            GUI.Label(new Rect(20, 100, 730, 20), $"songTime: {levelRunner.SongTimeSec:F3}s | offsetMs: {offsetSliderSec * 1000f:F1} | nextBeatDelta: {beatClock.NextBeatDeltaSec:F3}s");
            GUI.Label(new Rect(20, 120, 730, 20), $"LastJudge: {levelRunner.LastJudge} | TapJudge: {lastTapJudge} | SyncSamples: {syncDeltas.Count}/16 | {statusLine}");

            if (levelRunner.State == RunState.Countdown)
            {
                GUI.Label(new Rect(20, 140, 300, 20), $"Countdown: {levelRunner.CountdownRemaining:F2}s");
            }
            else if (levelRunner.State == RunState.Failed)
            {
                GUI.Label(new Rect(20, 140, 500, 20), $"Fail: {levelRunner.FailReason} (R/tap restart)");
            }
            else if (levelRunner.State == RunState.Completed)
            {
                GUI.Label(new Rect(20, 140, 500, 20), "Completed: N=next, R/tap=restart");
            }

            GUI.Label(new Rect(20, 166, 90, 20), "Offset");
            float oldOffset = offsetSliderSec;
            offsetSliderSec = GUI.HorizontalSlider(new Rect(72, 172, 220, 16), offsetSliderSec, -0.5f, 0.5f);
            if (Mathf.Abs(oldOffset - offsetSliderSec) > 0.0001f)
            {
                beatClock.UpdateOffset(offsetSliderSec);
            }

            if (GUI.Button(new Rect(300, 166, 95, 24), "Tap->Sync"))
            {
                RegisterSyncTap();
                if (syncDeltas.Count >= 16)
                {
                    ApplyTapSyncOffset();
                }
            }

            if (GUI.Button(new Rect(402, 166, 95, 24), "Apply Sync"))
            {
                ApplyTapSyncOffset();
            }

            if (GUI.Button(new Rect(504, 166, 95, 24), "Save Offset"))
            {
                SaveOffset();
            }

            if (GUI.Button(new Rect(606, 166, 70, 24), "Restart"))
            {
                StartPlayableLoop();
            }

            if (GUI.Button(new Rect(682, 166, 70, 24), "Next"))
            {
                StartCoroutine(LoadTrackAndRun((activeTrackIndex + 1) % catalog.tracks.Length));
            }

            if (GUI.Button(new Rect(682, 140, 70, 22), "Exit"))
            {
                ExitApp();
            }

            Rect timeline = new Rect(20, 202, 730, 90);
            GUI.Box(timeline, GUIContent.none);
            DrawTimeline(timeline);
        }

        private void DrawTimeline(Rect rect)
        {
            float duration = Mathf.Max(1f, activeTrack.durationSec > 0f ? activeTrack.durationSec : 60f);
            DrawBeatGrid(rect, duration);

            if (beatMap.sections != null)
            {
                for (int i = 0; i < beatMap.sections.Length; i++)
                {
                    BeatSection section = beatMap.sections[i];
                    float x0 = rect.x + Mathf.Clamp01(section.startSec / duration) * rect.width;
                    float x1 = rect.x + Mathf.Clamp01(section.endSec / duration) * rect.width;
                    Color c = section.IsRest ? new Color(0.22f, 0.42f, 0.72f, 0.75f) : new Color(0.78f, 0.34f, 0.22f, 0.75f);
                    DrawSolidRect(new Rect(x0, rect.y + 2, Mathf.Max(1f, x1 - x0), rect.height - 4), c);
                }
            }

            for (int i = 0; i < canonicalEvents.Length; i++)
            {
                BeatEvent evt = canonicalEvents[i];
                if (evt == null)
                {
                    continue;
                }

                float x = rect.x + Mathf.Clamp01(evt.timeSec / duration) * rect.width;
                if (evt.IsKind("Tap"))
                {
                    DrawSolidRect(new Rect(x, rect.y + 4, 2f, rect.height - 8), Color.cyan);
                }
                else if (evt.IsKind(BeatKinds.Long) || evt.IsKind(BeatKinds.HoldLegacy))
                {
                    float xEnd = rect.x + Mathf.Clamp01(evt.GetEndTimeSec() / duration) * rect.width;
                    DrawSolidRect(new Rect(x, rect.y + 20, Mathf.Max(2f, xEnd - x), rect.height - 40), Color.yellow);
                }
                else if (evt.IsKind("Accent"))
                {
                    DrawSolidRect(new Rect(x, rect.y + 4, 1f, rect.height - 8), new Color(1f, 0.8f, 0.25f));
                }
            }

            float playheadX = rect.x + Mathf.Clamp01(levelRunner.SongTimeSec / duration) * rect.width;
            DrawSolidRect(new Rect(playheadX, rect.y, 2f, rect.height), Color.white);
        }

        private void DrawBeatGrid(Rect rect, float duration)
        {
            float spb = 60f / Mathf.Max(1f, beatMap.bpm);
            int beats = Mathf.Clamp(Mathf.CeilToInt(duration / spb), 0, 2048);
            for (int beat = 0; beat <= beats; beat++)
            {
                float t = beat * spb;
                float x = rect.x + Mathf.Clamp01(t / duration) * rect.width;
                bool isBar = beat % 4 == 0;
                Color c = isBar ? new Color(1f, 1f, 1f, 0.23f) : new Color(1f, 1f, 1f, 0.08f);
                DrawSolidRect(new Rect(x, rect.y + 1, 1f, rect.height - 2), c);
            }
        }

        private static Texture2D solidTexture;

        private static void DrawSolidRect(Rect rect, Color color)
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(1, 1);
            }

            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, solidTexture);
            GUI.color = old;
        }

        private static void ExitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
