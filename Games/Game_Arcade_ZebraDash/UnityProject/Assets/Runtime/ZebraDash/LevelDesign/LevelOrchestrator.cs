using System;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash.LevelDesign
{
    [Serializable]
    public sealed class LevelOrchestration
    {
        public MovementProfile[] profiles = Array.Empty<MovementProfile>();
        public SectionPlan[] sections = Array.Empty<SectionPlan>();

        public SectionPlan ResolveSectionAt(float songSec)
        {
            if (sections == null || sections.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < sections.Length; i++)
            {
                SectionPlan section = sections[i];
                if (section == null)
                {
                    continue;
                }

                if (songSec >= section.startSec && songSec < section.endSec)
                {
                    return section;
                }
            }

            return sections[sections.Length - 1];
        }

        public MovementProfile ResolveProfile(string profileName)
        {
            if (profiles == null || profiles.Length == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(profileName))
            {
                for (int i = 0; i < profiles.Length; i++)
                {
                    MovementProfile profile = profiles[i];
                    if (profile != null && string.Equals(profile.name, profileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return profile;
                    }
                }
            }

            return profiles[0];
        }
    }

    public static class LevelOrchestrator
    {
        private sealed class TemplateSection
        {
            public SectionKind Kind;
            public int Bars;
            public string Profile;
            public float Density;
            public float Difficulty;
        }

        private static readonly TemplateSection[] SectionTemplate =
        {
            // Front-loaded readability curve: tutorial -> active -> drop must happen inside early run.
            new() { Kind = SectionKind.Active,     Bars = 3, Profile = "P0_TUTORIAL", Density = 0.18f, Difficulty = 0.12f },
            new() { Kind = SectionKind.Active,     Bars = 3, Profile = "P1_ACTIVE",   Density = 0.30f, Difficulty = 0.24f },
            new() { Kind = SectionKind.Transition, Bars = 1, Profile = "P1_ACTIVE",   Density = 0.05f, Difficulty = 0.16f },
            new() { Kind = SectionKind.Active,     Bars = 2, Profile = "P3_FLOATY",   Density = 0.40f, Difficulty = 0.34f },
            new() { Kind = SectionKind.Transition, Bars = 1, Profile = "P3_FLOATY",   Density = 0.05f, Difficulty = 0.18f },
            new() { Kind = SectionKind.Drop,       Bars = 3, Profile = "P2_FAST",     Density = 0.52f, Difficulty = 0.56f },
            new() { Kind = SectionKind.Transition, Bars = 1, Profile = "P2_FAST",     Density = 0.05f, Difficulty = 0.20f },
            new() { Kind = SectionKind.Active,     Bars = 4, Profile = "P1_ACTIVE",   Density = 0.46f, Difficulty = 0.50f },
            new() { Kind = SectionKind.Transition, Bars = 1, Profile = "P1_ACTIVE",   Density = 0.05f, Difficulty = 0.22f },
            new() { Kind = SectionKind.Active,     Bars = 4, Profile = "P4_TIGHT",    Density = 0.54f, Difficulty = 0.64f },
            new() { Kind = SectionKind.Rest,       Bars = 4, Profile = "P0_TUTORIAL", Density = 0.00f, Difficulty = 0.10f },
            new() { Kind = SectionKind.Drop,       Bars = 4, Profile = "P2_FAST",     Density = 0.58f, Difficulty = 0.72f }
        };

        public static LevelOrchestration Build(BeatMap beatMap, float durationSec, string trackId)
        {
            float bpm = beatMap != null && beatMap.bpm > 0.01f ? beatMap.bpm : 120f;
            MovementProfile[] profiles = BuildProfiles(bpm);
            float beatSec = 60f / Mathf.Max(1f, bpm);
            int totalBars = Mathf.Max(8, Mathf.CeilToInt(Mathf.Max(4f, durationSec) / (beatSec * 4f)));

            var sections = new List<SectionPlan>(Mathf.Max(8, totalBars / 2));
            int barCursor = 0;
            int sectionIndex = 0;
            int cycleSeedOffset = ComputeCycleStartIndex(trackId, beatMap != null ? beatMap.seed : 0);
            while (barCursor < totalBars)
            {
                int templateIndex;
                if (sectionIndex < SectionTemplate.Length)
                {
                    // Keep the first sequence fixed to preserve tutorial -> active -> drop readability.
                    templateIndex = sectionIndex;
                }
                else
                {
                    int loop = sectionIndex - SectionTemplate.Length;
                    templateIndex = Mathf.Abs((cycleSeedOffset + loop) % SectionTemplate.Length);
                }

                TemplateSection template = SectionTemplate[templateIndex];
                int bars = Mathf.Min(template.Bars, totalBars - barCursor);
                var section = new SectionPlan
                {
                    sectionType = template.Kind,
                    sectionIndex = sectionIndex,
                    barsLength = bars,
                    startBar = barCursor,
                    endBarExclusive = barCursor + bars,
                    startSec = barCursor * beatSec * 4f,
                    endSec = (barCursor + bars) * beatSec * 4f,
                    movementProfileRef = template.Profile,
                    densityTarget = template.Density,
                    difficultyRamp = template.Difficulty
                };
                sections.Add(section);
                barCursor += bars;
                sectionIndex++;
            }

            return new LevelOrchestration
            {
                profiles = profiles,
                sections = sections.ToArray()
            };
        }

        public static string ToLegacySectionType(SectionKind kind)
        {
            return kind switch
            {
                SectionKind.Rest => GameplaySectionTypes.Rest,
                SectionKind.Drop => GameplaySectionTypes.Drop,
                SectionKind.Transition => GameplaySectionTypes.Transition,
                _ => GameplaySectionTypes.Active
            };
        }

        private static int ComputeCycleStartIndex(string trackId, int seed)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + seed;
                hash = (hash * 31) + (trackId != null ? trackId.GetHashCode() : 0);
                int idx = Mathf.Abs(hash % SectionTemplate.Length);
                return idx;
            }
        }

        private static MovementProfile[] BuildProfiles(float bpm)
        {
            return new[]
            {
                new MovementProfile
                {
                    name = "P0_TUTORIAL",
                    bpm = bpm,
                    tileSize = 1.0f,
                    colsPerBeat = 3,
                    dxTilesPerBeat = 3.0f,
                    jumpCycleBeats = 1.0f,
                    apexHeightTiles = 2.8f,
                    jumpBufferSec = 0.08f,
                    coyoteSec = 0.07f
                },
                new MovementProfile
                {
                    name = "P1_ACTIVE",
                    bpm = bpm,
                    tileSize = 1.0f,
                    colsPerBeat = 4,
                    dxTilesPerBeat = 4.0f,
                    jumpCycleBeats = 1.0f,
                    apexHeightTiles = 2.9f,
                    jumpBufferSec = 0.07f,
                    coyoteSec = 0.06f
                },
                new MovementProfile
                {
                    name = "P2_FAST",
                    bpm = bpm,
                    tileSize = 1.0f,
                    colsPerBeat = 5,
                    dxTilesPerBeat = 5.0f,
                    jumpCycleBeats = 1.0f,
                    apexHeightTiles = 2.7f,
                    jumpBufferSec = 0.06f,
                    coyoteSec = 0.05f
                },
                new MovementProfile
                {
                    name = "P3_FLOATY",
                    bpm = bpm,
                    tileSize = 1.0f,
                    colsPerBeat = 4,
                    dxTilesPerBeat = 4.0f,
                    jumpCycleBeats = 1.15f,
                    apexHeightTiles = 3.4f,
                    jumpBufferSec = 0.08f,
                    coyoteSec = 0.07f
                },
                new MovementProfile
                {
                    name = "P4_TIGHT",
                    bpm = bpm,
                    tileSize = 1.0f,
                    colsPerBeat = 4,
                    dxTilesPerBeat = 4.0f,
                    jumpCycleBeats = 0.85f,
                    apexHeightTiles = 2.6f,
                    jumpBufferSec = 0.06f,
                    coyoteSec = 0.05f
                }
            };
        }
    }
}
