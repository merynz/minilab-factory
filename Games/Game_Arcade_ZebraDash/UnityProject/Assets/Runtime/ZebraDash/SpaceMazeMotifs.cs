using System;
using UnityEngine;

namespace ZebraDash
{
    [Serializable]
    public readonly struct SpaceMazeMotifSpec
    {
        public SpaceMazeMotifSpec(
            string id,
            Color layer0,
            Color layer1,
            Color layer2,
            Color layer3,
            Color ribA,
            Color ribB,
            Color lightA,
            Color lightB,
            Color dust,
            float ambientIntensity,
            float hueShiftDeg,
            float saturationMul,
            float contrastMul,
            float safeZoneDimMul,
            float dustRateMul,
            float scanlineIntensity,
            float warpVignette,
            float panelLightChaseSpeed,
            float cableSwayAmp,
            float rotorSpinRate,
            float hazardOutlineMul,
            float railPulseMul)
        {
            Id = id ?? "MOTIF_HYPERLANE_NEON";
            Layer0 = layer0;
            Layer1 = layer1;
            Layer2 = layer2;
            Layer3 = layer3;
            RibA = ribA;
            RibB = ribB;
            LightA = lightA;
            LightB = lightB;
            Dust = dust;
            AmbientIntensity = ambientIntensity;
            HueShiftDeg = hueShiftDeg;
            SaturationMul = saturationMul;
            ContrastMul = contrastMul;
            SafeZoneDimMul = safeZoneDimMul;
            DustRateMul = dustRateMul;
            ScanlineIntensity = scanlineIntensity;
            WarpVignette = warpVignette;
            PanelLightChaseSpeed = panelLightChaseSpeed;
            CableSwayAmp = cableSwayAmp;
            RotorSpinRate = rotorSpinRate;
            HazardOutlineMul = hazardOutlineMul;
            RailPulseMul = railPulseMul;
        }

        public string Id { get; }
        public Color Layer0 { get; }
        public Color Layer1 { get; }
        public Color Layer2 { get; }
        public Color Layer3 { get; }
        public Color RibA { get; }
        public Color RibB { get; }
        public Color LightA { get; }
        public Color LightB { get; }
        public Color Dust { get; }
        public float AmbientIntensity { get; }
        public float HueShiftDeg { get; }
        public float SaturationMul { get; }
        public float ContrastMul { get; }
        public float SafeZoneDimMul { get; }
        public float DustRateMul { get; }
        public float ScanlineIntensity { get; }
        public float WarpVignette { get; }
        public float PanelLightChaseSpeed { get; }
        public float CableSwayAmp { get; }
        public float RotorSpinRate { get; }
        public float HazardOutlineMul { get; }
        public float RailPulseMul { get; }
    }

