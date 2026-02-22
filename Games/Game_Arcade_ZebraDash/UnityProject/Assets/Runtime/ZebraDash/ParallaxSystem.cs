using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZebraDash
{
    public sealed class ParallaxSystem : MonoBehaviour
    {
        private readonly List<ParallaxLayer> layers = new List<ParallaxLayer>();
        private float pulseStrength;
        private float lastSongTimeSec;
        private float speedMultiplier = 1f;
        private float speedMultiplierTarget = 1f;
        private float bobMultiplier = 1f;
        private float bobMultiplierTarget = 1f;
        private float beatPulse;
        private float barPulse;
        private bool initialized;
        private Transform layerRoot;

        public float SpeedPulseMultiplier { get; private set; } = 1f;
        public float EmissivePulseMultiplier { get; private set; } = 1f;

        private sealed class ParallaxLayer
        {
            public Transform A;
            public Transform B;
            public float Width;
            public float ScrollX;
            public float BaseY;
            public float Speed;
            public float BobAmp;
            public float BobFreq;
            public float Phase;
            public float PulseScale;
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
                width: 44f,
                height: 18f,
                z: -8.5f,
                y: 0.2f,
                color: new Color(0.07f, 0.11f, 0.19f, 1f),
                speed: 0.55f,
                bobAmp: 0.04f,
                bobFreq: 0.35f,
                pulseScale: 0.08f);
            AddLayer(
                width: 40f,
                height: 12f,
                z: -7.8f,
                y: -0.3f,
                color: new Color(0.09f, 0.17f, 0.28f, 1f),
                speed: 0.95f,
                bobAmp: 0.07f,
                bobFreq: 0.55f,
                pulseScale: 0.10f);
            AddLayer(
                width: 36f,
                height: 9f,
                z: -7.1f,
                y: -0.8f,
                color: new Color(0.11f, 0.24f, 0.36f, 1f),
                speed: 1.45f,
                bobAmp: 0.10f,
                bobFreq: 0.80f,
                pulseScale: 0.14f);
            AddLayer(
                width: 32f,
                height: 6.5f,
                z: -6.2f,
                y: -1.4f,
                color: new Color(0.14f, 0.30f, 0.44f, 1f),
                speed: 2.10f,
                bobAmp: 0.15f,
                bobFreq: 1.20f,
                pulseScale: 0.18f);
            AddLayer(
                width: 28f,
                height: 4.5f,
                z: -5.4f,
                y: -2.0f,
                color: new Color(0.18f, 0.35f, 0.49f, 1f),
                speed: 2.80f,
                bobAmp: 0.20f,
                bobFreq: 1.65f,
                pulseScale: 0.22f);

            initialized = true;
        }

        public void Tick(float songTimeSec, bool isPlaying, float phaseBeat = 0f, float phaseBar = 0f)
        {
            if (!initialized)
            {
                return;
            }

            float delta = Mathf.Max(0f, songTimeSec - lastSongTimeSec);
            lastSongTimeSec = songTimeSec;

            if (!isPlaying)
            {
                delta = Time.unscaledDeltaTime;
            }

            pulseStrength = Mathf.MoveTowards(pulseStrength, 0f, delta * 2.2f);
            speedMultiplier = Mathf.MoveTowards(speedMultiplier, speedMultiplierTarget, delta * 1.6f);
            bobMultiplier = Mathf.MoveTowards(bobMultiplier, bobMultiplierTarget, delta * 1.6f);
            beatPulse = PulseEnvelope(phaseBeat, 0.12f);
            barPulse = PulseEnvelope(phaseBar, 0.18f);
            SpeedPulseMultiplier = 1f + (beatPulse * 0.08f) + (barPulse * 0.04f) + (pulseStrength * 0.06f);
            EmissivePulseMultiplier = 1f + (beatPulse * 0.38f) + (pulseStrength * 0.52f);

            for (int i = 0; i < layers.Count; i++)
            {
                ParallaxLayer layer = layers[i];
                // Integrate using DSP-derived delta to keep motion smooth when pulse multipliers change.
                layer.ScrollX -= delta * layer.Speed * speedMultiplier * SpeedPulseMultiplier;
                float wrappedX = -Mathf.Repeat(-layer.ScrollX, layer.Width);
                float bob = Mathf.Sin((songTimeSec * layer.BobFreq * bobMultiplier) + layer.Phase) * (layer.BobAmp * bobMultiplier);
                float pulse = pulseStrength * layer.PulseScale;
                float y = layer.BaseY + bob + pulse;

                layer.A.localPosition = new Vector3(wrappedX, y, layer.A.localPosition.z);
                layer.B.localPosition = new Vector3(wrappedX + layer.Width, y, layer.B.localPosition.z);
            }
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

        private void AddLayer(
            float width,
            float height,
            float z,
            float y,
            Color color,
            float speed,
            float bobAmp,
            float bobFreq,
            float pulseScale)
        {
            var layer = new ParallaxLayer
            {
                Width = width,
                ScrollX = 0f,
                BaseY = y,
                Speed = speed,
                BobAmp = bobAmp,
                BobFreq = bobFreq,
                Phase = (layers.Count + 1) * 1.0472f,
                PulseScale = pulseScale
            };

            int sortingOrder = -30 + layers.Count;
            layer.A = CreateTile($"Layer_{layers.Count}_A", width, height, z, color, sortingOrder);
            layer.B = CreateTile($"Layer_{layers.Count}_B", width, height, z, color, sortingOrder);
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
    }
}
