using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MiniLab.Core.Rhythm;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

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
        private BeatMap beatMap;

        private AudioSource audioSource;
        private BeatClock beatClock;
        private InputJudge inputJudge;

        private LevelRunner levelRunner;
        private ObstacleSpawner obstacleSpawner;
        private PlayerController playerController;

        private readonly List<float> syncDeltas = new List<float>(16);
        private float offsetSlider;
        private string lastJudgement = "-";
        private string currentSectionState = "Active";
        private int combo;
        private bool audioLoaded;

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
            catalog = ZebraDashCatalogIo.LoadCatalog();
            activeTrack = catalog.FindTrack(defaultTrackId);
            if (activeTrack == null && catalog.tracks.Length > 0)
            {
                activeTrack = catalog.tracks[0];
            }

            if (activeTrack == null)
            {
                yield break;
            }

            beatMap = ZebraDashCatalogIo.LoadBeatMap(activeTrack);
            offsetSlider = activeTrack.offsetSec;

            yield return LoadAudioOrMetronome();

            if (previewRunEnabled)
            {
                SetupPreviewRun();
            }

            beatClock.StartClock(audioSource, beatMap.bpm, activeTrack.offsetSec);
            audioLoaded = true;
        }

        private IEnumerator LoadAudioOrMetronome()
        {
            string absoluteAudioPath = ZebraDashPaths.ResolvePath(activeTrack.filePath);
            if (!string.IsNullOrWhiteSpace(absoluteAudioPath) && File.Exists(absoluteAudioPath))
            {
                string fileUrl = "file:///" + absoluteAudioPath.Replace("\\", "/");
                using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(fileUrl, AudioType.WAV);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
                    yield break;
                }
            }

            audioSource.clip = MetronomeClipFactory.Create(beatMap.bpm, 16);
            audioSource.loop = true;
        }

        private void SetupPreviewRun()
        {
            GameObject worldRoot = new GameObject("WorldRoot");
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(-4f, 0f, 0f);

            levelRunner = gameObject.AddComponent<LevelRunner>();
            SetPrivateField(levelRunner, "targetCamera", sceneCamera);
            SetPrivateField(levelRunner, "playerTransform", player.transform);
            SetPrivateField(levelRunner, "worldRoot", worldRoot.transform);

            playerController = player.AddComponent<PlayerController>();

            obstacleSpawner = gameObject.AddComponent<ObstacleSpawner>();
            SetPrivateField(obstacleSpawner, "beatClock", beatClock);
            SetPrivateField(obstacleSpawner, "levelRunner", levelRunner);
            obstacleSpawner.Configure(beatMap);
        }

        private static void SetPrivateField<T>(object instance, string fieldName, T value)
        {
            var field = instance.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(instance, value);
        }

        private void Update()
        {
            if (!audioLoaded || beatMap == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            {
                RegisterSyncTap();
            }

            currentSectionState = ResolveSectionState(beatClock.SongTimeSec);
        }

        private void RegisterSyncTap()
        {
            float songTime = beatClock.SongTimeSec;
            float spb = 60f / Mathf.Max(1f, beatMap.bpm);
            int nearestBeat = Mathf.RoundToInt(songTime / spb);
            float nearestBeatTime = nearestBeat * spb;
            float delta = songTime - nearestBeatTime;

            syncDeltas.Add(delta);
            if (syncDeltas.Count > 16)
            {
                syncDeltas.RemoveAt(0);
            }

            JudgeResult judge = inputJudge.Evaluate(nearestBeatTime, songTime);
            lastJudgement = judge.ToString();
            combo = judge == JudgeResult.Miss ? 0 : combo + 1;
        }

        private void SaveOffset()
        {
            if (activeTrack == null)
            {
                return;
            }

            activeTrack.offsetSec = offsetSlider;
            if (beatMap != null)
            {
                beatMap.offsetSec = offsetSlider;
                string levelPath = ZebraDashPaths.ResolvePath(activeTrack.levelPath);
                BeatMapJson.SaveToFile(levelPath, beatMap);
            }

            ZebraDashCatalogIo.SaveCatalog(catalog);
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
            offsetSlider -= avgDelta;
            syncDeltas.Clear();
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, 520, 210), "Beatmap Workbench");
            if (activeTrack == null || beatMap == null)
            {
                GUI.Label(new Rect(20, 40, 400, 20), "Track yok. analyze-audio ile catalog uretin.");
                return;
            }

            GUI.Label(new Rect(20, 40, 490, 20), $"Track: {activeTrack.trackId}");
            GUI.Label(new Rect(20, 60, 490, 20), $"BPM: {beatMap.bpm:F2}  Offset: {offsetSlider:F3}s");
            GUI.Label(new Rect(20, 80, 490, 20), $"Time: {beatClock.SongTimeSec:F2}s Beat: {beatClock.BeatFloat:F2} Bar: {beatClock.BarIndex + 1}");
            GUI.Label(new Rect(20, 100, 490, 20), $"Section: {currentSectionState}  Last Judge: {lastJudgement}  Combo: {combo}");
            GUI.Label(new Rect(20, 118, 490, 20), $"Offset(ms): {offsetSlider * 1000f:F1}");

            GUI.Label(new Rect(20, 138, 90, 20), "Offset");
            offsetSlider = GUI.HorizontalSlider(new Rect(72, 144, 210, 16), offsetSlider, -0.5f, 0.5f);
            if (GUI.Button(new Rect(290, 138, 90, 24), "Tap->Sync"))
            {
                ApplyTapSyncOffset();
            }

            if (GUI.Button(new Rect(390, 138, 120, 24), "Save Catalog"))
            {
                SaveOffset();
            }

            Rect timeline = new Rect(20, 158, 490, 42);
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
                    Color c = section.IsRest ? new Color(0.25f, 0.45f, 0.75f, 0.8f) : new Color(0.75f, 0.35f, 0.2f, 0.85f);
                    DrawSolidRect(new Rect(x0, rect.y + 2, Mathf.Max(1f, x1 - x0), rect.height - 4), c);
                }
            }

            if (beatMap.tapEvents != null)
            {
                for (int i = 0; i < beatMap.tapEvents.Length; i++)
                {
                    float x = rect.x + Mathf.Clamp01(beatMap.tapEvents[i].timeSec / duration) * rect.width;
                    DrawSolidRect(new Rect(x, rect.y + 2, 1f, rect.height - 4), Color.cyan);
                }
            }

            if (beatMap.holdEvents != null)
            {
                for (int i = 0; i < beatMap.holdEvents.Length; i++)
                {
                    HoldObstacleEvent hold = beatMap.holdEvents[i];
                    float x0 = rect.x + Mathf.Clamp01(hold.startSec / duration) * rect.width;
                    float x1 = rect.x + Mathf.Clamp01(hold.endSec / duration) * rect.width;
                    DrawSolidRect(new Rect(x0, rect.y + 10, Mathf.Max(2f, x1 - x0), rect.height - 20), Color.yellow);
                }
            }

            float playheadX = rect.x + Mathf.Clamp01(beatClock.SongTimeSec / duration) * rect.width;
            DrawSolidRect(new Rect(playheadX, rect.y, 2f, rect.height), Color.white);
        }

        private void DrawBeatGrid(Rect rect, float duration)
        {
            if (beatMap == null || beatMap.bpm <= 0.01f)
            {
                return;
            }

            float spb = 60f / beatMap.bpm;
            int beats = Mathf.Clamp(Mathf.CeilToInt(duration / spb), 0, 1024);
            for (int beat = 0; beat <= beats; beat++)
            {
                float t = beat * spb;
                float x = rect.x + Mathf.Clamp01(t / duration) * rect.width;
                bool isBar = (beat % 4) == 0;
                Color c = isBar ? new Color(1f, 1f, 1f, 0.25f) : new Color(1f, 1f, 1f, 0.1f);
                DrawSolidRect(new Rect(x, rect.y + 1, 1f, rect.height - 2), c);
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
                    return string.IsNullOrWhiteSpace(section.type) ? "Active" : section.type;
                }
            }

            return "Active";
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
    }
}
