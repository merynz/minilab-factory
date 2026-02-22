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
        public const string Rise = "Rise";
    }

    public static class GameplayArchetypes
    {
        public const string LaneBlock = "LaneBlock";
        public const string AccentCrusher = "AccentCrusher";
        public const string AlternatorPair = "AlternatorPair";
        public const string StreakBreaker = "StreakBreaker";
        public const string HoldLaneLock = "HoldLaneLock";
        public const string HoldReleaseGate = "HoldReleaseGate";
        public const string CrossGate = "CrossGate";
        public const string OffbeatSnap = "OffbeatSnap";
        public const string FakeoutGhost = "FakeoutGhost";
        public const string RestPulse = "RestPulse";
    }

    public static class GameplaySectionTypes
    {
        public const string Rest = "Rest";
        public const string Active = "Active";
        public const string Drop = "Drop";
        public const string Transition = "Transition";
    }

    [Serializable]
    public sealed class GameplayPatternEvent
    {
        public string kind = GameplayPatternKinds.Jump;
        public string archetype = GameplayArchetypes.LaneBlock;
        public string sectionType = GameplaySectionTypes.Active;
        public float hitTimeSec;
        public float endTimeSec;
        public int lane;
        public float intensity = 0.6f;
        public bool isHazard = true;
        public float travelTimeSec = 1.25f;
        public string sourceKind = BeatKinds.Tap;
        public string presentation = GameplayPresentationKinds.Straight;
        public string presetId = "";
    }

    [Serializable]
    public sealed class GameplayPatternSectionInfo
    {
        public int index;
        public string sectionType = GameplaySectionTypes.Active;
        public string presetId = "";
        public float startSec;
        public float endSec;
        public float targetStrain;
        public float currentStrain;
        public float density;
        public float switchFreq;
        public float holdLoad;
        public float offbeatRatio;
        public float accentDensity;
    }

    [Serializable]
    public sealed class GameplayPattern
    {
        public int seed;
        public float difficulty = 1f;
        public GameplayPatternEvent[] events = Array.Empty<GameplayPatternEvent>();
        public RestSectionEvent[] restSections = Array.Empty<RestSectionEvent>();
        public GameplayPatternSectionInfo[] sections = Array.Empty<GameplayPatternSectionInfo>();
    }

    public static class GameplayPatternGenerator
    {
        private readonly struct PresetDef
        {
            public PresetDef(string id, float targetStrain, string sectionMask)
            {
                Id = id;
                TargetStrain = targetStrain;
                SectionMask = sectionMask;
            }

            public string Id { get; }
            public float TargetStrain { get; }
            public string SectionMask { get; }

            public bool Supports(string sectionType)
            {
                char key = SectionToMask(sectionType);
                return SectionMask.IndexOf(key) >= 0;
            }
        }

        private struct SectionWindow
        {
            public int Index;
            public string Type;
            public float StartSec;
            public float EndSec;
        }

        private struct SectionMetrics
        {
            public float Density;
            public float SwitchFreq;
            public float HoldLoad;
            public float OffbeatRatio;
            public float AccentDensity;
            public float Strain;
        }

        private static readonly PresetDef[] Presets =
        {
            new("ALT_1212_1BAR", 0.40f, "A"),
            new("ALT_1212_2BAR_ACCENT_END", 0.48f, "AD"),
            new("STREAK3_BREAK", 0.52f, "AD"),
            new("STREAK2_SYNCOPATED", 0.57f, "AD"),
            new("HOLD_SHORT_RELEASE", 0.60f, "AD"),
            new("HOLD_LONG_SAFE", 0.46f, "A"),
            new("DOUBLE_SWAP_PAIR", 0.58f, "AD"),
            new("CROSS_GATE_BRIDGE", 0.62f, "AD"),
            new("BUILD_RAMP_4BAR", 0.70f, "DT"),
            new("DROP_DENSE_ACCENTED", 0.82f, "D"),
            new("REST_RESET_2TO4S", 0.14f, "R"),
            new("FAKEOUT_BRIDGE_TO_DROP", 0.55f, "AT")
        };

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
            float beatSec = 60f / Mathf.Max(1f, beatMap.bpm);
            float difficulty01 = Mathf.Clamp01(difficulty);
            var events = new List<GameplayPatternEvent>(canonical.Length * 4);
            var sectionInfos = new List<GameplayPatternSectionInfo>();
            var sections = BuildSectionWindows(beatMap, rests, canonical);

            int laneState = 0;
            int laneStreak = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                SectionWindow section = sections[i];
                var sourceEvents = canonical
                    .Where(e => e != null && e.timeSec >= section.StartSec && e.timeSec <= section.EndSec)
                    .OrderBy(e => e.timeSec)
                    .ToList();

                var sectionRng = new System.Random(seed ^ (section.Index * 131071));
                SectionMetrics metrics = ComputeMetrics(sourceEvents, beatMap.bpm, section.EndSec - section.StartSec);
                float targetStrain = ResolveTargetStrain(section.Type, metrics.Strain, difficulty01, sectionRng);
                PresetDef preset = SelectPreset(section.Type, targetStrain, sectionRng);

                sectionInfos.Add(new GameplayPatternSectionInfo
                {
                    index = section.Index,
                    sectionType = section.Type,
                    presetId = preset.Id,
                    startSec = section.StartSec,
                    endSec = section.EndSec,
                    targetStrain = targetStrain,
                    currentStrain = metrics.Strain,
                    density = metrics.Density,
                    switchFreq = metrics.SwitchFreq,
                    holdLoad = metrics.HoldLoad,
                    offbeatRatio = metrics.OffbeatRatio,
                    accentDensity = metrics.AccentDensity
                });

                if (string.Equals(section.Type, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
                {
                    AddRestPulse(events, section, preset.Id);
                    continue;
                }

                for (int j = 0; j < sourceEvents.Count; j++)
                {
                    BeatEvent source = sourceEvents[j];
                    if (source == null || BeatMapEventUtils.IsRestTime(beatMap, source.timeSec))
                    {
                        continue;
                    }

                    string archetype = SelectArchetype(preset.Id, source, j, sourceEvents.Count, sectionRng);
                    int lane = ResolveLane(ref laneState, ref laneStreak, source.lane, archetype, sectionRng);
                    float intensity = Mathf.Clamp01(source.intensity <= 0f ? 0.6f : source.intensity);
                    EmitArchetypeEvents(events, beatMap, section, preset.Id, source, archetype, lane, intensity, beatSec, sectionRng);
                }
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
                restSections = rests,
                sections = sectionInfos.ToArray()
            };
        }

        private static void EmitArchetypeEvents(
            List<GameplayPatternEvent> output,
            BeatMap beatMap,
            SectionWindow section,
            string presetId,
            BeatEvent source,
            string archetype,
            int lane,
            float intensity,
            float beatSec,
            System.Random rng)
        {
            switch (archetype)
            {
                case GameplayArchetypes.AccentCrusher:
                    AddJump(output, source.timeSec, lane, section.Type, presetId, archetype, Mathf.Max(0.75f, intensity), 1.35f, BeatKinds.Accent);
                    AddAccentFx(output, source.timeSec, section.Type, presetId, intensity);
                    break;
                case GameplayArchetypes.AlternatorPair:
                    AddJump(output, source.timeSec, lane, section.Type, presetId, archetype, intensity, 1.25f, BeatKinds.Tap);
                    TryAddJump(output, beatMap, source.timeSec + (beatSec * Mathf.Lerp(0.40f, 0.80f, (float)rng.NextDouble())), 1 - lane, section, presetId, archetype, intensity * 0.95f, 1.25f);
                    break;
                case GameplayArchetypes.StreakBreaker:
                    AddJump(output, source.timeSec, 1 - lane, section.Type, presetId, archetype, intensity, 1.30f, BeatKinds.Tap);
                    break;
                case GameplayArchetypes.HoldLaneLock:
                    AddHold(output, source.timeSec, ResolveHoldEnd(source, section.EndSec, beatSec, false, rng), lane, section.Type, presetId, archetype, intensity);
                    break;
                case GameplayArchetypes.HoldReleaseGate:
                {
                    float holdEnd = ResolveHoldEnd(source, section.EndSec, beatSec, true, rng);
                    AddHold(output, source.timeSec, holdEnd, lane, section.Type, presetId, archetype, Mathf.Max(0.7f, intensity));
                    float release = Mathf.Min(section.EndSec - 0.02f, holdEnd + (beatSec * Mathf.Lerp(0.25f, 0.50f, (float)rng.NextDouble())));
                    TryAddJump(output, beatMap, release, 1 - lane, section, presetId, archetype, intensity, 1.28f, BeatKinds.Accent);
                    AddAccentFx(output, release, section.Type, presetId, intensity);
                    break;
                }
                case GameplayArchetypes.CrossGate:
                    AddJump(output, source.timeSec, lane, section.Type, presetId, archetype, intensity, 1.32f, BeatKinds.Tap);
                    TryAddJump(output, beatMap, source.timeSec + (beatSec * 0.5f), 1 - lane, section, presetId, archetype, intensity * 0.92f, 1.32f);
                    break;
                case GameplayArchetypes.OffbeatSnap:
                    TryAddJump(output, beatMap, QuantizeOffbeat(source.timeSec, beatSec), lane, section, presetId, archetype, intensity, 1.18f);
                    break;
                case GameplayArchetypes.FakeoutGhost:
                    AddFakeout(output, source.timeSec, lane, section.Type, presetId, archetype, intensity);
                    break;
                case GameplayArchetypes.LaneBlock:
                default:
                {
                    string sourceKind = source.IsKind(BeatKinds.Accent) ? BeatKinds.Accent : BeatKinds.Tap;
                    float travel = sourceKind == BeatKinds.Accent ? 1.35f : 1.25f;
                    AddJump(output, source.timeSec, lane, section.Type, presetId, archetype, intensity, travel, sourceKind);
                    if (sourceKind == BeatKinds.Accent)
                    {
                        AddAccentFx(output, source.timeSec, section.Type, presetId, intensity);
                    }
                    break;
                }
            }
        }

        private static void AddRestPulse(List<GameplayPatternEvent> output, SectionWindow section, string presetId)
        {
            if (section.EndSec <= section.StartSec)
            {
                return;
            }

            output.Add(new GameplayPatternEvent
            {
                kind = GameplayPatternKinds.Rest,
                archetype = GameplayArchetypes.RestPulse,
                sectionType = GameplaySectionTypes.Rest,
                hitTimeSec = section.StartSec,
                endTimeSec = section.EndSec,
                lane = 0,
                intensity = 0.14f,
                isHazard = false,
                travelTimeSec = 0f,
                sourceKind = BeatKinds.RestSection,
                presentation = GameplayPresentationKinds.Straight,
                presetId = presetId
            });

            float mid = (section.StartSec + section.EndSec) * 0.5f;
            output.Add(new GameplayPatternEvent
            {
                kind = GameplayPatternKinds.AccentPulse,
                archetype = GameplayArchetypes.RestPulse,
                sectionType = GameplaySectionTypes.Rest,
                hitTimeSec = mid,
                endTimeSec = mid + 0.02f,
                lane = 0,
                intensity = 0.22f,
                isHazard = false,
                travelTimeSec = 0f,
                sourceKind = BeatKinds.RestSection,
                presentation = GameplayPresentationKinds.Straight,
                presetId = presetId
            });
        }

        private static void AddAccentFx(List<GameplayPatternEvent> output, float hitTime, string sectionType, string presetId, float intensity)
        {
            output.Add(new GameplayPatternEvent
            {
                kind = GameplayPatternKinds.AccentPulse,
                archetype = GameplayArchetypes.AccentCrusher,
                sectionType = sectionType,
                hitTimeSec = hitTime,
                endTimeSec = hitTime + 0.02f,
                lane = 0,
                intensity = Mathf.Clamp01(Mathf.Max(0.55f, intensity)),
                isHazard = false,
                travelTimeSec = 0f,
                sourceKind = BeatKinds.Accent,
                presentation = GameplayPresentationKinds.Straight,
                presetId = presetId
            });
            output.Add(new GameplayPatternEvent
            {
                kind = GameplayPatternKinds.CameraShift,
                archetype = GameplayArchetypes.AccentCrusher,
                sectionType = sectionType,
                hitTimeSec = hitTime,
                endTimeSec = hitTime + 0.30f,
                lane = 0,
                intensity = Mathf.Clamp01(Mathf.Max(0.55f, intensity)),
                isHazard = false,
                travelTimeSec = 0f,
                sourceKind = BeatKinds.Accent,
                presentation = GameplayPresentationKinds.Straight,
                presetId = presetId
            });
        }

        private static void AddJump(List<GameplayPatternEvent> output, float hitTime, int lane, string sectionType, string presetId, string archetype, float intensity, float travel, string sourceKind)
        {
            output.Add(new GameplayPatternEvent
            {
                kind = GameplayPatternKinds.Jump,
                archetype = archetype,
                sectionType = sectionType,
                hitTimeSec = hitTime,
                endTimeSec = hitTime,
                lane = Mathf.Clamp(lane, 0, 1),
                intensity = Mathf.Clamp01(intensity),
                isHazard = true,
                travelTimeSec = Mathf.Max(0.45f, travel),
                sourceKind = sourceKind,
                presentation = ResolvePresentation(archetype, true),
                presetId = presetId
            });
        }

        private static void AddHold(List<GameplayPatternEvent> output, float start, float end, int lane, string sectionType, string presetId, string archetype, float intensity)
        {
            output.Add(new GameplayPatternEvent
            {
                kind = GameplayPatternKinds.HoldSlide,
                archetype = archetype,
                sectionType = sectionType,
                hitTimeSec = start,
                endTimeSec = Mathf.Max(end, start + 0.8f),
                lane = Mathf.Clamp(lane, 0, 1),
                intensity = Mathf.Clamp01(intensity),
                isHazard = true,
                travelTimeSec = 1.35f,
                sourceKind = BeatKinds.Long,
                presentation = ResolvePresentation(archetype, true),
                presetId = presetId
            });
        }

        private static void AddFakeout(List<GameplayPatternEvent> output, float time, int lane, string sectionType, string presetId, string archetype, float intensity)
        {
            output.Add(new GameplayPatternEvent
            {
                kind = GameplayPatternKinds.Fakeout,
                archetype = archetype,
                sectionType = sectionType,
                hitTimeSec = time,
                endTimeSec = time + Mathf.Lerp(0.20f, 0.80f, Mathf.Clamp01(intensity)),
                lane = Mathf.Clamp(lane, 0, 1),
                intensity = Mathf.Clamp01(intensity * 0.7f),
                isHazard = false,
                travelTimeSec = 1.05f,
                sourceKind = BeatKinds.Tap,
                presentation = ResolvePresentation(archetype, false),
                presetId = presetId
            });
        }

        private static void TryAddJump(List<GameplayPatternEvent> output, BeatMap beatMap, float time, int lane, SectionWindow section, string presetId, string archetype, float intensity, float travel, string sourceKind = BeatKinds.Tap)
        {
            float clampedTime = Mathf.Clamp(time, section.StartSec + 0.02f, section.EndSec - 0.02f);
            if (clampedTime <= 0f || BeatMapEventUtils.IsRestTime(beatMap, clampedTime))
            {
                return;
            }

            AddJump(output, clampedTime, lane, section.Type, presetId, archetype, intensity, travel, sourceKind);
        }

        private static float ResolveHoldEnd(BeatEvent source, float sectionEnd, float beatSec, bool shortRelease, System.Random rng)
        {
            float end = source.GetEndTimeSec();
            if (end <= source.timeSec + 0.05f)
            {
                float min = shortRelease ? 0.8f : 1.4f;
                float max = shortRelease ? 1.8f : 2.5f;
                end = source.timeSec + Mathf.Lerp(min, max, (float)rng.NextDouble());
            }

            return Mathf.Max(source.timeSec + 0.75f, Mathf.Min(sectionEnd - 0.05f, end));
        }

        private static string SelectArchetype(string presetId, BeatEvent source, int index, int count, System.Random rng)
        {
            if (source.IsKind(BeatKinds.Long))
            {
                return rng.NextDouble() < 0.52d ? GameplayArchetypes.HoldLaneLock : GameplayArchetypes.HoldReleaseGate;
            }

            if (source.IsKind(BeatKinds.Accent))
            {
                return GameplayArchetypes.AccentCrusher;
            }

            float roll = (float)rng.NextDouble();
            return presetId switch
            {
                "ALT_1212_1BAR" => roll < 0.55f ? GameplayArchetypes.LaneBlock : (roll < 0.80f ? GameplayArchetypes.AlternatorPair : (roll < 0.90f ? GameplayArchetypes.OffbeatSnap : GameplayArchetypes.FakeoutGhost)),
                "ALT_1212_2BAR_ACCENT_END" => roll < 0.32f ? GameplayArchetypes.LaneBlock : (roll < 0.56f ? GameplayArchetypes.AlternatorPair : (roll < 0.86f ? GameplayArchetypes.AccentCrusher : GameplayArchetypes.FakeoutGhost)),
                "STREAK3_BREAK" => roll < 0.42f ? GameplayArchetypes.StreakBreaker : (roll < 0.68f ? GameplayArchetypes.LaneBlock : (roll < 0.84f ? GameplayArchetypes.CrossGate : GameplayArchetypes.AccentCrusher)),
                "STREAK2_SYNCOPATED" => roll < 0.34f ? GameplayArchetypes.OffbeatSnap : (roll < 0.62f ? GameplayArchetypes.StreakBreaker : (roll < 0.86f ? GameplayArchetypes.LaneBlock : GameplayArchetypes.FakeoutGhost)),
                "HOLD_SHORT_RELEASE" => roll < 0.56f ? GameplayArchetypes.HoldReleaseGate : (roll < 0.78f ? GameplayArchetypes.HoldLaneLock : (roll < 0.92f ? GameplayArchetypes.LaneBlock : GameplayArchetypes.AccentCrusher)),
                "HOLD_LONG_SAFE" => roll < 0.62f ? GameplayArchetypes.HoldLaneLock : (roll < 0.86f ? GameplayArchetypes.LaneBlock : GameplayArchetypes.FakeoutGhost),
                "DOUBLE_SWAP_PAIR" => roll < 0.44f ? GameplayArchetypes.AlternatorPair : (roll < 0.78f ? GameplayArchetypes.CrossGate : GameplayArchetypes.LaneBlock),
                "CROSS_GATE_BRIDGE" => roll < 0.50f ? GameplayArchetypes.CrossGate : (roll < 0.70f ? GameplayArchetypes.OffbeatSnap : (roll < 0.90f ? GameplayArchetypes.LaneBlock : GameplayArchetypes.FakeoutGhost)),
                "BUILD_RAMP_4BAR" => roll < 0.24f ? GameplayArchetypes.StreakBreaker : (roll < 0.48f ? GameplayArchetypes.AccentCrusher : (roll < 0.68f ? GameplayArchetypes.AlternatorPair : (roll < 0.88f ? GameplayArchetypes.CrossGate : GameplayArchetypes.OffbeatSnap))),
                "DROP_DENSE_ACCENTED" => roll < 0.34f ? GameplayArchetypes.AccentCrusher : (roll < 0.58f ? GameplayArchetypes.CrossGate : (roll < 0.76f ? GameplayArchetypes.OffbeatSnap : (roll < 0.90f ? GameplayArchetypes.HoldReleaseGate : GameplayArchetypes.LaneBlock))),
                "FAKEOUT_BRIDGE_TO_DROP" => roll < 0.36f ? GameplayArchetypes.FakeoutGhost : (roll < 0.60f ? GameplayArchetypes.LaneBlock : (roll < 0.80f ? GameplayArchetypes.OffbeatSnap : GameplayArchetypes.AccentCrusher)),
                _ => index == count - 1 && rng.NextDouble() < 0.3d ? GameplayArchetypes.AccentCrusher : GameplayArchetypes.LaneBlock
            };
        }

        private static int ResolveLane(ref int currentLane, ref int streak, int sourceLane, string archetype, System.Random rng)
        {
            int lane = Mathf.Clamp(sourceLane, 0, 1);
            if (string.Equals(archetype, GameplayArchetypes.StreakBreaker, StringComparison.OrdinalIgnoreCase))
            {
                lane = 1 - currentLane;
            }
            else if (lane == currentLane && rng.NextDouble() < 0.26d)
            {
                lane = 1 - currentLane;
            }

            if (lane == currentLane)
            {
                streak++;
            }
            else
            {
                currentLane = lane;
                streak = 1;
            }

            if (streak >= 4)
            {
                currentLane = 1 - currentLane;
                streak = 1;
            }

            return currentLane;
        }

        private static float QuantizeOffbeat(float timeSec, float beatSec)
        {
            float beat = timeSec / Mathf.Max(0.0001f, beatSec);
            return (Mathf.Floor(beat) + 0.5f) * beatSec;
        }

        private static float ResolveTargetStrain(string sectionType, float currentStrain, float difficulty01, System.Random rng)
        {
            float min;
            float max;
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                min = 0.10f;
                max = 0.20f;
            }
            else if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                min = 0.65f;
                max = 0.85f;
            }
            else
            {
                min = 0.35f;
                max = 0.60f;
            }

            float sampled = Mathf.Lerp(min, max, (float)rng.NextDouble());
            float withDifficulty = sampled + Mathf.Lerp(-0.06f, 0.08f, difficulty01);
            return Mathf.Clamp01(Mathf.Lerp(withDifficulty, currentStrain, 0.35f));
        }

        private static PresetDef SelectPreset(string sectionType, float targetStrain, System.Random rng)
        {
            PresetDef best = Presets[0];
            float bestScore = float.MaxValue;
            for (int i = 0; i < Presets.Length; i++)
            {
                PresetDef preset = Presets[i];
                if (!preset.Supports(sectionType))
                {
                    continue;
                }

                float score = Mathf.Abs(targetStrain - preset.TargetStrain) + ((float)rng.NextDouble() * 0.0125f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = preset;
                }
            }

            return best;
        }

        private static List<SectionWindow> BuildSectionWindows(BeatMap beatMap, RestSectionEvent[] rests, BeatEvent[] events)
        {
            var windows = new List<SectionWindow>();
            int index = 0;

            if (beatMap?.sections != null)
            {
                for (int i = 0; i < beatMap.sections.Length; i++)
                {
                    BeatSection section = beatMap.sections[i];
                    if (section == null || section.endSec <= section.startSec)
                    {
                        continue;
                    }

                    windows.Add(new SectionWindow
                    {
                        Index = index++,
                        Type = ResolveSectionType(section),
                        StartSec = section.startSec,
                        EndSec = section.endSec
                    });
                }
            }

            if (windows.Count == 0)
            {
                float maxTime = 20f;
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i] != null)
                    {
                        maxTime = Mathf.Max(maxTime, events[i].GetEndTimeSec() + 0.5f);
                    }
                }

                windows.Add(new SectionWindow
                {
                    Index = index++,
                    Type = GameplaySectionTypes.Active,
                    StartSec = 0f,
                    EndSec = maxTime
                });
            }

            if (rests != null)
            {
                for (int i = 0; i < rests.Length; i++)
                {
                    RestSectionEvent rest = rests[i];
                    if (rest == null || rest.endSec <= rest.startSec)
                    {
                        continue;
                    }

                    windows.Add(new SectionWindow
                    {
                        Index = index++,
                        Type = GameplaySectionTypes.Rest,
                        StartSec = rest.startSec,
                        EndSec = rest.endSec
                    });
                }
            }

            return windows.OrderBy(w => w.StartSec).ThenBy(w => w.EndSec).ToList();
        }

        private static SectionMetrics ComputeMetrics(IReadOnlyList<BeatEvent> events, float bpm, float durationSec)
        {
            if (events == null || events.Count == 0 || durationSec <= 0.001f)
            {
                return new SectionMetrics();
            }

            int switchCount = 0;
            int accentCount = 0;
            int offbeatCount = 0;
            float holdSec = 0f;
            float beatSec = 60f / Mathf.Max(1f, bpm);
            int previousLane = Mathf.Clamp(events[0].lane, 0, 1);

            for (int i = 0; i < events.Count; i++)
            {
                BeatEvent evt = events[i];
                if (evt == null)
                {
                    continue;
                }

                int lane = Mathf.Clamp(evt.lane, 0, 1);
                if (i > 0 && lane != previousLane)
                {
                    switchCount++;
                }

                previousLane = lane;
                if (evt.IsKind(BeatKinds.Accent))
                {
                    accentCount++;
                }
                if (evt.IsKind(BeatKinds.Long))
                {
                    holdSec += Mathf.Max(0f, evt.GetEndTimeSec() - evt.timeSec);
                }

                float frac = (evt.timeSec / Mathf.Max(0.0001f, beatSec)) % 1f;
                if (Mathf.Abs(frac - 0.5f) <= 0.15f)
                {
                    offbeatCount++;
                }
            }

            float density = Mathf.Clamp01((events.Count / Mathf.Max(durationSec, 0.1f)) / 3.2f);
            float switchFreq = events.Count > 1 ? Mathf.Clamp01((float)switchCount / (events.Count - 1)) : 0f;
            float holdLoad = Mathf.Clamp01(holdSec / Mathf.Max(durationSec, 0.1f));
            float offbeatRatio = Mathf.Clamp01((float)offbeatCount / events.Count);
            float accentDensity = Mathf.Clamp01((float)accentCount / events.Count);
            float strain = (0.32f * density) + (0.24f * switchFreq) + (0.18f * holdLoad) + (0.16f * offbeatRatio) + (0.10f * accentDensity);

            return new SectionMetrics
            {
                Density = density,
                SwitchFreq = switchFreq,
                HoldLoad = holdLoad,
                OffbeatRatio = offbeatRatio,
                AccentDensity = accentDensity,
                Strain = Mathf.Clamp01(strain)
            };
        }

        private static string ResolveSectionType(BeatSection section)
        {
            if (section == null)
            {
                return GameplaySectionTypes.Active;
            }

            if (section.IsRest)
            {
                return GameplaySectionTypes.Rest;
            }

            string type = section.type ?? string.Empty;
            if (type.IndexOf("drop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameplaySectionTypes.Drop;
            }
            if (type.IndexOf("transition", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameplaySectionTypes.Transition;
            }

            return section.intensity >= 0.72f || section.density >= 0.65f
                ? GameplaySectionTypes.Drop
                : GameplaySectionTypes.Active;
        }

        private static string ResolvePresentation(string archetype, bool hazard)
        {
            if (!hazard)
            {
                return string.Equals(archetype, GameplayArchetypes.RestPulse, StringComparison.OrdinalIgnoreCase)
                    ? GameplayPresentationKinds.Straight
                    : GameplayPresentationKinds.Pop;
            }

            return archetype switch
            {
                GameplayArchetypes.HoldLaneLock => GameplayPresentationKinds.Drop,
                GameplayArchetypes.HoldReleaseGate => GameplayPresentationKinds.Drop,
                GameplayArchetypes.CrossGate => GameplayPresentationKinds.Diagonal,
                GameplayArchetypes.AccentCrusher => GameplayPresentationKinds.Diagonal,
                GameplayArchetypes.OffbeatSnap => GameplayPresentationKinds.Pop,
                GameplayArchetypes.StreakBreaker => GameplayPresentationKinds.Rise,
                _ => GameplayPresentationKinds.Straight
            };
        }

        private static char SectionToMask(string sectionType)
        {
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                return 'R';
            }
            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                return 'D';
            }
            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                return 'T';
            }

            return 'A';
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
