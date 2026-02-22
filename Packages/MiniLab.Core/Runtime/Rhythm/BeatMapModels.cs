using System;
using System.Collections.Generic;
using System.Linq;
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
        public BeatEvent[] events = Array.Empty<BeatEvent>();
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
        public float density = 0.5f;
        public float intensity = 0.5f;

        public bool IsRest => string.Equals(type, "Rest", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public sealed class BeatEvent
    {
        public float timeSec;
        public int lane;
        public string kind = "Tap";
        public float durationSec;
        public float intensity = 1f;
        public string prefabId = "tap_basic";
        public int laneTo;
        public string motion = "Slide";

        public bool IsKind(string value) => string.Equals(kind, value, StringComparison.OrdinalIgnoreCase);
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

    public static class BeatMapEventUtils
    {
        public static BeatEvent[] GetCanonicalEvents(BeatMap beatMap)
        {
            if (beatMap == null)
            {
                return Array.Empty<BeatEvent>();
            }

            if (beatMap.events != null && beatMap.events.Length > 0)
            {
                return beatMap.events
                    .Where(e => e != null)
                    .OrderBy(e => e.timeSec)
                    .ToArray();
            }

            var events = new List<BeatEvent>();
            if (beatMap.tapEvents != null)
            {
                events.AddRange(beatMap.tapEvents.Select(t => new BeatEvent
                {
                    timeSec = t.timeSec,
                    lane = t.lane,
                    kind = "Tap",
                    durationSec = 0f,
                    intensity = string.Equals(t.intensityTag, "high", StringComparison.OrdinalIgnoreCase) ? 1f : 0.6f,
                    prefabId = string.IsNullOrWhiteSpace(t.prefabId) ? "tap_basic" : t.prefabId,
                    laneTo = t.lane
                }));
            }

            if (beatMap.holdEvents != null)
            {
                events.AddRange(beatMap.holdEvents.Select(h => new BeatEvent
                {
                    timeSec = h.startSec,
                    lane = h.laneFrom,
                    kind = "Hold",
                    durationSec = Mathf.Max(0f, h.endSec - h.startSec),
                    intensity = 0.8f,
                    prefabId = string.IsNullOrWhiteSpace(h.prefabId) ? "hold_basic" : h.prefabId,
                    laneTo = h.laneTo,
                    motion = string.IsNullOrWhiteSpace(h.motion) ? "Slide" : h.motion
                }));
            }

            if (beatMap.visualPulses != null)
            {
                events.AddRange(beatMap.visualPulses.Select(p => new BeatEvent
                {
                    timeSec = p.timeSec,
                    lane = 0,
                    kind = "Accent",
                    durationSec = 0f,
                    intensity = Mathf.Clamp01(p.strength),
                    prefabId = "accent_pulse",
                    laneTo = 0
                }));
            }

            if (beatMap.breathGaps != null)
            {
                events.AddRange(beatMap.breathGaps.Select(g => new BeatEvent
                {
                    timeSec = g.startSec,
                    lane = 0,
                    kind = "Gap",
                    durationSec = Mathf.Max(0f, g.endSec - g.startSec),
                    intensity = 0f,
                    prefabId = "gap",
                    laneTo = 0
                }));
            }

            BeatEvent[] canonical = events.OrderBy(e => e.timeSec).ToArray();
            beatMap.events = canonical;
            return canonical;
        }
    }
}
