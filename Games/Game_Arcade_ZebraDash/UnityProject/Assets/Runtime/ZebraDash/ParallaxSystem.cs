using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ParallaxSystem : MonoBehaviour
    {
        private const int MazeRibCount = 18;
        private const float MazeRibSpacing = 4.6f;
        private const float MazeRibScrollRatio = 0.52f;
        private const float MazeHalfHeight = 5.4f;
        private const float MazeGapHalf = 1.75f;
        private const float MazeRibWidth = 0.20f;
        private readonly List<ParallaxLayer> layers = new List<ParallaxLayer>();
        private readonly List<MazeRib> mazeRibs = new List<MazeRib>(MazeRibCount);
        private float pulseStrength;
        private float lastSongTimeSec;
        private float speedMultiplier = 1f;
        private float speedMultiplierTarget = 1f;
        private float bobMultiplier = 1f;
        private float bobMultiplierTarget = 1f;
        private float beatPulse;
        private float barPulse;
        private bool initialized;
        private bool hasClockSample;
        private Transform layerRoot;

        public float SpeedPulseMultiplier { get; private set; } = 1f;
        public float EmissivePulseMultiplier { get; private set; } = 1f;

        private sealed class ParallaxLayer
        {
            public Transform A;
            public Transform B;
            public float Width;
            public float Overlap;
            public float BaseY;
            public float ScrollRatio;
            public float BobAmp;
            public float BobFreq;
            public float Phase;
            public float PulseScale;
            public float BeatAmp;
            public float BarAmp;
            public float AccentAmp;
            public double ScrollX;
        }

        private sealed class MazeRib
        {
            public Transform Top;
            public Transform Bottom;
            public Material TopMaterial;
            public Material BottomMaterial;
            public float Phase;
            public float Offset;
        }

        public void Initialize(Transform parentRoot)
        {
            if (initialized)
            {
                return;
            }

            layerRoot = new GameObject("ParallaxRoot").transform;
            layerRoot.SetParent(parentRoot, false);

            AddLayer(
                width: 48f,
                height: 18f,
                z: -8.5f,
                y: 0.2f,
                color: new Color(0.06f, 0.10f, 0.17f, 1f),
                speedRatio: 0.15f,
                bobAmp: 0.02f,
                bobFreq: 0.28f,
                pulseScale: 0.03f,
                beatAmp: 0.020f,
                barAmp: 0.015f,
                accentAmp: 0.04f);
            AddLayer(
                width: 44f,
                height: 12f,
                z: -7.8f,
                y: -0.3f,
                color: new Color(0.09f, 0.16f, 0.27f, 1f),
                speedRatio: 0.25f,
                bobAmp: 0.04f,
                bobFreq: 0.45f,
                pulseScale: 0.05f,
                beatAmp: 0.030f,
                barAmp: 0.020f,
                accentAmp: 0.06f);
            AddLayer(
                width: 40f,
                height: 9f,
                z: -7.1f,
                y: -0.8f,
                color: new Color(0.11f, 0.23f, 0.35f, 1f),
                speedRatio: 0.45f,
                bobAmp: 0.06f,
                bobFreq: 0.72f,
                pulseScale: 0.08f,
                beatAmp: 0.040f,
                barAmp: 0.030f,
                accentAmp: 0.09f);
            AddLayer(
                width: 36f,
                height: 6.5f,
                z: -6.2f,
                y: -1.4f,
                color: new Color(0.14f, 0.30f, 0.44f, 1f),
                speedRatio: 0.75f,
                bobAmp: 0.08f,
                bobFreq: 1.15f,
                pulseScale: 0.11f,
                beatAmp: 0.055f,
                barAmp: 0.040f,
                accentAmp: 0.12f);
            BuildSpaceMaze();

            initialized = true;
            hasClockSample = false;
        }

        public void Tick(float songTimeSec, bool isPlaying, float phaseBeat = 0f, float phaseBar = 0f, float worldScrollPos = 0f)
        {
            if (!initialized)
            {
                return;
            }

            float delta;
            if (!hasClockSample)
            {
                lastSongTimeSec = songTimeSec;
                hasClockSample = true;
                delta = 0f;
            }
            else
            {
                delta = Mathf.Max(0f, songTimeSec - lastSongTimeSec);
                lastSongTimeSec = songTimeSec;
            }

            if (!isPlaying)
            {
                delta = Time.unscaledDeltaTime;
            }

            pulseStrength = Mathf.MoveTowards(pulseStrength, 0f, delta * 2.2f);
            speedMultiplier = Mathf.MoveTowards(speedMultiplier, speedMultiplierTarget, delta * 1.6f);
            bobMultiplier = Mathf.MoveTowards(bobMultiplier, bobMultiplierTarget, delta * 1.6f);
            beatPulse = PulseEnvelope(phaseBeat, 0.12f);
            barPulse = PulseEnvelope(phaseBar, 0.18f);
            SpeedPulseMultiplier = 1f + (beatPulse * 0.04f) + (barPulse * 0.02f) + (pulseStrength * 0.03f);
            EmissivePulseMultiplier = 1f + (beatPulse * 0.38f) + (pulseStrength * 0.52f);

            for (int i = 0; i < layers.Count; i++)
            {
                ParallaxLayer layer = layers[i];
                float envelope = 1f
                    + (beatPulse * layer.BeatAmp)
                    + (barPulse * layer.BarAmp)
                    + (pulseStrength * layer.AccentAmp);
                float pulseOffset = layer.Width * 0.03f * (envelope - 1f);
                float layerScroll = worldScrollPos * layer.ScrollRatio * speedMultiplier;
                layer.ScrollX = -((double)layerScroll + pulseOffset);
                float wrapRange = Mathf.Max(1f, layer.Width - layer.Overlap);
                float wrappedX = (float)(-RepeatPositive(layer.ScrollX, wrapRange));
                wrappedX = Mathf.Round(wrappedX * 512f) / 512f;
                float bob = Mathf.Sin((songTimeSec * layer.BobFreq * bobMultiplier) + layer.Phase) * (layer.BobAmp * bobMultiplier);
                float pulse = pulseStrength * layer.PulseScale;
                float y = layer.BaseY + bob + pulse;
                y = Mathf.Round(y * 512f) / 512f;

                layer.A.localPosition = new Vector3(wrappedX, y, layer.A.localPosition.z);
                layer.B.localPosition = new Vector3(wrappedX + wrapRange, y, layer.B.localPosition.z);
            }

            TickSpaceMaze(songTimeSec, worldScrollPos);
        }

        public void PushAccent(float intensity)
        {
            pulseStrength = Mathf.Max(pulseStrength, Mathf.Lerp(0.16f, 0.55f, Mathf.Clamp01(intensity)));
        }

        public void SetSectionMood(string sectionType, float currentStrain, float targetStrain)
        {
            float blend = Mathf.Clamp01(Mathf.Lerp(currentStrain, targetStrain, 0.5f));
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                speedMultiplierTarget = 0.55f;
                bobMultiplierTarget = 0.70f;
                return;
            }

            if (string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                speedMultiplierTarget = Mathf.Lerp(1.10f, 1.35f, blend);
                bobMultiplierTarget = Mathf.Lerp(1.05f, 1.30f, blend);
                return;
            }

            speedMultiplierTarget = Mathf.Lerp(0.90f, 1.10f, blend);
            bobMultiplierTarget = Mathf.Lerp(0.95f, 1.05f, blend);
        }

        private static float PulseEnvelope(float phase, float width)
        {
            float p = Mathf.Repeat(phase, 1f);
            float dist = Mathf.Min(p, 1f - p);
            float t = Mathf.Clamp01(1f - (dist / Mathf.Max(0.001f, width)));
            return t * t * (3f - (2f * t));
        }

        private void BuildSpaceMaze()
        {
            mazeRibs.Clear();
            float ribHeight = Mathf.Max(0.5f, MazeHalfHeight - MazeGapHalf);
            float topY = MazeGapHalf + (ribHeight * 0.5f);
            float bottomY = -topY;
            for (int i = 0; i < MazeRibCount; i++)
            {
                Color topColor = new Color(0.24f, 0.52f, 0.78f, 0.18f);
                Color bottomColor = new Color(0.20f, 0.44f, 0.66f, 0.17f);
                Transform top = CreateMazeSegment($"MazeRibTop_{i}", topY, ribHeight, topColor, -9);
                Transform bottom = CreateMazeSegment($"MazeRibBottom_{i}", bottomY, ribHeight, bottomColor, -9);
                var rib = new MazeRib
                {
                    Top = top,
                    Bottom = bottom,
                    TopMaterial = top != null ? top.GetComponent<Renderer>()?.material : null,
                    BottomMaterial = bottom != null ? bottom.GetComponent<Renderer>()?.material : null,
                    Phase = i * 0.42f,
                    Offset = ((i % 3) - 1) * 0.13f
                };
                mazeRibs.Add(rib);
            }
        }

        private void TickSpaceMaze(float songTimeSec, float worldScrollPos)
        {
            if (mazeRibs.Count == 0)
            {
                return;
            }

            float ribHeight = Mathf.Max(0.5f, MazeHalfHeight - MazeGapHalf);
            float topYBase = MazeGapHalf + (ribHeight * 0.5f);
            float bottomYBase = -topYBase;
            float totalWidth = MazeRibSpacing * MazeRibCount;
            float minX = -totalWidth * 0.5f;
            float maxX = totalWidth * 0.5f;
            float pulseEnvelope = (beatPulse * 0.12f) + (barPulse * 0.07f) + (pulseStrength * 0.12f);
            float widthScale = 1f + pulseEnvelope;

            for (int i = 0; i < mazeRibs.Count; i++)
            {
                MazeRib rib = mazeRibs[i];
                float baseX = (i * MazeRibSpacing) + rib.Offset;
                float scrolledX = baseX - (worldScrollPos * MazeRibScrollRatio * speedMultiplier);
                float x = RepeatRange(scrolledX, minX, maxX);
                float bob = Mathf.Sin((songTimeSec * 0.55f * bobMultiplier) + rib.Phase) * 0.06f;
                float topY = topYBase + bob;
                float bottomY = bottomYBase - bob;

                if (rib.Top != null)
                {
                    rib.Top.localPosition = new Vector3(x, topY, -5.9f);
                    rib.Top.localScale = new Vector3(MazeRibWidth * widthScale, ribHeight, 1f);
                }

                if (rib.Bottom != null)
                {
                    rib.Bottom.localPosition = new Vector3(x, bottomY, -5.9f);
                    rib.Bottom.localScale = new Vector3(MazeRibWidth * widthScale, ribHeight, 1f);
                }

                float alpha = Mathf.Clamp01(0.12f + (pulseEnvelope * 0.55f));
                if (rib.TopMaterial != null)
                {
                    RenderMaterialUtils.ApplyColor(rib.TopMaterial, new Color(0.24f, 0.52f, 0.78f, alpha));
                }

                if (rib.BottomMaterial != null)
                {
                    RenderMaterialUtils.ApplyColor(rib.BottomMaterial, new Color(0.20f, 0.44f, 0.66f, alpha * 0.95f));
                }
            }
        }

        private Transform CreateMazeSegment(string name, float y, float height, Color color, int sortingOrder)
        {
            GameObject go = RuntimeSpriteFactory.Create(
                name,
                layerRoot,
                new Vector3(0f, y, -5.9f),
                new Vector3(MazeRibWidth, height, 1f),
                sortingOrder: sortingOrder);
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = RenderMaterialUtils.CreateSolidMaterial(color, true);
            }

            return go.transform;
        }

        private void AddLayer(
            float width,
            float height,
            float z,
            float y,
            Color color,
            float speedRatio,
            float bobAmp,
            float bobFreq,
            float pulseScale,
            float beatAmp,
            float barAmp,
            float accentAmp)
        {
            var layer = new ParallaxLayer
            {
                Width = width,
                Overlap = 0.22f,
                ScrollX = 0d,
                BaseY = y,
                ScrollRatio = Mathf.Max(0.01f, speedRatio),
                BobAmp = bobAmp,
                BobFreq = bobFreq,
                Phase = (layers.Count + 1) * 1.0472f,
                PulseScale = pulseScale,
                BeatAmp = beatAmp,
                BarAmp = barAmp,
                AccentAmp = accentAmp
            };

            int sortingOrder = -30 + layers.Count;
            layer.A = CreateTile($"Layer_{layers.Count}_A", width + layer.Overlap, height, z, color, sortingOrder);
            layer.B = CreateTile($"Layer_{layers.Count}_B", width + layer.Overlap, height, z, color, sortingOrder);
            layers.Add(layer);
        }

        private Transform CreateTile(string name, float width, float height, float z, Color color, int sortingOrder)
        {
            GameObject go = RuntimeSpriteFactory.Create(
                name,
                layerRoot,
                new Vector3(0f, 0f, z),
                new Vector3(width, height, 1f),
                sortingOrder: sortingOrder);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = BuildLayerMaterial(color);
            }

            return go.transform;
        }

        private static Material BuildLayerMaterial(Color color)
        {
            Material material = RenderMaterialUtils.CreateSolidMaterial(color);
            return material ?? new Material(Shader.Find("Sprites/Default"));
        }

        private static double RepeatPositive(double value, float length)
        {
            double safeLength = Math.Max(0.0001d, length);
            double mod = value % safeLength;
            if (mod < 0d)
            {
                mod += safeLength;
            }

            return mod;
        }

        private static float RepeatRange(float value, float min, float max)
        {
            float range = Mathf.Max(0.001f, max - min);
            float shifted = value - min;
            float mod = shifted - (Mathf.Floor(shifted / range) * range);
            return min + mod;
        }
    }
}
