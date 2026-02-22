using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    public static class BeatKinds
    {
        public const string Tap = "Tap";
        public const string Accent = "Accent";
        public const string Long = "Long";
        public const string RestSection = "RestSection";

        // Backward-compat aliases.
        public const string HoldLegacy = "Hold";
        public const string GapLegacy = "Gap";
    }

    [Serializable]
    public sealed class BeatMap
    {
        public string schemaVersion = "2.0.0";
        public string trackId = "";
        public float bpm = 120f;
        public float offsetSec = 0f;
        public int seed = 1;
        public BeatSection[] sections = Array.Empty<BeatSection>();
        public RestSectionEvent[] restSections = Array.Empty<RestSectionEvent>();
        public BeatEvent[] events = Array.Empty<BeatEvent>();

        // Legacy fields (kept for backward compatibility).
        public TapObstacleEvent[] tapEvents = Array.Empty<TapObstacleEvent>();
        public HoldObstacleEvent[] holdEvents = Array.Empty<HoldObstacleEvent>();
        public LongObstacleEvent[] longEvents = Array.Empty<LongObstacleEvent>();
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
    public sealed class RestSectionEvent
    {
        public float startSec;
        public float endSec;
    }

    [Serializable]
    public sealed class BeatEvent
    {
        public float timeSec;
        public float endTimeSec;
        public int lane;
        public string kind = BeatKinds.Tap;
        public float intensity = 1f;
        public string prefabId = "tap_basic";
        public string motion = "Slide";

        // Legacy compatibility.
        public float durationSec;
        public int laneTo;

        public bool IsKind(string value) => string.Equals(kind, value, StringComparison.OrdinalIgnoreCase);

        public float GetEndTimeSec()
        {
            if (endTimeSec > timeSec)
            {
                return endTimeSec;
            }

            if (durationSec > 0f)
            {
                return timeSec + durationSec;
            }

            return timeSec;
        }
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
    public sealed class LongObstacleEvent
    {
        public float startSec;
        public float endSec;
        public int lane;
        public float intensity = 0.8f;
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

            var events = new List<BeatEvent>();

            if (beatMap.events != null)
            {
                for (int i = 0; i < beatMap.events.Length; i++)
                {
                    BeatEvent evt = Normalize(beatMap.events[i]);
                    if (evt != null)
                    {
                        events.Add(evt);
                    }
                }
            }

            if (events.Count == 0 && beatMap.tapEvents != null)
            {
                events.AddRange(beatMap.tapEvents.Select(t => Normalize(new BeatEvent
                {
                    timeSec = t.timeSec,
                    endTimeSec = t.timeSec,
                    lane = t.lane,
                    kind = BeatKinds.Tap,
                    intensity = string.Equals(t.intensityTag, "high", StringComparison.OrdinalIgnoreCase) ? 1f : 0.65f,
                    prefabId = string.IsNullOrWhiteSpace(t.prefabId) ? "tap_basic" : t.prefabId
                })));
            }

            if (beatMap.holdEvents != null)
            {
                events.AddRange(beatMap.holdEvents.Select(h => Normalize(new BeatEvent
                {
                    timeSec = h.startSec,
                    endTimeSec = Mathf.Max(h.endSec, h.startSec + 0.1f),
                    durationSec = Mathf.Max(0f, h.endSec - h.startSec),
                    lane = h.laneFrom,
                    laneTo = h.laneTo,
                    kind = BeatKinds.Long,
                    intensity = 0.8f,
                    motion = string.IsNullOrWhiteSpace(h.motion) ? "Slide" : h.motion,
                    prefabId = string.IsNullOrWhiteSpace(h.prefabId) ? "long_basic" : h.prefabId
                })));
            }

            if (beatMap.longEvents != null)
            {
                events.AddRange(beatMap.longEvents.Select(l => Normalize(new BeatEvent
                {
                    timeSec = l.startSec,
                    endTimeSec = Mathf.Max(l.endSec, l.startSec + 0.1f),
                    durationSec = Mathf.Max(0f, l.endSec - l.startSec),
                    lane = l.lane,
                    kind = BeatKinds.Long,
                    intensity = Mathf.Clamp01(l.intensity),
                    prefabId = "long_basic"
                })));
            }

            if (beatMap.visualPulses != null)
            {
                events.AddRange(beatMap.visualPulses.Select(p => Normalize(new BeatEvent
                {
                    timeSec = p.timeSec,
                    endTimeSec = p.timeSec,
                    lane = 0,
                    kind = BeatKinds.Accent,
                    intensity = Mathf.Clamp01(p.strength),
                    prefabId = "accent_pulse"
                })));
            }

            BeatEvent[] canonical = events
                .Where(e => e != null)
                .OrderBy(e => e.timeSec)
                .ToArray();

            beatMap.events = canonical;
            beatMap.restSections = GetRestSections(beatMap);
            return canonical;
        }

        public static BeatEvent[] GetTapAccentEvents(BeatMap beatMap)
        {
            return GetCanonicalEvents(beatMap)
                .Where(e => e.IsKind(BeatKinds.Tap) || e.IsKind(BeatKinds.Accent))
                .ToArray();
        }

        public static RestSectionEvent[] GetRestSections(BeatMap beatMap)
        {
            if (beatMap == null)
            {
                return Array.Empty<RestSectionEvent>();
            }

            var rest = new List<RestSectionEvent>();
            if (beatMap.restSections != null && beatMap.restSections.Length > 0)
            {
                rest.AddRange(beatMap.restSections.Where(r => r != null && r.endSec > r.startSec));
            }

            if (beatMap.sections != null)
            {
                foreach (BeatSection section in beatMap.sections)
                {
                    if (section == null || !section.IsRest || section.endSec <= section.startSec)
                    {
                        continue;
                    }

                    rest.Add(new RestSectionEvent
                    {
                        startSec = section.startSec,
                        endSec = section.endSec
                    });
                }
            }

            if (beatMap.breathGaps != null)
            {
                foreach (BreathGapEvent gap in beatMap.breathGaps)
                {
                    if (gap == null || gap.endSec <= gap.startSec)
                    {
                        continue;
                    }

                    rest.Add(new RestSectionEvent
                    {
                        startSec = gap.startSec,
                        endSec = gap.endSec
                    });
                }
            }

            return rest
                .OrderBy(r => r.startSec)
                .ThenBy(r => r.endSec)
                .ToArray();
        }

        public static bool IsRestTime(BeatMap beatMap, float timeSec)
        {
            RestSectionEvent[] rests = GetRestSections(beatMap);
            for (int i = 0; i < rests.Length; i++)
            {
                if (timeSec >= rests[i].startSec && timeSec <= rests[i].endSec)
                {
                    return true;
                }
            }

            return false;
        }

        private static BeatEvent Normalize(BeatEvent evt)
        {
            if (evt == null)
            {
                return null;
            }

            if (evt.timeSec < 0f)
            {
                evt.timeSec = 0f;
            }

            if (string.IsNullOrWhiteSpace(evt.kind))
            {
                evt.kind = BeatKinds.Tap;
            }

            if (evt.IsKind(BeatKinds.HoldLegacy))
            {
                evt.kind = BeatKinds.Long;
            }

            if (evt.IsKind(BeatKinds.GapLegacy))
            {
                evt.kind = BeatKinds.RestSection;
            }

            evt.intensity = Mathf.Clamp01(evt.intensity <= 0f ? 0.6f : evt.intensity);

            if (evt.IsKind(BeatKinds.Long))
            {
                float end = evt.endTimeSec;
                if (end <= evt.timeSec)
                {
                    end = evt.durationSec > 0f ? evt.timeSec + evt.durationSec : evt.timeSec + 1f;
                }

                evt.endTimeSec = end;
                evt.durationSec = Mathf.Max(0f, end - evt.timeSec);
                if (string.IsNullOrWhiteSpace(evt.prefabId))
                {
                    evt.prefabId = "long_basic";
                }
            }
            else
            {
                evt.endTimeSec = evt.timeSec;
                evt.durationSec = 0f;
                if (string.IsNullOrWhiteSpace(evt.prefabId))
                {
                    evt.prefabId = evt.IsKind(BeatKinds.Accent) ? "accent_basic" : "tap_basic";
                }
            }

            return evt;
        }
    }
}
