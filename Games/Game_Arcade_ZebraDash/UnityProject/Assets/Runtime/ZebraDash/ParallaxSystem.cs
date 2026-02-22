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
        private const int DustStreakCount = 30;
        private const float DustScrollRatio = 0.86f;
        private const float DustFieldHalfWidth = 23f;
        private const float DustFieldHalfHeight = 4.8f;
        private readonly List<ParallaxLayer> layers = new List<ParallaxLayer>();
        private readonly List<MazeRib> mazeRibs = new List<MazeRib>(MazeRibCount);
        private readonly List<DustStreak> dustStreaks = new List<DustStreak>(DustStreakCount);
        private float pulseStrength;
        private float lastSongTimeSec;
        private float speedMultiplier = 1f;
        private float speedMultiplierTarget = 1f;
        private float bobMultiplier = 1f;
        private float bobMultiplierTarget = 1f;
        private float beatPulse;
        private float barPulse;
        private float subPulseStrength;
        private float beatTickStrength;
        private float barTickStrength;
        private float phrasePulseStrength;
        private bool initialized;
        private bool hasClockSample;
        private Transform layerRoot;
        private int lastSubIndex = int.MinValue;
        private int lastBeatIndex = int.MinValue;
        private int lastBarIndex = int.MinValue;
        private int lastPhraseIndex = int.MinValue;

        public float SpeedPulseMultiplier { get; private set; } = 1f;
        public float EmissivePulseMultiplier { get; private set; } = 1f;
        public float WarpPulseMultiplier { get; private set; } = 1f;

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
            public Transform TopLight;
            public Transform BottomLight;
            public Material TopMaterial;
            public Material BottomMaterial;
            public Material TopLightMaterial;
            public Material BottomLightMaterial;
            public float Phase;
            public float Offset;
        }

        private sealed class DustStreak
        {
            public Transform Visual;
            public Material Material;
            public float Phase;
            public float BaseY;
            public float Length;
            public float Width;
            public float DriftAmp;
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
            BuildDustField();

            initialized = true;
            hasClockSample = false;
            lastSubIndex = int.MinValue;
            lastBeatIndex = int.MinValue;
            lastBarIndex = int.MinValue;
            lastPhraseIndex = int.MinValue;
        }

        public void Tick(float songTimeSec, bool isPlaying, float phaseBeat = 0f, float phaseBar = 0f, float worldScrollPos = 0f, float beatSec = 0.5f)
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

            if (isPlaying)
            {
                TickVisualGrid(songTimeSec, beatSec);
            }

            pulseStrength = Mathf.MoveTowards(pulseStrength, 0f, delta * 2.2f);
            subPulseStrength = Mathf.MoveTowards(subPulseStrength, 0f, delta * 6.4f);
            beatTickStrength = Mathf.MoveTowards(beatTickStrength, 0f, delta * 5.0f);
            barTickStrength = Mathf.MoveTowards(barTickStrength, 0f, delta * 3.5f);
            phrasePulseStrength = Mathf.MoveTowards(phrasePulseStrength, 0f, delta * 2.4f);
            speedMultiplier = Mathf.MoveTowards(speedMultiplier, speedMultiplierTarget, delta * 1.6f);
            bobMultiplier = Mathf.MoveTowards(bobMultiplier, bobMultiplierTarget, delta * 1.6f);
            beatPulse = PulseEnvelope(phaseBeat, 0.12f);
            barPulse = PulseEnvelope(phaseBar, 0.18f);
            float gridPulse = (subPulseStrength * 0.12f)
                + (beatTickStrength * 0.25f)
                + (barTickStrength * 0.38f)
                + (phrasePulseStrength * 0.55f);
            SpeedPulseMultiplier = 1f + (beatPulse * 0.04f) + (barPulse * 0.02f) + (pulseStrength * 0.03f) + (gridPulse * 0.05f);
            EmissivePulseMultiplier = 1f + (beatPulse * 0.32f) + (barPulse * 0.20f) + (pulseStrength * 0.45f) + (gridPulse * 0.55f);
            WarpPulseMultiplier = 1f + (barTickStrength * 0.28f) + (phrasePulseStrength * 0.90f);

            for (int i = 0; i < layers.Count; i++)
            {
                ParallaxLayer layer = layers[i];
                float envelope = 1f
                    + (beatPulse * layer.BeatAmp)
                    + (barPulse * layer.BarAmp)
                    + (pulseStrength * layer.AccentAmp)
                    + (gridPulse * layer.AccentAmp * 0.55f);
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
            TickDustField(songTimeSec, worldScrollPos);
        }

        public void PushAccent(float intensity)
        {
            pulseStrength = Mathf.Max(pulseStrength, Mathf.Lerp(0.16f, 0.55f, Mathf.Clamp01(intensity)));
        }

        public void PushSubTick(float intensity = 0.2f)
        {
            subPulseStrength = Mathf.Max(subPulseStrength, Mathf.Lerp(0.06f, 0.20f, Mathf.Clamp01(intensity)));
        }

        public void PushBeatTick(float intensity = 0.3f)
        {
            beatTickStrength = Mathf.Max(beatTickStrength, Mathf.Lerp(0.12f, 0.34f, Mathf.Clamp01(intensity)));
        }

        public void PushBarPulse(float intensity = 0.5f)
        {
            barTickStrength = Mathf.Max(barTickStrength, Mathf.Lerp(0.18f, 0.50f, Mathf.Clamp01(intensity)));
        }

        public void PushPhraseWarp(float intensity = 0.7f)
        {
            phrasePulseStrength = Mathf.Max(phrasePulseStrength, Mathf.Lerp(0.25f, 0.90f, Mathf.Clamp01(intensity)));
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

        private void TickVisualGrid(float songTimeSec, float beatSec)
        {
            float safeBeatSec = Mathf.Max(0.0001f, beatSec);
            float subSec = Mathf.Max(0.0125f, safeBeatSec * 0.25f);

            int subIndex = Mathf.FloorToInt(songTimeSec / subSec);
            if (subIndex != lastSubIndex)
            {
                lastSubIndex = subIndex;
                PushSubTick(0.22f);
            }

            int beatIndex = Mathf.FloorToInt(songTimeSec / safeBeatSec);
            if (beatIndex != lastBeatIndex)
            {
                lastBeatIndex = beatIndex;
                PushBeatTick(0.30f);
            }

            int barIndex = Mathf.FloorToInt(songTimeSec / (safeBeatSec * 4f));
            if (barIndex != lastBarIndex)
            {
                lastBarIndex = barIndex;
                PushBarPulse(0.56f);
            }

            int phraseIndex = Mathf.FloorToInt(songTimeSec / (safeBeatSec * 8f));
            if (phraseIndex != lastPhraseIndex)
            {
                lastPhraseIndex = phraseIndex;
                PushPhraseWarp(0.72f);
            }
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
                Transform topLight = CreateMazeSegment($"MazeRibTopLight_{i}", topY, 0.38f, new Color(0.36f, 0.92f, 1f, 0.25f), -7);
                Transform bottomLight = CreateMazeSegment($"MazeRibBottomLight_{i}", bottomY, 0.38f, new Color(1f, 0.48f, 0.88f, 0.25f), -7);
                var rib = new MazeRib
                {
                    Top = top,
                    Bottom = bottom,
                    TopLight = topLight,
                    BottomLight = bottomLight,
                    TopMaterial = top != null ? top.GetComponent<Renderer>()?.material : null,
                    BottomMaterial = bottom != null ? bottom.GetComponent<Renderer>()?.material : null,
                    TopLightMaterial = topLight != null ? topLight.GetComponent<Renderer>()?.material : null,
                    BottomLightMaterial = bottomLight != null ? bottomLight.GetComponent<Renderer>()?.material : null,
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
            float laneLightEnvelope = (beatTickStrength * 0.32f) + (barTickStrength * 0.42f) + (phrasePulseStrength * 0.56f);
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

                float lightSweep01 = Mathf.Repeat((songTimeSec * (0.95f + (beatPulse * 0.45f))) + rib.Phase, 1f);
                float lightYOffset = Mathf.Lerp(-ribHeight * 0.45f, ribHeight * 0.45f, lightSweep01);
                float lightWidth = Mathf.Lerp(0.10f, 0.24f, 0.45f + (0.55f * laneLightEnvelope));
                float lightAlpha = Mathf.Clamp01(0.22f + (laneLightEnvelope * 0.65f));
                if (rib.TopLight != null)
                {
                    rib.TopLight.localPosition = new Vector3(x, topY + lightYOffset, -5.7f);
                    rib.TopLight.localScale = new Vector3(lightWidth, 0.34f, 1f);
                }

                if (rib.BottomLight != null)
                {
                    rib.BottomLight.localPosition = new Vector3(x, bottomY - lightYOffset, -5.7f);
                    rib.BottomLight.localScale = new Vector3(lightWidth, 0.34f, 1f);
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

                if (rib.TopLightMaterial != null)
                {
                    RenderMaterialUtils.ApplyColor(rib.TopLightMaterial, new Color(0.36f, 0.92f, 1f, lightAlpha));
                }

                if (rib.BottomLightMaterial != null)
                {
                    RenderMaterialUtils.ApplyColor(rib.BottomLightMaterial, new Color(1f, 0.48f, 0.88f, lightAlpha * 0.95f));
                }
            }
        }

        private void BuildDustField()
        {
            dustStreaks.Clear();
            for (int i = 0; i < DustStreakCount; i++)
            {
                float t = (i + 1f) / (DustStreakCount + 1f);
                float y = Mathf.Lerp(-DustFieldHalfHeight, DustFieldHalfHeight, t);
                float width = Mathf.Lerp(0.05f, 0.10f, Mathf.PingPong((i * 0.17f), 1f));
                float length = Mathf.Lerp(0.35f, 1.35f, Mathf.PingPong((i * 0.31f), 1f));
                Transform streak = CreateMazeSegment(
                    $"Dust_{i}",
                    y,
                    width,
                    new Color(0.70f, 0.95f, 1f, 0.08f),
                    -6);
                if (streak != null)
                {
                    streak.localScale = new Vector3(length, width, 1f);
                }

                dustStreaks.Add(new DustStreak
                {
                    Visual = streak,
                    Material = streak != null ? streak.GetComponent<Renderer>()?.material : null,
                    Phase = i * 0.61f,
                    BaseY = y,
                    Length = length,
                    Width = width,
                    DriftAmp = Mathf.Lerp(0.10f, 0.40f, Mathf.PingPong(i * 0.27f, 1f))
                });
            }
        }

        private void TickDustField(float songTimeSec, float worldScrollPos)
        {
            if (dustStreaks.Count == 0)
            {
                return;
            }

            float minX = -DustFieldHalfWidth;
            float maxX = DustFieldHalfWidth;
            float beatEnvelope = (beatPulse * 0.12f) + (subPulseStrength * 0.18f) + (barTickStrength * 0.22f);
            float phraseEnvelope = phrasePulseStrength * 0.38f;
            for (int i = 0; i < dustStreaks.Count; i++)
            {
                DustStreak streak = dustStreaks[i];
                if (streak.Visual == null)
                {
                    continue;
                }

                float baseX = (i * 1.43f) + (streak.Phase * 1.7f);
                float x = RepeatRange(baseX - (worldScrollPos * DustScrollRatio * SpeedPulseMultiplier), minX, maxX);
                float y = streak.BaseY + Mathf.Sin((songTimeSec * 0.95f) + streak.Phase) * streak.DriftAmp;
                float length = streak.Length * (1f + (beatEnvelope * 0.45f) + (phraseEnvelope * 0.35f));
                streak.Visual.localPosition = new Vector3(x, y, -5.45f);
                streak.Visual.localScale = new Vector3(length, streak.Width, 1f);
                if (streak.Material != null)
                {
                    float alpha = Mathf.Clamp01(0.05f + (beatEnvelope * 0.28f) + (phraseEnvelope * 0.40f));
                    RenderMaterialUtils.ApplyColor(streak.Material, new Color(0.70f, 0.95f, 1f, alpha));
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
