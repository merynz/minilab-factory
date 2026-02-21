using System;

namespace MiniLab.Core.Rhythm
{
    public enum JudgeResult
    {
        Perfect = 0,
        Good = 1,
        Ok = 2,
        Miss = 3
    }

    [Serializable]
    public sealed class JudgeWindows
    {
        public float perfectMs = 45f;
        public float goodMs = 90f;
        public float okMs = 140f;
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

            if (deltaMs <= windows.okMs)
            {
                return JudgeResult.Ok;
            }

            return JudgeResult.Miss;
        }
    }
}
