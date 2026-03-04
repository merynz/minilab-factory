using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluxOut.PCR
{
    public sealed class PCRValidationReport
    {
        public bool IsValid;
        public int RetryIndex;
        public string FailureReason = string.Empty;
        public float AverageMeaningfulIntervalSec;
        public float AverageShiftIntervalSec;
        public float MaxEmptyGapSec;
        public float MaxDecisionGapSec;
        public float MinRequiredTapRatePer10Sec;
        public float MaxSamePhaseRunSec;
        public float NoTapSurvivalSec;
        public int TotalShifts;
        public int MeaningfulCount;
        public int TapCount;
    }

    internal struct PCRSolverMetrics
    {
        public bool Solved;
        public int Shifts;
        public int MeaningfulCount;
        public int TapCount;
        public int MaxGapSteps;
        public int MaxPhaseRunSteps;
    }

    public sealed class PCRBuildResult
    {
        public PCRLevelRuntime Level;
        public PCRValidationReport Validation = new();
    }

    public static class PCRLevelBuilder
    {
        private const int MaxRetries = 40;
        private const int CoreLaneCount = 5;
        private const int MinInlineEventGapSteps = 2;
        private const int MinHazardGapSteps = 3;
        private const int RelocationSearchRadiusSteps = 8;
        private const int DeadLaneReactionWindowSteps = 10;
        private const int ForcedDeathLookaheadSteps = 10;
        private const int DecisionMinSpacingStepsFloor = 4;
        private const int DecisionMaxSpacingStepsCeil = 18;
        private const float DecisionRatioA = 0.45f;
        private const float DecisionRatioB = 0.45f;
        private const float DecisionRatioBoth = 0.10f;
        private const float DecisionReasonGateRatio = 0.14f;
        private const float DecisionReasonArcRatio = 0.12f;

        public static PCRBuildResult Build(PCRLevelDoc doc, int? seedOverride = null)
        {
            PCRDifficultyProfileSO difficulty = ResolveDifficulty(doc);
            PCRPacingProfileSO pacing = ResolvePacing(doc);
            List<PCRSegmentTemplateSO> templates = ResolveTemplates(doc);
            int baseSeed = seedOverride ?? (doc != null ? doc.Seed : 4242);
            int segmentCount = ResolveSegmentCount(doc, pacing);

            var result = new PCRBuildResult();
            for (int retry = 0; retry <= MaxRetries; retry++)
            {
                int seed = baseSeed + retry * 7919;
                var random = new System.Random(seed);
                PCRLevelRuntime level = BuildAttempt(seed, segmentCount, templates, difficulty, pacing, random);
                PCRSolverMetrics metrics = Solve(level);
                bool valid = Validate(level, pacing, metrics, out string reason, out PCRValidationReport report);
                if (valid)
                {
                    report.IsValid = true;
                    report.RetryIndex = retry;
                    result.Level = level;
                    result.Validation = report;
                    return result;
                }

                result.Level = level;
                result.Validation = report;
                result.Validation.IsValid = false;
                result.Validation.RetryIndex = retry;
                result.Validation.FailureReason = reason;
            }

            return result;
        }

        public static bool Revalidate(PCRLevelRuntime level, PCRPacingProfileSO pacing, out PCRValidationReport report)
        {
            report = new PCRValidationReport();
            if (level == null)
            {
                report.FailureReason = "Level is null.";
                return false;
            }

            BuildStepEventCache(level);
            BuildStepDecisionCache(level);
            StampWorldEventData(level);
            PCRSolverMetrics metrics = Solve(level);
            bool valid = Validate(level, pacing, metrics, out string reason, out PCRValidationReport validated);
            report = validated;
            report.IsValid = valid;
            report.FailureReason = valid ? string.Empty : reason;
            return valid;
        }

        private static PCRLevelRuntime BuildAttempt(
            int seed,
            int segmentCount,
            List<PCRSegmentTemplateSO> templates,
            PCRDifficultyProfileSO difficulty,
            PCRPacingProfileSO pacing,
            System.Random random)
        {
            float duration = pacing.TargetDurationSec;
            float simStep = pacing.SimStepSec;
            int totalSteps = Mathf.Max(10, Mathf.CeilToInt(duration / simStep));
            float[] stepDistances = BuildStepDistances(totalSteps, duration, simStep, difficulty.StartSpeed, difficulty.EndSpeed);

            List<PCRSegmentTemplateSO> selectedTemplates = ChooseSegments(segmentCount, templates, random);
            int totalUnits = 0;
            for (int i = 0; i < selectedTemplates.Count; i++)
            {
                totalUnits += Mathf.Max(1, selectedTemplates[i].LengthUnits);
            }

            var level = new PCRLevelRuntime
            {
                Seed = seed,
                DurationSec = duration,
                SimStepSec = simStep,
                StartSpeed = difficulty.StartSpeed,
                EndSpeed = difficulty.EndSpeed,
                TotalSteps = totalSteps,
                MaxLaneCount = CoreLaneCount,
                LaneSpacing = 1.7f,
                StepDistances = stepDistances,
                CenterlinePoints = new Vector3[totalSteps],
                Tangents = new Vector3[totalSteps]
            };

            int stepCursor = 0;
            float xCursor = 0f;
            float prevAmplitude = 3f;
            for (int i = 0; i < selectedTemplates.Count; i++)
            {
                PCRSegmentTemplateSO template = selectedTemplates[i];
                int remainingSteps = totalSteps - stepCursor;
                int remainingSegments = selectedTemplates.Count - i;
                int rawSteps = Mathf.RoundToInt(totalSteps * (template.LengthUnits / (float)totalUnits));
                int segmentSteps = i == selectedTemplates.Count - 1
                    ? remainingSteps
                    : Mathf.Clamp(rawSteps, 24, Mathf.Max(24, remainingSteps - (remainingSegments - 1) * 24));
                int startStep = stepCursor;
                int endStep = Mathf.Clamp(stepCursor + segmentSteps - 1, startStep, totalSteps - 1);
                stepCursor = endStep + 1;

                float ampJitter = Mathf.Lerp(0.9f, 1.1f, (float)random.NextDouble());
                float curveAmplitude = Mathf.Clamp(prevAmplitude * ampJitter + Mathf.Lerp(-0.5f, 0.5f, (float)random.NextDouble()), 2.2f, 5.2f);
                PCRCurveStyle curveStyle = template.CurveStyle;
                if (ShouldInjectTwist(i, selectedTemplates.Count, random))
                {
                    curveAmplitude = Mathf.Clamp(curveAmplitude * 1.35f + 0.7f, 2.8f, 7.2f);
                    curveStyle = PromoteCurveForTwist(curveStyle, random);
                }

                float relativeEnd = EvaluateCurveRelative(curveStyle, curveAmplitude, 1f);

                var runtime = new PCRSegmentRuntime
                {
                    TemplateId = template.TemplateId,
                    SegmentIndex = i,
                    StartStep = startStep,
                    EndStep = endStep,
                    LaneCountStart = CoreLaneCount,
                    LaneCountEnd = CoreLaneCount,
                    StaticDeadMask = 0,
                    CurveStyle = curveStyle,
                    CurveAmplitude = curveAmplitude,
                    StartDistance = stepDistances[startStep],
                    EndDistance = stepDistances[endStep],
                    XStart = xCursor,
                    XEnd = xCursor + relativeEnd
                };

                level.Segments.Add(runtime);
                xCursor = runtime.XEnd;
                prevAmplitude = curveAmplitude;
            }

            BuildCenterline(level);
            BuildCoreSignalLevel(level, pacing, random);
            BuildStepEventCache(level);
            BuildStepDecisionCache(level);
            StampWorldEventData(level);
            return level;
        }

        private static void BuildCoreSignalLevel(PCRLevelRuntime level, PCRPacingProfileSO pacing, System.Random random)
        {
            level.Events.Clear();
            level.Decisions.Clear();
            if (level.TotalSteps <= 8)
            {
                level.SafeLaneMaskByStep = null;
                return;
            }

            int minSpacing = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(pacing.EventCadenceMinSec, 0.9f, 1.5f) / level.SimStepSec),
                2,
                14);
            int maxSpacing = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(pacing.EventCadenceMaxSec, 1f, 1.8f) / level.SimStepSec),
                minSpacing,
                18);
            int leadSteps = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(pacing.DecisionLeadTimeSec, 0.5f, 1.8f) / level.SimStepSec),
                3,
                14);

            int[] safeMaskByStep = new int[level.TotalSteps];
            int currentMask = 0b10101; // lanes 1-3-5 (0,2,4)
            int rangeStartStep = 0;
            int playerLane = 2;
            PCRPhase playerPhase = PCRPhase.A;

            int firstDecisionStep = Mathf.Clamp(leadSteps + 2, 4, level.TotalSteps - 4);
            int stepCursor = firstDecisionStep;
            int eventId = 1;
            while (stepCursor < level.TotalSteps - 4)
            {
                int decisionStep = stepCursor;
                FillMaskRange(safeMaskByStep, rangeStartStep, decisionStep - 1, currentMask);

                PCRPhase requiredPhase = playerPhase == PCRPhase.A ? PCRPhase.B : PCRPhase.A;
                int delta = PickRouteDeltaForLane(playerLane, random);
                int requiredLane = Mathf.Clamp(playerLane + delta, 0, CoreLaneCount - 1);
                int wrongLane = Mathf.Clamp(playerLane - delta, 0, CoreLaneCount - 1);
                int nextMask = PickNextSafeMask(requiredLane, wrongLane, random);

                int routeA = requiredPhase == PCRPhase.A ? delta : -delta;
                int routeB = -routeA;
                for (int lane = 0; lane < CoreLaneCount; lane++)
                {
                    level.Events.Add(new PCREventRuntime
                    {
                        EventId = eventId++,
                        Type = PCRElementType.MuxSwitch,
                        SegmentIndex = FindSegment(level, decisionStep).SegmentIndex,
                        StepIndex = decisionStep,
                        LaneIndex = lane,
                        SideOffset = 0f,
                        PhaseParam = requiredPhase,
                        RouteDeltaA = routeA,
                        RouteDeltaB = routeB,
                        IsField = false
                    });
                }

                level.Decisions.Add(new PCRDecisionWindow
                {
                    StepIndex = decisionStep,
                    RequiredMask = requiredPhase == PCRPhase.A ? PCRPhaseMask.A : PCRPhaseMask.B,
                    Reason = PCRDecisionReason.Routing,
                    LeadSteps = leadSteps,
                    LaneIndex = playerLane
                });

                currentMask = nextMask;
                rangeStartStep = decisionStep;
                playerLane = requiredLane;
                playerPhase = requiredPhase;
                stepCursor += random.Next(minSpacing, maxSpacing + 1);
            }

            FillMaskRange(safeMaskByStep, rangeStartStep, level.TotalSteps - 1, currentMask);
            for (int s = 0; s < level.TotalSteps; s++)
            {
                int mask = safeMaskByStep[s];
                if (mask == 0)
                {
                    mask = 0b00100;
                }

                safeMaskByStep[s] = mask;
            }

            level.SafeLaneMaskByStep = safeMaskByStep;
            NormalizeDecisionWindows(level);
            level.Events.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
        }

        private static void FillMaskRange(int[] masks, int start, int end, int mask)
        {
            if (masks == null || masks.Length == 0)
            {
                return;
            }

            int from = Mathf.Clamp(start, 0, masks.Length - 1);
            int to = Mathf.Clamp(end, 0, masks.Length - 1);
            if (to < from)
            {
                return;
            }

            for (int i = from; i <= to; i++)
            {
                masks[i] = mask;
            }
        }

        private static int PickRouteDeltaForLane(int lane, System.Random random)
        {
            if (lane <= 0)
            {
                return 1;
            }

            if (lane >= CoreLaneCount - 1)
            {
                return -1;
            }

            return random.NextDouble() > 0.5 ? 1 : -1;
        }

        private static int PickNextSafeMask(int requiredLane, int wrongLane, System.Random random)
        {
            int[] masks =
            {
                0b10101, // 1-3-5
                0b01110, // 2-3-4
                0b00111, // 1-2-3
                0b11100, // 3-4-5
                0b01010, // 2-4
                0b10001, // 1-5
                0b00110, // 2-3
                0b01100, // 3-4
                0b00011, // 1-2
                0b11000, // 4-5
                0b00101, // 1-3
                0b10100  // 3-5
            };

            var preferred = new List<int>(masks.Length);
            var fallback = new List<int>(masks.Length);
            int requiredBit = 1 << Mathf.Clamp(requiredLane, 0, CoreLaneCount - 1);
            int wrongBit = 1 << Mathf.Clamp(wrongLane, 0, CoreLaneCount - 1);
            for (int i = 0; i < masks.Length; i++)
            {
                int mask = masks[i];
                bool hasRequired = (mask & requiredBit) != 0;
                if (!hasRequired)
                {
                    continue;
                }

                fallback.Add(mask);
                if ((mask & wrongBit) == 0)
                {
                    preferred.Add(mask);
                }
            }

            if (preferred.Count > 0)
            {
                return preferred[random.Next(0, preferred.Count)];
            }

            if (fallback.Count > 0)
            {
                return fallback[random.Next(0, fallback.Count)];
            }

            return 0b10101;
        }

        private static void BuildCenterline(PCRLevelRuntime level)
        {
            for (int s = 0; s < level.TotalSteps; s++)
            {
                PCRSegmentRuntime segment = FindSegment(level, s);
                float localT = Mathf.InverseLerp(segment.StartStep, segment.EndStep, s);
                float relativeX = EvaluateCurveRelative(segment.CurveStyle, segment.CurveAmplitude, localT);
                float x = segment.XStart + relativeX;
                float z = level.StepDistances[s];
                level.CenterlinePoints[s] = new Vector3(x, 0f, z);
            }

            for (int s = 0; s < level.TotalSteps; s++)
            {
                int a = Mathf.Max(0, s - 1);
                int b = Mathf.Min(level.TotalSteps - 1, s + 1);
                Vector3 tangent = (level.CenterlinePoints[b] - level.CenterlinePoints[a]).normalized;
                if (tangent.sqrMagnitude < 0.0001f)
                {
                    tangent = Vector3.forward;
                }

                level.Tangents[s] = tangent;
            }
        }

        private static void BuildEvents(PCRLevelRuntime level, List<PCRSegmentTemplateSO> selectedTemplates, System.Random random)
        {
            int eventId = 1;
            var fieldSlotUsed = new HashSet<int>();
            for (int i = 0; i < level.Segments.Count; i++)
            {
                PCRSegmentRuntime segment = level.Segments[i];
                PCRSegmentTemplateSO template = selectedTemplates[i];
                int segmentSpan = Mathf.Max(1, segment.EndStep - segment.StartStep);
                for (int p = 0; p < template.Placements.Count; p++)
                {
                    PCRElementPlacement placement = template.Placements[p];
                    int step = segment.StartStep + Mathf.RoundToInt(placement.SPosition01 * segmentSpan);
                    step = Mathf.Clamp(step, segment.StartStep, segment.EndStep);
                    int laneCount = PCRSimulation.GetLaneCountAtStep(level, step);
                    int lane = placement.LaneIndex;
                    if (lane < 0)
                    {
                        lane = random.Next(0, laneCount);
                    }

                    lane = Mathf.Clamp(lane, 0, laneCount - 1);
                    if (PCRSimulation.IsFieldType(placement.Type))
                    {
                        int key = BuildFieldKey(step, lane);
                        if (fieldSlotUsed.Contains(key))
                        {
                            bool moved = false;
                            for (int offset = 1; offset <= 2 && !moved; offset++)
                            {
                                int ahead = Mathf.Clamp(step + offset, segment.StartStep, segment.EndStep);
                                int aheadKey = BuildFieldKey(ahead, lane);
                                if (!fieldSlotUsed.Contains(aheadKey))
                                {
                                    step = ahead;
                                    moved = true;
                                    break;
                                }

                                int back = Mathf.Clamp(step - offset, segment.StartStep, segment.EndStep);
                                int backKey = BuildFieldKey(back, lane);
                                if (!fieldSlotUsed.Contains(backKey))
                                {
                                    step = back;
                                    moved = true;
                                    break;
                                }
                            }
                        }

                        fieldSlotUsed.Add(BuildFieldKey(step, lane));
                    }

                    level.Events.Add(new PCREventRuntime
                    {
                        EventId = eventId++,
                        Type = placement.Type,
                        SegmentIndex = i,
                        StepIndex = step,
                        LaneIndex = lane,
                        SideOffset = placement.SideOffset,
                        PhaseParam = placement.PhaseParam,
                        RouteDeltaA = placement.RouteDeltaA,
                        RouteDeltaB = placement.RouteDeltaB,
                        IsField = PCRSimulation.IsFieldType(placement.Type)
                    });
                }
            }

            level.Events.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
        }

        private static void EnforcePacingByInjection(PCRLevelRuntime level, PCRPacingProfileSO pacing, System.Random random)
        {
            int maxEmptyGapSteps = Mathf.Max(1, Mathf.RoundToInt(pacing.EmptyGapThresholdSec / level.SimStepSec));
            int maxShiftGapSteps = Mathf.Max(1, Mathf.RoundToInt(pacing.LaneShiftTargetSec / level.SimStepSec));

            EnsureEventDensity(
                level,
                maxEmptyGapSteps,
                IsMeaningfulType,
                () => new PCREventRuntime
                {
                    Type = PCRElementType.MuxSwitch,
                    PhaseParam = PCRPhase.A,
                    RouteDeltaA = 1,
                    RouteDeltaB = -1,
                    IsField = false
                },
                random);

            EnsureEventDensity(
                level,
                maxShiftGapSteps,
                IsRoutingSourceType,
                () => new PCREventRuntime
                {
                    Type = random.NextDouble() > 0.5 ? PCRElementType.MuxSwitch : PCRElementType.InductorCoupler,
                    PhaseParam = PCRPhase.A,
                    RouteDeltaA = 1,
                    RouteDeltaB = -1,
                    IsField = random.NextDouble() > 0.5
                },
                random);

            BuildStepEventCache(level);
            for (int step = 1; step < level.TotalSteps - 1; step++)
            {
                int laneCount = PCRSimulation.GetLaneCountAtStep(level, step);
                if (laneCount <= 2)
                {
                    continue;
                }

                // Safe path guard: keep center lane alive if dead by mistake.
                int centerLane = laneCount / 2;
                if (PCRSimulation.IsDeadTrace(level, step, centerLane))
                {
                    PCRSegmentRuntime segment = FindSegment(level, step);
                    segment.StaticDeadMask &= ~(1 << centerLane);
                }

                // Remove double field conflict on same lane/step.
                if (level.EventsByStep[step].Count > 1)
                {
                    var seenField = new HashSet<int>();
                    for (int i = level.EventsByStep[step].Count - 1; i >= 0; i--)
                    {
                        PCREventRuntime evt = level.EventsByStep[step][i];
                        if (!evt.IsField)
                        {
                            continue;
                        }

                        if (!seenField.Add(evt.LaneIndex))
                        {
                            level.Events.Remove(evt);
                            level.EventsByStep[step].RemoveAt(i);
                        }
                    }
                }
            }

            NormalizeInlineSpacing(level, random);
            SoftenUnavoidableDeadLanes(level);
            BuildStepEventCache(level);
        }

        private static void EnsureEventDensity(
            PCRLevelRuntime level,
            int maxGapSteps,
            Func<PCREventRuntime, bool> matcher,
            Func<PCREventRuntime> factory,
            System.Random random)
        {
            int gap = 0;
            int nextEventId = level.Events.Count + 1;
            for (int step = 0; step < level.TotalSteps; step++)
            {
                bool found = false;
                for (int i = 0; i < level.Events.Count; i++)
                {
                    if (level.Events[i].StepIndex == step && matcher(level.Events[i]))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    gap = 0;
                    continue;
                }

                gap++;
                if (gap <= maxGapSteps)
                {
                    continue;
                }

                int insertStep = Mathf.Max(0, step - maxGapSteps / 2);
                int laneCount = PCRSimulation.GetLaneCountAtStep(level, insertStep);
                var evt = factory();
                evt.EventId = nextEventId++;
                evt.StepIndex = insertStep;
                evt.LaneIndex = PickLaneForInjectedEvent(level, insertStep, laneCount, evt, random);
                evt.SegmentIndex = FindSegment(level, insertStep).SegmentIndex;
                evt.SideOffset = evt.IsField ? Mathf.Lerp(-1.2f, 1.2f, (float)random.NextDouble()) : 0f;
                if (evt.Type == PCRElementType.InductorCoupler || evt.Type == PCRElementType.MuxSwitch)
                {
                    evt.RouteDeltaA = 1;
                    evt.RouteDeltaB = -1;
                }

                level.Events.Add(evt);
                gap = 0;
            }

            level.Events.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
        }

        private static int PickLaneForInjectedEvent(PCRLevelRuntime level, int stepIndex, int laneCount, PCREventRuntime newEvent, System.Random random)
        {
            int bestLane = random.Next(0, laneCount);
            int bestScore = int.MinValue;
            for (int lane = 0; lane < laneCount; lane++)
            {
                int score = 0;
                bool blocked = false;
                for (int i = 0; i < level.Events.Count; i++)
                {
                    PCREventRuntime other = level.Events[i];
                    if (other.LaneIndex != lane)
                    {
                        continue;
                    }

                    int diff = Mathf.Abs(other.StepIndex - stepIndex);
                    if (newEvent.IsField && other.IsField && diff == 0)
                    {
                        blocked = true;
                        break;
                    }

                    if (!newEvent.IsField && !other.IsField)
                    {
                        int minGap = IsHazardType(newEvent.Type) || IsHazardType(other.Type)
                            ? MinHazardGapSteps
                            : MinInlineEventGapSteps;
                        if (diff < minGap)
                        {
                            score -= 120;
                        }
                    }

                    score += Mathf.Min(diff, 12);
                }

                if (blocked)
                {
                    continue;
                }

                score += random.Next(0, 4);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLane = lane;
                }
            }

            return bestLane;
        }

        private static void NormalizeInlineSpacing(PCRLevelRuntime level, System.Random random)
        {
            if (level.Events == null || level.Events.Count == 0)
            {
                return;
            }

            var removeIds = new HashSet<int>();
            level.Events.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            for (int lane = 0; lane < level.MaxLaneCount; lane++)
            {
                var laneEvents = new List<PCREventRuntime>();
                for (int i = 0; i < level.Events.Count; i++)
                {
                    PCREventRuntime evt = level.Events[i];
                    if (evt.IsField || evt.LaneIndex != lane || removeIds.Contains(evt.EventId))
                    {
                        continue;
                    }

                    laneEvents.Add(evt);
                }

                laneEvents.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
                int lastInlineStep = -1000;
                int lastHazardStep = -1000;
                for (int i = 0; i < laneEvents.Count; i++)
                {
                    PCREventRuntime evt = laneEvents[i];
                    int requiredGap = IsHazardType(evt.Type) ? MinHazardGapSteps : MinInlineEventGapSteps;
                    bool tooCloseInline = evt.StepIndex - lastInlineStep < requiredGap;
                    bool tooCloseHazard = IsHazardType(evt.Type) && evt.StepIndex - lastHazardStep < MinHazardGapSteps;
                    if (tooCloseInline || tooCloseHazard)
                    {
                        if (TryRelocateEvent(level, evt, out int relocatedStep))
                        {
                            evt.StepIndex = relocatedStep;
                        }
                        else if (CanDropForSpacing(evt.Type))
                        {
                            removeIds.Add(evt.EventId);
                            continue;
                        }
                    }

                    lastInlineStep = evt.StepIndex;
                    if (IsHazardType(evt.Type))
                    {
                        lastHazardStep = evt.StepIndex;
                    }
                }
            }

            if (removeIds.Count > 0)
            {
                level.Events.RemoveAll(evt => removeIds.Contains(evt.EventId));
            }

            level.Events.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
        }

        private static bool TryRelocateEvent(PCRLevelRuntime level, PCREventRuntime evt, out int relocatedStep)
        {
            relocatedStep = evt.StepIndex;
            PCRSegmentRuntime segment = FindSegment(level, evt.StepIndex);
            int minStep = Mathf.Max(segment.StartStep + 1, 1);
            int maxStep = Mathf.Min(segment.EndStep - 1, level.TotalSteps - 2);
            if (maxStep <= minStep)
            {
                return false;
            }

            if (IsCandidateStepValid(level, evt, evt.StepIndex))
            {
                relocatedStep = evt.StepIndex;
                return true;
            }

            for (int radius = 1; radius <= RelocationSearchRadiusSteps; radius++)
            {
                int candidateA = evt.StepIndex + radius;
                if (candidateA >= minStep && candidateA <= maxStep && IsCandidateStepValid(level, evt, candidateA))
                {
                    relocatedStep = candidateA;
                    return true;
                }
            }

            return false;
        }

        private static bool IsCandidateStepValid(PCRLevelRuntime level, PCREventRuntime evt, int stepIndex)
        {
            int laneCount = PCRSimulation.GetLaneCountAtStep(level, stepIndex);
            if (evt.LaneIndex >= laneCount)
            {
                return false;
            }

            for (int i = 0; i < level.Events.Count; i++)
            {
                PCREventRuntime other = level.Events[i];
                if (other.EventId == evt.EventId || other.LaneIndex != evt.LaneIndex)
                {
                    continue;
                }

                int diff = Mathf.Abs(other.StepIndex - stepIndex);
                if (other.IsField)
                {
                    if (diff == 0)
                    {
                        return false;
                    }

                    continue;
                }

                int minGap = IsHazardType(evt.Type) || IsHazardType(other.Type)
                    ? MinHazardGapSteps
                    : MinInlineEventGapSteps;
                if (diff < minGap)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsHazardType(PCRElementType type)
        {
            return type == PCRElementType.DiodeGate || type == PCRElementType.SparkGap;
        }

        private static bool CanDropForSpacing(PCRElementType type)
        {
            return type == PCRElementType.Capacitor
                   || type == PCRElementType.Amplifier
                   || type == PCRElementType.GroundClamp;
        }

        private static void SoftenUnavoidableDeadLanes(PCRLevelRuntime level)
        {
            for (int segmentIndex = 0; segmentIndex < level.Segments.Count; segmentIndex++)
            {
                PCRSegmentRuntime segment = level.Segments[segmentIndex];
                int startStep = Mathf.Clamp(segment.StartStep, 0, level.TotalSteps - 1);
                int laneCount = PCRSimulation.GetLaneCountAtStep(level, startStep);
                int mask = segment.StaticDeadMask;
                for (int lane = 0; lane < laneCount; lane++)
                {
                    if ((mask & (1 << lane)) == 0)
                    {
                        continue;
                    }

                    bool hadRouteOpportunity = HasNearbyRouteOpportunity(level, lane, startStep, DeadLaneReactionWindowSteps);
                    if (!hadRouteOpportunity)
                    {
                        segment.StaticDeadMask &= ~(1 << lane);
                    }
                }
            }
        }

        private static bool HasNearbyRouteOpportunity(PCRLevelRuntime level, int lane, int stepInclusive, int windowSteps)
        {
            int fromStep = Mathf.Max(0, stepInclusive - Mathf.Max(1, windowSteps));
            int toStep = Mathf.Min(level.TotalSteps - 1, stepInclusive + Mathf.Max(1, windowSteps / 2));
            for (int i = 0; i < level.Events.Count; i++)
            {
                PCREventRuntime evt = level.Events[i];
                if (evt.LaneIndex != lane)
                {
                    continue;
                }

                if (evt.StepIndex < fromStep || evt.StepIndex > toStep)
                {
                    continue;
                }

                if (evt.Type == PCRElementType.MuxSwitch || evt.Type == PCRElementType.InductorCoupler)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BuildDecisionSchedule(
            PCRLevelRuntime level,
            List<PCRSegmentTemplateSO> selectedTemplates,
            PCRPacingProfileSO pacing,
            System.Random random)
        {
            level.Decisions.Clear();
            if (level.TotalSteps < 8 || selectedTemplates == null || selectedTemplates.Count == 0)
            {
                return;
            }

            int minSpacing = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(pacing.EventCadenceMinSec, 0.8f, 1.4f) / level.SimStepSec),
                DecisionMinSpacingStepsFloor,
                DecisionMaxSpacingStepsCeil);
            int maxSpacing = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(pacing.EventCadenceMaxSec, 0.9f, 1.6f) / level.SimStepSec),
                minSpacing,
                Mathf.Max(minSpacing, DecisionMaxSpacingStepsCeil));
            int leadSteps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(pacing.DecisionLeadTimeSec, 0.4f, 2f) / level.SimStepSec), 2, 16);
            int maxSameRunSteps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(pacing.MaxSamePhaseRunSec, 1.5f, 5f) / level.SimStepSec), minSpacing + 1, 64);

            int lastDecisionStep = 1;
            int predictedPhaseRunSteps = 1;
            PCRPhase predictedPhase = PCRPhase.A;
            int countA = 0;
            int countB = 0;
            int countBoth = 0;

            for (int segmentIndex = 0; segmentIndex < level.Segments.Count; segmentIndex++)
            {
                PCRSegmentRuntime segment = level.Segments[segmentIndex];
                PCRSegmentTemplateSO template = selectedTemplates[Mathf.Clamp(segmentIndex, 0, selectedTemplates.Count - 1)];
                int intensity = Mathf.Clamp(template.DecisionIntensity, 1, 5);
                float intensity01 = (intensity - 1) / 4f;
                int spacing = Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(maxSpacing + 1, minSpacing, intensity01)),
                    minSpacing,
                    maxSpacing);

                int start = Mathf.Clamp(segment.StartStep + 2, 2, level.TotalSteps - 3);
                int end = Mathf.Clamp(segment.EndStep - 2, start + 1, level.TotalSteps - 2);
                int cursor = Mathf.Max(start, lastDecisionStep + minSpacing);
                int jitterRange = Mathf.Max(1, Mathf.RoundToInt(spacing * 0.2f));
                cursor += random.Next(-jitterRange, jitterRange + 1);

                while (cursor <= end)
                {
                    int step = Mathf.Clamp(cursor, start, end);
                    if (step - lastDecisionStep < minSpacing)
                    {
                        cursor += minSpacing;
                        continue;
                    }

                    PCRPhaseMask mask = PickDecisionMask(
                        template.PhaseDemandBias,
                        ref countA,
                        ref countB,
                        ref countBoth,
                        predictedPhase,
                        predictedPhaseRunSteps,
                        maxSameRunSteps,
                        step - lastDecisionStep,
                        random);
                    if (mask == PCRPhaseMask.A)
                    {
                        if (predictedPhase == PCRPhase.A)
                        {
                            predictedPhaseRunSteps += step - lastDecisionStep;
                        }
                        else
                        {
                            predictedPhase = PCRPhase.A;
                            predictedPhaseRunSteps = step - lastDecisionStep;
                        }
                    }
                    else if (mask == PCRPhaseMask.B)
                    {
                        if (predictedPhase == PCRPhase.B)
                        {
                            predictedPhaseRunSteps += step - lastDecisionStep;
                        }
                        else
                        {
                            predictedPhase = PCRPhase.B;
                            predictedPhaseRunSteps = step - lastDecisionStep;
                        }
                    }
                    else
                    {
                        predictedPhaseRunSteps += step - lastDecisionStep;
                    }

                    if (predictedPhaseRunSteps > maxSameRunSteps)
                    {
                        mask = predictedPhase == PCRPhase.A ? PCRPhaseMask.B : PCRPhaseMask.A;
                        predictedPhase = mask == PCRPhaseMask.A ? PCRPhase.A : PCRPhase.B;
                        predictedPhaseRunSteps = 1;
                    }

                    int laneCount = PCRSimulation.GetLaneCountAtStep(level, step);
                    int centerLane = Mathf.Clamp((laneCount - 1) / 2, 0, laneCount - 1);
                    var decision = new PCRDecisionWindow
                    {
                        StepIndex = step,
                        RequiredMask = mask,
                        Reason = PickDecisionReason(mask, random),
                        LeadSteps = leadSteps,
                        LaneIndex = centerLane
                    };
                    level.Decisions.Add(decision);
                    lastDecisionStep = step;
                    cursor += spacing + random.Next(-jitterRange, jitterRange + 1);
                }
            }

            // Hard max-gap fill so decision pressure never goes idle.
            level.Decisions.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            int decisionMaxGapSteps = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(pacing.MaxDecisionGapSec, 1f, 2.5f) / level.SimStepSec),
                minSpacing,
                DecisionMaxSpacingStepsCeil + 8);

            int lastStep = 1;
            int nextIdCursor = 0;
            while (nextIdCursor < level.Decisions.Count)
            {
                PCRDecisionWindow next = level.Decisions[nextIdCursor];
                int gap = next.StepIndex - lastStep;
                if (gap > decisionMaxGapSteps)
                {
                    int step = Mathf.Clamp(lastStep + decisionMaxGapSteps, 2, level.TotalSteps - 3);
                    int laneCount = PCRSimulation.GetLaneCountAtStep(level, step);
                    level.Decisions.Add(new PCRDecisionWindow
                    {
                        StepIndex = step,
                        RequiredMask = random.NextDouble() > 0.5 ? PCRPhaseMask.A : PCRPhaseMask.B,
                        Reason = PCRDecisionReason.Gate,
                        LeadSteps = leadSteps,
                        LaneIndex = Mathf.Clamp((laneCount - 1) / 2, 0, laneCount - 1)
                    });
                    level.Decisions.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
                    nextIdCursor = 0;
                    lastStep = 1;
                    continue;
                }

                lastStep = next.StepIndex;
                nextIdCursor++;
            }

            int finalGap = level.TotalSteps - 1 - lastStep;
            if (finalGap > decisionMaxGapSteps)
            {
                int step = Mathf.Clamp(lastStep + decisionMaxGapSteps, 2, level.TotalSteps - 3);
                int laneCount = PCRSimulation.GetLaneCountAtStep(level, step);
                level.Decisions.Add(new PCRDecisionWindow
                {
                    StepIndex = step,
                    RequiredMask = random.NextDouble() > 0.5 ? PCRPhaseMask.A : PCRPhaseMask.B,
                    Reason = PCRDecisionReason.Gate,
                    LeadSteps = leadSteps,
                    LaneIndex = Mathf.Clamp((laneCount - 1) / 2, 0, laneCount - 1)
                });
                level.Decisions.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            }

            NormalizeDecisionWindows(level);
        }

        private static void ApplyDecisionWindows(PCRLevelRuntime level, System.Random random)
        {
            if (level.Decisions == null || level.Decisions.Count == 0)
            {
                return;
            }

            int nextEventId = level.Events.Count + 1;
            level.Decisions.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            for (int i = 0; i < level.Decisions.Count; i++)
            {
                PCRDecisionWindow decision = level.Decisions[i];
                int step = Mathf.Clamp(decision.StepIndex, 1, level.TotalSteps - 2);
                int segmentIndex = FindSegment(level, step).SegmentIndex;
                int laneCount = PCRSimulation.GetLaneCountAtStep(level, step);
                int decisionLane = decision.LaneIndex >= 0
                    ? Mathf.Clamp(decision.LaneIndex, 0, laneCount - 1)
                    : Mathf.Clamp((laneCount - 1) / 2, 0, laneCount - 1);
                RemoveInlineEventsAtStep(level, step);

                if (decision.RequiredMask == PCRPhaseMask.Both)
                {
                    // "Both" is routing-only pressure: show shift logic without phase-locking.
                    for (int lane = 0; lane < laneCount; lane++)
                    {
                        ComputeBalancedRoutes(lane, laneCount, decisionLane, random, out int routeA, out int routeB);
                        bool useField = random.NextDouble() > 0.72;
                        level.Events.Add(new PCREventRuntime
                        {
                            EventId = nextEventId++,
                            Type = useField ? PCRElementType.InductorCoupler : PCRElementType.MuxSwitch,
                            SegmentIndex = segmentIndex,
                            StepIndex = step,
                            LaneIndex = lane,
                            SideOffset = useField ? (random.NextDouble() > 0.5 ? 0.86f : -0.86f) : 0f,
                            PhaseParam = PCRPhase.A,
                            RouteDeltaA = routeA,
                            RouteDeltaB = routeB,
                            IsField = useField
                        });
                    }

                    continue;
                }

                PCRPhase required = decision.RequiredMask == PCRPhaseMask.B ? PCRPhase.B : PCRPhase.A;
                PCRPhase lethal = required == PCRPhase.A ? PCRPhase.B : PCRPhase.A;
                if (decision.Reason == PCRDecisionReason.Routing)
                {
                    // Separate phase-check and routing by one step to avoid stacked unreadable events.
                    int routeStep = Mathf.Min(step + 1, level.TotalSteps - 2);
                    if (routeStep != step)
                    {
                        RemoveInlineEventsAtStep(level, routeStep);
                    }

                    for (int lane = 0; lane < laneCount; lane++)
                    {
                        // Phase-check first.
                        level.Events.Add(new PCREventRuntime
                        {
                            EventId = nextEventId++,
                            Type = PCRElementType.DiodeGate,
                            SegmentIndex = segmentIndex,
                            StepIndex = step,
                            LaneIndex = lane,
                            SideOffset = 0f,
                            PhaseParam = required,
                            RouteDeltaA = 0,
                            RouteDeltaB = 0,
                            IsField = false
                        });

                        ComputeRequiredRoutes(lane, laneCount, required, out int routeA, out int routeB);
                        bool useField = random.NextDouble() > 0.68;
                        level.Events.Add(new PCREventRuntime
                        {
                            EventId = nextEventId++,
                            Type = useField ? PCRElementType.InductorCoupler : PCRElementType.MuxSwitch,
                            SegmentIndex = segmentIndex,
                            StepIndex = routeStep,
                            LaneIndex = lane,
                            SideOffset = useField ? (random.NextDouble() > 0.5 ? 0.85f : -0.85f) : 0f,
                            PhaseParam = required,
                            RouteDeltaA = routeA,
                            RouteDeltaB = routeB,
                            IsField = useField
                        });
                    }

                    continue;
                }

                for (int lane = 0; lane < laneCount; lane++)
                {
                    if (decision.Reason == PCRDecisionReason.Arc)
                    {
                        level.Events.Add(new PCREventRuntime
                        {
                            EventId = nextEventId++,
                            Type = PCRElementType.SparkGap,
                            SegmentIndex = segmentIndex,
                            StepIndex = step,
                            LaneIndex = lane,
                            SideOffset = random.NextDouble() > 0.5 ? 0.9f : -0.9f,
                            PhaseParam = lethal,
                            RouteDeltaA = 0,
                            RouteDeltaB = 0,
                            IsField = true
                        });
                    }
                    else
                    {
                        level.Events.Add(new PCREventRuntime
                        {
                            EventId = nextEventId++,
                            Type = PCRElementType.DiodeGate,
                            SegmentIndex = segmentIndex,
                            StepIndex = step,
                            LaneIndex = lane,
                            SideOffset = 0f,
                            PhaseParam = required,
                            RouteDeltaA = 0,
                            RouteDeltaB = 0,
                            IsField = false
                        });
                    }
                }
            }

            level.Events.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            BuildStepEventCache(level);
            BuildStepDecisionCache(level);
        }

        private static void RemoveInlineEventsAtStep(PCRLevelRuntime level, int step)
        {
            for (int i = level.Events.Count - 1; i >= 0; i--)
            {
                PCREventRuntime evt = level.Events[i];
                if (evt.StepIndex != step || evt.IsField)
                {
                    continue;
                }

                level.Events.RemoveAt(i);
            }
        }

        private static void ComputeRequiredRoutes(int lane, int laneCount, PCRPhase required, out int routeA, out int routeB)
        {
            int preferredForRequired = required == PCRPhase.A
                ? (lane < laneCount - 1 ? 1 : -1)
                : (lane > 0 ? -1 : 1);

            if (lane <= 0)
            {
                routeA = 1;
                routeB = 1;
                return;
            }

            if (lane >= laneCount - 1)
            {
                routeA = -1;
                routeB = -1;
                return;
            }

            routeA = required == PCRPhase.A ? preferredForRequired : -preferredForRequired;
            routeB = -routeA;
        }

        private static void ComputeBalancedRoutes(int lane, int laneCount, int decisionLane, System.Random random, out int routeA, out int routeB)
        {
            if (lane <= 0)
            {
                routeA = 1;
                routeB = 1;
                return;
            }

            if (lane >= laneCount - 1)
            {
                routeA = -1;
                routeB = -1;
                return;
            }

            int centerBias = lane < decisionLane ? 1 : -1;
            if (lane == decisionLane)
            {
                centerBias = random.NextDouble() > 0.5 ? 1 : -1;
            }

            routeA = centerBias;
            routeB = -centerBias;
        }

        private static PCRPhaseMask PickDecisionMask(
            int phaseDemandBias,
            ref int countA,
            ref int countB,
            ref int countBoth,
            PCRPhase predictedPhase,
            int predictedPhaseRunSteps,
            int maxSameRunSteps,
            int stepDelta,
            System.Random random)
        {
            float weightA = DecisionRatioA + phaseDemandBias * 0.06f;
            float weightB = DecisionRatioB - phaseDemandBias * 0.06f;
            float weightBoth = DecisionRatioBoth;

            if (countA > countB + 2)
            {
                weightA *= 0.72f;
                weightB *= 1.18f;
            }
            else if (countB > countA + 2)
            {
                weightB *= 0.72f;
                weightA *= 1.18f;
            }

            if (predictedPhaseRunSteps + stepDelta > maxSameRunSteps - 2)
            {
                PCRPhaseMask forced = predictedPhase == PCRPhase.A ? PCRPhaseMask.B : PCRPhaseMask.A;
                if (forced == PCRPhaseMask.A)
                {
                    countA++;
                }
                else
                {
                    countB++;
                }

                return forced;
            }

            float sum = Mathf.Max(0.0001f, weightA + weightB + weightBoth);
            float pick = (float)random.NextDouble() * sum;
            if (pick < weightA)
            {
                countA++;
                return PCRPhaseMask.A;
            }

            pick -= weightA;
            if (pick < weightB)
            {
                countB++;
                return PCRPhaseMask.B;
            }

            countBoth++;
            return PCRPhaseMask.Both;
        }

        private static PCRDecisionReason PickDecisionReason(PCRPhaseMask mask, System.Random random)
        {
            if (mask == PCRPhaseMask.Both)
            {
                return PCRDecisionReason.Routing;
            }

            float pick = (float)random.NextDouble();
            if (pick < DecisionReasonGateRatio)
            {
                return PCRDecisionReason.Gate;
            }

            if (pick < DecisionReasonGateRatio + DecisionReasonArcRatio)
            {
                return PCRDecisionReason.Arc;
            }

            return PCRDecisionReason.Routing;
        }

        private static void NormalizeDecisionWindows(PCRLevelRuntime level)
        {
            if (level.Decisions == null || level.Decisions.Count <= 1)
            {
                return;
            }

            level.Decisions.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            var unique = new List<PCRDecisionWindow>(level.Decisions.Count);
            int lastStep = -1000;
            for (int i = 0; i < level.Decisions.Count; i++)
            {
                PCRDecisionWindow window = level.Decisions[i];
                if (window.StepIndex == lastStep)
                {
                    continue;
                }

                unique.Add(window);
                lastStep = window.StepIndex;
            }

            level.Decisions = unique;
        }

        private static void BuildStepEventCache(PCRLevelRuntime level)
        {
            level.EventsByStep = new List<PCREventRuntime>[level.TotalSteps];
            for (int i = 0; i < level.TotalSteps; i++)
            {
                level.EventsByStep[i] = new List<PCREventRuntime>();
            }

            for (int i = 0; i < level.Events.Count; i++)
            {
                int step = Mathf.Clamp(level.Events[i].StepIndex, 0, level.TotalSteps - 1);
                level.EventsByStep[step].Add(level.Events[i]);
            }
        }

        private static void BuildStepDecisionCache(PCRLevelRuntime level)
        {
            level.DecisionsByStep = new List<PCRDecisionWindow>[level.TotalSteps];
            for (int i = 0; i < level.TotalSteps; i++)
            {
                level.DecisionsByStep[i] = new List<PCRDecisionWindow>();
            }

            if (level.Decisions == null)
            {
                level.Decisions = new List<PCRDecisionWindow>();
                return;
            }

            for (int i = 0; i < level.Decisions.Count; i++)
            {
                int step = Mathf.Clamp(level.Decisions[i].StepIndex, 0, level.TotalSteps - 1);
                level.DecisionsByStep[step].Add(level.Decisions[i]);
            }
        }

        private static void StampWorldEventData(PCRLevelRuntime level)
        {
            for (int i = 0; i < level.Events.Count; i++)
            {
                PCREventRuntime evt = level.Events[i];
                Vector3 center = level.CenterlinePoints[Mathf.Clamp(evt.StepIndex, 0, level.TotalSteps - 1)];
                Vector3 tangent = level.Tangents[Mathf.Clamp(evt.StepIndex, 0, level.TotalSteps - 1)];
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                float laneOffset = (evt.LaneIndex - (PCRSimulation.GetLaneCountAtStep(level, evt.StepIndex) - 1) * 0.5f) * level.LaneSpacing;
                float fieldOffset = evt.IsField ? (0.65f + Mathf.Abs(evt.SideOffset)) * Mathf.Sign(evt.SideOffset == 0f ? 1f : evt.SideOffset) : 0f;
                evt.WorldNormal = right;
                evt.WorldPosition = center + right * (laneOffset + fieldOffset) + Vector3.up * 0.02f;
            }
        }

        private static PCRSolverMetrics Solve(PCRLevelRuntime level)
        {
            var current = new Dictionary<SolverKey, SolverMetricsState>(64);
            var next = new Dictionary<SolverKey, SolverMetricsState>(64);

            var initialRun = new PCRRunState
            {
                StepIndex = 0,
                LaneIndex = 2,
                Phase = PCRPhase.A,
                Modifiers = new PCRModifierState
                {
                    AmplifyNextRouting = 1
                },
                Dead = false,
                DeathType = PCRDeathType.None
            };

            int initialLaneCount = PCRSimulation.GetLaneCountAtStep(level, 0);
            initialRun.LaneIndex = Mathf.Clamp(initialRun.LaneIndex, 0, initialLaneCount - 1);
            var initial = new SolverMetricsState
            {
                RunState = initialRun,
                Shifts = 0,
                MeaningfulCount = 0,
                TapCount = 0,
                MaxGapSteps = 0,
                GapSteps = 0,
                PhaseRunSteps = 1,
                MaxPhaseRunSteps = 1
            };
            current[new SolverKey(initialRun)] = initial;

            for (int step = 0; step < level.TotalSteps; step++)
            {
                next.Clear();
                foreach (var kv in current)
                {
                    SolverMetricsState state = kv.Value;
                    for (int a = 0; a < 2; a++)
                    {
                        bool tap = a == 1;
                        PCRRunState run = state.RunState;
                        PCRPhase phaseBefore = run.Phase;
                        PCRStepOutput output = PCRSimulation.SimulateStep(level, step, tap, ref run);
                        if (output.Dead)
                        {
                            continue;
                        }

                        var candidate = state;
                        candidate.RunState = run;
                        if (tap)
                        {
                            candidate.TapCount++;
                        }

                        if (output.Shifted)
                        {
                            candidate.Shifts++;
                        }

                        if (output.MeaningfulEvent)
                        {
                            candidate.MeaningfulCount++;
                            candidate.GapSteps = 0;
                        }
                        else
                        {
                            candidate.GapSteps++;
                        }

                        int phaseRun = run.Phase == phaseBefore ? state.PhaseRunSteps + 1 : 1;
                        candidate.PhaseRunSteps = phaseRun;
                        candidate.MaxPhaseRunSteps = Mathf.Max(state.MaxPhaseRunSteps, phaseRun);
                        candidate.MaxGapSteps = Mathf.Max(candidate.MaxGapSteps, candidate.GapSteps);
                        SolverKey key = new SolverKey(run);
                        if (next.TryGetValue(key, out SolverMetricsState existing))
                        {
                            if (!IsCandidateBetter(candidate, existing))
                            {
                                continue;
                            }
                        }

                        next[key] = candidate;
                    }
                }

                if (next.Count == 0)
                {
                    return new PCRSolverMetrics
                    {
                        Solved = false
                    };
                }

                current.Clear();
                foreach (var kv in next)
                {
                    current[kv.Key] = kv.Value;
                }
            }

            SolverMetricsState best = default;
            bool hasBest = false;
            foreach (var kv in current)
            {
                SolverMetricsState candidate = kv.Value;
                if (!hasBest || IsCandidateBetter(candidate, best))
                {
                    hasBest = true;
                    best = candidate;
                }
            }

            return new PCRSolverMetrics
            {
                Solved = hasBest,
                Shifts = best.Shifts,
                MeaningfulCount = best.MeaningfulCount,
                TapCount = best.TapCount,
                MaxGapSteps = best.MaxGapSteps,
                MaxPhaseRunSteps = best.MaxPhaseRunSteps
            };
        }

        private static bool Validate(PCRLevelRuntime level, PCRPacingProfileSO pacing, PCRSolverMetrics metrics, out string reason, out PCRValidationReport report)
        {
            report = new PCRValidationReport();
            if (!metrics.Solved && TrySimulateDecisionPath(level, out PCRSolverMetrics fallback))
            {
                metrics = fallback;
            }

            if (!metrics.Solved)
            {
                reason = "No safe path from start to finish.";
                return false;
            }

            float duration = level.DurationSec;
            float avgMeaningfulInterval = duration / Mathf.Max(1, metrics.MeaningfulCount);
            float avgShiftInterval = duration / Mathf.Max(1, metrics.Shifts);
            float maxGapSec = metrics.MaxGapSteps * level.SimStepSec;
            float maxDecisionGapSec = ComputeMaxDecisionGapSec(level);
            float minRequiredTapRatePer10Sec = duration > 0.001f ? metrics.TapCount * 10f / duration : 0f;
            float maxSamePhaseRunSec = metrics.MaxPhaseRunSteps * level.SimStepSec;

            report.AverageMeaningfulIntervalSec = avgMeaningfulInterval;
            report.AverageShiftIntervalSec = avgShiftInterval;
            report.MaxEmptyGapSec = maxGapSec;
            report.MaxDecisionGapSec = maxDecisionGapSec;
            report.MinRequiredTapRatePer10Sec = minRequiredTapRatePer10Sec;
            report.MaxSamePhaseRunSec = maxSamePhaseRunSec;
            report.TotalShifts = metrics.Shifts;
            report.MeaningfulCount = metrics.MeaningfulCount;
            report.TapCount = metrics.TapCount;
            report.NoTapSurvivalSec = EvaluateNoTapSurvivalSec(level);

            float shiftHardLimit = Mathf.Max(pacing.LaneShiftTargetSec * 2.6f, 3f);
            if (avgShiftInterval > shiftHardLimit + 0.001f)
            {
                reason = $"Lane shifts too sparse ({avgShiftInterval:0.00}s > {shiftHardLimit:0.00}s hard limit).";
                return false;
            }

            float decisionGapTolerance = level.SimStepSec * 0.5f + 0.001f;
            if (maxDecisionGapSec > pacing.MaxDecisionGapSec + decisionGapTolerance)
            {
                reason = $"Decision gap too long ({maxDecisionGapSec:0.00}s > {pacing.MaxDecisionGapSec:0.00}s).";
                return false;
            }

            if (minRequiredTapRatePer10Sec + 0.001f < pacing.MinRequiredTapPer10Sec)
            {
                reason = $"Required tap rate too low ({minRequiredTapRatePer10Sec:0.00}/10s < {pacing.MinRequiredTapPer10Sec:0.00}/10s).";
                return false;
            }

            if (maxSamePhaseRunSec > pacing.MaxSamePhaseRunSec + 0.001f)
            {
                reason = $"Same-phase run too long ({maxSamePhaseRunSec:0.00}s > {pacing.MaxSamePhaseRunSec:0.00}s).";
                return false;
            }

            if (report.NoTapSurvivalSec > pacing.MaxNoTapSurvivalSec + 0.001f)
            {
                reason = $"No-tap survival too long ({report.NoTapSurvivalSec:0.00}s > {pacing.MaxNoTapSurvivalSec:0.00}s).";
                return false;
            }

            if (!HasNoForcedDeathWindows(level, ForcedDeathLookaheadSteps, out int forcedStep))
            {
                reason = $"Forced death window detected near step {forcedStep}.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool TrySimulateDecisionPath(PCRLevelRuntime level, out PCRSolverMetrics metrics)
        {
            metrics = default;
            if (level == null || level.TotalSteps <= 0)
            {
                return false;
            }

            BuildStepDecisionCache(level);
            var run = new PCRRunState
            {
                StepIndex = 0,
                LaneIndex = 2,
                Phase = PCRPhase.A,
                Modifiers = new PCRModifierState
                {
                    AmplifyNextRouting = 1
                },
                Dead = false,
                DeathType = PCRDeathType.None
            };
            int initialLaneCount = PCRSimulation.GetLaneCountAtStep(level, 0);
            run.LaneIndex = Mathf.Clamp(run.LaneIndex, 0, initialLaneCount - 1);

            int taps = 0;
            int shifts = 0;
            int meaningful = 0;
            int gapSteps = 0;
            int maxGapSteps = 0;
            int phaseRunSteps = 1;
            int maxPhaseRun = 1;
            for (int step = 0; step < level.TotalSteps; step++)
            {
                bool tap = false;
                if (level.DecisionsByStep != null && step >= 0 && step < level.DecisionsByStep.Length && level.DecisionsByStep[step].Count > 0)
                {
                    PCRDecisionWindow decision = level.DecisionsByStep[step][0];
                    if (decision.RequiredMask == PCRPhaseMask.A && run.Phase != PCRPhase.A)
                    {
                        tap = true;
                    }
                    else if (decision.RequiredMask == PCRPhaseMask.B && run.Phase != PCRPhase.B)
                    {
                        tap = true;
                    }
                }

                PCRPhase phaseBefore = run.Phase;
                PCRStepOutput output = PCRSimulation.SimulateStep(level, step, tap, ref run);
                if (output.Dead)
                {
                    metrics.Solved = false;
                    return false;
                }

                if (tap)
                {
                    taps++;
                }

                if (output.Shifted)
                {
                    shifts++;
                }

                if (output.MeaningfulEvent)
                {
                    meaningful++;
                    gapSteps = 0;
                }
                else
                {
                    gapSteps++;
                }

                maxGapSteps = Mathf.Max(maxGapSteps, gapSteps);
                phaseRunSteps = run.Phase == phaseBefore ? phaseRunSteps + 1 : 1;
                maxPhaseRun = Mathf.Max(maxPhaseRun, phaseRunSteps);
            }

            metrics = new PCRSolverMetrics
            {
                Solved = true,
                Shifts = shifts,
                MeaningfulCount = meaningful,
                TapCount = taps,
                MaxGapSteps = maxGapSteps,
                MaxPhaseRunSteps = maxPhaseRun
            };
            return true;
        }

        private static float EvaluateNoTapSurvivalSec(PCRLevelRuntime level)
        {
            if (level == null || level.TotalSteps <= 0)
            {
                return 0f;
            }

            var state = new PCRRunState
            {
                StepIndex = 0,
                LaneIndex = 2,
                Phase = PCRPhase.A,
                Modifiers = new PCRModifierState
                {
                    AmplifyNextRouting = 1
                },
                Dead = false,
                DeathType = PCRDeathType.None
            };

            int initialLaneCount = PCRSimulation.GetLaneCountAtStep(level, 0);
            state.LaneIndex = Mathf.Clamp(state.LaneIndex, 0, initialLaneCount - 1);

            int survivedSteps = 0;
            for (int step = 0; step < level.TotalSteps; step++)
            {
                PCRStepOutput output = PCRSimulation.SimulateStep(level, step, false, ref state);
                if (output.Dead)
                {
                    break;
                }

                survivedSteps++;
                if (output.Finished || state.StepIndex >= level.TotalSteps)
                {
                    break;
                }
            }

            return survivedSteps * level.SimStepSec;
        }

        private static float ComputeMaxDecisionGapSec(PCRLevelRuntime level)
        {
            if (level.Decisions == null || level.Decisions.Count == 0)
            {
                return level.DurationSec;
            }

            level.Decisions.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            int previousStep = 0;
            int maxGapSteps = 0;
            for (int i = 0; i < level.Decisions.Count; i++)
            {
                int step = Mathf.Clamp(level.Decisions[i].StepIndex, 0, level.TotalSteps - 1);
                maxGapSteps = Mathf.Max(maxGapSteps, step - previousStep);
                previousStep = step;
            }

            maxGapSteps = Mathf.Max(maxGapSteps, level.TotalSteps - previousStep);
            return maxGapSteps * level.SimStepSec;
        }

        private static bool IsCandidateBetter(SolverMetricsState a, SolverMetricsState b)
        {
            if (a.TapCount != b.TapCount)
            {
                return a.TapCount < b.TapCount;
            }

            if (a.MaxPhaseRunSteps != b.MaxPhaseRunSteps)
            {
                return a.MaxPhaseRunSteps < b.MaxPhaseRunSteps;
            }

            if (a.MaxGapSteps != b.MaxGapSteps)
            {
                return a.MaxGapSteps < b.MaxGapSteps;
            }

            if (a.Shifts != b.Shifts)
            {
                return a.Shifts < b.Shifts;
            }

            if (a.MeaningfulCount != b.MeaningfulCount)
            {
                return a.MeaningfulCount < b.MeaningfulCount;
            }

            return true;
        }

        private static bool HasNoForcedDeathWindows(PCRLevelRuntime level, int lookaheadSteps, out int forcedStep)
        {
            forcedStep = -1;
            List<Dictionary<SolverKey, PCRRunState>> reachableByStep = BuildReachableStateLayers(level);
            var memo = new Dictionary<WindowKey, bool>(512);
            int maxStep = Mathf.Min(level.TotalSteps - 1, reachableByStep.Count - 1);
            for (int step = 0; step <= maxStep; step++)
            {
                Dictionary<SolverKey, PCRRunState> states = reachableByStep[step];
                foreach (var kv in states)
                {
                    PCRRunState state = kv.Value;
                    int startStep = Mathf.Clamp(state.StepIndex, 0, level.TotalSteps - 1);
                    if (!CanSurviveWindow(level, state, startStep, lookaheadSteps, memo))
                    {
                        forcedStep = startStep;
                        return false;
                    }
                }
            }

            return true;
        }

        private static List<Dictionary<SolverKey, PCRRunState>> BuildReachableStateLayers(PCRLevelRuntime level)
        {
            var layers = new List<Dictionary<SolverKey, PCRRunState>>(level.TotalSteps + 1);
            for (int i = 0; i <= level.TotalSteps; i++)
            {
                layers.Add(new Dictionary<SolverKey, PCRRunState>(32));
            }

            var initialRun = new PCRRunState
            {
                StepIndex = 0,
                LaneIndex = 2,
                Phase = PCRPhase.A,
                Modifiers = new PCRModifierState
                {
                    AmplifyNextRouting = 1
                },
                Dead = false,
                DeathType = PCRDeathType.None
            };
            int initialLaneCount = PCRSimulation.GetLaneCountAtStep(level, 0);
            initialRun.LaneIndex = Mathf.Clamp(initialRun.LaneIndex, 0, initialLaneCount - 1);
            layers[0][new SolverKey(initialRun)] = initialRun;

            for (int step = 0; step < level.TotalSteps; step++)
            {
                Dictionary<SolverKey, PCRRunState> current = layers[step];
                if (current.Count == 0)
                {
                    break;
                }

                Dictionary<SolverKey, PCRRunState> next = layers[step + 1];
                foreach (var kv in current)
                {
                    PCRRunState state = kv.Value;
                    for (int a = 0; a < 2; a++)
                    {
                        bool tap = a == 1;
                        PCRRunState run = state;
                        PCRStepOutput output = PCRSimulation.SimulateStep(level, step, tap, ref run);
                        if (output.Dead)
                        {
                            continue;
                        }

                        next[new SolverKey(run)] = run;
                    }
                }
            }

            return layers;
        }

        private static bool CanSurviveWindow(PCRLevelRuntime level, PCRRunState state, int step, int remaining, Dictionary<WindowKey, bool> memo)
        {
            if (state.Dead)
            {
                return false;
            }

            if (step >= level.TotalSteps || remaining <= 0)
            {
                return true;
            }

            var key = new WindowKey(step, remaining, new SolverKey(state));
            if (memo.TryGetValue(key, out bool cached))
            {
                return cached;
            }

            bool survives = false;
            for (int a = 0; a < 2 && !survives; a++)
            {
                bool tap = a == 1;
                PCRRunState nextState = state;
                PCRStepOutput output = PCRSimulation.SimulateStep(level, step, tap, ref nextState);
                if (output.Dead)
                {
                    continue;
                }

                survives = CanSurviveWindow(level, nextState, step + 1, remaining - 1, memo);
            }

            memo[key] = survives;
            return survives;
        }

        private static bool IsMeaningfulType(PCREventRuntime evt)
        {
            return evt.Type == PCRElementType.MuxSwitch;
        }

        private static bool IsRoutingSourceType(PCREventRuntime evt)
        {
            return evt.Type == PCRElementType.MuxSwitch;
        }

        private static bool ShouldInjectTwist(int segmentIndex, int segmentCount, System.Random random)
        {
            if (segmentIndex < 2 || segmentIndex > segmentCount - 3)
            {
                return false;
            }

            return random.NextDouble() < 0.22;
        }

        private static PCRCurveStyle PromoteCurveForTwist(PCRCurveStyle style, System.Random random)
        {
            return style switch
            {
                PCRCurveStyle.Straight => random.NextDouble() > 0.5 ? PCRCurveStyle.TurnLeft : PCRCurveStyle.TurnRight,
                PCRCurveStyle.GentleC => random.NextDouble() > 0.5 ? PCRCurveStyle.TurnLeft : PCRCurveStyle.TurnRight,
                PCRCurveStyle.GentleS => random.NextDouble() > 0.5 ? PCRCurveStyle.TurnLeft : PCRCurveStyle.TurnRight,
                _ => style
            };
        }

        private static float EvaluateCurveRelative(PCRCurveStyle style, float amplitude, float t)
        {
            return style switch
            {
                PCRCurveStyle.Straight => 0f,
                PCRCurveStyle.GentleS => Mathf.Sin(t * Mathf.PI * 2f) * amplitude * 0.65f,
                PCRCurveStyle.GentleC => (1f - Mathf.Cos(t * Mathf.PI)) * amplitude * 0.5f,
                PCRCurveStyle.TurnLeft => -Mathf.SmoothStep(0f, amplitude, t),
                PCRCurveStyle.TurnRight => Mathf.SmoothStep(0f, amplitude, t),
                _ => 0f
            };
        }

        private static int BuildDeadMask(List<int> deadLanes, int maxLaneCount)
        {
            int mask = 0;
            for (int i = 0; i < deadLanes.Count; i++)
            {
                int lane = Mathf.Clamp(deadLanes[i], 0, maxLaneCount - 1);
                mask |= 1 << lane;
            }

            return mask;
        }

        private static int BuildFieldKey(int step, int lane)
        {
            return (step << 8) ^ lane;
        }

        private static PCRSegmentRuntime FindSegment(PCRLevelRuntime level, int step)
        {
            for (int i = 0; i < level.Segments.Count; i++)
            {
                PCRSegmentRuntime segment = level.Segments[i];
                if (step >= segment.StartStep && step <= segment.EndStep)
                {
                    return segment;
                }
            }

            return level.Segments[level.Segments.Count - 1];
        }

        private static List<PCRSegmentTemplateSO> ChooseSegments(int segmentCount, List<PCRSegmentTemplateSO> templates, System.Random random)
        {
            segmentCount = Mathf.Max(12, segmentCount);
            var selected = new List<PCRSegmentTemplateSO>(segmentCount);
            if (templates.Count == 0)
            {
                templates = PCRTemplateDefaults.CreateRuntimeDefaults();
            }

            int introCount = Mathf.Min(2, templates.Count);
            for (int i = 0; i < introCount; i++)
            {
                selected.Add(templates[i]);
            }

            while (selected.Count < segmentCount - 2)
            {
                int idx = random.Next(2, Mathf.Max(3, templates.Count));
                selected.Add(templates[idx % templates.Count]);
            }

            selected.Add(templates[Mathf.Clamp(templates.Count - 2, 0, templates.Count - 1)]);
            selected.Add(templates[Mathf.Clamp(templates.Count - 1, 0, templates.Count - 1)]);
            return selected;
        }

        private static float[] BuildStepDistances(int totalSteps, float duration, float simStep, float startSpeed, float endSpeed)
        {
            var distances = new float[totalSteps];
            float distance = 0f;
            for (int i = 0; i < totalSteps; i++)
            {
                if (i == 0)
                {
                    distances[i] = 0f;
                    continue;
                }

                float t = Mathf.Clamp01(i * simStep / duration);
                float speed = Mathf.Lerp(startSpeed, endSpeed, t);
                distance += speed * simStep;
                distances[i] = distance;
            }

            return distances;
        }

        private static int ResolveSegmentCount(PCRLevelDoc doc, PCRPacingProfileSO pacing)
        {
            if (doc != null && doc.SegmentCount > 0)
            {
                return Mathf.Clamp(doc.SegmentCount, pacing.SegmentCountMin, pacing.SegmentCountMax);
            }

            return Mathf.Clamp(14, pacing.SegmentCountMin, pacing.SegmentCountMax);
        }

        private static PCRDifficultyProfileSO ResolveDifficulty(PCRLevelDoc doc)
        {
            if (doc != null && doc.DifficultyProfile != null)
            {
                return doc.DifficultyProfile;
            }

            var fallback = ScriptableObject.CreateInstance<PCRDifficultyProfileSO>();
            fallback.StartSpeed = 7.5f;
            fallback.EndSpeed = 10.5f;
            fallback.MinLaneCount = 2;
            fallback.MaxLaneCount = 5;
            return fallback;
        }

        private static PCRPacingProfileSO ResolvePacing(PCRLevelDoc doc)
        {
            if (doc != null && doc.PacingProfile != null)
            {
                return doc.PacingProfile;
            }

            var fallback = ScriptableObject.CreateInstance<PCRPacingProfileSO>();
            fallback.SimStepSec = 0.12f;
            fallback.TargetDurationSec = doc != null ? doc.TargetDurationSec : 90f;
            fallback.EmptyGapThresholdSec = 2f;
            fallback.LaneShiftTargetSec = 1.2f;
            fallback.EventCadenceMinSec = 1f;
            fallback.EventCadenceMaxSec = 1.4f;
            fallback.MaxNoTapSurvivalSec = 9f;
            fallback.MaxDecisionGapSec = 2f;
            fallback.MinRequiredTapPer10Sec = 4f;
            fallback.MaxSamePhaseRunSec = 3f;
            fallback.DecisionLeadTimeSec = 1f;
            fallback.SegmentCountMin = 12;
            fallback.SegmentCountMax = 16;
            return fallback;
        }

        private static List<PCRSegmentTemplateSO> ResolveTemplates(PCRLevelDoc doc)
        {
            if (doc != null && doc.SegmentLibrary != null && doc.SegmentLibrary.Templates != null && doc.SegmentLibrary.Templates.Count > 0)
            {
                return new List<PCRSegmentTemplateSO>(doc.SegmentLibrary.Templates);
            }

            return PCRTemplateDefaults.CreateRuntimeDefaults();
        }

        private readonly struct SolverKey : IEquatable<SolverKey>
        {
            private readonly int lane;
            private readonly int phase;
            private readonly int delay;
            private readonly int hasQueue;
            private readonly int queue;
            private readonly int amp;
            private readonly int cancel;

            public SolverKey(PCRRunState run)
            {
                lane = run.LaneIndex;
                phase = (int)run.Phase;
                delay = run.Modifiers.DelayNextRouting ? 1 : 0;
                hasQueue = run.Modifiers.HasQueuedRoute ? 1 : 0;
                queue = run.Modifiers.QueuedRouteDelta + 8;
                amp = Mathf.Clamp(run.Modifiers.AmplifyNextRouting, 1, 3);
                cancel = Mathf.Clamp(run.Modifiers.CancelRoutingSteps, 0, 3);
            }

            public bool Equals(SolverKey other)
            {
                return lane == other.lane
                       && phase == other.phase
                       && delay == other.delay
                       && hasQueue == other.hasQueue
                       && queue == other.queue
                       && amp == other.amp
                       && cancel == other.cancel;
            }

            public override bool Equals(object obj)
            {
                return obj is SolverKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = lane;
                    hash = (hash * 397) ^ phase;
                    hash = (hash * 397) ^ delay;
                    hash = (hash * 397) ^ hasQueue;
                    hash = (hash * 397) ^ queue;
                    hash = (hash * 397) ^ amp;
                    hash = (hash * 397) ^ cancel;
                    return hash;
                }
            }
        }

        private readonly struct WindowKey : IEquatable<WindowKey>
        {
            private readonly int step;
            private readonly int remaining;
            private readonly SolverKey state;

            public WindowKey(int step, int remaining, SolverKey state)
            {
                this.step = step;
                this.remaining = remaining;
                this.state = state;
            }

            public bool Equals(WindowKey other)
            {
                return step == other.step && remaining == other.remaining && state.Equals(other.state);
            }

            public override bool Equals(object obj)
            {
                return obj is WindowKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = step;
                    hash = (hash * 397) ^ remaining;
                    hash = (hash * 397) ^ state.GetHashCode();
                    return hash;
                }
            }
        }

        private struct SolverMetricsState
        {
            public PCRRunState RunState;
            public int Shifts;
            public int MeaningfulCount;
            public int TapCount;
            public int GapSteps;
            public int MaxGapSteps;
            public int PhaseRunSteps;
            public int MaxPhaseRunSteps;
        }
    }
}
