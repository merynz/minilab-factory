using System;
using System.Collections.Generic;
using System.Linq;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    public static class GameplayPatternKinds
    {
        public const string Jump = "Jump";
        public const string HoldSlide = "HoldSlide";
        public const string Rest = "Rest";
        public const string Fakeout = "Fakeout";
        public const string AccentPulse = "AccentPulse";
        public const string CameraShift = "CameraShift";
    }

    public static class GameplayPresentationKinds
    {
        public const string Straight = "Straight";
        public const string Diagonal = "Diagonal";
        public const string Drop = "Drop";
        public const string Pop = "Pop";
    }

    [Serializable]
    public sealed class GameplayPatternEvent
    {
        public string kind = GameplayPatternKinds.Jump;
        public float hitTimeSec;
        public float endTimeSec;
        public int lane;
        public float intensity = 0.6f;
        public bool isHazard = true;
        public float travelTimeSec = 1.25f;
        public string sourceKind = BeatKinds.Tap;
        public string presentation = GameplayPresentationKinds.Straight;
    }

    [Serializable]
    public sealed class GameplayPattern
    {
        public int seed;
        public float difficulty = 1f;
        public GameplayPatternEvent[] events = Array.Empty<GameplayPatternEvent>();
        public RestSectionEvent[] restSections = Array.Empty<RestSectionEvent>();
    }

    public static class GameplayPatternGenerator
    {
        public static GameplayPattern Build(BeatMap beatMap, string trackId, float difficulty = 1f)
        {
            if (beatMap == null)
            {
                return new GameplayPattern();
            }

            BeatEvent[] canonical = BeatMapEventUtils.GetCanonicalEvents(beatMap);
            RestSectionEvent[] rests = BeatMapEventUtils.GetRestSections(beatMap);
            if (rests == null || rests.Length == 0)
            {
                rests = BuildFallbackRests(canonical);
            }

            int seed = ComputeSeed(beatMap.seed, trackId);
            var rng = new System.Random(seed);
            float difficulty01 = Mathf.Clamp01(difficulty);
            float fakeoutChance = Mathf.Lerp(0.24f, 0.10f, difficulty01);

            var events = new List<GameplayPatternEvent>(canonical.Length * 2);
            int lane = 0;
            int laneStreak = 0;

            for (int i = 0; i < canonical.Length; i++)
            {
                BeatEvent source = canonical[i];
                if (source == null)
                {
                    continue;
                }

                if (BeatMapEventUtils.IsRestTime(beatMap, source.timeSec))
                {
                    continue;
                }

                lane = ResolveLane(source, lane, ref laneStreak, rng);
                float intensity = Mathf.Clamp01(source.intensity <= 0f ? 0.6f : source.intensity);

                if (source.IsKind(BeatKinds.Long))
                {
                    float endTime = Mathf.Max(source.GetEndTimeSec(), source.timeSec + 0.8f);
                    events.Add(new GameplayPatternEvent
                    {
                        kind = GameplayPatternKinds.HoldSlide,
                        hitTimeSec = source.timeSec,
                        endTimeSec = endTime,
                        lane = lane,
                        intensity = intensity,
                        isHazard = true,
                        travelTimeSec = 1.35f,
                        sourceKind = BeatKinds.Long,
                        presentation = ResolvePresentation(source, true, rng)
                    });
                    continue;
                }

                bool accent = source.IsKind(BeatKinds.Accent);
                bool fakeout = !accent && rng.NextDouble() < fakeoutChance;
                string kind = fakeout ? GameplayPatternKinds.Fakeout : GameplayPatternKinds.Jump;
                bool hazard = !fakeout;

                events.Add(new GameplayPatternEvent
                {
                    kind = kind,
                    hitTimeSec = source.timeSec,
                    endTimeSec = source.timeSec,
                    lane = lane,
                    intensity = intensity,
                    isHazard = hazard,
                    travelTimeSec = accent ? 1.35f : 1.25f,
                    sourceKind = accent ? BeatKinds.Accent : BeatKinds.Tap,
                    presentation = ResolvePresentation(source, hazard, rng)
                });

                if (accent)
                {
                    events.Add(new GameplayPatternEvent
                    {
                        kind = GameplayPatternKinds.AccentPulse,
                        hitTimeSec = source.timeSec,
                        endTimeSec = source.timeSec,
                        lane = lane,
                        intensity = intensity,
                        isHazard = false,
                        travelTimeSec = 0f,
                        sourceKind = BeatKinds.Accent,
                        presentation = GameplayPresentationKinds.Straight
                    });

                    events.Add(new GameplayPatternEvent
                    {
                        kind = GameplayPatternKinds.CameraShift,
                        hitTimeSec = source.timeSec,
                        endTimeSec = source.timeSec + 0.32f,
                        lane = lane,
                        intensity = intensity,
                        isHazard = false,
                        travelTimeSec = 0f,
                        sourceKind = BeatKinds.Accent,
                        presentation = GameplayPresentationKinds.Straight
                    });
                }
            }

            for (int i = 0; i < rests.Length; i++)
            {
                RestSectionEvent rest = rests[i];
                if (rest == null || rest.endSec <= rest.startSec)
                {
                    continue;
                }

                events.Add(new GameplayPatternEvent
                {
                    kind = GameplayPatternKinds.Rest,
                    hitTimeSec = rest.startSec,
                    endTimeSec = rest.endSec,
                    lane = 0,
                    intensity = 0.1f,
                    isHazard = false,
                    travelTimeSec = 0f,
                    sourceKind = BeatKinds.RestSection,
                    presentation = GameplayPresentationKinds.Straight
                });
            }

            GameplayPatternEvent[] sorted = events
                .OrderBy(e => e.hitTimeSec)
                .ThenBy(e => e.kind)
                .ToArray();

            return new GameplayPattern
            {
                seed = seed,
                difficulty = difficulty01,
                events = sorted,
                restSections = rests
            };
        }

        private static int ResolveLane(BeatEvent source, int currentLane, ref int currentStreak, System.Random rng)
        {
            int lane = Mathf.Clamp(source.lane, 0, 1);
            if (lane != currentLane)
            {
                currentLane = lane;
                currentStreak = 1;
                return currentLane;
            }

            currentStreak++;
            if (currentStreak >= 4)
            {
                currentLane = 1 - currentLane;
                currentStreak = 1;
                return currentLane;
            }

            // Add deterministic lane motion even when source lane is flat.
            if (rng.NextDouble() < 0.28d)
            {
                currentLane = 1 - currentLane;
                currentStreak = 1;
            }

            return currentLane;
        }

        private static string ResolvePresentation(BeatEvent source, bool hazard, System.Random rng)
        {
            if (source == null)
            {
                return GameplayPresentationKinds.Straight;
            }

            if (!hazard)
            {
                return GameplayPresentationKinds.Pop;
            }

            if (source.IsKind(BeatKinds.Long))
            {
                return GameplayPresentationKinds.Drop;
            }

            if (source.IsKind(BeatKinds.Accent))
            {
                return GameplayPresentationKinds.Diagonal;
            }

            int roll = Mathf.Abs(rng.Next()) % 3;
            return roll switch
            {
                0 => GameplayPresentationKinds.Straight,
                1 => GameplayPresentationKinds.Diagonal,
                _ => GameplayPresentationKinds.Drop
            };
        }

        private static RestSectionEvent[] BuildFallbackRests(BeatEvent[] events)
        {
            float maxTime = 0f;
            for (int i = 0; i < events.Length; i++)
            {
                BeatEvent evt = events[i];
                if (evt == null)
                {
                    continue;
                }

                maxTime = Mathf.Max(maxTime, evt.GetEndTimeSec());
            }

            if (maxTime <= 12f)
            {
                return Array.Empty<RestSectionEvent>();
            }

            var rest = new List<RestSectionEvent>();
            float cursor = 8f;
            while (cursor < maxTime - 2.5f)
            {
                rest.Add(new RestSectionEvent
                {
                    startSec = cursor,
                    endSec = Mathf.Min(maxTime - 0.5f, cursor + 2.2f)
                });
                cursor += 10f;
            }

            return rest.ToArray();
        }

        private static int ComputeSeed(int baseSeed, string trackId)
        {
            unchecked
            {
                int hash = 216613626;
                string id = string.IsNullOrWhiteSpace(trackId) ? "default_track" : trackId.Trim();
                for (int i = 0; i < id.Length; i++)
                {
                    hash ^= id[i];
                    hash *= 16777619;
                }

                return baseSeed ^ hash;
            }
        }
    }
}
