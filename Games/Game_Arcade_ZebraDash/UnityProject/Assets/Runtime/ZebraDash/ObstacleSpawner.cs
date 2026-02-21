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
        [SerializeField] private float spawnAheadSeconds = 2.2f;
        [SerializeField] private float spawnX = 17f;
        [SerializeField] private float laneStep = 1.5f;

        private BeatMap beatMap;
        private int tapIndex;
        private int holdIndex;
        private readonly List<BreathGapEvent> breathGaps = new List<BreathGapEvent>();

        public void Configure(BeatMap map)
        {
            beatMap = map ?? new BeatMap();
            tapIndex = 0;
            holdIndex = 0;
            breathGaps.Clear();
            if (beatMap.breathGaps != null)
            {
                breathGaps.AddRange(beatMap.breathGaps);
            }
        }

        private void Update()
        {
            if (beatMap == null || beatClock == null || !beatClock.IsRunning)
            {
                return;
            }

            float songTime = beatClock.SongTimeSec;
            float threshold = songTime + spawnAheadSeconds;

            while (beatMap.tapEvents != null && tapIndex < beatMap.tapEvents.Length && beatMap.tapEvents[tapIndex].timeSec <= threshold)
            {
                TapObstacleEvent tap = beatMap.tapEvents[tapIndex++];
                if (IsInsideBreathGap(tap.timeSec))
                {
                    continue;
                }

                SpawnTap(tap);
            }

            while (beatMap.holdEvents != null && holdIndex < beatMap.holdEvents.Length && beatMap.holdEvents[holdIndex].startSec <= threshold)
            {
                HoldObstacleEvent hold = beatMap.holdEvents[holdIndex++];
                if (IsInsideBreathGap(hold.startSec))
                {
                    continue;
                }

                SpawnHold(hold);
            }
        }

        private bool IsInsideBreathGap(float timeSec)
        {
            for (int i = 0; i < breathGaps.Count; i++)
            {
                BreathGapEvent gap = breathGaps[i];
                if (timeSec >= gap.startSec && timeSec <= gap.endSec)
                {
                    return true;
                }
            }

            return false;
        }

        private void SpawnTap(TapObstacleEvent tap)
        {
            GameObject obstacle = CreateObstacle(tap.prefabId, "TapObstacle");
            obstacle.transform.position = new Vector3(spawnX, tap.lane * laneStep, 0f);
        }

        private void SpawnHold(HoldObstacleEvent hold)
        {
            GameObject obstacle = CreateObstacle(hold.prefabId, "HoldObstacle");
            float yFrom = hold.laneFrom * laneStep;
            float yTo = hold.laneTo * laneStep;
            obstacle.transform.position = new Vector3(spawnX, yFrom, 0f);
            MovingObstacle mover = obstacle.GetComponent<MovingObstacle>();
            if (mover == null)
            {
                mover = obstacle.AddComponent<MovingObstacle>();
            }

            mover.ConfigureHold(hold.motion, yFrom, yTo, hold.endSec - hold.startSec);
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
