using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ParallaxSystem : MonoBehaviour
    {
        private const int MazeRibCount = 24;
        private const float MazeRibSpacing = 3.4f;
        private const float MazeRibScrollRatio = 0.52f;
        private const int MazeWallColumns = 28;
        private const int MazeWallRows = 6;
        private const float MazeWallColumnSpacing = 1.58f;
        private const float MazeWallScrollRatio = 0.50f;
        private const float MazeWallTileHeight = 0.88f;
        private const float MazeWallTileWidth = 1.60f;
        private const float MazeWallRowOverlap = 0.08f;
        private const float MazeHalfHeight = 5.4f;
        private const float MazeGapHalf = 1.38f;
        private const float MazeRibWidth = 0.20f;
        private const int MazeBlockerColumns = 44;
        private const float MazeBlockerSpacing = 1.40f;
        private const float MazeBlockerScrollRatio = 1.0f;
        private const float MazeBlockerMinGap = 2.20f;
        private const float MazeBlockerMaxGap = 3.10f;
        private const float MazeBlockerTopY = 2.30f;
        private const float MazeBlockerBottomY = -2.30f;
        private const float MazeBlockerEdgeThickness = 0.16f;
        private const int DustStreakCount = 30;
        private const float DustScrollRatio = 0.86f;
        private const float DustFieldHalfWidth = 23f;
        private const float DustFieldHalfHeight = 4.8f;
        private readonly List<ParallaxLayer> layers = new List<ParallaxLayer>();
        private readonly List<MazeRib> mazeRibs = new List<MazeRib>(MazeRibCount);
        private readonly List<MazeWallTile> mazeWallTiles = new List<MazeWallTile>(MazeWallColumns * MazeWallRows);
        private readonly List<MazeBlocker> mazeBlockers = new List<MazeBlocker>(MazeBlockerColumns);
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
        private float motifBlend = 1f;
        private bool motifStateInitialized;
        private bool initialized;
        private bool hasClockSample;
        private Transform layerRoot;
        private Transform mazeCeilingBand;
        private Transform mazeFloorBand;
        private Material mazeCeilingBandMaterial;
        private Material mazeFloorBandMaterial;
        private int lastSubIndex = int.MinValue;
        private int lastBeatIndex = int.MinValue;
        private int lastBarIndex = int.MinValue;
        private int lastPhraseIndex = int.MinValue;
        private int motifSeed;
        private int currentMotifIndex;
        private int targetMotifIndex;
        private float energy01;
        private float tension01;
        private float brightness01 = 0.5f;
        private float currentBeatSec = 0.5f;
        private string currentSectionType = GameplaySectionTypes.Active;
        private float motifAmbient = 1f;
        private float motifHueShift;
        private float motifSaturation = 1f;
        private float motifContrast = 1f;
        private float motifSafeDim = 0.38f;
        private float motifDustRate = 1f;
        private float motifScanline;
        private float motifWarpVignette;
        private float motifPanelChaseSpeed = 1f;
        private float motifCableSwayAmp = 0.4f;
        private float motifRotorSpinRate = 0.2f;
        private float motifRailPulseMul = 1f;
        private SpaceMazeMotifSpec activeMotif;

        public float SpeedPulseMultiplier { get; private set; } = 1f;
        public float EmissivePulseMultiplier { get; private set; } = 1f;
        public float WarpPulseMultiplier { get; private set; } = 1f;
        public float SafeZoneDimMultiplier { get; private set; } = 0.38f;
        public float HazardOutlineMultiplier { get; private set; } = 1.2f;
        public float Energy01 => energy01;
        public float Tension01 => tension01;
        public float Brightness01 => brightness01;
        public int CurrentMotifIndex => currentMotifIndex;
        public string CurrentMotifId => SpaceMazeMotifs.ResolveByIndex(currentMotifIndex).Id;
        public float AmbientIntensity => motifAmbient;
        public float DustRateMultiplier => motifDustRate;
        public float ScanlineIntensity => motifScanline;
        public float RotorSpinRate => motifRotorSpinRate;
        public string CurrentSectionType => currentSectionType;

        private sealed class ParallaxLayer
        {
            public Transform A;
            public Transform B;
            public Material MaterialA;
            public Material MaterialB;
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
            public int LayerIndex;
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

        private sealed class MazeWallTile
        {
            public Transform Top;
            public Transform Bottom;
            public Material TopMaterial;
            public Material BottomMaterial;
            public float Phase;
            public float Offset;
            public int Column;
            public int Row;
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

        private sealed class MazeBlocker
        {
            public Transform Top;
            public Transform Bottom;
            public Transform TopEdge;
            public Transform BottomEdge;
            public Material TopMaterial;
            public Material BottomMaterial;
            public Material TopEdgeMaterial;
            public Material BottomEdgeMaterial;
            public float BaseX;
            public float GapCenter;
            public float GapSize;
            public float Width;
            public float Phase;
        }

        public void Initialize(Transform parentRoot)
        {
            if (initialized)
            {
                return;
            }

            motifSeed = 17;
            currentMotifIndex = 0;
            targetMotifIndex = 0;

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
            BuildMazeBlockers();
            BuildDustField();

            initialized = true;
            hasClockSample = false;
            lastSubIndex = int.MinValue;
            lastBeatIndex = int.MinValue;
            lastBarIndex = int.MinValue;
            lastPhraseIndex = int.MinValue;
            motifBlend = 1f;
            motifStateInitialized = false;
            ApplyMotifBlendImmediate(SpaceMazeMotifs.ResolveByIndex(currentMotifIndex));
        }

        public void Tick(float songTimeSec, bool isPlaying, float phaseBeat = 0f, float phaseBar = 0f, float worldScrollPos = 0f, float beatSec = 0.5f)
        {
            if (!initialized)
            {
                return;
            }

            currentBeatSec = Mathf.Max(0.0001f, beatSec);

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
            else if (!motifStateInitialized)
            {
                ApplyMotifBlendImmediate(SpaceMazeMotifs.ResolveByIndex(currentMotifIndex));
            }

            TickMotifBlend(delta);

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
            float motifBeatAmp = Mathf.Lerp(0.02f, 0.06f, motifRailPulseMul);
            float motifAccentAmp = Mathf.Lerp(0.03f, 0.08f, motifRailPulseMul);
            SpeedPulseMultiplier = 1f + (beatPulse * motifBeatAmp) + (barPulse * 0.02f) + (pulseStrength * motifAccentAmp) + (gridPulse * 0.05f);
            EmissivePulseMultiplier = 1f + (beatPulse * (0.20f + (0.18f * motifRailPulseMul))) + (barPulse * 0.20f) + (pulseStrength * 0.45f) + (gridPulse * 0.55f);
            WarpPulseMultiplier = 1f + (barTickStrength * 0.28f) + (phrasePulseStrength * (0.55f + motifWarpVignette));
            SafeZoneDimMultiplier = Mathf.Clamp(motifSafeDim, 0.25f, 0.62f);

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
                if (layer.MaterialA != null)
                {
                    Color gradedA = BuildLayerColor(layer.LayerIndex, true);
                    gradedA.a = 1f;
                    RenderMaterialUtils.ApplyColor(layer.MaterialA, gradedA);
                }

                if (layer.MaterialB != null)
                {
                    Color gradedB = BuildLayerColor(layer.LayerIndex, false);
                    gradedB.a = 1f;
                    RenderMaterialUtils.ApplyColor(layer.MaterialB, gradedB);
                }
            }

            TickSpaceMaze(songTimeSec, worldScrollPos);
            TickMazeBlockers(songTimeSec, worldScrollPos);
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

        public void SetOrchestrationState(
            string trackId,
            int patternSeed,
            string sectionType,
            float songTimeSec,
            float beatSec,
            float energy,
            float tension,
            float brightness)
        {
            currentSectionType = sectionType ?? GameplaySectionTypes.Active;
            energy01 = Mathf.Clamp01(energy);
            tension01 = Mathf.Clamp01(tension);
            brightness01 = Mathf.Clamp01(brightness);

            unchecked
            {
                motifSeed = 17;
                motifSeed = (motifSeed * 31) + (patternSeed * 131);
                motifSeed = (motifSeed * 31) + (trackId != null ? trackId.GetHashCode() : 0);
            }

            int phraseIndex = Mathf.FloorToInt(songTimeSec / Mathf.Max(0.001f, beatSec * 8f));
            int selected = ResolveMotifIndex(phraseIndex, sectionType, energy01, tension01, brightness01);
            if (!motifStateInitialized)
            {
                currentMotifIndex = selected;
                targetMotifIndex = selected;
                motifBlend = 1f;
                ApplyMotifBlendImmediate(SpaceMazeMotifs.ResolveByIndex(selected));
                return;
            }

            if (selected != targetMotifIndex)
            {
                targetMotifIndex = selected;
                motifBlend = 0f;
            }
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

        private void TickMotifBlend(float deltaSec)
        {
            if (SpaceMazeMotifs.Count <= 0)
            {
                return;
            }

            if (!motifStateInitialized)
            {
                currentMotifIndex = Mathf.Clamp(currentMotifIndex, 0, SpaceMazeMotifs.Count - 1);
                targetMotifIndex = currentMotifIndex;
                motifBlend = 1f;
                ApplyMotifBlendImmediate(SpaceMazeMotifs.ResolveByIndex(currentMotifIndex));
                motifStateInitialized = true;
                return;
            }

            if (targetMotifIndex == currentMotifIndex)
            {
                motifBlend = 1f;
                activeMotif = SpaceMazeMotifs.ResolveByIndex(currentMotifIndex);
                return;
            }

            motifBlend = Mathf.MoveTowards(motifBlend, 1f, Mathf.Max(0.001f, deltaSec) * 0.85f);
            SpaceMazeMotifSpec from = SpaceMazeMotifs.ResolveByIndex(currentMotifIndex);
            SpaceMazeMotifSpec to = SpaceMazeMotifs.ResolveByIndex(targetMotifIndex);
            SpaceMazeMotifSpec blended = BlendMotif(from, to, motifBlend);
            activeMotif = blended;
            ApplyMotifKnobs(blended);

            if (motifBlend >= 0.999f)
            {
                currentMotifIndex = targetMotifIndex;
                motifBlend = 1f;
                ApplyMotifBlendImmediate(to);
            }
        }

        private void ApplyMotifBlendImmediate(SpaceMazeMotifSpec motif)
        {
            activeMotif = motif;
            ApplyMotifKnobs(motif);
            ApplyArtworkForMotif(motif);
            motifStateInitialized = true;
        }

        private void ApplyMotifKnobs(SpaceMazeMotifSpec motif)
        {
            motifAmbient = Mathf.Clamp(motif.AmbientIntensity + 0.30f, 0.95f, 1.55f);
            motifHueShift = motif.HueShiftDeg;
            motifSaturation = Mathf.Clamp(motif.SaturationMul * 1.10f, 0.85f, 1.60f);
            motifContrast = Mathf.Clamp(motif.ContrastMul * 1.08f, 0.95f, 1.60f);
            motifSafeDim = Mathf.Clamp(motif.SafeZoneDimMul * 0.68f, 0.18f, 0.42f);
            motifDustRate = Mathf.Clamp(motif.DustRateMul, 0.50f, 3.00f);
            motifScanline = Mathf.Clamp(motif.ScanlineIntensity, 0f, 0.04f);
            motifWarpVignette = Mathf.Clamp(motif.WarpVignette, 0f, 0.40f);
            motifPanelChaseSpeed = Mathf.Clamp(motif.PanelLightChaseSpeed, 0.50f, 2.20f);
            motifCableSwayAmp = Mathf.Clamp(motif.CableSwayAmp, 0f, 1.0f);
            motifRotorSpinRate = Mathf.Clamp(motif.RotorSpinRate, 0f, 1.4f);
            motifRailPulseMul = Mathf.Clamp(motif.RailPulseMul, 0.70f, 1.35f);
            HazardOutlineMultiplier = Mathf.Clamp(motif.HazardOutlineMul * 1.10f, 1.10f, 2.00f);
        }

        private void ApplyArtworkForMotif(SpaceMazeMotifSpec motif)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                ParallaxLayer layer = layers[i];
                if (layer == null)
                {
                    continue;
                }

                Sprite sprite = SpaceMazeArtCatalog.ResolveLayerSprite(layer.LayerIndex, motif.Id, motifSeed + (i * 37));
                if (sprite == null)
                {
                    continue;
                }

                if (layer.A != null)
                {
                    SpriteRenderer renderer = layer.A.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        renderer.sprite = sprite;
                    }
                }

                if (layer.B != null)
                {
                    SpriteRenderer renderer = layer.B.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        renderer.sprite = sprite;
                    }
                }
            }

            for (int i = 0; i < mazeWallTiles.Count; i++)
            {
                MazeWallTile tile = mazeWallTiles[i];
                int segmentIndex = (tile.Row * MazeWallColumns) + tile.Column;
                Sprite topTile = SpaceMazeArtCatalog.ResolveMazeWallTile(motif.Id, segmentIndex, true, motifSeed + (segmentIndex * 23));
                Sprite bottomTile = SpaceMazeArtCatalog.ResolveMazeWallTile(motif.Id, segmentIndex, false, motifSeed + (segmentIndex * 29));
                if (tile.Top != null)
                {
                    SpriteRenderer renderer = tile.Top.GetComponent<SpriteRenderer>();
                    if (renderer != null && topTile != null)
                    {
                        renderer.sprite = topTile;
                    }
                }

                if (tile.Bottom != null)
                {
                    SpriteRenderer renderer = tile.Bottom.GetComponent<SpriteRenderer>();
                    if (renderer != null && bottomTile != null)
                    {
                        renderer.sprite = bottomTile;
                    }
                }
            }

            for (int i = 0; i < mazeRibs.Count; i++)
            {
                MazeRib rib = mazeRibs[i];
                Sprite ribTile = SpaceMazeArtCatalog.ResolveRibSprite(motif.Id, i, motifSeed + (i * 17));
                if (rib.Top != null)
                {
                    SpriteRenderer renderer = rib.Top.GetComponent<SpriteRenderer>();
                    if (renderer != null && ribTile != null)
                    {
                        renderer.sprite = ribTile;
                    }
                }

                if (rib.Bottom != null)
                {
                    SpriteRenderer renderer = rib.Bottom.GetComponent<SpriteRenderer>();
                    if (renderer != null && ribTile != null)
                    {
                        renderer.sprite = ribTile;
                    }
                }
            }
        }

        private static SpaceMazeMotifSpec BlendMotif(in SpaceMazeMotifSpec a, in SpaceMazeMotifSpec b, float t)
        {
            float blend = Mathf.Clamp01(t);
            return new SpaceMazeMotifSpec(
                id: blend >= 0.999f ? b.Id : a.Id,
                layer0: Color.Lerp(a.Layer0, b.Layer0, blend),
                layer1: Color.Lerp(a.Layer1, b.Layer1, blend),
                layer2: Color.Lerp(a.Layer2, b.Layer2, blend),
                layer3: Color.Lerp(a.Layer3, b.Layer3, blend),
                ribA: Color.Lerp(a.RibA, b.RibA, blend),
                ribB: Color.Lerp(a.RibB, b.RibB, blend),
                lightA: Color.Lerp(a.LightA, b.LightA, blend),
                lightB: Color.Lerp(a.LightB, b.LightB, blend),
                dust: Color.Lerp(a.Dust, b.Dust, blend),
                ambientIntensity: Mathf.Lerp(a.AmbientIntensity, b.AmbientIntensity, blend),
                hueShiftDeg: Mathf.Lerp(a.HueShiftDeg, b.HueShiftDeg, blend),
                saturationMul: Mathf.Lerp(a.SaturationMul, b.SaturationMul, blend),
                contrastMul: Mathf.Lerp(a.ContrastMul, b.ContrastMul, blend),
                safeZoneDimMul: Mathf.Lerp(a.SafeZoneDimMul, b.SafeZoneDimMul, blend),
                dustRateMul: Mathf.Lerp(a.DustRateMul, b.DustRateMul, blend),
                scanlineIntensity: Mathf.Lerp(a.ScanlineIntensity, b.ScanlineIntensity, blend),
                warpVignette: Mathf.Lerp(a.WarpVignette, b.WarpVignette, blend),
                panelLightChaseSpeed: Mathf.Lerp(a.PanelLightChaseSpeed, b.PanelLightChaseSpeed, blend),
                cableSwayAmp: Mathf.Lerp(a.CableSwayAmp, b.CableSwayAmp, blend),
                rotorSpinRate: Mathf.Lerp(a.RotorSpinRate, b.RotorSpinRate, blend),
                hazardOutlineMul: Mathf.Lerp(a.HazardOutlineMul, b.HazardOutlineMul, blend),
                railPulseMul: Mathf.Lerp(a.RailPulseMul, b.RailPulseMul, blend));
        }

        private Color BuildLayerColor(int layerIndex, bool primaryTile)
        {
            SpaceMazeMotifSpec motif = activeMotif;
            Color baseColor = layerIndex switch
            {
                0 => motif.Layer0,
                1 => motif.Layer1,
                2 => motif.Layer2,
                _ => motif.Layer3
            };

            if (!primaryTile)
            {
                baseColor = Color.Lerp(baseColor, Color.black, 0.06f);
            }

            return ApplyColorGrade(baseColor, motifAmbient, motifHueShift, motifSaturation, motifContrast);
        }

        private static Color ApplyColorGrade(Color source, float ambient, float hueShiftDeg, float saturationMul, float contrastMul)
        {
            Color.RGBToHSV(source, out float hue, out float sat, out float val);
            hue = Mathf.Repeat(hue + (hueShiftDeg / 360f), 1f);
            sat = Mathf.Clamp01(sat * saturationMul);
            val = Mathf.Clamp01(val * ambient);
            Color graded = Color.HSVToRGB(hue, sat, val);
            graded.r = Mathf.Clamp01(((graded.r - 0.5f) * contrastMul) + 0.5f);
            graded.g = Mathf.Clamp01(((graded.g - 0.5f) * contrastMul) + 0.5f);
            graded.b = Mathf.Clamp01(((graded.b - 0.5f) * contrastMul) + 0.5f);
            graded.a = source.a;
            return graded;
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

        private int ResolveMotifIndex(int phraseIndex, string sectionType, float energy, float tension, float brightness)
        {
            int[] candidates;
            if (string.Equals(sectionType, GameplaySectionTypes.Rest, StringComparison.OrdinalIgnoreCase))
            {
                candidates = new[] { 5, 8, 9, 4 };
            }
            else if (tension >= 0.72f || string.Equals(sectionType, GameplaySectionTypes.Drop, StringComparison.OrdinalIgnoreCase))
            {
                candidates = new[] { 2, 3, 6, 7, 1 };
            }
            else if (brightness <= 0.35f)
            {
                candidates = new[] { 5, 8, 9, 0 };
            }
            else if (energy >= 0.62f)
            {
                candidates = new[] { 0, 1, 4, 6, 7 };
            }
            else
            {
                candidates = new[] { 0, 1, 4, 8, 9 };
            }

            int hash;
            unchecked
            {
                hash = motifSeed;
                hash = (hash * 397) ^ phraseIndex;
                hash = (hash * 397) ^ Mathf.RoundToInt(energy * 100f);
                hash = (hash * 397) ^ Mathf.RoundToInt(tension * 100f);
                hash = (hash * 397) ^ Mathf.RoundToInt(brightness * 100f);
            }

            int idx = Mathf.Abs(hash) % Mathf.Max(1, candidates.Length);
            return Mathf.Clamp(candidates[idx], 0, SpaceMazeMotifs.Count - 1);
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
            mazeWallTiles.Clear();
            string motifId = SpaceMazeMotifs.ResolveByIndex(currentMotifIndex).Id;
            float topBaseY = MazeGapHalf + (MazeWallTileHeight * 0.5f);
            for (int row = 0; row < MazeWallRows; row++)
            {
                float rowY = topBaseY + (row * (MazeWallTileHeight - MazeWallRowOverlap));
                for (int col = 0; col < MazeWallColumns; col++)
                {
                    int segmentIndex = (row * MazeWallColumns) + col;
                    Sprite topWallTile = SpaceMazeArtCatalog.ResolveMazeWallTile(motifId, segmentIndex, true, motifSeed + (segmentIndex * 19));
                    Sprite bottomWallTile = SpaceMazeArtCatalog.ResolveMazeWallTile(motifId, segmentIndex, false, motifSeed + (segmentIndex * 23));
                    Transform top = CreateMazeSegment(
                        $"MazeWallTop_r{row}_c{col}",
                        rowY,
                        MazeWallTileWidth,
                        MazeWallTileHeight,
                        new Color(0.18f, 0.34f, 0.48f, 0.62f),
                        -10,
                        topWallTile);
                    Transform bottom = CreateMazeSegment(
                        $"MazeWallBottom_r{row}_c{col}",
                        -rowY,
                        MazeWallTileWidth,
                        MazeWallTileHeight,
                        new Color(0.16f, 0.30f, 0.42f, 0.58f),
                        -10,
                        bottomWallTile);

                    mazeWallTiles.Add(new MazeWallTile
                    {
                        Top = top,
                        Bottom = bottom,
                        TopMaterial = top != null ? top.GetComponent<Renderer>()?.material : null,
                        BottomMaterial = bottom != null ? bottom.GetComponent<Renderer>()?.material : null,
                        Phase = (row * 0.27f) + (col * 0.21f),
                        Offset = ((col % 3) - 1f) * 0.08f,
                        Column = col,
                        Row = row
                    });
                }
            }

            float ribHeight = Mathf.Max(0.5f, MazeHalfHeight - MazeGapHalf);
            float topY = MazeGapHalf + (ribHeight * 0.5f);
            float bottomY = -topY;
            for (int i = 0; i < MazeRibCount; i++)
            {
                Color topColor = new Color(0.24f, 0.52f, 0.78f, 0.28f);
                Color bottomColor = new Color(0.20f, 0.44f, 0.66f, 0.26f);
                Sprite ribSprite = SpaceMazeArtCatalog.ResolveRibSprite(motifId, i, motifSeed + (i * 13));
                Sprite lightSprite = SpaceMazeArtCatalog.ResolveTelegraphSprite("glyph_ring", GameplayArchetypes.RestPulse, motifSeed + (i * 7));
                Transform top = CreateMazeSegment($"MazeRibTop_{i}", topY, ribHeight, topColor, -9, ribSprite);
                Transform bottom = CreateMazeSegment($"MazeRibBottom_{i}", bottomY, ribHeight, bottomColor, -9, ribSprite);
                Transform topLight = CreateMazeSegment($"MazeRibTopLight_{i}", topY, 0.38f, new Color(0.36f, 0.92f, 1f, 0.25f), -7, lightSprite);
                Transform bottomLight = CreateMazeSegment($"MazeRibBottomLight_{i}", bottomY, 0.38f, new Color(1f, 0.48f, 0.88f, 0.25f), -7, lightSprite);
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

            mazeCeilingBand = CreateMazeSegment(
                "MazeCeilingBand",
                3.15f,
                60f,
                1.40f,
                new Color(0.02f, 0.04f, 0.08f, 0.90f),
                12,
                null);
            mazeFloorBand = CreateMazeSegment(
                "MazeFloorBand",
                -3.15f,
                60f,
                1.40f,
                new Color(0.02f, 0.04f, 0.08f, 0.90f),
                12,
                null);
            mazeCeilingBandMaterial = mazeCeilingBand != null ? mazeCeilingBand.GetComponent<Renderer>()?.material : null;
            mazeFloorBandMaterial = mazeFloorBand != null ? mazeFloorBand.GetComponent<Renderer>()?.material : null;
        }

        private void TickSpaceMaze(float songTimeSec, float worldScrollPos)
        {
            if (mazeRibs.Count == 0)
            {
                return;
            }

            SpaceMazeMotifSpec motif = activeMotif;
            float ribHeight = Mathf.Max(0.5f, MazeHalfHeight - MazeGapHalf);
            float topYBase = MazeGapHalf + (ribHeight * 0.5f);
            float bottomYBase = -topYBase;
            float totalWidth = MazeRibSpacing * MazeRibCount;
            float minX = -totalWidth * 0.5f;
            float maxX = totalWidth * 0.5f;
            float pulseEnvelope = (beatPulse * 0.12f * motifRailPulseMul) + (barPulse * 0.07f) + (pulseStrength * 0.12f);
            float laneLightEnvelope = (beatTickStrength * 0.32f) + (barTickStrength * 0.42f) + (phrasePulseStrength * 0.56f);
            float widthScale = 1f + pulseEnvelope;
            float cableSway = Mathf.Lerp(0.02f, 0.11f, motifCableSwayAmp);
            float chaseSpeed = Mathf.Lerp(0.7f, 1.9f, (motifPanelChaseSpeed - 0.5f) / 1.7f);
            float rotorPulse = Mathf.Lerp(0f, 0.12f, motifRotorSpinRate);
            Color ribA = ApplyColorGrade(motif.RibA, motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color ribB = ApplyColorGrade(motif.RibB, motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color lightA = ApplyColorGrade(motif.LightA, motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color lightB = ApplyColorGrade(motif.LightB, motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color wallTopColor = ApplyColorGrade(Color.Lerp(motif.Layer3, motif.RibA, 0.56f), motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color wallBottomColor = ApplyColorGrade(Color.Lerp(motif.Layer2, motif.RibB, 0.54f), motifAmbient, motifHueShift, motifSaturation, motifContrast);

            if (mazeWallTiles.Count > 0)
            {
                float wallTotalWidth = MazeWallColumnSpacing * MazeWallColumns;
                float wallMinX = -wallTotalWidth * 0.5f;
                float wallMaxX = wallTotalWidth * 0.5f;
                float wallPulse = 1f + (pulseEnvelope * 0.18f) + (barPulse * 0.06f);
                float topBaseY = MazeGapHalf + (MazeWallTileHeight * 0.5f);
                for (int i = 0; i < mazeWallTiles.Count; i++)
                {
                    MazeWallTile tile = mazeWallTiles[i];
                    float row01 = MazeWallRows <= 1 ? 0f : (tile.Row / (float)(MazeWallRows - 1));
                    float baseX = (tile.Column * MazeWallColumnSpacing) + tile.Offset;
                    float x = RepeatRange(baseX - (worldScrollPos * MazeWallScrollRatio * speedMultiplier), wallMinX, wallMaxX);
                    float bob = Mathf.Sin((songTimeSec * 0.42f * bobMultiplier) + tile.Phase) * (0.02f + (motifCableSwayAmp * 0.01f));
                    float topY = topBaseY + (tile.Row * (MazeWallTileHeight - MazeWallRowOverlap));
                    float heightScale = 1f + (barPulse * 0.05f);
                    if (tile.Top != null)
                    {
                        tile.Top.localPosition = new Vector3(x, topY + bob, -6.05f);
                        tile.Top.localScale = new Vector3(MazeWallTileWidth * wallPulse, MazeWallTileHeight * heightScale, 1f);
                    }

                    if (tile.Bottom != null)
                    {
                        tile.Bottom.localPosition = new Vector3(x, -topY - bob, -6.05f);
                        tile.Bottom.localScale = new Vector3(MazeWallTileWidth * wallPulse, MazeWallTileHeight * heightScale, 1f);
                    }

                    if (tile.TopMaterial != null)
                    {
                        Color c = wallTopColor;
                        c.a = Mathf.Clamp01(0.66f + ((1f - row01) * 0.16f) + (pulseEnvelope * 0.24f));
                        RenderMaterialUtils.ApplyColor(tile.TopMaterial, c);
                    }

                    if (tile.BottomMaterial != null)
                    {
                        Color c = wallBottomColor;
                        c.a = Mathf.Clamp01(0.64f + ((1f - row01) * 0.16f) + (pulseEnvelope * 0.22f));
                        RenderMaterialUtils.ApplyColor(tile.BottomMaterial, c);
                    }
                }
            }

            if (mazeCeilingBandMaterial != null)
            {
                Color c = ApplyColorGrade(Color.Lerp(motif.Layer0, Color.black, 0.58f), motifAmbient, motifHueShift, motifSaturation, motifContrast);
                c.a = Mathf.Clamp01(0.82f + (barPulse * 0.06f));
                RenderMaterialUtils.ApplyColor(mazeCeilingBandMaterial, c);
            }

            if (mazeFloorBandMaterial != null)
            {
                Color c = ApplyColorGrade(Color.Lerp(motif.Layer0, Color.black, 0.58f), motifAmbient, motifHueShift, motifSaturation, motifContrast);
                c.a = Mathf.Clamp01(0.82f + (barPulse * 0.06f));
                RenderMaterialUtils.ApplyColor(mazeFloorBandMaterial, c);
            }

            for (int i = 0; i < mazeRibs.Count; i++)
            {
                MazeRib rib = mazeRibs[i];
                float baseX = (i * MazeRibSpacing) + rib.Offset;
                float scrolledX = baseX - (worldScrollPos * MazeRibScrollRatio * speedMultiplier);
                float x = RepeatRange(scrolledX, minX, maxX);
                float bob = Mathf.Sin((songTimeSec * 0.55f * bobMultiplier * chaseSpeed) + rib.Phase) * (0.05f + cableSway);
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

                float lightSweep01 = Mathf.Repeat((songTimeSec * ((0.95f + (beatPulse * 0.45f)) * chaseSpeed)) + rib.Phase, 1f);
                float lightYOffset = Mathf.Lerp(-ribHeight * 0.45f, ribHeight * 0.45f, lightSweep01);
                float lightWidth = Mathf.Lerp(0.10f, 0.24f, 0.45f + (0.55f * laneLightEnvelope));
                float lightAlpha = Mathf.Clamp01(0.20f + (laneLightEnvelope * 0.65f) + rotorPulse);
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

                float alpha = Mathf.Clamp01(0.24f + (pulseEnvelope * 0.62f));
                if (rib.TopMaterial != null)
                {
                    ribA.a = alpha;
                    RenderMaterialUtils.ApplyColor(rib.TopMaterial, ribA);
                }

                if (rib.BottomMaterial != null)
                {
                    ribB.a = alpha * 0.95f;
                    RenderMaterialUtils.ApplyColor(rib.BottomMaterial, ribB);
                }

                if (rib.TopLightMaterial != null)
                {
                    lightA.a = lightAlpha;
                    RenderMaterialUtils.ApplyColor(rib.TopLightMaterial, lightA);
                }

                if (rib.BottomLightMaterial != null)
                {
                    lightB.a = lightAlpha * 0.95f;
                    RenderMaterialUtils.ApplyColor(rib.BottomLightMaterial, lightB);
                }
            }
        }

        private void BuildMazeBlockers()
        {
            mazeBlockers.Clear();
            string motifId = SpaceMazeMotifs.ResolveByIndex(currentMotifIndex).Id;
            float previousCenter = 0f;
            for (int i = 0; i < MazeBlockerColumns; i++)
            {
                float phase = i * 0.37f;
                float noise = Mathf.Sin((i * 0.73f) + (motifSeed * 0.021f));
                float targetCenter = Mathf.Clamp(noise * 0.54f, -0.54f, 0.54f);
                float maxStep = 0.08f;
                float step = Mathf.Clamp(targetCenter - previousCenter, -maxStep, maxStep);
                float gapCenter = Mathf.Clamp(previousCenter + step, -0.64f, 0.64f);
                if (i < 8)
                {
                    gapCenter *= 0.10f;
                }

                float gapNoise = Mathf.Sin((i * 0.59f) + (motifSeed * 0.017f));
                float gapSize = Mathf.Clamp(Mathf.Lerp(2.55f, 3.15f, (gapNoise * 0.5f) + 0.5f), MazeBlockerMinGap, MazeBlockerMaxGap);
                if (i < 8)
                {
                    gapSize = Mathf.Min(MazeBlockerMaxGap, gapSize + 0.58f);
                }

                float width = Mathf.Lerp(1.55f, 2.35f, Mathf.PingPong((i * 0.21f) + 0.3f, 1f));
                previousCenter = gapCenter;

                Sprite topSprite = SpaceMazeArtCatalog.ResolveMazeWallTile(motifId, i * 2, true, motifSeed + (i * 31));
                Sprite bottomSprite = SpaceMazeArtCatalog.ResolveMazeWallTile(motifId, (i * 2) + 1, false, motifSeed + (i * 37));
                Transform top = CreateMazeSegment(
                    $"MazeBlockerTop_{i}",
                    0f,
                    width,
                    1f,
                    new Color(0.18f, 0.34f, 0.48f, 0.66f),
                    9,
                    topSprite);
                Transform bottom = CreateMazeSegment(
                    $"MazeBlockerBottom_{i}",
                    0f,
                    width,
                    1f,
                    new Color(0.16f, 0.30f, 0.44f, 0.66f),
                    9,
                    bottomSprite);
                Sprite topEdgeSprite = SpaceMazeArtCatalog.ResolveTelegraphSprite("floor", GameplayArchetypes.RisingWall, motifSeed + (i * 41));
                Sprite bottomEdgeSprite = SpaceMazeArtCatalog.ResolveTelegraphSprite("floor", GameplayArchetypes.HoldLaneLock, motifSeed + (i * 47));
                Transform topEdge = CreateMazeSegment(
                    $"MazeBlockerTopEdge_{i}",
                    0f,
                    width,
                    MazeBlockerEdgeThickness,
                    new Color(1f, 0.86f, 0.38f, 0.78f),
                    11,
                    topEdgeSprite);
                Transform bottomEdge = CreateMazeSegment(
                    $"MazeBlockerBottomEdge_{i}",
                    0f,
                    width,
                    MazeBlockerEdgeThickness,
                    new Color(0.26f, 0.94f, 1f, 0.78f),
                    11,
                    bottomEdgeSprite);

                mazeBlockers.Add(new MazeBlocker
                {
                    Top = top,
                    Bottom = bottom,
                    TopEdge = topEdge,
                    BottomEdge = bottomEdge,
                    TopMaterial = top != null ? top.GetComponent<Renderer>()?.material : null,
                    BottomMaterial = bottom != null ? bottom.GetComponent<Renderer>()?.material : null,
                    TopEdgeMaterial = topEdge != null ? topEdge.GetComponent<Renderer>()?.material : null,
                    BottomEdgeMaterial = bottomEdge != null ? bottomEdge.GetComponent<Renderer>()?.material : null,
                    BaseX = i * MazeBlockerSpacing,
                    GapCenter = gapCenter,
                    GapSize = gapSize,
                    Width = width,
                    Phase = phase
                });
            }
        }

        private void TickMazeBlockers(float songTimeSec, float worldScrollPos)
        {
            if (mazeBlockers.Count == 0)
            {
                return;
            }

            SpaceMazeMotifSpec motif = activeMotif;
            float totalWidth = MazeBlockerColumns * MazeBlockerSpacing;
            float minX = -totalWidth * 0.5f;
            float maxX = totalWidth * 0.5f;
            float pulse = (beatPulse * 0.08f) + (barPulse * 0.06f) + (phrasePulseStrength * 0.10f);
            Color topColor = ApplyColorGrade(Color.Lerp(motif.Layer2, motif.RibA, 0.50f), motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color bottomColor = ApplyColorGrade(Color.Lerp(motif.Layer1, motif.RibB, 0.52f), motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color topEdgeColor = ApplyColorGrade(Color.Lerp(motif.LightA, motif.RibA, 0.42f), motifAmbient, motifHueShift, motifSaturation, motifContrast);
            Color bottomEdgeColor = ApplyColorGrade(Color.Lerp(motif.LightB, motif.RibB, 0.38f), motifAmbient, motifHueShift, motifSaturation, motifContrast);

            for (int i = 0; i < mazeBlockers.Count; i++)
            {
                MazeBlocker blocker = mazeBlockers[i];
                float x = RepeatRange(blocker.BaseX - (worldScrollPos * MazeBlockerScrollRatio * speedMultiplier), minX, maxX);
                EvaluateBlockerState(blocker, songTimeSec, pulse, out float dynamicCenter, out float dynamicGap, out float width);

                float gapTop = dynamicCenter + (dynamicGap * 0.5f);
                float gapBottom = dynamicCenter - (dynamicGap * 0.5f);
                float topHeight = Mathf.Max(0.18f, MazeBlockerTopY - gapTop);
                float bottomHeight = Mathf.Max(0.18f, gapBottom - MazeBlockerBottomY);
                float topY = MazeBlockerTopY - (topHeight * 0.5f);
                float bottomY = MazeBlockerBottomY + (bottomHeight * 0.5f);

                if (blocker.Top != null)
                {
                    blocker.Top.localPosition = new Vector3(x, topY, -1.32f);
                    blocker.Top.localScale = new Vector3(width, topHeight, 1f);
                }

                if (blocker.Bottom != null)
                {
                    blocker.Bottom.localPosition = new Vector3(x, bottomY, -1.32f);
                    blocker.Bottom.localScale = new Vector3(width, bottomHeight, 1f);
                }

                float edgeWidth = Mathf.Max(0.70f, width * (1.00f + (pulse * 0.18f)));
                float edgeHeight = MazeBlockerEdgeThickness * (1.00f + (barPulse * 0.24f));
                if (blocker.TopEdge != null)
                {
                    blocker.TopEdge.localPosition = new Vector3(x, gapTop + (edgeHeight * 0.42f), -1.20f);
                    blocker.TopEdge.localScale = new Vector3(edgeWidth, edgeHeight, 1f);
                }

                if (blocker.BottomEdge != null)
                {
                    blocker.BottomEdge.localPosition = new Vector3(x, gapBottom - (edgeHeight * 0.42f), -1.20f);
                    blocker.BottomEdge.localScale = new Vector3(edgeWidth, edgeHeight, 1f);
                }

                if (blocker.TopMaterial != null)
                {
                    Color c = topColor;
                    c.a = Mathf.Clamp01(0.74f + (pulse * 0.30f));
                    RenderMaterialUtils.ApplyColor(blocker.TopMaterial, c);
                }

                if (blocker.BottomMaterial != null)
                {
                    Color c = bottomColor;
                    c.a = Mathf.Clamp01(0.74f + (pulse * 0.30f));
                    RenderMaterialUtils.ApplyColor(blocker.BottomMaterial, c);
                }

                if (blocker.TopEdgeMaterial != null)
                {
                    Color c = topEdgeColor;
                    c.a = Mathf.Clamp01(0.74f + (pulse * 0.28f) + (barPulse * 0.20f));
                    RenderMaterialUtils.ApplyColor(blocker.TopEdgeMaterial, c);
                }

                if (blocker.BottomEdgeMaterial != null)
                {
                    Color c = bottomEdgeColor;
                    c.a = Mathf.Clamp01(0.74f + (pulse * 0.28f) + (barPulse * 0.20f));
                    RenderMaterialUtils.ApplyColor(blocker.BottomEdgeMaterial, c);
                }
            }
        }

        private void EvaluateBlockerState(
            MazeBlocker blocker,
            float songTimeSec,
            float pulse,
            out float center,
            out float gap,
            out float width)
        {
            float widthPulse = 1f + (pulse * 0.15f);
            width = blocker.Width * widthPulse;

            float wobble = Mathf.Sin((songTimeSec * 0.42f) + blocker.Phase) * 0.03f;
            float beatPhase = Mathf.Repeat(songTimeSec, Mathf.Max(0.0001f, currentBeatSec)) / Mathf.Max(0.0001f, currentBeatSec);
            float rhythmShift = Mathf.Sin((beatPhase * Mathf.PI * 2f) + (blocker.Phase * 0.35f)) * Mathf.Lerp(0.010f, 0.035f, tension01);
            center = blocker.GapCenter + wobble + rhythmShift;
            center = Mathf.Clamp(center, -0.64f, 0.64f);

            float dynamicGap = blocker.GapSize + (Mathf.Sin((songTimeSec * 0.65f) + blocker.Phase) * 0.07f);
            dynamicGap += Mathf.Lerp(0.22f, 0.02f, tension01);
            dynamicGap += Mathf.Lerp(0.00f, 0.05f, energy01);
            gap = Mathf.Clamp(dynamicGap, MazeBlockerMinGap, MazeBlockerMaxGap);
        }

        public bool TrySampleMazeGap(
            float worldX,
            float songTimeSec,
            float worldScrollPos,
            out float gapBottom,
            out float gapTop,
            out float blockerWidth)
        {
            gapBottom = float.NegativeInfinity;
            gapTop = float.PositiveInfinity;
            blockerWidth = 0f;
            if (mazeBlockers.Count == 0)
            {
                return false;
            }

            float totalWidth = MazeBlockerColumns * MazeBlockerSpacing;
            float minX = -totalWidth * 0.5f;
            float maxX = totalWidth * 0.5f;
            bool foundA = false;
            bool foundB = false;
            float bestDistA = float.MaxValue;
            float bestDistB = float.MaxValue;
            float gapBottomA = 0f;
            float gapTopA = 0f;
            float widthA = 0f;
            float gapBottomB = 0f;
            float gapTopB = 0f;
            float widthB = 0f;

            for (int i = 0; i < mazeBlockers.Count; i++)
            {
                MazeBlocker blocker = mazeBlockers[i];
                float x = RepeatRange(blocker.BaseX - (worldScrollPos * MazeBlockerScrollRatio * speedMultiplier), minX, maxX);
                float pulse = (beatPulse * 0.08f) + (barPulse * 0.06f) + (phrasePulseStrength * 0.10f);
                EvaluateBlockerState(blocker, songTimeSec, pulse, out float dynamicCenter, out float dynamicGap, out float width);
                float distance = Mathf.Abs(worldX - x);
                float localGapTop = dynamicCenter + (dynamicGap * 0.5f);
                float localGapBottom = dynamicCenter - (dynamicGap * 0.5f);

                if (!foundA || distance < bestDistA)
                {
                    foundB = foundA;
                    bestDistB = bestDistA;
                    gapBottomB = gapBottomA;
                    gapTopB = gapTopA;
                    widthB = widthA;

                    foundA = true;
                    bestDistA = distance;
                    gapBottomA = localGapBottom;
                    gapTopA = localGapTop;
                    widthA = width;
                    continue;
                }

                if (!foundB || distance < bestDistB)
                {
                    foundB = true;
                    bestDistB = distance;
                    gapBottomB = localGapBottom;
                    gapTopB = localGapTop;
                    widthB = width;
                }
            }

            if (!foundA)
            {
                return false;
            }

            if (!foundB)
            {
                gapBottom = gapBottomA;
                gapTop = gapTopA;
                blockerWidth = widthA;
                return true;
            }

            // Blend nearest two columns to avoid abrupt, unfair gap jumps.
            float wA = 1f / (bestDistA + 0.06f);
            float wB = 1f / (bestDistB + 0.06f);
            float inv = 1f / Mathf.Max(0.0001f, wA + wB);
            gapBottom = ((gapBottomA * wA) + (gapBottomB * wB)) * inv;
            gapTop = ((gapTopA * wA) + (gapTopB * wB)) * inv;
            blockerWidth = ((widthA * wA) + (widthB * wB)) * inv;
            return true;
        }

        private void BuildDustField()
        {
            dustStreaks.Clear();
            string motifId = SpaceMazeMotifs.ResolveByIndex(currentMotifIndex).Id;
            for (int i = 0; i < DustStreakCount; i++)
            {
                float t = (i + 1f) / (DustStreakCount + 1f);
                float y = Mathf.Lerp(-DustFieldHalfHeight, DustFieldHalfHeight, t);
                float width = Mathf.Lerp(0.05f, 0.10f, Mathf.PingPong((i * 0.17f), 1f));
                float length = Mathf.Lerp(0.35f, 1.35f, Mathf.PingPong((i * 0.31f), 1f));
                Sprite dustSprite = SpaceMazeArtCatalog.ResolveDustSprite(i, motifSeed + (i * 59));
                if (dustSprite == null)
                {
                    dustSprite = SpaceMazeArtCatalog.ResolveLayerSprite(0, motifId, motifSeed + i);
                }

                Transform streak = CreateMazeSegment(
                    $"Dust_{i}",
                    y,
                    width,
                    new Color(0.70f, 0.95f, 1f, 0.08f),
                    -6,
                    dustSprite);
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

            SpaceMazeMotifSpec motif = activeMotif;
            float minX = -DustFieldHalfWidth;
            float maxX = DustFieldHalfWidth;
            float beatEnvelope = (beatPulse * 0.12f) + (subPulseStrength * 0.18f) + (barTickStrength * 0.22f);
            float phraseEnvelope = phrasePulseStrength * 0.38f;
            float dustScroll = DustScrollRatio * Mathf.Lerp(0.70f, 1.35f, motifDustRate / 3.0f);
            Color dustColor = ApplyColorGrade(motif.Dust, motifAmbient, motifHueShift, motifSaturation, motifContrast);
            for (int i = 0; i < dustStreaks.Count; i++)
            {
                DustStreak streak = dustStreaks[i];
                if (streak.Visual == null)
                {
                    continue;
                }

                float baseX = (i * 1.43f) + (streak.Phase * 1.7f);
                float x = RepeatRange(baseX - (worldScrollPos * dustScroll * SpeedPulseMultiplier), minX, maxX);
                float y = streak.BaseY + Mathf.Sin((songTimeSec * (0.95f + motifScanline)) + streak.Phase) * (streak.DriftAmp * (1f + (motifRotorSpinRate * 0.30f)));
                float length = streak.Length * (1f + (beatEnvelope * 0.45f) + (phraseEnvelope * 0.35f));
                streak.Visual.localPosition = new Vector3(x, y, -5.45f);
                streak.Visual.localScale = new Vector3(length, streak.Width, 1f);
                if (streak.Material != null)
                {
                    float alpha = Mathf.Clamp01(0.04f + (beatEnvelope * 0.30f) + (phraseEnvelope * 0.42f) + (motifWarpVignette * 0.08f));
                    dustColor.a = alpha;
                    RenderMaterialUtils.ApplyColor(streak.Material, dustColor);
                }
            }
        }

        private Transform CreateMazeSegment(string name, float y, float height, Color color, int sortingOrder)
        {
            return CreateMazeSegment(name, y, MazeRibWidth, height, color, sortingOrder, null);
        }

        private Transform CreateMazeSegment(string name, float y, float height, Color color, int sortingOrder, Sprite sprite)
        {
            return CreateMazeSegment(name, y, MazeRibWidth, height, color, sortingOrder, sprite);
        }

        private Transform CreateMazeSegment(string name, float y, float width, float height, Color color, int sortingOrder, Sprite sprite)
        {
            GameObject go = RuntimeSpriteFactory.Create(
                name,
                layerRoot,
                new Vector3(0f, y, -5.9f),
                new Vector3(width, height, 1f),
                sprite: sprite,
                sortingOrder: sortingOrder);
            SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.drawMode = SpriteDrawMode.Tiled;
                spriteRenderer.size = Vector2.one;
            }

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
                AccentAmp = accentAmp,
                LayerIndex = layers.Count
            };

            int sortingOrder = -30 + layers.Count;
            layer.A = CreateTile($"Layer_{layers.Count}_A", width + layer.Overlap, height, z, color, sortingOrder);
            layer.B = CreateTile($"Layer_{layers.Count}_B", width + layer.Overlap, height, z, color, sortingOrder);
            if (layer.A != null)
            {
                Renderer ra = layer.A.GetComponent<Renderer>();
                layer.MaterialA = ra != null ? ra.material : null;
            }

            if (layer.B != null)
            {
                Renderer rb = layer.B.GetComponent<Renderer>();
                layer.MaterialB = rb != null ? rb.material : null;
            }
            layers.Add(layer);
        }

        private Transform CreateTile(string name, float width, float height, float z, Color color, int sortingOrder)
        {
            Sprite sprite = SpaceMazeArtCatalog.ResolveLayerSprite(layers.Count, SpaceMazeMotifs.ResolveByIndex(currentMotifIndex).Id, motifSeed + (layers.Count * 29));
            GameObject go = RuntimeSpriteFactory.Create(
                name,
                layerRoot,
                new Vector3(0f, 0f, z),
                new Vector3(width, height, 1f),
                sprite: sprite,
                sortingOrder: sortingOrder);
            SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.drawMode = SpriteDrawMode.Tiled;
                spriteRenderer.size = Vector2.one;
            }

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