    public static class SpaceMazeMotifs
    {
        public static readonly SpaceMazeMotifSpec[] All =
        {
            new SpaceMazeMotifSpec("MOTIF_BULKHEAD_SPINE",
                new Color(0.05f, 0.10f, 0.18f), new Color(0.08f, 0.16f, 0.25f), new Color(0.11f, 0.24f, 0.34f), new Color(0.16f, 0.32f, 0.44f),
                new Color(0.24f, 0.54f, 0.78f), new Color(0.20f, 0.45f, 0.65f),
                new Color(0.38f, 0.94f, 1f), new Color(0.98f, 0.56f, 0.88f),
                new Color(0.74f, 0.96f, 1f), 0.82f, 8f, 1.18f, 1.10f, 0.40f, 1.18f, 0.08f, 0.16f, 1.15f, 0.48f, 0.28f, 1.22f, 1.12f),

            new SpaceMazeMotifSpec("MOTIF_IRIS_GATE_TUNNEL",
                new Color(0.06f, 0.12f, 0.20f), new Color(0.09f, 0.19f, 0.28f), new Color(0.12f, 0.26f, 0.38f), new Color(0.16f, 0.34f, 0.47f),
                new Color(0.30f, 0.66f, 0.90f), new Color(0.24f, 0.52f, 0.74f),
                new Color(0.44f, 0.98f, 1f), new Color(0.96f, 0.62f, 0.92f),
                new Color(0.76f, 0.96f, 1f), 0.88f, -4f, 1.10f, 1.08f, 0.42f, 1.06f, 0.06f, 0.18f, 1.10f, 0.42f, 0.36f, 1.25f, 1.10f),

            new SpaceMazeMotifSpec("MOTIF_PISTON_TRENCH",
                new Color(0.08f, 0.10f, 0.15f), new Color(0.14f, 0.14f, 0.20f), new Color(0.22f, 0.19f, 0.25f), new Color(0.32f, 0.25f, 0.28f),
                new Color(0.72f, 0.42f, 0.26f), new Color(0.60f, 0.33f, 0.22f),
                new Color(1.0f, 0.70f, 0.34f), new Color(1.0f, 0.44f, 0.30f),
                new Color(1.0f, 0.75f, 0.45f), 0.74f, 20f, 1.06f, 1.12f, 0.46f, 1.28f, 0.03f, 0.22f, 1.26f, 0.56f, 0.22f, 1.34f, 1.20f),

            new SpaceMazeMotifSpec("MOTIF_ROTOR_JUNCTION",
                new Color(0.05f, 0.09f, 0.14f), new Color(0.08f, 0.13f, 0.22f), new Color(0.13f, 0.19f, 0.32f), new Color(0.18f, 0.26f, 0.42f),
                new Color(0.28f, 0.62f, 0.86f), new Color(0.21f, 0.48f, 0.70f),
                new Color(0.40f, 0.96f, 1f), new Color(0.92f, 0.52f, 0.90f),
                new Color(0.72f, 0.94f, 1f), 0.80f, 14f, 1.15f, 1.10f, 0.38f, 1.20f, 0.08f, 0.20f, 1.35f, 0.50f, 0.92f, 1.28f, 1.14f),

            new SpaceMazeMotifSpec("MOTIF_CONDUIT_MAZE",
                new Color(0.05f, 0.11f, 0.16f), new Color(0.08f, 0.17f, 0.24f), new Color(0.12f, 0.24f, 0.31f), new Color(0.18f, 0.30f, 0.38f),
                new Color(0.22f, 0.68f, 0.72f), new Color(0.18f, 0.54f, 0.58f),
                new Color(0.34f, 1f, 0.82f), new Color(0.58f, 0.96f, 0.64f),
                new Color(0.70f, 0.98f, 0.86f), 0.76f, -6f, 1.12f, 1.06f, 0.37f, 1.10f, 0.05f, 0.14f, 1.40f, 0.70f, 0.18f, 1.20f, 1.05f),

            new SpaceMazeMotifSpec("MOTIF_MAINTENANCE_SHAFT",
                new Color(0.03f, 0.06f, 0.10f), new Color(0.05f, 0.10f, 0.16f), new Color(0.08f, 0.14f, 0.22f), new Color(0.12f, 0.19f, 0.30f),
                new Color(0.20f, 0.36f, 0.56f), new Color(0.15f, 0.27f, 0.44f),
                new Color(0.60f, 0.84f, 1f), new Color(0.84f, 0.88f, 1f),
                new Color(0.74f, 0.86f, 0.98f), 0.54f, -15f, 0.92f, 1.08f, 0.30f, 0.76f, 0.04f, 0.10f, 0.85f, 0.34f, 0.10f, 1.18f, 1.02f),

            new SpaceMazeMotifSpec("MOTIF_LASER_LATTICE",
                new Color(0.08f, 0.07f, 0.15f), new Color(0.14f, 0.10f, 0.24f), new Color(0.20f, 0.15f, 0.33f), new Color(0.28f, 0.20f, 0.42f),
                new Color(0.66f, 0.30f, 0.82f), new Color(0.52f, 0.24f, 0.66f),
                new Color(1f, 0.56f, 0.98f), new Color(0.76f, 0.48f, 1f),
                new Color(0.94f, 0.76f, 1f), 0.82f, 40f, 1.26f, 1.14f, 0.42f, 1.14f, 0.12f, 0.22f, 1.42f, 0.58f, 0.44f, 1.30f, 1.18f),

            new SpaceMazeMotifSpec("MOTIF_REACTOR_SPINE",
                new Color(0.09f, 0.08f, 0.13f), new Color(0.16f, 0.12f, 0.18f), new Color(0.24f, 0.18f, 0.24f), new Color(0.34f, 0.25f, 0.30f),
                new Color(0.78f, 0.36f, 0.24f), new Color(0.62f, 0.28f, 0.18f),
                new Color(1f, 0.64f, 0.30f), new Color(1f, 0.42f, 0.24f),
                new Color(1f, 0.72f, 0.42f), 0.90f, 24f, 1.10f, 1.10f, 0.44f, 1.26f, 0.04f, 0.24f, 1.26f, 0.52f, 0.24f, 1.36f, 1.20f),

            new SpaceMazeMotifSpec("MOTIF_GRAVITY_BAFFLE",
                new Color(0.04f, 0.09f, 0.13f), new Color(0.06f, 0.14f, 0.20f), new Color(0.10f, 0.20f, 0.28f), new Color(0.14f, 0.26f, 0.37f),
                new Color(0.26f, 0.56f, 0.74f), new Color(0.20f, 0.44f, 0.60f),
                new Color(0.48f, 0.90f, 1f), new Color(0.92f, 0.56f, 0.86f),
                new Color(0.72f, 0.92f, 1f), 0.68f, -10f, 1.04f, 1.12f, 0.36f, 1.00f, 0.07f, 0.15f, 1.02f, 0.62f, 0.30f, 1.24f, 1.08f),

            new SpaceMazeMotifSpec("MOTIF_SENTINEL_VAULT",
                new Color(0.03f, 0.05f, 0.08f), new Color(0.05f, 0.08f, 0.13f), new Color(0.08f, 0.12f, 0.19f), new Color(0.12f, 0.18f, 0.28f),
                new Color(0.18f, 0.42f, 0.64f), new Color(0.14f, 0.33f, 0.50f),
                new Color(0.80f, 0.94f, 1f), new Color(1f, 0.70f, 0.86f),
                new Color(0.78f, 0.90f, 1f), 0.62f, 0f, 0.92f, 1.22f, 0.28f, 0.86f, 0.03f, 0.18f, 0.92f, 0.30f, 0.12f, 1.40f, 1.06f)
        };

        public static int Count => All.Length;

        public static SpaceMazeMotifSpec ResolveByIndex(int index)
        {
            if (All.Length == 0)
            {
                throw new InvalidOperationException("No motifs configured.");
            }

            int i = index % All.Length;
            if (i < 0)
            {
                i += All.Length;
            }

            return All[i];
        }
    }
}
