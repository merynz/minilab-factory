using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ObstacleSpawner : MonoBehaviour
    {
        [SerializeField] private LevelRunner levelRunner;
        [SerializeField] private float spawnX = 14f;
        [SerializeField] private float hitX = -4f;
        [SerializeField] private float lowerLaneY = -1.2f;
        [SerializeField] private float upperLaneY = 1.2f;
        [SerializeField] private float minSpawnLeadUnits = 20f;

        private readonly List<SpawnDirective> directives = new List<SpawnDirective>(1024);
        private readonly List<ObstacleKinematics> active = new List<ObstacleKinematics>(128);
        private readonly Stack<ObstacleKinematics> pool = new Stack<ObstacleKinematics>(128);
        private GameplayPattern pattern;
        private int spawnIndex;
        private int createdCount;

        [Serializable]
        public struct SpawnDirective
        {
            public GameplayPatternEvent PatternEvent;
            public float SpawnTimeSec;
            public float TravelTimeSec;
            public float HitTimeSec;
            public float EndTimeSec;
            public int Seed;
        }

        public IReadOnlyList<SpawnDirective> Directives => directives;
        public GameplayPattern Pattern => pattern;
        public int ActiveCount => active.Count;
        public int PoolCount => pool.Count;
        public int CreatedCount => createdCount;

        public void Configure(GameplayPattern sourcePattern)
        {
            if (levelRunner == null)
            {
                levelRunner = GetComponent<LevelRunner>();
            }

            SyncAnchorsWithRunner();
            pattern = sourcePattern ?? new GameplayPattern();
            directives.Clear();
            active.Clear();
            spawnIndex = 0;

            if (pattern.events == null || pattern.events.Length == 0)
            {
                return;
            }

            for (int i = 0; i < pattern.events.Length; i++)
            {
                GameplayPatternEvent evt = pattern.events[i];
                if (evt == null || !IsSpawnable(evt.kind))
                {
                    continue;
                }

                float travel = ResolveTravelTime(evt);
                float hitTime = evt.hitTimeSec;
                float endTime = Mathf.Max(evt.endTimeSec, hitTime);

                directives.Add(new SpawnDirective
                {
                    PatternEvent = evt,
                    TravelTimeSec = travel,
                    HitTimeSec = hitTime,
                    EndTimeSec = endTime,
                    SpawnTimeSec = hitTime - travel,
                    Seed = pattern.seed + (i * 37)
                });
            }

            directives.Sort((a, b) => a.SpawnTimeSec.CompareTo(b.SpawnTimeSec));
        }

        public void Tick(float nowSec)
        {
            while (spawnIndex < directives.Count && nowSec >= directives[spawnIndex].SpawnTimeSec)
            {
                Spawn(directives[spawnIndex]);
                spawnIndex++;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                ObstacleKinematics obstacle = active[i];
                if (!obstacle.Tick(nowSec))
                {
                    active.RemoveAt(i);
                    obstacle.ResetVisual();
                    pool.Push(obstacle);
                }
            }
        }

        public void ResetAll()
        {
            for (int i = 0; i < active.Count; i++)
            {
                active[i].ResetVisual();
                pool.Push(active[i]);
            }

            active.Clear();
            spawnIndex = 0;
        }

        private void Spawn(SpawnDirective directive)
        {
            GameplayPatternEvent patternEvent = directive.PatternEvent;
            if (patternEvent == null)
            {
                return;
            }

            if (directive.SpawnTimeSec > directive.HitTimeSec + 0.001f)
            {
                Debug.LogError($"[ZebraDash] Invalid spawn timing for {patternEvent.archetype}: spawn {directive.SpawnTimeSec:F3} > hit {directive.HitTimeSec:F3}");
            }

            ObstacleKinematics obstacle = pool.Count > 0 ? pool.Pop() : CreateNewObstacle();
            obstacle.gameObject.SetActive(true);

            int lane = Mathf.Clamp(patternEvent.lane, 0, 1);
            float laneY = lane == 0 ? lowerLaneY : upperLaneY;
            obstacle.Configure(
                eventKind: patternEvent.kind,
                presentationKind: patternEvent.presentation,
                lane: lane,
                spawnTime: directive.SpawnTimeSec,
                hitTime: directive.HitTimeSec,
                endTime: directive.EndTimeSec,
                startX: spawnX,
                targetX: hitX,
                travelTime: directive.TravelTimeSec,
                beatSec: levelRunner != null ? Mathf.Max(0.0001f, levelRunner.BeatSec) : 0.5f,
                targetLaneY: laneY,
                eventIntensity: patternEvent.intensity,
                seed: directive.Seed);

            ConfigureVisual(obstacle, patternEvent);
            active.Add(obstacle);
        }

        private ObstacleKinematics CreateNewObstacle()
        {
            Transform parent = levelRunner != null && levelRunner.WorldRoot != null
                ? levelRunner.WorldRoot
                : transform;
            GameObject go = RuntimeSpriteFactory.Create(
                "Obstacle",
                parent,
                Vector3.zero,
                new Vector3(1f, 1f, 1f),
                sortingOrder: 22);

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = BuildDefaultMaterial();
            }

            ObstacleKinematics obstacle = go.GetComponent<ObstacleKinematics>();
            if (obstacle == null)
            {
                obstacle = go.AddComponent<ObstacleKinematics>();
            }

            go.SetActive(false);
            createdCount++;
            return obstacle;
        }

        private static Material BuildDefaultMaterial()
        {
            Material material = RenderMaterialUtils.CreateSolidMaterial(new Color(0.16f, 0.9f, 0.95f, 1f));
            return material ?? new Material(Shader.Find("Sprites/Default"));
        }

        private static void ConfigureVisual(ObstacleKinematics obstacle, GameplayPatternEvent evt)
        {
            var renderer = obstacle.GetComponent<Renderer>();
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            Color color;
            if (string.Equals(evt.kind, GameplayPatternKinds.HoldSlide, StringComparison.OrdinalIgnoreCase))
            {
                color = new Color(0.95f, 0.88f, 0.28f, 1f);
            }
            else if (string.Equals(evt.archetype, GameplayArchetypes.AccentCrusher, StringComparison.OrdinalIgnoreCase))
            {
                color = new Color(1f, 0.48f, 0.22f, 1f);
            }
            else if (string.Equals(evt.archetype, GameplayArchetypes.CrossGate, StringComparison.OrdinalIgnoreCase))
            {
                color = new Color(0.98f, 0.62f, 0.24f, 1f);
            }
            else if (string.Equals(evt.kind, GameplayPatternKinds.Fakeout, StringComparison.OrdinalIgnoreCase))
            {
                color = new Color(0.78f, 0.78f, 0.82f, 0.35f);
            }
            else if (evt.intensity >= 0.85f)
            {
                color = new Color(1f, 0.54f, 0.26f, 1f);
            }
            else
            {
                color = new Color(0.16f, 0.9f, 0.95f, evt.isHazard ? 1f : 0.45f);
            }

            Color laneTint = evt.lane <= 0
                ? new Color(0.30f, 0.92f, 1f, color.a)
                : new Color(1f, 0.52f, 0.88f, color.a);
            color = Color.Lerp(color, laneTint, 0.24f);
            RenderMaterialUtils.ApplyColor(renderer.material, color);
        }

        private static bool IsSpawnable(string kind)
        {
            return string.Equals(kind, GameplayPatternKinds.Jump, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, GameplayPatternKinds.HoldSlide, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, GameplayPatternKinds.Fakeout, StringComparison.OrdinalIgnoreCase);
        }

        private float ResolveTravelTime(GameplayPatternEvent evt)
        {
            float beatSec = levelRunner != null ? Mathf.Max(0.0001f, levelRunner.BeatSec) : 0.5f;
            float minVisibleSec = Mathf.Clamp(1.60f * beatSec, 0.82f, 1.25f);
            if (evt != null && evt.travelTimeSec > 0.01f)
            {
                return Mathf.Max(evt.travelTimeSec, minVisibleSec);
            }

            if (evt != null && string.Equals(evt.kind, GameplayPatternKinds.HoldSlide, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Max(1.55f, minVisibleSec);
            }

            return Mathf.Max(1.35f, minVisibleSec);
        }

        private void SyncAnchorsWithRunner()
        {
            if (levelRunner == null)
            {
                return;
            }

            hitX = levelRunner.HitLineX;
            float desiredSpawnX = hitX + Mathf.Max(10f, minSpawnLeadUnits);
            if (spawnX < desiredSpawnX)
            {
                spawnX = desiredSpawnX;
            }
        }
    }
}
