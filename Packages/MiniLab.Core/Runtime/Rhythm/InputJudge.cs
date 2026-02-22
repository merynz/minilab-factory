using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniLab.Core.Rhythm
{
    public enum JudgeResult
    {
        Perfect = 0,
        Good = 1,
        Miss = 2
    }

    public enum EmptyTapDecision
    {
        Ignored = 0,
        Miss = 1,
        Matched = 2
    }

    [Serializable]
    public sealed class JudgeWindows
    {
        public float perfectMs = 45f;
        public float goodMs = 90f;
    }

    public readonly struct JudgeOutcome
    {
        public JudgeOutcome(JudgeResult result, BeatEvent beatEvent, int eventIndex, float deltaMs)
        {
            Result = result;
            Event = beatEvent;
            EventIndex = eventIndex;
            DeltaMs = deltaMs;
        }

        public JudgeResult Result { get; }
        public BeatEvent Event { get; }
        public int EventIndex { get; }
        public float DeltaMs { get; }
    }

    public sealed class InputJudge
    {
        private const float SessionPhaseMinMs = -70f;
        private const float SessionPhaseMaxMs = 70f;
        private const float PhaseErrorClampMs = 12f;
        private const float PerfectPhaseAlpha = 0.015f;
        private const float GoodPhaseAlpha = 0.007f;

        private const float PerfectScaleRatio = 0.10f;
        private const float GoodScaleRatio = 0.22f;
        private const float PerfectMinMs = 30f;
        private const float PerfectMaxMs = 46f;
        private const float GoodMinMs = 68f;
        private const float GoodMaxMs = 98f;

        public float DeviceOffsetSec { get; private set; }
        public float SessionPhaseMs { get; private set; }
        public float LastTapOffsetMs { get; private set; }
        public float CurrentBpm { get; private set; } = 120f;
        public float CurrentBeatMs => CurrentBpm > 0.001f ? 60000f / CurrentBpm : 500f;
        public float EffectivePerfectMs { get; private set; }
        public float EffectiveGoodMs { get; private set; }

        private readonly JudgeWindows windows;

        public InputJudge(JudgeWindows judgeWindows)
        {
            windows = judgeWindows ?? new JudgeWindows();
            ConfigureForBpm(CurrentBpm);
        }

        public void SetDeviceOffset(float offsetSec)
        {
            DeviceOffsetSec = offsetSec;
        }

        public void ConfigureForBpm(float bpm)
        {
            CurrentBpm = Math.Max(1f, bpm);
            float beatMs = CurrentBeatMs;
            EffectivePerfectMs = Clamp(beatMs * PerfectScaleRatio, PerfectMinMs, PerfectMaxMs);
            EffectiveGoodMs = Math.Max(EffectivePerfectMs, Clamp(beatMs * GoodScaleRatio, GoodMinMs, GoodMaxMs));
        }

        public void ResetSessionPhase()
        {
            SessionPhaseMs = 0f;
            LastTapOffsetMs = 0f;
        }

        public JudgeResult Evaluate(float noteTimeSec, float inputTimeSec)
        {
            float signedErrorMs = ComputeSignedErrorMs(noteTimeSec, inputTimeSec);
            LastTapOffsetMs = signedErrorMs;
            float deltaMs = Math.Abs(signedErrorMs);
            if (deltaMs <= EffectivePerfectMs)
            {
                UpdateSessionPhase(signedErrorMs, JudgeResult.Perfect);
                return JudgeResult.Perfect;
            }

            if (deltaMs <= EffectiveGoodMs)
            {
                UpdateSessionPhase(signedErrorMs, JudgeResult.Good);
                return JudgeResult.Good;
            }

            return JudgeResult.Miss;
        }

        public JudgeOutcome EvaluateNearest(BeatEvent[] events, IReadOnlyCollection<int> consumedIndices, float inputTimeSec, Func<BeatEvent, bool> filter = null)
        {
            if (events == null || events.Length == 0)
            {
                return new JudgeOutcome(JudgeResult.Miss, null, -1, float.PositiveInfinity);
            }

            int nearestIndex = -1;
            BeatEvent nearest = null;
            float nearestAbsDeltaMs = float.PositiveInfinity;
            float correctedInput = inputTimeSec + DeviceOffsetSec;
            for (int i = 0; i < events.Length; i++)
            {
                if (consumedIndices != null && consumedIndices.Contains(i))
                {
                    continue;
                }

                BeatEvent evt = events[i];
                if (evt == null)
                {
                    continue;
                }

                if (filter != null && !filter(evt))
                {
                    continue;
                }

                float deltaMs = ((correctedInput - evt.timeSec) * 1000f) - SessionPhaseMs;
                float absDeltaMs = Math.Abs(deltaMs);
                if (absDeltaMs < nearestAbsDeltaMs)
                {
                    nearestAbsDeltaMs = absDeltaMs;
                    nearest = evt;
                    nearestIndex = i;
                    LastTapOffsetMs = deltaMs;
                }
            }

            if (nearest == null)
            {
                return new JudgeOutcome(JudgeResult.Miss, null, -1, float.PositiveInfinity);
            }

            JudgeResult result = Evaluate(nearest.timeSec, inputTimeSec);
            return new JudgeOutcome(result, nearest, nearestIndex, LastTapOffsetMs);
        }

        public JudgeOutcome EvaluateNearestTapOrAccent(BeatEvent[] events, IReadOnlyCollection<int> consumedIndices, float inputTimeSec)
        {
            return EvaluateNearest(
                events,
                consumedIndices,
                inputTimeSec,
                e => e != null && (e.IsKind(BeatKinds.Tap) || e.IsKind(BeatKinds.Accent)));
        }

        public float MissWindowSec => EffectiveGoodMs / 1000f;

        public bool IsMissedByTime(BeatEvent beatEvent, float songTimeSec)
        {
            if (beatEvent == null)
            {
                return false;
            }

            return songTimeSec > beatEvent.timeSec + MissWindowSec;
        }

        public IEnumerable<int> UnconsumedMissedIndices(BeatEvent[] events, IReadOnlyCollection<int> consumedIndices, float songTimeSec, Func<BeatEvent, bool> filter = null)
        {
            if (events == null)
            {
                return Enumerable.Empty<int>();
            }

            var missed = new List<int>();
            for (int i = 0; i < events.Length; i++)
            {
                if (consumedIndices != null && consumedIndices.Contains(i))
                {
                    continue;
                }

                BeatEvent evt = events[i];
                if (evt == null)
                {
                    continue;
                }

                if (filter != null && !filter(evt))
                {
                    continue;
                }

                if (IsMissedByTime(evt, songTimeSec))
                {
                    missed.Add(i);
                }
            }

            return missed;
        }

        public EmptyTapDecision ResolveEmptyTapDecision(
            bool inPunishSection,
            float inputTimeSec,
            float? nextHazardHitTimeSec,
            bool matchedHazard,
            float beatSec)
        {
            if (matchedHazard)
            {
                return EmptyTapDecision.Matched;
            }

            if (!inPunishSection)
            {
                return EmptyTapDecision.Ignored;
            }

            if (!nextHazardHitTimeSec.HasValue)
            {
                return EmptyTapDecision.Ignored;
            }

            float expectedHazardWindowSec = Clamp(beatSec * 0.85f, 0.22f, 0.42f);
            float deltaToNext = nextHazardHitTimeSec.Value - inputTimeSec;
            if (deltaToNext >= 0f && deltaToNext <= expectedHazardWindowSec)
            {
                return EmptyTapDecision.Miss;
            }

            return EmptyTapDecision.Ignored;
        }

        private float ComputeSignedErrorMs(float noteTimeSec, float inputTimeSec)
        {
            return ((inputTimeSec + DeviceOffsetSec - noteTimeSec) * 1000f) - SessionPhaseMs;
        }

        private void UpdateSessionPhase(float signedErrorMs, JudgeResult result)
        {
            if (result == JudgeResult.Miss)
            {
                return;
            }

            float alpha = result == JudgeResult.Perfect ? PerfectPhaseAlpha : GoodPhaseAlpha;
            float update = alpha * Clamp(signedErrorMs, -PhaseErrorClampMs, PhaseErrorClampMs);
            SessionPhaseMs = Clamp(SessionPhaseMs + update, SessionPhaseMinMs, SessionPhaseMaxMs);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
