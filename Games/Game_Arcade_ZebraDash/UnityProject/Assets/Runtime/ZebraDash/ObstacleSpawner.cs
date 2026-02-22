using System;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ObstacleSpawner : MonoBehaviour
    {
        [SerializeField] private BeatClock beatClock;
        [SerializeField] private PrefabRegistry prefabRegistry;
        [SerializeField] private LevelRunner levelRunner;
        [SerializeField] private float spawnX = 14f;
        [SerializeField] private float laneStep = 1.5f;
        [SerializeField] private float lookAheadSec = 0.02f;

        private BeatMap beatMap;
        private BeatEvent[] canonicalEvents = Array.Empty<BeatEvent>();
        private SpawnDirective[] spawnDirectives = Array.Empty<SpawnDirective>();
        private int spawnIndex;

        [Serializable]
        private struct SpawnDirective
        {
            public BeatEvent Event;
            public float SpawnTimeSec;
            public int Seed;
        }

        public BeatEvent[] CanonicalEvents => canonicalEvents;

        public void Configure(BeatMap map)
        {
            beatMap = map ?? new BeatMap();
            canonicalEvents = BeatMapEventUtils.GetCanonicalEvents(beatMap);
            spawnIndex = 0;
            BuildSpawnDirectives();
        }

        private void Awake()
        {
            if (beatClock == null)
            {
                beatClock = GetComponent<BeatClock>();
            }

            if (levelRunner == null)
            {
                levelRunner = GetComponent<LevelRunner>();
            }
        }

        private void Update()
        {
            if (beatClock == null || levelRunner == null || !beatClock.IsRunning || spawnDirectives.Length == 0)
            {
                return;
            }

            float songTime = beatClock.SongTimeSec;
            UpdateSectionState(songTime);

            while (spawnIndex < spawnDirectives.Length && songTime + lookAheadSec >= spawnDirectives[spawnIndex].SpawnTimeSec)
            {
                SpawnDirective directive = spawnDirectives[spawnIndex++];
                HandleDirective(directive);
            }
        }

        private void BuildSpawnDirectives()
        {
            float speed = Mathf.Max(0.1f, levelRunner != null ? levelRunner.ScrollSpeed : 7f);
            float hitX = levelRunner != null ? levelRunner.HitLineX : -4f;
            float travelTime = Mathf.Max(0.01f, (spawnX - hitX) / speed);

            var directives = new List<SpawnDirective>(canonicalEvents.Length);
            for (int i = 0; i < canonicalEvents.Length; i++)
            {
                BeatEvent evt = canonicalEvents[i];
                if (evt == null)
                {
                    continue;
                }

                directives.Add(new SpawnDirective
                {
                    Event = evt,
                    SpawnTimeSec = evt.timeSec - travelTime,
                    Seed = beatMap.seed + (i * 17)
                });
            }

            directives.Sort((a, b) => a.SpawnTimeSec.CompareTo(b.SpawnTimeSec));
            spawnDirectives = directives.ToArray();
        }

        private void HandleDirective(SpawnDirective directive)
        {
            BeatEvent evt = directive.Event;
            if (evt == null)
            {
                return;
            }

            if (evt.IsKind("Accent"))
            {
                levelRunner.TriggerAccentPulse(Mathf.Clamp01(evt.intensity));
                return;
            }

            if (evt.IsKind("Gap"))
            {
                return;
            }

            SpawnObstacle(directive);
        }

        private void SpawnObstacle(SpawnDirective directive)
        {
            BeatEvent evt = directive.Event;
            string fallbackName = evt.IsKind("Hold") ? "HoldObstacle" : "TapObstacle";
            GameObject obstacle = CreateObstacle(evt.prefabId, fallbackName);

            float laneY = evt.lane * laneStep;
            obstacle.transform.position = new Vector3(spawnX, laneY, 0f);

            ObstacleKinematics kinematics = obstacle.GetComponent<ObstacleKinematics>();
            if (kinematics == null)
            {
                kinematics = obstacle.AddComponent<ObstacleKinematics>();
            }

            float hitX = levelRunner != null ? levelRunner.HitLineX : -4f;
            float speed = Mathf.Max(0.1f, levelRunner != null ? levelRunner.ScrollSpeed : 7f);
            float frequency = 3f + (Mathf.Clamp01(evt.intensity) * 4f);
            float wobbleAmp = evt.IsKind("Hold") ? 0.08f : Mathf.Lerp(0.05f, 0.22f, Mathf.Clamp01(evt.intensity));
            kinematics.Configure(
                beatClock,
                directive.SpawnTimeSec,
                evt.timeSec,
                spawnX,
                hitX,
                laneY,
                speed,
                evt.kind,
                Mathf.Max(0f, evt.durationSec),
                wobbleAmp,
                frequency,
                directive.Seed);
        }

        private void UpdateSectionState(float songTime)
        {
            if (levelRunner == null || beatMap?.sections == null)
            {
                return;
            }

            bool isRest = false;
            for (int i = 0; i < beatMap.sections.Length; i++)
            {
                BeatSection section = beatMap.sections[i];
                if (songTime >= section.startSec && songTime < section.endSec)
                {
                    isRest = section.IsRest;
                    break;
                }
            }

            levelRunner.SetRestSection(isRest);
        }

        private GameObject CreateObstacle(string prefabId, string fallbackName)
        {
            GameObject source = prefabRegistry != null ? prefabRegistry.GetPrefab(prefabId) : null;
            GameObject obstacle;
            if (source != null)
            {
                obstacle = Instantiate(source, levelRunner != null ? levelRunner.WorldRoot : transform);
            }
            else
            {
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.SetParent(levelRunner != null ? levelRunner.WorldRoot : transform, false);
                obstacle.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
                Renderer renderer = obstacle.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = fallbackName == "HoldObstacle" ? new Color(1f, 0.55f, 0.2f) : new Color(0.2f, 0.9f, 1f);
                }
            }

            obstacle.name = fallbackName;
            return obstacle;
        }
    }
}
