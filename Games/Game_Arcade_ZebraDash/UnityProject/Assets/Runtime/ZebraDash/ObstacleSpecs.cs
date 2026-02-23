using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZebraDash
{
    public readonly struct ObstacleSpec
    {
        public ObstacleSpec(
            string archetype,
            string gameplayRule,
            string motionLaw,
            string telegraphLaw,
            string defaultStyle,
            float baseTravelSec,
            float telegraphLeadBeats,
            float telegraphLeadMinSec,
            float postHitBeatFactor,
            float postHitMinSec,
            float postHitMaxSec,
            float diagonalDxMax,
            float tetherSwayAmp,
            float tetherSwayFreq)
        {
            Archetype = archetype ?? GameplayArchetypes.LaneBlock;
            GameplayRule = gameplayRule ?? "AvoidBlockedLaneAtHit";
            MotionLaw = motionLaw ?? "x(t)=lerp(spawnX,hitX,tau), y(t)=laneY";
            TelegraphLaw = telegraphLaw ?? "alpha=smoothstep((t-teleStart)/lead)";
            DefaultStyle = string.IsNullOrWhiteSpace(defaultStyle) ? GameplayPresentationKinds.Straight : defaultStyle;
            BaseTravelSec = Mathf.Max(0.1f, baseTravelSec);
            TelegraphLeadBeats = Mathf.Max(0.1f, telegraphLeadBeats);
            TelegraphLeadMinSec = Mathf.Max(0.05f, telegraphLeadMinSec);
            PostHitBeatFactor = Mathf.Max(0.01f, postHitBeatFactor);
            PostHitMinSec = Mathf.Max(0.01f, postHitMinSec);
            PostHitMaxSec = Mathf.Max(PostHitMinSec, postHitMaxSec);
            DiagonalDxMax = Mathf.Max(0f, diagonalDxMax);
            TetherSwayAmp = Mathf.Max(0f, tetherSwayAmp);
            TetherSwayFreq = Mathf.Max(0f, tetherSwayFreq);
        }

        public string Archetype { get; }
        public string GameplayRule { get; }
        public string MotionLaw { get; }
        public string TelegraphLaw { get; }
        public string DefaultStyle { get; }
        public float BaseTravelSec { get; }
        public float TelegraphLeadBeats { get; }
        public float TelegraphLeadMinSec { get; }
        public float PostHitBeatFactor { get; }
        public float PostHitMinSec { get; }
        public float PostHitMaxSec { get; }
        public float DiagonalDxMax { get; }
        public float TetherSwayAmp { get; }
        public float TetherSwayFreq { get; }
    }

    public static class ObstacleSpecs
    {
        private static readonly ObstacleSpec DefaultSpec = new ObstacleSpec(
            archetype: GameplayArchetypes.LaneBlock,
            gameplayRule: "AvoidBlockedLaneAtHit",
            motionLaw: "x=lerp(spawnX,hitX,tau), y=laneY",
            telegraphLaw: "lock band + hit tick",
            defaultStyle: GameplayPresentationKinds.Straight,
            baseTravelSec: 1.25f,
            telegraphLeadBeats: 1f,
            telegraphLeadMinSec: 0.35f,
            postHitBeatFactor: 0.20f,
            postHitMinSec: 0.08f,
            postHitMaxSec: 0.18f,
            diagonalDxMax: 0.70f,
            tetherSwayAmp: 0.16f,
            tetherSwayFreq: 2.4f);

        private static readonly Dictionary<string, ObstacleSpec> Specs = new Dictionary<string, ObstacleSpec>(StringComparer.OrdinalIgnoreCase)
        {
            [GameplayArchetypes.LaneBlock] = new ObstacleSpec(
                GameplayArchetypes.LaneBlock,
                "AvoidBlockedLaneAtHit",
                "Linear lane approach",
                "Lane lock band + hit tick",
                GameplayPresentationKinds.Straight,
                1.25f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.60f, 0.12f, 2.2f),

            [GameplayArchetypes.AccentCrusher] = new ObstacleSpec(
                GameplayArchetypes.AccentCrusher,
                "AvoidBlockedLaneAtHit(Accent)",
                "Linear + diagonal flavor",
                "Strong lock band + ring tick",
                GameplayPresentationKinds.Diagonal,
                1.35f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.90f, 0.20f, 2.8f),

            [GameplayArchetypes.AlternatorPair] = new ObstacleSpec(
                GameplayArchetypes.AlternatorPair,
                "Two sequential opposite-lane checks",
                "Linear pair with lane alternation",
                "Dual telegraph bands",
                GameplayPresentationKinds.Diagonal,
                1.25f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.80f, 0.15f, 2.4f),

            [GameplayArchetypes.StreakBreaker] = new ObstacleSpec(
                GameplayArchetypes.StreakBreaker,
                "Break repeated lane streak",
                "Linear rise flavor",
                "Lane break telegraph",
                GameplayPresentationKinds.Rise,
                1.30f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.65f, 0.10f, 2.0f),

            [GameplayArchetypes.HoldLaneLock] = new ObstacleSpec(
                GameplayArchetypes.HoldLaneLock,
                "Lane closed in [start,end]",
                "Linear approach then hold",
                "Progress lock bar",
                GameplayPresentationKinds.Drop,
                1.35f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.30f, 0.00f, 0.00f),

            [GameplayArchetypes.HoldReleaseGate] = new ObstacleSpec(
                GameplayArchetypes.HoldReleaseGate,
                "Hold lane then release gate",
                "Linear approach + release tick",
                "Progress + release cue",
                GameplayPresentationKinds.Drop,
                1.35f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.35f, 0.00f, 0.00f),

            [GameplayArchetypes.CrossGate] = new ObstacleSpec(
                GameplayArchetypes.CrossGate,
                "Sequential lane closures (cross)",
                "Linear diagonal gate",
                "Cross lane dual telegraph",
                GameplayPresentationKinds.Diagonal,
                1.32f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.95f, 0.12f, 2.6f),

            [GameplayArchetypes.OffbeatSnap] = new ObstacleSpec(
                GameplayArchetypes.OffbeatSnap,
                "Offbeat lane check without beat-switch",
                "Short linear pop approach",
                "Micro telegraph near hitline",
                GameplayPresentationKinds.Pop,
                1.18f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.45f, 0.06f, 2.0f),

            [GameplayArchetypes.FakeoutGhost] = new ObstacleSpec(
                GameplayArchetypes.FakeoutGhost,
                "Visual fakeout only (no collision)",
                "Ghost linear dissolve",
                "Ghost telegraph",
                GameplayPresentationKinds.Pop,
                1.05f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.50f, 0.08f, 1.8f),

            [GameplayArchetypes.RestPulse] = new ObstacleSpec(
                GameplayArchetypes.RestPulse,
                "Visual ambience only",
                "No hazard motion",
                "Ambient pulse only",
                GameplayPresentationKinds.Straight,
                1.00f, 1f, 0.35f,
                0.20f, 0.08f, 0.18f,
                0.00f, 0.00f, 0.00f)
        };

        public static ObstacleSpec Resolve(string archetype)
        {
            if (!string.IsNullOrWhiteSpace(archetype) && Specs.TryGetValue(archetype, out ObstacleSpec spec))
            {
                return spec;
            }

            return DefaultSpec;
        }

        public static float ComputeMinVisibleSec(float beatSec)
        {
            return Mathf.Clamp(0.90f * Mathf.Max(0.0001f, beatSec), 0.45f, 0.85f);
        }

        public static float ResolveTravelSec(GameplayPatternEvent evt, float beatSec)
        {
            ObstacleSpec spec = Resolve(evt != null ? evt.archetype : null);
            float requested = evt != null && evt.travelTimeSec > 0.01f
                ? evt.travelTimeSec
                : spec.BaseTravelSec;
            return Mathf.Max(requested, ComputeMinVisibleSec(beatSec));
        }

        public static float ResolveTelegraphLeadSec(GameplayPatternEvent evt, float beatSec)
        {
            ObstacleSpec spec = Resolve(evt != null ? evt.archetype : null);
            float beatLead = spec.TelegraphLeadBeats * Mathf.Max(0.0001f, beatSec);
            return Mathf.Max(beatLead, spec.TelegraphLeadMinSec);
        }

        public static float ResolvePostHitSec(string archetype, float beatSec)
        {
            ObstacleSpec spec = Resolve(archetype);
            return Mathf.Clamp(
                spec.PostHitBeatFactor * Mathf.Max(0.0001f, beatSec),
                spec.PostHitMinSec,
                spec.PostHitMaxSec);
        }

        public static string ResolvePresentation(GameplayPatternEvent evt)
        {
            if (evt != null && !string.IsNullOrWhiteSpace(evt.presentation))
            {
                return evt.presentation;
            }

            return Resolve(evt != null ? evt.archetype : null).DefaultStyle;
        }
    }
}
