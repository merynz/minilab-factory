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

    [Serializable]
    public sealed class JudgeWindows
    {
        public float perfectMs = 35f;
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
        public float DeviceOffsetSec { get; private set; }

        private readonly JudgeWindows windows;

        public InputJudge(JudgeWindows judgeWindows)
        {
            windows = judgeWindows ?? new JudgeWindows();
        }

        public void SetDeviceOffset(float offsetSec)
        {
            DeviceOffsetSec = offsetSec;
        }

        public JudgeResult Evaluate(float noteTimeSec, float inputTimeSec)
        {
            float deltaMs = Math.Abs((inputTimeSec + DeviceOffsetSec - noteTimeSec) * 1000f);
            if (deltaMs <= windows.perfectMs)
            {
                return JudgeResult.Perfect;
            }

            if (deltaMs <= windows.goodMs)
            {
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

                float deltaMs = (correctedInput - evt.timeSec) * 1000f;
                float absDeltaMs = Math.Abs(deltaMs);
                if (absDeltaMs < nearestAbsDeltaMs)
                {
                    nearestAbsDeltaMs = absDeltaMs;
                    nearest = evt;
                    nearestIndex = i;
                }
            }

            if (nearest == null)
            {
                return new JudgeOutcome(JudgeResult.Miss, null, -1, float.PositiveInfinity);
            }

            JudgeResult result = Evaluate(nearest.timeSec, inputTimeSec);
            return new JudgeOutcome(result, nearest, nearestIndex, nearestAbsDeltaMs);
        }

        public float MissWindowSec => windows.goodMs / 1000f;

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
    }
}
