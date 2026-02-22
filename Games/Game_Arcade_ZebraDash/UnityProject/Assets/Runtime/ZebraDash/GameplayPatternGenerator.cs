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
        public GameplayGridDebugBar[] gridDebugBars = Array.Empty<GameplayGridDebugBar>();
        public GameplayLanePlanBeat[] lanePlan = Array.Empty<GameplayLanePlanBeat>();
        public GameplayTapScheduleEvent[] tapSchedule = Array.Empty<GameplayTapScheduleEvent>();
    }

    [Serializable]
    public sealed class GameplayGridDebugBar
    {
        public int barIndex;
        public string sectionType = GameplaySectionTypes.Active;
        public string presetId = "";
        public string hazardMask16 = "0000000000000000";
        public string lanePlan = "----";
        public int hazardTarget;
        public int switchTarget;
        public float averageEnergy;
        public float targetStrain;
        public string fallbackStep = "base";
    }

    [Serializable]
    public sealed class GameplayLanePlanBeat
    {
        public int beatIndex;
        public int barIndex;
        public int beatInBar;
        public float timeSec;
        public int lane;
        public string sectionType = GameplaySectionTypes.Active;
        public string presetId = "";
    }

    [Serializable]
    public sealed class GameplayTapScheduleEvent
    {
        public int beatIndex;
        public int barIndex;
        public int beatInBar;
        public float timeSec;
        public int laneFrom;
        public int laneTo;
        public string sectionType = GameplaySectionTypes.Active;
        public string presetId = "";
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

        private sealed class PlannerBarContext
        {
            public int BarIndex;
            public string SectionType = GameplaySectionTypes.Active;
            public string PresetId = "";
            public string FallbackStep = "base";
            public float BarStartSec;
            public float BeatSec;
            public float TargetStrain;
            public float[] SlotEnergy = new float[16];
            public bool[] SlotAccent = new bool[16];
            public int HazardTarget;
            public int SwitchTarget;
            public int MaxConsecutiveHazardBeats;
            public int MinGapSlots;
            public float OffbeatMinRatio;
            public float OffbeatMaxRatio;
        }

        private sealed class PlannerHazard
        {
            public int BeatInBar;
            public int SlotInBar;
            public int Lane;
            public bool RequiresTap;
            public float Intensity;
            public bool Accent;
        }

        private sealed class PlannerBarDecision
        {
            public int BarIndex;
            public int[] LaneAfterBeat = new int[4];
            public bool[] SwitchAtBeat = new bool[4];
            public PlannerHazard[] Hazards = Array.Empty<PlannerHazard>();
        }

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
            var gridDebugBars = new List<GameplayGridDebugBar>(64);
            var lanePlanBeats = new List<GameplayLanePlanBeat>(256);
            var tapSchedule = new List<GameplayTapScheduleEvent>(128);
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

                var sectionEvents = new List<GameplayPatternEvent>(Mathf.Max(12, sourceEvents.Count * 2));
                GenerateSectionWithPhrasePlanner(
                    sectionEvents,
                    gridDebugBars,
                    lanePlanBeats,
                    tapSchedule,
                    beatMap,
                    section,
                    preset.Id,
                    sourceEvents,
                    targetStrain,
                    beatSec,
                    sectionRng,
                    ref laneState,
                    ref laneStreak);
                events.AddRange(sectionEvents);
            }

            GameplayPatternEvent[] sorted = events
                .OrderBy(e => e.hitTimeSec)
                .ThenBy(e => e.kind)
                .ToArray();
            GameplayLanePlanBeat[] lanePlanArray = lanePlanBeats
                .OrderBy(e => e.beatIndex)
                .ThenBy(e => e.timeSec)
                .GroupBy(e => e.beatIndex)
                .Select(g => g.First())
                .ToArray();
            GameplayTapScheduleEvent[] tapScheduleArray = tapSchedule
                .OrderBy(e => e.beatIndex)
                .ThenBy(e => e.timeSec)
                .GroupBy(e => e.beatIndex)
                .Select(g => g.First())
                .ToArray();

            return new GameplayPattern
            {
                seed = seed,
                difficulty = difficulty01,
                events = sorted,
                restSections = rests,
                sections = sectionInfos.ToArray(),
                gridDebugBars = gridDebugBars.ToArray(),
                lanePlan = lanePlanArray,
                tapSchedule = tapScheduleArray
            };
        }

        private static void GenerateSectionFromGrid(
            List<GameplayPatternEvent> output,
            BeatMap beatMap,
            SectionWindow section,
            string presetId,
            IReadOnlyList<BeatEvent> sourceEvents,
            float targetStrain,
            float beatSec,
            System.Random rng,
            ref int laneState,
            ref int laneStreak)
        {
            if (output == null || beatSec <= 0.0001f)
            {
                return;
            }

            float barSec = beatSec * 4f;
            float subSec = beatSec * 0.25f;
            int barStartIndex = Mathf.FloorToInt(section.StartSec / barSec);
            int barEndIndex = Mathf.FloorToInt((Mathf.Max(section.StartSec + 0.001f, section.EndSec - 0.001f)) / barSec);
            for (int barIndex = barStartIndex; barIndex <= barEndIndex; barIndex++)
            {
                float barStart = barIndex * barSec;
                float localStart = Mathf.Max(section.StartSec, barStart);
                float localEnd = Mathf.Min(section.EndSec, barStart + barSec);
                if (localEnd <= localStart + (subSec * 0.6f))
                {
                    continue;
                }

                int hazardCount = ResolveHazardCountForBar(section.Type, targetStrain, rng);
                if (hazardCount <= 0)
                {
                    continue;
                }

                bool[] slotMask = BuildEuclideanMask(hazardCount, 16);
                ApplyMaskHarmonyRules(slotMask, section.Type, hazardCount, rng);

                for (int slot = 0; slot < slotMask.Length; slot++)
                {
                    if (!slotMask[slot])
                    {
                        continue;
                    }

                    float slotTime = barStart + (slot * subSec);
                    if (slotTime < localStart + 0.01f || slotTime > localEnd - 0.01f)
                    {
                        continue;
                    }

                    if (BeatMapEventUtils.IsRestTime(beatMap, slotTime))
                    {
                        continue;
                    }

                    BeatEvent anchor = FindNearestSourceEvent(sourceEvents, slotTime, beatSec * 0.55f);
                    bool downbeat = (slot % 4) == 0;
                    bool offbeat = (slot % 4) != 0;
                    float intensity = ResolveSlotIntensity(anchor, section.Type, downbeat, targetStrain);
                    string archetype = SelectGridArchetype(presetId, section.Type, downbeat, offbeat, intensity, rng);
                    int sourceLane = anchor != null ? Mathf.Clamp(anchor.lane, 0, 1) : ((slot / 2) % 2);
                    int lane = ResolveLane(ref laneState, ref laneStreak, sourceLane, archetype, rng);
                    BeatEvent source = CreateGridSourceEvent(anchor, slotTime, lane, downbeat, intensity, archetype, beatSec);

                    EmitArchetypeEvents(output, beatMap, section, presetId, source, archetype, lane, intensity, beatSec, rng);
                }
            }
        }

        private sealed class PhrasePlannerState
        {
            public int BeatCursor;
            public int CurrentLane;
            public float Score;
            public int TieHash;
            public int[] HazardCounts = Array.Empty<int>();
            public int[] SwitchCounts = Array.Empty<int>();
            public int[] OffbeatCounts = Array.Empty<int>();
            public int[] LastHazardSlot = Array.Empty<int>();
            public bool[][] SwitchAtBeat = Array.Empty<bool[]>();
            public int[][] LaneAfterBeat = Array.Empty<int[]>();
            public List<PlannerHazard>[] Hazards = Array.Empty<List<PlannerHazard>>();
            public int ConsecutiveSwitch;
            public int ConsecutiveHazardBeats;
        }

        private static void GenerateSectionWithPhrasePlanner(
            List<GameplayPatternEvent> output,
            List<GameplayGridDebugBar> gridDebugBars,
            List<GameplayLanePlanBeat> lanePlanBeats,
            List<GameplayTapScheduleEvent> tapSchedule,
            BeatMap beatMap,
            SectionWindow section,
            string presetId,
            IReadOnlyList<BeatEvent> sourceEvents,
            float targetStrain,
            float beatSec,
            System.Random rng,
            ref int laneState,
            ref int laneStreak)
        {
            if (output == null || beatSec <= 0.0001f)
            {
                return;
            }

            List<PlannerBarContext> barContexts = BuildPlannerBarContexts(section, presetId, sourceEvents, targetStrain, beatSec);
            if (barContexts.Count == 0)
            {
                return;
            }

            int contextCursor = 0;
            while (contextCursor < barContexts.Count)
            {
                int phraseCount = Mathf.Min(2, barContexts.Count - contextCursor);
                PlannerBarContext[] phrase = new PlannerBarContext[phraseCount];
                for (int i = 0; i < phraseCount; i++)
                {
                    phrase[i] = barContexts[contextCursor + i];
                }

                PlannerBarDecision[] decisions;
                if (!TryPlanPhraseWithFallback(phrase, laneState, rng, out decisions))
                {
                    // Last-resort deterministic fallback uses the older grid heuristic.
                    GenerateSectionFromGrid(
                        output,
                        beatMap,
                        section,
                        presetId,
                        sourceEvents,
                        targetStrain,
                        beatSec,
                        rng,
                        ref laneState,
                        ref laneStreak);
                    return;
                }

                for (int i = 0; i < decisions.Length; i++)
                {
                    PlannerBarDecision decision = decisions[i];
                    PlannerBarContext ctx = phrase[i];
                    int laneBeforeBar = laneState;
                    EmitPlannedBarEvents(output, beatMap, section, ctx, decision, rng);
                    gridDebugBars?.Add(BuildGridDebugBar(ctx, decision));
                    AppendPlannerTimeline(ctx, decision, laneBeforeBar, lanePlanBeats, tapSchedule);

                    laneState = decision.LaneAfterBeat != null && decision.LaneAfterBeat.Length > 0
                        ? decision.LaneAfterBeat[decision.LaneAfterBeat.Length - 1]
                        : laneState;
                    laneStreak = 1;
                }

                contextCursor += phraseCount;
            }
        }

        private static void AppendPlannerTimeline(
            PlannerBarContext context,
            PlannerBarDecision decision,
            int laneBeforeBar,
            List<GameplayLanePlanBeat> lanePlanBeats,
            List<GameplayTapScheduleEvent> tapSchedule)
        {
            if (context == null || decision == null)
            {
                return;
            }

            int prevLane = Mathf.Clamp(laneBeforeBar, 0, 1);
            for (int beat = 0; beat < 4; beat++)
            {
                int laneTo = decision.LaneAfterBeat != null && beat < decision.LaneAfterBeat.Length
                    ? Mathf.Clamp(decision.LaneAfterBeat[beat], 0, 1)
                    : prevLane;
                float beatTime = context.BarStartSec + (beat * context.BeatSec);
                int beatIndex = (context.BarIndex * 4) + beat;

                lanePlanBeats?.Add(new GameplayLanePlanBeat
                {
                    beatIndex = beatIndex,
                    barIndex = context.BarIndex,
                    beatInBar = beat,
                    timeSec = beatTime,
                    lane = laneTo,
                    sectionType = context.SectionType,
                    presetId = context.PresetId
                });

                bool switched = decision.SwitchAtBeat != null
                    && beat < decision.SwitchAtBeat.Length
                    && decision.SwitchAtBeat[beat]
                    && laneTo != prevLane;
                if (switched)
                {
                    tapSchedule?.Add(new GameplayTapScheduleEvent
                    {
                        beatIndex = beatIndex,
                        barIndex = context.BarIndex,
                        beatInBar = beat,
                        timeSec = beatTime,
                        laneFrom = prevLane,
                        laneTo = laneTo,
                        sectionType = context.SectionType,
                        presetId = context.PresetId
                    });
                }

                prevLane = laneTo;
            }
        }

        private static GameplayGridDebugBar BuildGridDebugBar(PlannerBarContext context, PlannerBarDecision decision)
        {
            string lanePlan = "----";
            if (decision?.LaneAfterBeat != null && decision.LaneAfterBeat.Length >= 4)
            {
                lanePlan = $"{decision.LaneAfterBeat[0]}{decision.LaneAfterBeat[1]}{decision.LaneAfterBeat[2]}{decision.LaneAfterBeat[3]}";
            }

            int mask = 0;
            if (decision?.Hazards != null)
            {
                for (int i = 0; i < decision.Hazards.Length; i++)
                {
                    int slot = Mathf.Clamp(decision.Hazards[i].SlotInBar, 0, 15);
                    mask |= 1 << slot;
                }
            }

            char[] bits = new char[16];
            for (int i = 0; i < bits.Length; i++)
            {
                bits[15 - i] = (mask & (1 << i)) != 0 ? '1' : '0';
            }

            float avgEnergy = 0f;
            if (context != null && context.SlotEnergy != null && context.SlotEnergy.Length > 0)
            {
                float sum = 0f;
                for (int i = 0; i < context.SlotEnergy.Length; i++)
                {
                    sum += context.SlotEnergy[i];
                }

                avgEnergy = sum / context.SlotEnergy.Length;
            }

            return new GameplayGridDebugBar
            {
                barIndex = context != null ? context.BarIndex : 0,
                sectionType = context != null ? context.SectionType : GameplaySectionTypes.Active,
                presetId = context != null ? context.PresetId : "",
                hazardMask16 = new string(bits),
                lanePlan = lanePlan,
                hazardTarget = context != null ? context.HazardTarget : 0,
                switchTarget = context != null ? context.SwitchTarget : 0,
                averageEnergy = avgEnergy,
                targetStrain = context != null ? context.TargetStrain : 0f,
                fallbackStep = context != null ? context.FallbackStep : "base"
            };
        }

        private static void EmitPlannedBarEvents(
            List<GameplayPatternEvent> output,
            BeatMap beatMap,
            SectionWindow section,
            PlannerBarContext context,
            PlannerBarDecision decision,
            System.Random rng)
        {
            if (decision?.Hazards == null || context == null)
            {
                return;
            }

            float subSec = context.BeatSec * 0.25f;
            for (int i = 0; i < decision.Hazards.Length; i++)
            {
                PlannerHazard hazard = decision.Hazards[i];
                float hitTime = context.BarStartSec + (hazard.SlotInBar * subSec);
                if (hitTime < section.StartSec + 0.01f || hitTime > section.EndSec - 0.01f)
                {
                    continue;
                }

                if (BeatMapEventUtils.IsRestTime(beatMap, hitTime))
                {
                    continue;
                }

                string archetype = ResolvePlannedArchetype(context, hazard);
                bool downbeat = (hazard.SlotInBar % 4) == 0;
                BeatEvent source = CreateGridSourceEvent(
                    anchor: null,
                    slotTimeSec: hitTime,
                    lane: hazard.Lane,
                    downbeat: downbeat || hazard.Accent,
                    intensity: hazard.Intensity,
                    archetype: archetype,
                    beatSec: context.BeatSec);

                string forcedSourceKind = hazard.RequiresTap
                    ? ((downbeat || hazard.Accent) ? BeatKinds.Accent : BeatKinds.Tap)
                    : BeatKinds.GapLegacy;
                EmitArchetypeEvents(
                    output,
                    beatMap,
                    section,
                    context.PresetId,
                    source,
                    archetype,
                    hazard.Lane,
                    hazard.Intensity,
                    context.BeatSec,
                    rng,
                    forcedSourceKind);
            }
        }

        private static string ResolvePlannedArchetype(PlannerBarContext context, PlannerHazard hazard)
        {
            bool downbeat = (hazard.SlotInBar % 4) == 0;
            bool offbeat = !downbeat;
            bool isDrop = string.Equals(context.SectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase);

            if (offbeat)
            {
                return GameplayArchetypes.OffbeatSnap;
            }

            if (isDrop && downbeat && hazard.Intensity >= 0.72f)
            {
                int selector = (context.BarIndex + hazard.BeatInBar + hazard.Lane) % 3;
                return selector == 0 ? GameplayArchetypes.CrossGate : GameplayArchetypes.AlternatorPair;
            }

            if (downbeat && hazard.Intensity >= 0.70f)
            {
                return GameplayArchetypes.AccentCrusher;
            }

            if (!hazard.RequiresTap && downbeat && hazard.Intensity >= 0.82f)
            {
                return GameplayArchetypes.HoldLaneLock;
            }

            return GameplayArchetypes.LaneBlock;
        }

        private static List<PlannerBarContext> BuildPlannerBarContexts(
            SectionWindow section,
            string presetId,
            IReadOnlyList<BeatEvent> sourceEvents,
            float targetStrain,
            float beatSec)
        {
            var result = new List<PlannerBarContext>();
            float barSec = beatSec * 4f;
            float subSec = beatSec * 0.25f;
            int startBar = Mathf.FloorToInt(section.StartSec / barSec);
            int endBar = Mathf.FloorToInt((Mathf.Max(section.StartSec + 0.001f, section.EndSec - 0.001f)) / barSec);
            for (int bar = startBar; bar <= endBar; bar++)
            {
                float barStart = bar * barSec;
                float localStart = Mathf.Max(section.StartSec, barStart);
                float localEnd = Mathf.Min(section.EndSec, barStart + barSec);
                if (localEnd <= localStart + (subSec * 0.6f))
                {
                    continue;
                }

                var context = new PlannerBarContext
                {
                    BarIndex = bar,
                    SectionType = section.Type,
                    PresetId = presetId,
                    BarStartSec = barStart,
                    BeatSec = beatSec,
                    TargetStrain = targetStrain
                };

                float energySum = 0f;
                for (int slot = 0; slot < 16; slot++)
                {
                    float slotTime = barStart + (slot * subSec);
                    if (slotTime < localStart - 0.001f || slotTime > localEnd + 0.001f)
                    {
                        context.SlotEnergy[slot] = 0f;
                        context.SlotAccent[slot] = false;
                        continue;
                    }

                    bool accent = HasAccentNear(sourceEvents, slotTime, subSec * 0.56f);
                    float density = ComputeLocalDensity(sourceEvents, slotTime, barSec);
                    float syncHint = (slot % 4) != 0 ? 0.65f : 0.20f;
                    float sectionEnergy = ResolveSectionEnergy(section.Type, targetStrain);
                    float energy = (0.45f * sectionEnergy) + (0.25f * density) + (0.20f * (accent ? 1f : 0f)) + (0.10f * syncHint);
                    context.SlotEnergy[slot] = Mathf.Clamp01(energy);
                    context.SlotAccent[slot] = accent;
                    energySum += context.SlotEnergy[slot];
                }

                float avgEnergy = energySum / 16f;
                ResolvePlannerTargets(section.Type, targetStrain, avgEnergy, out int hazardTarget, out int switchTarget);
                context.HazardTarget = hazardTarget;
                context.SwitchTarget = switchTarget;
                if (string.Equals(section.Type, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
                {
                    context.MaxConsecutiveHazardBeats = 3;
                    context.MinGapSlots = 1;
                    context.OffbeatMinRatio = 0.18f;
                    context.OffbeatMaxRatio = 0.42f;
                }
                else
                {
                    context.MaxConsecutiveHazardBeats = 2;
                    context.MinGapSlots = 2;
                    context.OffbeatMinRatio = 0.06f;
                    context.OffbeatMaxRatio = 0.24f;
                }

                result.Add(context);
            }

            return result;
        }

        private static float ResolveSectionEnergy(string sectionType, float targetStrain)
        {
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                return 0.10f;
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Lerp(0.72f, 0.92f, Mathf.InverseLerp(0.65f, 0.85f, targetStrain));
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Lerp(0.45f, 0.65f, Mathf.InverseLerp(0.35f, 0.60f, targetStrain));
            }

            return Mathf.Lerp(0.42f, 0.60f, Mathf.InverseLerp(0.35f, 0.60f, targetStrain));
        }

        private static void ResolvePlannerTargets(string sectionType, float targetStrain, float avgEnergy, out int hazardTarget, out int switchTarget)
        {
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                hazardTarget = 0;
                switchTarget = 0;
                return;
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                hazardTarget = (targetStrain >= 0.83f && avgEnergy >= 0.78f) ? 4 : 3;
                switchTarget = (targetStrain >= 0.80f && avgEnergy >= 0.74f) ? 3 : 2;
                return;
            }

            hazardTarget = (targetStrain >= 0.56f || avgEnergy >= 0.60f) ? 2 : 1;
            switchTarget = (targetStrain >= 0.53f || avgEnergy >= 0.57f) ? 2 : 1;
        }

        private static bool HasAccentNear(IReadOnlyList<BeatEvent> events, float timeSec, float windowSec)
        {
            if (events == null)
            {
                return false;
            }

            for (int i = 0; i < events.Count; i++)
            {
                BeatEvent evt = events[i];
                if (evt == null || !evt.IsKind(BeatKinds.Accent))
                {
                    continue;
                }

                if (Mathf.Abs(evt.timeSec - timeSec) <= windowSec)
                {
                    return true;
                }
            }

            return false;
        }

        private static float ComputeLocalDensity(IReadOnlyList<BeatEvent> events, float timeSec, float barSec)
        {
            if (events == null || events.Count == 0)
            {
                return 0f;
            }

            float half = barSec * 0.5f;
            int count = 0;
            for (int i = 0; i < events.Count; i++)
            {
                BeatEvent evt = events[i];
                if (evt == null)
                {
                    continue;
                }

                if (Mathf.Abs(evt.timeSec - timeSec) <= half)
                {
                    count++;
                }
            }

            return Mathf.Clamp01(count / 8f);
        }

        private static bool TryPlanPhraseWithFallback(
            PlannerBarContext[] phraseBars,
            int startLane,
            System.Random rng,
            out PlannerBarDecision[] decisions)
        {
            decisions = null;
            if (phraseBars == null || phraseBars.Length == 0)
            {
                return false;
            }

            int tieSeed = rng != null ? rng.Next(int.MinValue, int.MaxValue) : 17;
            PlannerBarContext[] phase0 = ClonePlannerBars(phraseBars, allowOffbeat: true, switchReduction: 0, hazardReduction: 0, fallbackStep: "base");
            if (TryPlanPhrase(phase0, startLane, beamWidth: 12, tieSeed, out decisions))
            {
                return true;
            }

            PlannerBarContext[] phase1 = ClonePlannerBars(phraseBars, allowOffbeat: false, switchReduction: 0, hazardReduction: 0, fallbackStep: "offbeat_down");
            if (TryPlanPhrase(phase1, startLane, beamWidth: 12, tieSeed, out decisions))
            {
                return true;
            }

            PlannerBarContext[] phase2 = ClonePlannerBars(phraseBars, allowOffbeat: false, switchReduction: 1, hazardReduction: 0, fallbackStep: "switch_down");
            if (TryPlanPhrase(phase2, startLane, beamWidth: 10, tieSeed, out decisions))
            {
                return true;
            }

            PlannerBarContext[] phase3 = ClonePlannerBars(phraseBars, allowOffbeat: false, switchReduction: 1, hazardReduction: 1, fallbackStep: "hazard_down");
            return TryPlanPhrase(phase3, startLane, beamWidth: 8, tieSeed, out decisions);
        }

        private static PlannerBarContext[] ClonePlannerBars(
            PlannerBarContext[] source,
            bool allowOffbeat,
            int switchReduction,
            int hazardReduction,
            string fallbackStep)
        {
            var clone = new PlannerBarContext[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                PlannerBarContext s = source[i];
                var c = new PlannerBarContext
                {
                    BarIndex = s.BarIndex,
                    SectionType = s.SectionType,
                    PresetId = s.PresetId,
                    FallbackStep = fallbackStep ?? "base",
                    BarStartSec = s.BarStartSec,
                    BeatSec = s.BeatSec,
                    TargetStrain = s.TargetStrain,
                    HazardTarget = Mathf.Max(0, s.HazardTarget - hazardReduction),
                    SwitchTarget = Mathf.Max(0, s.SwitchTarget - switchReduction),
                    MaxConsecutiveHazardBeats = s.MaxConsecutiveHazardBeats,
                    MinGapSlots = s.MinGapSlots,
                    OffbeatMinRatio = allowOffbeat ? s.OffbeatMinRatio : 0f,
                    OffbeatMaxRatio = allowOffbeat ? s.OffbeatMaxRatio : 0f
                };
                c.SlotEnergy = (float[])s.SlotEnergy.Clone();
                c.SlotAccent = (bool[])s.SlotAccent.Clone();
                clone[i] = c;
            }

            return clone;
        }

        private static bool TryPlanPhrase(
            PlannerBarContext[] phraseBars,
            int startLane,
            int beamWidth,
            int tieSeed,
            out PlannerBarDecision[] decisions)
        {
            decisions = null;
            int barCount = phraseBars.Length;
            int totalBeats = barCount * 4;
            var initial = CreateInitialPhraseState(barCount, startLane, tieSeed);
            var beam = new List<PhrasePlannerState> { initial };

            for (int beat = 0; beat < totalBeats; beat++)
            {
                var next = new List<PhrasePlannerState>(beamWidth * 12);
                for (int i = 0; i < beam.Count; i++)
                {
                    ExpandPhraseState(next, beam[i], phraseBars);
                }

                if (next.Count == 0)
                {
                    return false;
                }

                beam = next
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.TieHash)
                    .Take(Mathf.Max(1, beamWidth))
                    .ToList();
            }

            PhrasePlannerState best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < beam.Count; i++)
            {
                PhrasePlannerState state = beam[i];
                if (!IsPhraseStateComplete(state, phraseBars))
                {
                    continue;
                }

                float finalScore = state.Score + EvaluateFinalPhraseScore(state, phraseBars);
                if (finalScore > bestScore || (Mathf.Approximately(finalScore, bestScore) && (best == null || state.TieHash < best.TieHash)))
                {
                    best = state;
                    bestScore = finalScore;
                }
            }

            if (best == null)
            {
                return false;
            }

            decisions = new PlannerBarDecision[barCount];
            for (int bar = 0; bar < barCount; bar++)
            {
                decisions[bar] = new PlannerBarDecision
                {
                    BarIndex = phraseBars[bar].BarIndex,
                    LaneAfterBeat = (int[])best.LaneAfterBeat[bar].Clone(),
                    SwitchAtBeat = (bool[])best.SwitchAtBeat[bar].Clone(),
                    Hazards = best.Hazards[bar]
                        .OrderBy(h => h.SlotInBar)
                        .ToArray()
                };
            }

            return true;
        }

        private static PhrasePlannerState CreateInitialPhraseState(int barCount, int startLane, int tieSeed)
        {
            var state = new PhrasePlannerState
            {
                BeatCursor = 0,
                CurrentLane = Mathf.Clamp(startLane, 0, 1),
                Score = 0f,
                TieHash = tieSeed,
                HazardCounts = new int[barCount],
                SwitchCounts = new int[barCount],
                OffbeatCounts = new int[barCount],
                LastHazardSlot = Enumerable.Repeat(-1000, barCount).ToArray(),
                SwitchAtBeat = new bool[barCount][],
                LaneAfterBeat = new int[barCount][],
                Hazards = new List<PlannerHazard>[barCount],
                ConsecutiveSwitch = 0,
                ConsecutiveHazardBeats = 0
            };

            for (int i = 0; i < barCount; i++)
            {
                state.SwitchAtBeat[i] = new bool[4];
                state.LaneAfterBeat[i] = new int[4];
                state.Hazards[i] = new List<PlannerHazard>(4);
                for (int b = 0; b < 4; b++)
                {
                    state.LaneAfterBeat[i][b] = state.CurrentLane;
                }
            }

            return state;
        }

        private static PhrasePlannerState ClonePhraseState(PhrasePlannerState source)
        {
            var clone = new PhrasePlannerState
            {
                BeatCursor = source.BeatCursor,
                CurrentLane = source.CurrentLane,
                Score = source.Score,
                TieHash = source.TieHash,
                HazardCounts = (int[])source.HazardCounts.Clone(),
                SwitchCounts = (int[])source.SwitchCounts.Clone(),
                OffbeatCounts = (int[])source.OffbeatCounts.Clone(),
                LastHazardSlot = (int[])source.LastHazardSlot.Clone(),
                SwitchAtBeat = new bool[source.SwitchAtBeat.Length][],
                LaneAfterBeat = new int[source.LaneAfterBeat.Length][],
                Hazards = new List<PlannerHazard>[source.Hazards.Length],
                ConsecutiveSwitch = source.ConsecutiveSwitch,
                ConsecutiveHazardBeats = source.ConsecutiveHazardBeats
            };

            for (int i = 0; i < source.SwitchAtBeat.Length; i++)
            {
                clone.SwitchAtBeat[i] = (bool[])source.SwitchAtBeat[i].Clone();
                clone.LaneAfterBeat[i] = (int[])source.LaneAfterBeat[i].Clone();
                clone.Hazards[i] = new List<PlannerHazard>(source.Hazards[i]);
            }

            return clone;
        }

        private static void ExpandPhraseState(List<PhrasePlannerState> output, PhrasePlannerState state, PlannerBarContext[] bars)
        {
            int totalBeats = bars.Length * 4;
            if (state.BeatCursor >= totalBeats)
            {
                output.Add(state);
                return;
            }

            int globalBeat = state.BeatCursor;
            int barLocal = globalBeat / 4;
            int beatInBar = globalBeat % 4;
            PlannerBarContext bar = bars[barLocal];

            for (int switchDecision = 0; switchDecision <= 1; switchDecision++)
            {
                if (switchDecision == 1)
                {
                    if (state.SwitchCounts[barLocal] >= bar.SwitchTarget)
                    {
                        continue;
                    }

                    if (state.ConsecutiveSwitch >= 2)
                    {
                        continue;
                    }
                }

                int laneAfter = switchDecision == 1 ? 1 - state.CurrentLane : state.CurrentLane;
                int onbeatSlot = beatInBar * 4;
                int offbeatSlot = PickBestOffbeatSlot(bar, beatInBar);
                bool allowOffbeatChoice = bar.OffbeatMaxRatio > 0.001f
                    && switchDecision == 1
                    && beatInBar > 0;
                int[] slotOptions = allowOffbeatChoice
                    ? new[] { -1, onbeatSlot, offbeatSlot }
                    : new[] { -1, onbeatSlot };

                for (int optionIndex = 0; optionIndex < slotOptions.Length; optionIndex++)
                {
                    int slot = slotOptions[optionIndex];
                    bool hasHazard = slot >= 0;
                    if (hasHazard && state.HazardCounts[barLocal] >= bar.HazardTarget)
                    {
                        continue;
                    }

                    if (hasHazard)
                    {
                        if (state.ConsecutiveHazardBeats >= bar.MaxConsecutiveHazardBeats)
                        {
                            continue;
                        }

                        int gap = slot - state.LastHazardSlot[barLocal];
                        if (gap <= bar.MinGapSlots)
                        {
                            continue;
                        }
                    }

                    var next = ClonePhraseState(state);
                    next.BeatCursor = state.BeatCursor + 1;
                    next.CurrentLane = laneAfter;
                    next.SwitchAtBeat[barLocal][beatInBar] = switchDecision == 1;
                    next.LaneAfterBeat[barLocal][beatInBar] = laneAfter;
                    next.ConsecutiveSwitch = switchDecision == 1 ? state.ConsecutiveSwitch + 1 : 0;
                    next.ConsecutiveHazardBeats = hasHazard ? state.ConsecutiveHazardBeats + 1 : 0;
                    next.TieHash = unchecked((state.TieHash * 16777619) ^ ((switchDecision * 31) + ((slot + 1) * 17) + (globalBeat * 13)));

                    if (switchDecision == 1)
                    {
                        next.SwitchCounts[barLocal]++;
                        next.Score += 0.12f;
                    }
                    else
                    {
                        next.Score += 0.02f;
                    }

                    if (hasHazard)
                    {
                        bool offbeat = (slot % 4) != 0;
                        bool accent = bar.SlotAccent[slot] || (slot == 0) || (slot == 8);
                        float energy = bar.SlotEnergy[slot];
                        int hazardLane = 1 - laneAfter;
                        bool requiresTap = switchDecision == 1;
                        next.HazardCounts[barLocal]++;
                        next.LastHazardSlot[barLocal] = slot;
                        if (offbeat)
                        {
                            next.OffbeatCounts[barLocal]++;
                        }

                        next.Hazards[barLocal].Add(new PlannerHazard
                        {
                            BeatInBar = beatInBar,
                            SlotInBar = slot,
                            Lane = hazardLane,
                            RequiresTap = requiresTap,
                            Intensity = Mathf.Clamp01(energy + (accent ? 0.18f : 0f)),
                            Accent = accent
                        });

                        float anchorBonus = (slot == 0 || slot == 8) ? 0.90f : 0f;
                        float offbeatBonus = offbeat ? 0.12f : 0f;
                        next.Score += (energy * 4.6f) + 1.45f + anchorBonus + offbeatBonus;
                        if (!requiresTap)
                        {
                            next.Score -= 0.08f;
                        }
                    }
                    else
                    {
                        if (switchDecision == 1)
                        {
                            next.Score -= 0.09f;
                        }
                    }

                    if (!CanStillMeetTargets(next, bars, totalBeats))
                    {
                        continue;
                    }

                    output.Add(next);
                }
            }
        }

        private static int PickBestOffbeatSlot(PlannerBarContext bar, int beatInBar)
        {
            int start = beatInBar * 4;
            int best = start + 1;
            float bestEnergy = bar.SlotEnergy[best];
            for (int i = 2; i <= 3; i++)
            {
                int idx = start + i;
                float e = bar.SlotEnergy[idx];
                if (e > bestEnergy)
                {
                    best = idx;
                    bestEnergy = e;
                }
            }

            return best;
        }

        private static bool CanStillMeetTargets(PhrasePlannerState state, PlannerBarContext[] bars, int totalBeats)
        {
            int beatsDone = state.BeatCursor;
            for (int bar = 0; bar < bars.Length; bar++)
            {
                int barStartBeat = bar * 4;
                int completed = Mathf.Clamp(beatsDone - barStartBeat, 0, 4);
                int remaining = 4 - completed;
                if (state.HazardCounts[bar] > bars[bar].HazardTarget || state.SwitchCounts[bar] > bars[bar].SwitchTarget)
                {
                    return false;
                }

                if (state.HazardCounts[bar] + remaining < bars[bar].HazardTarget)
                {
                    return false;
                }

                if (state.SwitchCounts[bar] + remaining < bars[bar].SwitchTarget)
                {
                    return false;
                }
            }

            return beatsDone <= totalBeats;
        }

        private static bool IsPhraseStateComplete(PhrasePlannerState state, PlannerBarContext[] bars)
        {
            for (int bar = 0; bar < bars.Length; bar++)
            {
                if (state.HazardCounts[bar] != bars[bar].HazardTarget)
                {
                    return false;
                }

                if (state.SwitchCounts[bar] != bars[bar].SwitchTarget)
                {
                    return false;
                }
            }

            return true;
        }

        private static float EvaluateFinalPhraseScore(PhrasePlannerState state, PlannerBarContext[] bars)
        {
            float score = 0f;
            for (int bar = 0; bar < bars.Length; bar++)
            {
                int hazards = Mathf.Max(1, state.HazardCounts[bar]);
                float offRatio = state.OffbeatCounts[bar] / (float)hazards;
                if (offRatio < bars[bar].OffbeatMinRatio)
                {
                    score -= (bars[bar].OffbeatMinRatio - offRatio) * 4.0f;
                }
                else if (offRatio > bars[bar].OffbeatMaxRatio)
                {
                    score -= (offRatio - bars[bar].OffbeatMaxRatio) * 4.0f;
                }

                // Keep phrase anchors strong even in fallback.
                bool hasAnchor = state.Hazards[bar].Any(h => h.SlotInBar == 0 || h.SlotInBar == 8);
                if (hasAnchor)
                {
                    score += 0.60f;
                }
                else
                {
                    score -= 0.50f;
                }
            }

            return score;
        }

        private static BeatEvent CreateGridSourceEvent(
            BeatEvent anchor,
            float slotTimeSec,
            int lane,
            bool downbeat,
            float intensity,
            string archetype,
            float beatSec)
        {
            if (anchor != null)
            {
                return new BeatEvent
                {
                    timeSec = slotTimeSec,
                    endTimeSec = Mathf.Max(slotTimeSec, anchor.GetEndTimeSec()),
                    durationSec = Mathf.Max(0f, anchor.GetEndTimeSec() - slotTimeSec),
                    lane = lane,
                    laneTo = anchor.laneTo,
                    kind = ResolveGridKind(anchor.kind, downbeat, archetype),
                    intensity = intensity,
                    prefabId = anchor.prefabId,
                    motion = anchor.motion
                };
            }

            bool hold = string.Equals(archetype, GameplayArchetypes.HoldLaneLock, StringComparison.OrdinalIgnoreCase)
                || string.Equals(archetype, GameplayArchetypes.HoldReleaseGate, StringComparison.OrdinalIgnoreCase);
            float holdDur = hold ? Mathf.Clamp(beatSec * 2f, 0.8f, 2.4f) : 0f;
            return new BeatEvent
            {
                timeSec = slotTimeSec,
                endTimeSec = slotTimeSec + holdDur,
                durationSec = holdDur,
                lane = lane,
                laneTo = lane,
                kind = hold ? BeatKinds.Long : (downbeat ? BeatKinds.Accent : BeatKinds.Tap),
                intensity = intensity,
                prefabId = hold ? "hold_basic" : "tap_basic",
                motion = "grid"
            };
        }

        private static string ResolveGridKind(string originalKind, bool downbeat, string archetype)
        {
            bool hold = string.Equals(archetype, GameplayArchetypes.HoldLaneLock, StringComparison.OrdinalIgnoreCase)
                || string.Equals(archetype, GameplayArchetypes.HoldReleaseGate, StringComparison.OrdinalIgnoreCase);
            if (hold)
            {
                return BeatKinds.Long;
            }

            if (!string.IsNullOrWhiteSpace(originalKind) && string.Equals(originalKind, BeatKinds.Accent, StringComparison.OrdinalIgnoreCase))
            {
                return BeatKinds.Accent;
            }

            return downbeat ? BeatKinds.Accent : BeatKinds.Tap;
        }

        private static float ResolveSlotIntensity(BeatEvent anchor, string sectionType, bool downbeat, float targetStrain)
        {
            float intensity = anchor != null && anchor.intensity > 0f ? anchor.intensity : Mathf.Lerp(0.52f, 0.82f, targetStrain);
            if (downbeat)
            {
                intensity += 0.16f;
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                intensity += 0.10f;
            }
            else if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                intensity += 0.04f;
            }

            return Mathf.Clamp01(intensity);
        }

        private static string SelectGridArchetype(
            string presetId,
            string sectionType,
            bool downbeat,
            bool offbeat,
            float intensity,
            System.Random rng)
        {
            float roll = (float)rng.NextDouble();
            bool isDrop = string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase);
            bool isTransition = string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase);

            if (downbeat && (roll < 0.40f || intensity >= 0.78f))
            {
                return GameplayArchetypes.AccentCrusher;
            }

            if (offbeat && roll < (isDrop ? 0.45f : 0.30f))
            {
                return GameplayArchetypes.OffbeatSnap;
            }

            if (isDrop && roll < 0.26f)
            {
                return GameplayArchetypes.CrossGate;
            }

            if (isDrop && roll < 0.38f)
            {
                return GameplayArchetypes.AlternatorPair;
            }

            if (roll < 0.14f)
            {
                return GameplayArchetypes.HoldLaneLock;
            }

            if (roll < 0.21f)
            {
                return GameplayArchetypes.HoldReleaseGate;
            }

            if (isTransition && roll < 0.30f)
            {
                return GameplayArchetypes.FakeoutGhost;
            }

            if (string.Equals(presetId, "STREAK3_BREAK", StringComparison.OrdinalIgnoreCase) && roll < 0.34f)
            {
                return GameplayArchetypes.StreakBreaker;
            }

            if (string.Equals(presetId, "FAKEOUT_BRIDGE_TO_DROP", StringComparison.OrdinalIgnoreCase) && roll < 0.45f)
            {
                return GameplayArchetypes.FakeoutGhost;
            }

            return GameplayArchetypes.LaneBlock;
        }

        private static int ResolveHazardCountForBar(string sectionType, float targetStrain, System.Random rng)
        {
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                float t = Mathf.InverseLerp(0.65f, 0.85f, targetStrain);
                int baseCount = t >= 0.72f ? 4 : 3;
                if (t > 0.92f && rng.NextDouble() < 0.20d)
                {
                    return 5;
                }

                return baseCount;
            }

            // Active / transition
            float activeT = Mathf.InverseLerp(0.35f, 0.60f, targetStrain);
            int count = activeT >= 0.48f ? 2 : 1;
            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase)
                && rng.NextDouble() < 0.15d)
            {
                count = 2;
            }

            return count;
        }

        private static bool[] BuildEuclideanMask(int hits, int slots)
        {
            int n = Mathf.Max(1, slots);
            int k = Mathf.Clamp(hits, 0, n);
            var mask = new bool[n];
            if (k <= 0)
            {
                return mask;
            }

            for (int i = 0; i < n; i++)
            {
                int a = Mathf.FloorToInt(((i + 1f) * k) / n);
                int b = Mathf.FloorToInt((i * k) / (float)n);
                mask[i] = a != b;
            }

            return mask;
        }

        private static void ApplyMaskHarmonyRules(bool[] mask, string sectionType, int targetHits, System.Random rng)
        {
            if (mask == null || mask.Length == 0 || targetHits <= 0)
            {
                return;
            }

            bool isDrop = string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase);
            bool isActive = string.Equals(sectionType, GameplaySectionTypes.Active, StringComparison.OrdinalIgnoreCase);
            bool isTransition = string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase);
            if ((isDrop && rng.NextDouble() < 0.78d) || (!isDrop && rng.NextDouble() < 0.58d))
            {
                ForceSlot(mask, 0, true);
            }

            EnforceMaxRun(mask, isDrop ? 3 : 2);
            if (isActive)
            {
                EnforceMinGap(mask, 2);
            }

            float minOffbeat = isDrop ? 0.35f : (isTransition ? 0.24f : 0.18f);
            float maxOffbeat = isDrop ? 0.80f : 0.60f;
            BalanceOffbeatRatio(mask, minOffbeat, maxOffbeat, rng);
            NormalizeMaskCount(mask, targetHits, isDrop ? 2 : 3, rng);
        }

        private static void NormalizeMaskCount(bool[] mask, int targetHits, int minGapSlots, System.Random rng)
        {
            int current = CountTrue(mask);
            if (current > targetHits)
            {
                for (int i = mask.Length - 1; i >= 0 && current > targetHits; i--)
                {
                    if (!mask[i] || i == 0)
                    {
                        continue;
                    }

                    mask[i] = false;
                    current--;
                }
            }
            else if (current < targetHits)
            {
                for (int i = 0; i < mask.Length && current < targetHits; i++)
                {
                    int candidate = (i * 3) % mask.Length;
                    if (mask[candidate])
                    {
                        continue;
                    }

                    if (ViolatesMinGapMask(mask, candidate, minGapSlots))
                    {
                        continue;
                    }

                    mask[candidate] = true;
                    current++;
                }

                while (current < targetHits)
                {
                    int idx = rng.Next(0, mask.Length);
                    if (mask[idx])
                    {
                        continue;
                    }

                    mask[idx] = true;
                    current++;
                }
            }
        }

        private static void BalanceOffbeatRatio(bool[] mask, float minRatio, float maxRatio, System.Random rng)
        {
            int hits = CountTrue(mask);
            if (hits <= 0)
            {
                return;
            }

            int offbeats = CountOffbeats(mask);
            float ratio = offbeats / (float)hits;
            if (ratio < minRatio)
            {
                for (int slot = 0; slot < mask.Length && ratio < minRatio; slot += 4)
                {
                    if (!mask[slot])
                    {
                        continue;
                    }

                    int candidate = slot + 1 + rng.Next(0, 3);
                    if (candidate >= mask.Length || mask[candidate])
                    {
                        continue;
                    }

                    mask[slot] = false;
                    mask[candidate] = true;
                    offbeats++;
                    ratio = offbeats / (float)hits;
                }
            }
            else if (ratio > maxRatio)
            {
                for (int slot = 0; slot < mask.Length && ratio > maxRatio; slot++)
                {
                    if (!mask[slot] || (slot % 4) == 0)
                    {
                        continue;
                    }

                    int onbeat = (slot / 4) * 4;
                    if (mask[onbeat])
                    {
                        continue;
                    }

                    mask[slot] = false;
                    mask[onbeat] = true;
                    offbeats--;
                    ratio = offbeats / (float)hits;
                }
            }
        }

        private static void EnforceMinGap(bool[] mask, int emptySlotsBetweenHazards)
        {
            int minDistance = Mathf.Max(1, emptySlotsBetweenHazards + 1);
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                for (int j = i + 1; j < mask.Length; j++)
                {
                    if (!mask[j])
                    {
                        continue;
                    }

                    if (j - i < minDistance)
                    {
                        mask[j] = false;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private static bool ViolatesMinGapMask(bool[] mask, int candidate, int emptySlotsBetweenHazards)
        {
            int minDistance = Mathf.Max(1, emptySlotsBetweenHazards + 1);
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                if (Mathf.Abs(i - candidate) < minDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnforceMaxRun(bool[] mask, int maxRun)
        {
            int run = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    run = 0;
                    continue;
                }

                run++;
                if (run > maxRun)
                {
                    mask[i] = false;
                    run = maxRun;
                }
            }
        }

        private static void ForceSlot(bool[] mask, int slot, bool value)
        {
            int idx = Mathf.Clamp(slot, 0, mask.Length - 1);
            mask[idx] = value;
        }

        private static int CountTrue(bool[] mask)
        {
            int count = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i])
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOffbeats(bool[] mask)
        {
            int count = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] && (i % 4) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static BeatEvent FindNearestSourceEvent(IReadOnlyList<BeatEvent> sourceEvents, float slotTimeSec, float maxDeltaSec)
        {
            if (sourceEvents == null || sourceEvents.Count == 0)
            {
                return null;
            }

            BeatEvent nearest = null;
            float bestDelta = maxDeltaSec;
            for (int i = 0; i < sourceEvents.Count; i++)
            {
                BeatEvent evt = sourceEvents[i];
                if (evt == null)
                {
                    continue;
                }

                float delta = Mathf.Abs(evt.timeSec - slotTimeSec);
                if (delta <= bestDelta)
                {
                    bestDelta = delta;
                    nearest = evt;
                }
            }

            return nearest;
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
            System.Random rng,
            string forcedSourceKind = null)
        {
            switch (archetype)
            {
                case GameplayArchetypes.AccentCrusher:
                    AddJump(output, source.timeSec, lane, section.Type, presetId, archetype, Mathf.Max(0.75f, intensity), 1.35f, forcedSourceKind ?? BeatKinds.Accent);
                    AddAccentFx(output, source.timeSec, section.Type, presetId, intensity);
                    break;
                case GameplayArchetypes.AlternatorPair:
                    AddJump(output, source.timeSec, lane, section.Type, presetId, archetype, intensity, 1.25f, forcedSourceKind ?? BeatKinds.Tap);
                    TryAddJump(output, beatMap, source.timeSec + (beatSec * Mathf.Lerp(0.40f, 0.80f, (float)rng.NextDouble())), 1 - lane, section, presetId, archetype, intensity * 0.95f, 1.25f, forcedSourceKind ?? BeatKinds.Tap);
                    break;
                case GameplayArchetypes.StreakBreaker:
                    AddJump(output, source.timeSec, 1 - lane, section.Type, presetId, archetype, intensity, 1.30f, forcedSourceKind ?? BeatKinds.Tap);
                    break;
                case GameplayArchetypes.HoldLaneLock:
                    AddHold(output, source.timeSec, ResolveHoldEnd(source, section.EndSec, beatSec, false, rng), lane, section.Type, presetId, archetype, intensity);
                    break;
                case GameplayArchetypes.HoldReleaseGate:
                {
                    float holdEnd = ResolveHoldEnd(source, section.EndSec, beatSec, true, rng);
                    AddHold(output, source.timeSec, holdEnd, lane, section.Type, presetId, archetype, Mathf.Max(0.7f, intensity));
                    float release = Mathf.Min(section.EndSec - 0.02f, holdEnd + (beatSec * Mathf.Lerp(0.25f, 0.50f, (float)rng.NextDouble())));
                    TryAddJump(output, beatMap, release, 1 - lane, section, presetId, archetype, intensity, 1.28f, forcedSourceKind ?? BeatKinds.Accent);
                    AddAccentFx(output, release, section.Type, presetId, intensity);
                    break;
                }
                case GameplayArchetypes.CrossGate:
                    AddJump(output, source.timeSec, lane, section.Type, presetId, archetype, intensity, 1.32f, forcedSourceKind ?? BeatKinds.Tap);
                    TryAddJump(output, beatMap, source.timeSec + (beatSec * 0.5f), 1 - lane, section, presetId, archetype, intensity * 0.92f, 1.32f, forcedSourceKind ?? BeatKinds.Tap);
                    break;
                case GameplayArchetypes.OffbeatSnap:
                    TryAddJump(output, beatMap, QuantizeOffbeat(source.timeSec, beatSec), lane, section, presetId, archetype, intensity, 1.18f, forcedSourceKind ?? BeatKinds.Tap);
                    break;
                case GameplayArchetypes.FakeoutGhost:
                    AddFakeout(output, source.timeSec, lane, section.Type, presetId, archetype, intensity);
                    break;
                case GameplayArchetypes.LaneBlock:
                default:
                {
                    string sourceKind = forcedSourceKind ?? (source.IsKind(BeatKinds.Accent) ? BeatKinds.Accent : BeatKinds.Tap);
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

        private static void ApplyHazardDensityBudget(
            List<GameplayPatternEvent> sectionEvents,
            SectionWindow section,
            float beatSec,
            float targetStrain)
        {
            if (sectionEvents == null || sectionEvents.Count == 0)
            {
                return;
            }

            if (string.Equals(section.Type, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                sectionEvents.RemoveAll(e => e != null && e.isHazard);
                return;
            }

            var hazards = new List<GameplayPatternEvent>(sectionEvents.Count);
            for (int i = 0; i < sectionEvents.Count; i++)
            {
                GameplayPatternEvent evt = sectionEvents[i];
                if (evt != null && evt.isHazard)
                {
                    hazards.Add(evt);
                }
            }

            if (hazards.Count <= 1)
            {
                return;
            }

            float sectionDurationSec = Mathf.Max(beatSec, section.EndSec - section.StartSec);
            float barSec = Mathf.Max(0.001f, beatSec * 4f);
            int barCount = Mathf.Max(1, Mathf.CeilToInt(sectionDurationSec / barSec));
            float hazardsPerBar = ResolveHazardsPerBar(section.Type, targetStrain);
            int budget = Mathf.Max(1, Mathf.RoundToInt(hazardsPerBar * barCount));
            if (hazards.Count <= budget)
            {
                return;
            }

            float minSpacingSec = ResolveHazardSpacingSec(section.Type, beatSec);
            var ranked = hazards
                .OrderByDescending(e => ComputeHazardPriority(e))
                .ThenBy(e => e.hitTimeSec)
                .ToList();
            var keep = new List<GameplayPatternEvent>(budget);
            for (int i = 0; i < ranked.Count && keep.Count < budget; i++)
            {
                GameplayPatternEvent candidate = ranked[i];
                if (candidate == null)
                {
                    continue;
                }

                bool isHold = string.Equals(candidate.kind, GameplayPatternKinds.HoldSlide, StringComparison.OrdinalIgnoreCase);
                if (!isHold && ViolatesSpacing(candidate, keep, minSpacingSec))
                {
                    continue;
                }

                keep.Add(candidate);
            }

            if (keep.Count < budget)
            {
                for (int i = 0; i < ranked.Count && keep.Count < budget; i++)
                {
                    GameplayPatternEvent candidate = ranked[i];
                    if (candidate == null || keep.Contains(candidate))
                    {
                        continue;
                    }

                    keep.Add(candidate);
                }
            }

            var keepSet = new HashSet<GameplayPatternEvent>(keep);
            sectionEvents.RemoveAll(e => e != null && e.isHazard && !keepSet.Contains(e));
            PruneOrphanFxEvents(sectionEvents, keepSet, Mathf.Max(0.08f, beatSec * 0.28f));
        }

        private static void PruneOrphanFxEvents(
            List<GameplayPatternEvent> sectionEvents,
            HashSet<GameplayPatternEvent> keptHazards,
            float attachWindowSec)
        {
            if (sectionEvents == null || sectionEvents.Count == 0)
            {
                return;
            }

            sectionEvents.RemoveAll(e =>
            {
                if (e == null || e.isHazard)
                {
                    return false;
                }

                bool isAccentFx = string.Equals(e.kind, GameplayPatternKinds.AccentPulse, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.kind, GameplayPatternKinds.CameraShift, StringComparison.OrdinalIgnoreCase);
                if (!isAccentFx)
                {
                    return false;
                }

                foreach (GameplayPatternEvent hazard in keptHazards)
                {
                    if (hazard == null)
                    {
                        continue;
                    }

                    if (Mathf.Abs(hazard.hitTimeSec - e.hitTimeSec) <= attachWindowSec)
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        private static bool ViolatesSpacing(GameplayPatternEvent candidate, List<GameplayPatternEvent> keep, float minSpacingSec)
        {
            for (int i = 0; i < keep.Count; i++)
            {
                GameplayPatternEvent existing = keep[i];
                if (existing == null)
                {
                    continue;
                }

                if (Mathf.Abs(existing.hitTimeSec - candidate.hitTimeSec) < minSpacingSec)
                {
                    return true;
                }
            }

            return false;
        }

        private static float ComputeHazardPriority(GameplayPatternEvent evt)
        {
            if (evt == null)
            {
                return 0f;
            }

            float score = 1f + (evt.intensity * 0.45f);
            if (string.Equals(evt.kind, GameplayPatternKinds.HoldSlide, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.85f;
            }

            if (string.Equals(evt.sourceKind, BeatKinds.Accent, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.55f;
            }

            if (string.Equals(evt.archetype, GameplayArchetypes.HoldReleaseGate, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.30f;
            }
            else if (string.Equals(evt.archetype, GameplayArchetypes.AccentCrusher, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.24f;
            }
            else if (string.Equals(evt.archetype, GameplayArchetypes.CrossGate, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.16f;
            }

            return score;
        }

        private static float ResolveHazardsPerBar(string sectionType, float targetStrain)
        {
            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                float t = Mathf.InverseLerp(0.65f, 0.85f, targetStrain);
                return Mathf.Lerp(3.00f, 3.70f, t);
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                float t = Mathf.InverseLerp(0.35f, 0.60f, targetStrain);
                return Mathf.Lerp(1.00f, 1.60f, t);
            }

            float activeT = Mathf.InverseLerp(0.35f, 0.60f, targetStrain);
            return Mathf.Lerp(1.00f, 1.85f, activeT);
        }

        private static float ResolveHazardSpacingSec(string sectionType, float beatSec)
        {
            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Max(0.16f, beatSec * 0.38f);
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Transition, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Max(0.22f, beatSec * 0.58f);
            }

            return Mathf.Max(0.24f, beatSec * 0.62f);
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
