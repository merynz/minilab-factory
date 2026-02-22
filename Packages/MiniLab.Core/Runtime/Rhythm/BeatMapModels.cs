using System;
using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    [Serializable]
    public sealed class BeatMap
    {
        public string schemaVersion = "1.0.0";
        public string trackId = "";
        public float bpm = 120f;
        public float offsetSec = 0f;
        public int seed = 1;
        public BeatSection[] sections = Array.Empty<BeatSection>();
        public TapObstacleEvent[] tapEvents = Array.Empty<TapObstacleEvent>();
        public HoldObstacleEvent[] holdEvents = Array.Empty<HoldObstacleEvent>();
        public VisualPulseEvent[] visualPulses = Array.Empty<VisualPulseEvent>();
        public BreathGapEvent[] breathGaps = Array.Empty<BreathGapEvent>();
    }

    [Serializable]
    public sealed class BeatSection
    {
        public string type = "Active";
        public float startSec;
        public float endSec;

        public bool IsRest => string.Equals(type, "Rest", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public sealed class TapObstacleEvent
    {
        public float timeSec;
        public int beatIndex = -1;
        public int lane;
        public string prefabId = "tap_basic";
        public string intensityTag = "mid";
    }

    [Serializable]
    public sealed class HoldObstacleEvent
    {
        public float startSec;
        public float endSec;
        public string motion = "Slide";
        public int laneFrom;
        public int laneTo;
        public string prefabId = "hold_basic";
    }

    [Serializable]
    public sealed class VisualPulseEvent
    {
        public float timeSec;
        public float strength = 1f;
    }

    [Serializable]
    public sealed class BreathGapEvent
    {
        public float startSec;
        public float endSec;
    }
}
