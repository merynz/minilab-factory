using System;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ObstacleSpawner : MonoBehaviour
    {
        [SerializeField] private BeatClock beatClock;
        [SerializeField] private LevelRunner levelRunner;
        [SerializeField] private float spawnX = 14f;
        [SerializeField] private float hitX = -4f;
        [SerializeField] private float lowerLaneY = -1.2f;
        [SerializeField] private float upperLaneY = 1.2f;

        private readonly List<SpawnDirective> directives = new List<SpawnDirective>(1024);
        private readonly List<ObstacleKinematics> active = new List<ObstacleKinematics>(128);
        private readonly Stack<ObstacleKinematics> pool = new Stack<ObstacleKinematics>(128);
        private static Material cachedObstacleMaterial;

        private int spawnIndex;

        [Serializable]
        public struct SpawnDirective
        {
            public BeatEvent Event;
            public float SpawnTimeSec;
            public float TravelTimeSec;
            public float HitTimeSec;
            public float EndTimeSec;
            public int Seed;
        }

        public IReadOnlyList<SpawnDirective> Directives => directives;

        public void Configure(BeatMap map)
        {
            if (beatClock == null)
            {
                beatClock = GetComponent<BeatClock>();
            }

            if (levelRunner == null)
            {
                levelRunner = GetComponent<LevelRunner>();
            }

            directives.Clear();
            active.Clear();
            spawnIndex = 0;

            BeatEvent[] canonical = BeatMapEventUtils.GetCanonicalEvents(map);
            for (int i = 0; i < canonical.Length; i++)
            {
                BeatEvent evt = canonical[i];
                if (evt == null)
                {
                    continue;
                }

                if (!(evt.IsKind(BeatKinds.Tap) || evt.IsKind(BeatKinds.Accent) || evt.IsKind(BeatKinds.Long)))
                {
                    continue;
                }

                if (BeatMapEventUtils.IsRestTime(map, evt.timeSec))
                {
                    continue;
                }

                float travel = ResolveTravelTime(evt.kind);
                float hitTime = evt.timeSec;
                float endTime = evt.IsKind(BeatKinds.Long) ? evt.GetEndTimeSec() : hitTime;
                directives.Add(new SpawnDirective
                {
                    Event = evt,
                    TravelTimeSec = travel,
                    HitTimeSec = hitTime,
                    EndTimeSec = endTime,
                    SpawnTimeSec = hitTime - travel,
                    Seed = map.seed + (i * 37)
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
            ObstacleKinematics obstacle = pool.Count > 0 ? pool.Pop() : CreateNewObstacle();
            obstacle.gameObject.SetActive(true);

            int lane = Mathf.Clamp(directive.Event.lane, 0, 1);
            float laneY = lane == 0 ? lowerLaneY : upperLaneY;
            obstacle.Configure(
                beatClock,
                directive.Event.kind,
                lane,
                directive.SpawnTimeSec,
                directive.HitTimeSec,
                directive.EndTimeSec,
                spawnX,
                hitX,
                directive.TravelTimeSec,
                laneY,
                directive.Event.intensity,
                directive.Seed);

            active.Add(obstacle);
        }

        private ObstacleKinematics CreateNewObstacle()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Obstacle";
            if (levelRunner != null && levelRunner.WorldRoot != null)
            {
                go.transform.SetParent(levelRunner.WorldRoot, false);
            }
            else
            {
                go.transform.SetParent(transform, false);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material = GetObstacleMaterial();
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }
                else
                {
                    renderer.material.color = new Color(0.16f, 0.9f, 0.95f, 1f);
                }
            }

            ObstacleKinematics obstacle = go.GetComponent<ObstacleKinematics>();
            if (obstacle == null)
            {
                obstacle = go.AddComponent<ObstacleKinematics>();
            }

            go.SetActive(false);
            return obstacle;
        }

        private static float ResolveTravelTime(string kind)
        {
            if (string.Equals(kind, BeatKinds.Accent, StringComparison.OrdinalIgnoreCase))
            {
                return 1.35f;
            }

            if (string.Equals(kind, BeatKinds.Long, StringComparison.OrdinalIgnoreCase))
            {
                return 1.25f;
            }

            return 1.25f;
        }

        private static Material GetObstacleMaterial()
        {
            if (cachedObstacleMaterial != null)
            {
                return cachedObstacleMaterial;
            }

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return null;
            }

            cachedObstacleMaterial = new Material(shader)
            {
                color = new Color(0.16f, 0.9f, 0.95f, 1f)
            };
            return cachedObstacleMaterial;
        }
    }
}
