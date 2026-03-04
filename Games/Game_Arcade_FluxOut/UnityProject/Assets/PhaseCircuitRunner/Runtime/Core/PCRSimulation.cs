using UnityEngine;

namespace FluxOut.PCR
{
    public static class PCRSimulation
    {
        private static bool IsCoreElementType(PCRElementType type)
        {
            // Core loop: only routing arrows (Mux) are active gameplay events.
            return type == PCRElementType.MuxSwitch;
        }

        public static bool IsFieldType(PCRElementType type)
        {
            return false;
        }

        public static int GetLaneCountAtStep(PCRLevelRuntime level, int stepIndex)
        {
            // Core mode: lane count is fixed to 5 for readability and telegraph clarity.
            if (level != null && level.MaxLaneCount == 5)
            {
                return 5;
            }

            stepIndex = Mathf.Clamp(stepIndex, 0, level.TotalSteps - 1);
            for (int i = 0; i < level.Segments.Count; i++)
            {
                PCRSegmentRuntime segment = level.Segments[i];
                if (stepIndex >= segment.StartStep && stepIndex <= segment.EndStep)
                {
                    float t = Mathf.InverseLerp(segment.StartStep, segment.EndStep, stepIndex);
                    int laneCount = Mathf.RoundToInt(Mathf.Lerp(segment.LaneCountStart, segment.LaneCountEnd, t));
                    return Mathf.Clamp(laneCount, 2, level.MaxLaneCount);
                }
            }

            return 3;
        }

        public static int GetStaticDeadMaskAtStep(PCRLevelRuntime level, int stepIndex)
        {
            stepIndex = Mathf.Clamp(stepIndex, 0, level.TotalSteps - 1);
            for (int i = 0; i < level.Segments.Count; i++)
            {
                PCRSegmentRuntime segment = level.Segments[i];
                if (stepIndex >= segment.StartStep && stepIndex <= segment.EndStep)
                {
                    return segment.StaticDeadMask;
                }
            }

            return 0;
        }

        public static bool IsDeadTrace(PCRLevelRuntime level, int stepIndex, int laneIndex)
        {
            if (level.SafeLaneMaskByStep != null && level.SafeLaneMaskByStep.Length == level.TotalSteps)
            {
                int step = Mathf.Clamp(stepIndex, 0, level.TotalSteps - 1);
                int safeMask = level.SafeLaneMaskByStep[step];
                return laneIndex < 0 || laneIndex >= 31 || (safeMask & (1 << laneIndex)) == 0;
            }

            int mask = GetStaticDeadMaskAtStep(level, stepIndex);
            return laneIndex >= 0 && laneIndex < 31 && (mask & (1 << laneIndex)) != 0;
        }

        public static PCRStepOutput SimulateStep(PCRLevelRuntime level, int stepIndex, bool tap, ref PCRRunState state)
        {
            var output = new PCRStepOutput();
            if (state.Dead || stepIndex < 0 || stepIndex >= level.TotalSteps)
            {
                output.Dead = state.Dead;
                output.DeathType = state.DeathType;
                output.Finished = stepIndex >= level.TotalSteps;
                return output;
            }

            int laneCount = GetLaneCountAtStep(level, stepIndex);
            state.LaneIndex = Mathf.Clamp(state.LaneIndex, 0, laneCount - 1);

            if (tap)
            {
                state.Phase = state.Phase == PCRPhase.A ? PCRPhase.B : PCRPhase.A;
            }

            int rawRouteDelta = 0;
            var eventsAtStep = level.EventsByStep[stepIndex];
            for (int i = 0; i < eventsAtStep.Count; i++)
            {
                PCREventRuntime evt = eventsAtStep[i];
                if (!IsCoreElementType(evt.Type))
                {
                    // Legacy elements are disabled in the new core mode.
                    continue;
                }

                bool affectsLane = evt.LaneIndex == state.LaneIndex;
                if (!affectsLane)
                {
                    continue;
                }

                bool isMeaningful = evt.Type == PCRElementType.MuxSwitch;
                output.MeaningfulEvent |= isMeaningful;

                switch (evt.Type)
                {
                    case PCRElementType.MuxSwitch:
                        rawRouteDelta += state.Phase == PCRPhase.A ? evt.RouteDeltaA : evt.RouteDeltaB;
                        break;
                }
            }

            if (rawRouteDelta != 0)
            {
                int nextLaneCount = GetLaneCountAtStep(level, Mathf.Min(stepIndex + 1, level.TotalSteps - 1));
                int prevLane = state.LaneIndex;
                state.LaneIndex = Mathf.Clamp(state.LaneIndex + rawRouteDelta, 0, nextLaneCount - 1);
                output.Shifted = state.LaneIndex != prevLane;
                output.AppliedRouteDelta = state.LaneIndex - prevLane;
            }

            if (IsDeadTrace(level, stepIndex, state.LaneIndex))
            {
                state.Dead = true;
                state.DeathType = PCRDeathType.DeadTrace;
                output.Dead = true;
                output.DeathType = state.DeathType;
                return output;
            }

            state.StepIndex = stepIndex + 1;
            output.Finished = state.StepIndex >= level.TotalSteps;
            return output;
        }

        public static void EvaluateCenterline(PCRLevelRuntime level, float stepFloat, out Vector3 point, out Vector3 tangent)
        {
            if (level.TotalSteps <= 1 || level.CenterlinePoints == null || level.CenterlinePoints.Length == 0)
            {
                point = Vector3.zero;
                tangent = Vector3.forward;
                return;
            }

            stepFloat = Mathf.Clamp(stepFloat, 0f, level.TotalSteps - 1f);
            int a = Mathf.FloorToInt(stepFloat);
            int b = Mathf.Min(a + 1, level.TotalSteps - 1);
            float t = stepFloat - a;
            point = Vector3.Lerp(level.CenterlinePoints[a], level.CenterlinePoints[b], t);
            tangent = Vector3.Slerp(level.Tangents[a], level.Tangents[b], t).normalized;
        }

        public static Vector3 EvaluateLanePoint(PCRLevelRuntime level, float stepFloat, float laneFloat)
        {
            EvaluateCenterline(level, stepFloat, out Vector3 center, out Vector3 tangent);
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            int laneCount = GetLaneCountAtStep(level, Mathf.RoundToInt(stepFloat));
            laneFloat = Mathf.Clamp(laneFloat, 0f, Mathf.Max(0f, laneCount - 1f));
            float offset = (laneFloat - (laneCount - 1) * 0.5f) * level.LaneSpacing;
            return center + right * offset;
        }
    }
}
