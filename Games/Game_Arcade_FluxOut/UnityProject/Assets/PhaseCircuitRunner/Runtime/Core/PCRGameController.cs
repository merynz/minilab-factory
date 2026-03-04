using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FluxOut.PCR
{
    public sealed class PCRGameController : MonoBehaviour
    {
        private const float PhaseIndicatorDurationSec = 0.8f;
        private const float ShiftIndicatorDurationSec = 0.7f;
        private const float CountdownStartSec = 3f;
        private const int WakePoolSize = 64;
        private const float WakeSpawnDistance = 0.17f;

        [Header("Optional Authoring")]
        [SerializeField] private PCRLevelDoc levelDoc;

        [Header("Runtime Toggles")]
        [SerializeField] private bool autoRebuildOnPlay = true;
        [SerializeField] private bool startPaused = false;
        [Header("Audio")]
        [SerializeField] private string backgroundMusicFileName = "Forecast.mp3";
        [SerializeField, Range(0f, 1f)] private float backgroundMusicVolume = 0.48f;
        [SerializeField, Range(0f, 1f)] private float menuMusicVolume = 0.24f;
        [Header("Visual Tuning")]
        [SerializeField] private bool enableWakeDecals = true;

        private PCRLevelRuntime level;
        private PCRValidationReport validation;
        private PCRRunState runState;
        private Camera mainCamera;

        private GameObject worldRoot;
        private GameObject traceRoot;
        private GameObject elementRoot;
        private GameObject playerRoot;
        private Renderer playerCoreRenderer;
        private Renderer playerGlowRenderer;
        private Renderer playerAuraRenderer;
        private Renderer playerLightRenderer;
        private LineRenderer playerOrbitRing;
        private TextMesh playerChargeText;
        private TrailRenderer playerTrail;
        private TrailRenderer playerLightTrail;
        private ParticleSystem sparkTrail;
        private ParticleSystem phaseBurst;
        private ParticleSystem shiftBurst;
        private ParticleSystem deathBurst;
        private AudioSource backgroundMusicSource;
        private AudioClip backgroundMusicClip;
        private Coroutine backgroundMusicRoutine;
        private bool backgroundMusicPlayOnLoad;
        private bool backgroundMusicMenuMode;
        private readonly List<PCREventView> eventViews = new();
        private readonly List<PCRDecisionTelegraphView> decisionViews = new();
        private readonly List<GameObject> runtimeObjects = new();
        private readonly List<ParallaxLayerState> parallaxLayers = new();

        private Material traceMatLive;
        private Material traceMatDead;
        private Material traceMatGlow;
        private Material iconMatBase;
        private Material playerMat;
        private Material haloMat;
        private Material playerLightMat;
        private Material sparkMat;
        private Material wakeMat;
        private Font menuTitleFont;
        private Font menuBodyFont;

        private PCRFlowState flowState = PCRFlowState.MainMenu;
        private bool pendingTap;
        private int currentStep;
        private float accumulator;
        private float laneVisual;
        private float laneVisualVel;
        private float timerRemaining;

        private float phaseFlash;
        private float shiftFlash;
        private float deathFlash;
        private float phaseIndicatorTimer;
        private float shiftIndicatorTimer;
        private string shiftIndicatorText = string.Empty;
        private string deathReasonText = string.Empty;
        private float countdownRemainingSec;
        private int countdownDisplayValue = 3;
        private bool tutorialOpen;
        private Vector2 tutorialScroll;
        private PCRFlowState tutorialResumeState = PCRFlowState.MainMenu;

        private bool showDebug;
        private int debugSeed = 4242;
        private float debugLookahead = 12.5f;
        private float debugOrthoSize = 10.2f;
        private float debugCameraHeight = 21f;
        private float debugBloomStrength = 0.75f;
        private float debugLaneShiftSmooth = 0.1f;
        private float debugSpeedMultiplier = 1f;
        private float debugSimStep = 0.12f;
        private int debugLaneOverride = 0;
        private float debugEmptyGapThreshold = 2f;
        private float debugLaneShiftTarget = 1.2f;
        private float debugMaxNoTapSurvival = 9f;
        private float debugMaxDecisionGap = 2f;
        private float debugMinRequiredTapPer10Sec = 4f;
        private float debugMaxSamePhaseRunSec = 3f;
        private float debugDecisionLeadTimeSec = 1f;
        private float densityGate = 1f;
        private float densityMux = 1f;
        private float densityInductor = 1f;
        private float densityArc = 1f;
        private float densityCap = 1f;
        private float densityAmp = 1f;
        private float densityClamp = 1f;
        private bool debugShowTelegraphs = true;
        private string bootErrorMessage = string.Empty;

        private Volume postVolume;
        private float guiScale = 1f;
        private float menuGuiScale = 1f;
        private float hudGuiScale = 1f;
        private readonly List<PCRWakeDecal> wakeDecals = new();
        private int wakeDecalCursor;
        private Vector3 lastWakeSpawnPos;

        private static Texture2D WhiteTexture
        {
            get
            {
                if (_whiteTexture != null)
                {
                    return _whiteTexture;
                }

                _whiteTexture = new Texture2D(1, 1);
                _whiteTexture.SetPixel(0, 0, Color.white);
                _whiteTexture.Apply();
                return _whiteTexture;
            }
        }

        private static Texture2D _whiteTexture;
        private static readonly TutorialEntry[] TutorialEntries =
        {
            new("A/B", "Phase Tap", "Tap flips your signal phase: A (green) <-> B (orange).", new Color(0.6f, 1f, 0.62f, 1f)),
            new("ROUTE", "Arrow Routing", "Route arrows shift you to opposite lanes depending on your current phase.", new Color(0.76f, 0.95f, 1f, 1f)),
            new("LANE", "Glow Lanes", "Stay on glow lanes. Matte lanes are signal lost and kill instantly.", new Color(0.94f, 0.42f, 0.5f, 1f))
        };

        private bool IsMenu => flowState == PCRFlowState.MainMenu;
        private bool IsCountdown => flowState == PCRFlowState.Countdown;
        private bool IsPlaying => flowState == PCRFlowState.Playing;
        private bool IsPaused => flowState == PCRFlowState.Paused;
        private bool IsDead => flowState == PCRFlowState.Dead;
        private bool IsFinished => flowState == PCRFlowState.Finished;
        private float UiWidth => Screen.width / Mathf.Max(1f, guiScale);
        private float UiHeight => Screen.height / Mathf.Max(1f, guiScale);

        private void Awake()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;

            EnsureCamera();
            EnsureMaterials();
            EnsureWorldRoot();
            EnsureAudio();
            if (levelDoc != null)
            {
                debugSeed = levelDoc.Seed;
                if (levelDoc.PacingProfile != null)
                {
                    debugMaxNoTapSurvival = levelDoc.PacingProfile.MaxNoTapSurvivalSec;
                    debugMaxDecisionGap = levelDoc.PacingProfile.MaxDecisionGapSec;
                    debugMinRequiredTapPer10Sec = levelDoc.PacingProfile.MinRequiredTapPer10Sec;
                    debugMaxSamePhaseRunSec = levelDoc.PacingProfile.MaxSamePhaseRunSec;
                    debugDecisionLeadTimeSec = levelDoc.PacingProfile.DecisionLeadTimeSec;
                }
            }

            if (autoRebuildOnPlay)
            {
                BuildAndStartLevel(debugSeed, true);
            }
            else
            {
                PlayMenuMusic();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                showDebug = !showDebug;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                BuildAndStartLevel(debugSeed, IsMenu);
            }

            if (Input.GetKeyDown(KeyCode.P) && !tutorialOpen)
            {
                TogglePause();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (tutorialOpen)
                {
                    CloseTutorial();
                }
                else if (!IsMenu)
                {
                    EnterMainMenu();
                }
            }

            if (level == null)
            {
                return;
            }

            if (tutorialOpen)
            {
                UpdateVisuals();
                UpdateCamera();
                UpdateEffects();
                UpdateMusicVolume();

                return;
            }

            bool tapPressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space);
            if (tapPressed && IsPlaying)
            {
                HandlePrimaryAction();
            }

            if (IsPlaying)
            {
                float scaledDelta = Time.deltaTime * debugSpeedMultiplier;
                accumulator += scaledDelta;
                float simStep = Mathf.Max(0.05f, level.SimStepSec);
                while (accumulator >= simStep && currentStep < level.TotalSteps)
                {
                    SimTick();
                    accumulator -= simStep;
                    if (IsDead || IsFinished)
                    {
                        break;
                    }
                }
            }
            else if (IsCountdown)
            {
                countdownRemainingSec = Mathf.Max(0f, countdownRemainingSec - Time.deltaTime);
                countdownDisplayValue = Mathf.Clamp(Mathf.CeilToInt(countdownRemainingSec), 1, 3);
                if (countdownRemainingSec <= 0f)
                {
                    flowState = PCRFlowState.Playing;
                    StartBackgroundMusicForRun();
                }
            }

            UpdateVisuals();
            UpdateCamera();
            UpdateEffects();
            UpdateMusicVolume();
        }

        private void OnGUI()
        {
            Matrix4x4 prev = GUI.matrix;
            menuGuiScale = ComputeUiScale();
            hudGuiScale = Mathf.Clamp(menuGuiScale * 0.6f, 0.78f, 1.35f);

            if (level != null && !IsMenu)
            {
                ApplyGuiScale(hudGuiScale);
                DrawHud();
                DrawActionIndicators();
            }

            ApplyGuiScale(menuGuiScale);
            DrawFlowPanel();
            if (showDebug)
            {
                DrawDebugPanel();
            }

            GUI.matrix = prev;
            guiScale = 1f;
            DrawScreenFlash();
        }

        private void BuildAndStartLevel(int seed, bool openInMainMenu = false)
        {
            var runtimeDoc = BuildRuntimeLevelDoc(seed);
            const int maxBuildAttempts = 7;
            PCRBuildResult build = null;
            int selectedSeed = seed;
            for (int attempt = 0; attempt < maxBuildAttempts; attempt++)
            {
                int candidateSeed = seed + attempt * 137;
                runtimeDoc.Seed = candidateSeed;
                PCRBuildResult candidate = PCRLevelBuilder.Build(runtimeDoc, candidateSeed);
                if (candidate?.Level == null)
                {
                    continue;
                }

                build = candidate;
                if (candidate.Validation != null && candidate.Validation.IsValid)
                {
                    selectedSeed = candidateSeed;
                    break;
                }
            }

            bool hasValidBuild = build?.Level != null && build.Validation != null && build.Validation.IsValid;
            if (!hasValidBuild && TryBuildEmergencyLevel(seed, out PCRBuildResult emergencyBuild, out int emergencySeed))
            {
                build = emergencyBuild;
                selectedSeed = emergencySeed;
                hasValidBuild = build?.Level != null && build.Validation != null && build.Validation.IsValid;
                Debug.LogWarning($"PCRGameController: primary build failed for seed {seed}, recovered with emergency build seed {emergencySeed}.");
            }

            if (build?.Level == null)
            {
                bootErrorMessage = $"Build failed for seed {seed}. Runtime level could not be produced.";
                Debug.LogError($"PCRGameController: {bootErrorMessage}");
                return;
            }

            if (build.Validation == null)
            {
                build.Validation = new PCRValidationReport
                {
                    IsValid = false,
                    FailureReason = "Validation report was null."
                };
            }

            if (!build.Validation.IsValid)
            {
                string failReason = string.IsNullOrWhiteSpace(build.Validation.FailureReason)
                    ? "Unknown validation state."
                    : build.Validation.FailureReason;
                Debug.LogWarning($"PCRGameController: continuing with non-validated level for seed {seed}. Reason: {failReason}");
            }

            bootErrorMessage = string.Empty;
            if (debugSeed != selectedSeed)
            {
                debugSeed = selectedSeed;
            }

            ClearRuntimeObjects();
            level = build.Level;
            validation = build.Validation;

            // Core-mode cleanup: legacy density mutation pipeline is disabled.

            BuildStepCacheIfNeeded(level);
            BuildTraceVisuals();
            BuildElements();
            BuildDecisionTelegraphs();
            BuildPlayer();
            SetupPost();
            ResetRunState();
            if (openInMainMenu)
            {
                EnterMainMenu(false);
            }
            else
            {
                StartRunCountdown();
            }
        }

        private void ResetRunState()
        {
            runState = new PCRRunState
            {
                StepIndex = 0,
                Phase = PCRPhase.A,
                LaneIndex = 2,
                Modifiers = new PCRModifierState
                {
                    AmplifyNextRouting = 1
                },
                Dead = false,
                DeathType = PCRDeathType.None
            };
            int laneCount = PCRSimulation.GetLaneCountAtStep(level, 0);
            runState.LaneIndex = Mathf.Clamp(runState.LaneIndex, 0, laneCount - 1);
            laneVisual = runState.LaneIndex;
            laneVisualVel = 0f;
            pendingTap = false;
            currentStep = 0;
            accumulator = 0f;
            timerRemaining = level.DurationSec;
            deathReasonText = string.Empty;
            phaseFlash = 0f;
            shiftFlash = 0f;
            deathFlash = 0f;
            phaseIndicatorTimer = 0f;
            shiftIndicatorTimer = 0f;
            shiftIndicatorText = string.Empty;
            countdownRemainingSec = 0f;
            countdownDisplayValue = 3;
            lastWakeSpawnPos = Vector3.positiveInfinity;
            for (int i = 0; i < wakeDecals.Count; i++)
            {
                wakeDecals[i].SetActive(false);
            }
        }

        private void StartRunCountdown()
        {
            if (level == null)
            {
                return;
            }

            pendingTap = false;
            tutorialOpen = false;
            countdownRemainingSec = CountdownStartSec;
            countdownDisplayValue = 3;
            flowState = PCRFlowState.Countdown;
            StopBackgroundMusic();
        }

        private void RestartRun()
        {
            if (level == null)
            {
                return;
            }

            ResetRunState();
            tutorialOpen = false;
            StartRunCountdown();
        }

        private void EnterMainMenu(bool resetRun = true)
        {
            if (level == null)
            {
                return;
            }

            if (resetRun)
            {
                ResetRunState();
            }

            pendingTap = false;
            tutorialOpen = false;
            tutorialResumeState = PCRFlowState.MainMenu;
            flowState = PCRFlowState.MainMenu;
            PlayMenuMusic();
        }

        private void TogglePause()
        {
            if (IsPlaying)
            {
                flowState = PCRFlowState.Paused;
            }
            else if (IsPaused)
            {
                flowState = PCRFlowState.Playing;
            }
        }

        private void HandlePrimaryAction()
        {
            if (level == null)
            {
                return;
            }

            if (tutorialOpen)
            {
                return;
            }

            if (IsCountdown)
            {
                return;
            }

            if (IsDead || IsFinished)
            {
                return;
            }

            if (IsPlaying)
            {
                pendingTap = true;
                phaseFlash = 1f;
                phaseIndicatorTimer = PhaseIndicatorDurationSec;
            }
        }

        private void EnsureAudio()
        {
            if (backgroundMusicSource != null)
            {
                return;
            }

            GameObject audioRoot = new GameObject("BgmAudio");
            audioRoot.transform.SetParent(transform, false);
            backgroundMusicSource = audioRoot.AddComponent<AudioSource>();
            backgroundMusicSource.playOnAwake = false;
            backgroundMusicSource.loop = true;
            backgroundMusicSource.spatialBlend = 0f;
            backgroundMusicSource.volume = 0f;
        }

        private void StartBackgroundMusicForRun()
        {
            if (backgroundMusicSource == null || string.IsNullOrWhiteSpace(backgroundMusicFileName))
            {
                return;
            }

            backgroundMusicMenuMode = false;
            backgroundMusicPlayOnLoad = true;
            if (backgroundMusicClip != null)
            {
                backgroundMusicSource.Stop();
                backgroundMusicSource.clip = backgroundMusicClip;
                backgroundMusicSource.time = 0f;
                backgroundMusicSource.Play();
                backgroundMusicPlayOnLoad = false;
                return;
            }

            if (backgroundMusicRoutine == null)
            {
                backgroundMusicRoutine = StartCoroutine(LoadAndPlayBackgroundMusic());
            }
        }

        private void PlayMenuMusic()
        {
            if (backgroundMusicSource == null || string.IsNullOrWhiteSpace(backgroundMusicFileName))
            {
                return;
            }

            backgroundMusicMenuMode = true;
            backgroundMusicPlayOnLoad = true;
            if (backgroundMusicClip != null)
            {
                backgroundMusicSource.Stop();
                backgroundMusicSource.clip = backgroundMusicClip;
                backgroundMusicSource.time = 0f;
                backgroundMusicSource.Play();
                backgroundMusicPlayOnLoad = false;
                return;
            }

            if (backgroundMusicRoutine == null)
            {
                backgroundMusicRoutine = StartCoroutine(LoadAndPlayBackgroundMusic());
            }
        }

        private void StopBackgroundMusic()
        {
            if (backgroundMusicSource != null && backgroundMusicSource.isPlaying)
            {
                backgroundMusicSource.Stop();
            }

            backgroundMusicPlayOnLoad = false;
        }

        private void UpdateMusicVolume()
        {
            if (backgroundMusicSource == null)
            {
                return;
            }

            float target = backgroundMusicMenuMode ? menuMusicVolume : backgroundMusicVolume;
            backgroundMusicSource.volume = Mathf.MoveTowards(backgroundMusicSource.volume, Mathf.Clamp01(target), Time.deltaTime * 0.6f);
        }

        private IEnumerator LoadAndPlayBackgroundMusic()
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, backgroundMusicFileName);
            string url = fullPath.Contains("://") ? fullPath : $"file://{fullPath}";
            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"PCRGameController: background music load failed ({url}) -> {request.error}");
                backgroundMusicRoutine = null;
                yield break;
            }

            backgroundMusicClip = DownloadHandlerAudioClip.GetContent(request);
            if (backgroundMusicClip == null)
            {
                Debug.LogWarning("PCRGameController: background music clip is null after download.");
                backgroundMusicRoutine = null;
                yield break;
            }

            backgroundMusicClip.name = "FluxOut_BGM";
            backgroundMusicSource.clip = backgroundMusicClip;
            backgroundMusicSource.volume = Mathf.Clamp01(backgroundMusicMenuMode ? menuMusicVolume : backgroundMusicVolume);
            if (backgroundMusicPlayOnLoad)
            {
                backgroundMusicSource.time = 0f;
                backgroundMusicSource.Play();
                backgroundMusicPlayOnLoad = false;
            }

            backgroundMusicRoutine = null;
        }

        private void BuildNewSeedAndPlay()
        {
            debugSeed++;
            BuildAndStartLevel(debugSeed);
        }

        private void SimTick()
        {
            bool tap = pendingTap;
            pendingTap = false;
            int stepToRun = Mathf.Clamp(currentStep, 0, level.TotalSteps - 1);
            PCRPhase phaseBeforeStep = runState.Phase;
            PCRStepOutput output = PCRSimulation.SimulateStep(level, stepToRun, tap, ref runState);
            if (runState.Phase != phaseBeforeStep || output.PhaseFlippedByInverter)
            {
                phaseFlash = 1f;
                phaseIndicatorTimer = PhaseIndicatorDurationSec;
                Color burstColor = runState.Phase == PCRPhase.A ? new Color(0.4f, 1f, 0.46f, 1f) : new Color(1f, 0.6f, 0.2f, 1f);
                EmitParticleBurst(phaseBurst, GetPlayerWorldPosition(), burstColor);
                EmitSparkTrailBurst(26, burstColor);
            }

            if (output.Shifted)
            {
                shiftFlash = 1f;
                shiftIndicatorTimer = ShiftIndicatorDurationSec;
                shiftIndicatorText = output.AppliedRouteDelta > 0 ? "ROUTE UP" : "ROUTE DOWN";
                Color shiftColor = new Color(1f, 0.92f, 0.62f, 1f);
                EmitParticleBurst(shiftBurst, GetPlayerWorldPosition(), shiftColor);
                EmitSparkTrailBurst(22, shiftColor);
            }

            if (output.Dead)
            {
                flowState = PCRFlowState.Dead;
                deathFlash = 1f;
                StopBackgroundMusic();
                deathReasonText = output.DeathType switch
                {
                    PCRDeathType.DeadTrace => "Signal Lost",
                    _ => "Signal Lost"
                };
                EmitParticleBurst(deathBurst, GetPlayerWorldPosition(), Color.white);
            }

            if (output.Finished || runState.StepIndex >= level.TotalSteps)
            {
                if (!IsDead)
                {
                    flowState = PCRFlowState.Finished;
                    StopBackgroundMusic();
                }
            }

            currentStep = runState.StepIndex;
            timerRemaining = Mathf.Max(0f, level.DurationSec - currentStep * level.SimStepSec);
            if (!IsDead && timerRemaining <= 0f)
            {
                flowState = PCRFlowState.Finished;
                StopBackgroundMusic();
            }
        }

        private void UpdateVisuals()
        {
            if (level == null || playerRoot == null)
            {
                return;
            }

            float simStep = Mathf.Max(0.05f, level.SimStepSec);
            float stepAlpha = Mathf.Clamp01(accumulator / simStep);
            float renderStep = Mathf.Clamp(currentStep + stepAlpha, 0f, level.TotalSteps - 1f);
            laneVisual = Mathf.SmoothDamp(laneVisual, runState.LaneIndex, ref laneVisualVel, debugLaneShiftSmooth);
            int renderStepIndex = Mathf.Clamp(Mathf.RoundToInt(renderStep), 0, level.TotalSteps - 1);
            Vector3 moveTangent = level.Tangents[renderStepIndex];
            if (moveTangent.sqrMagnitude < 0.0001f)
            {
                moveTangent = Vector3.forward;
            }

            moveTangent.Normalize();
            Vector3 playerPos = PCRSimulation.EvaluateLanePoint(level, renderStep, laneVisual) + Vector3.up * 0.12f;
            playerRoot.transform.position = playerPos;
            playerRoot.transform.rotation = Quaternion.LookRotation(moveTangent, Vector3.up);

            bool phaseA = runState.Phase == PCRPhase.A;
            Color phaseCore = phaseA ? new Color(0.96f, 1f, 0.94f, 1f) : new Color(1f, 0.95f, 0.84f, 1f);
            Color phaseMid = phaseA ? new Color(0.28f, 1f, 0.4f, 1f) : new Color(1f, 0.56f, 0.16f, 1f);
            Color phaseEdge = phaseA ? new Color(0.08f, 0.44f, 0.14f, 1f) : new Color(0.6f, 0.18f, 0.03f, 1f);
            Color phaseColor = phaseA ? new Color(0.28f, 1f, 0.4f, 1f) : new Color(1f, 0.56f, 0.16f, 1f);
            float phaseBoost = 1f + phaseFlash * 0.24f;
            UpdateBackgroundParallax(playerPos, phaseColor, phaseA);
            if (playerCoreRenderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", phaseMid * phaseBoost);
                mpb.SetColor("_CoreColor", phaseCore * phaseBoost);
                mpb.SetColor("_MidColor", phaseMid * phaseBoost);
                mpb.SetColor("_EdgeColor", phaseEdge);
                mpb.SetColor("_Color", phaseMid * phaseBoost);
                mpb.SetFloat("_Glow", 1.2f + phaseFlash * 0.38f);
                mpb.SetColor("_EmissionColor", phaseCore * (1.35f + phaseFlash * 0.52f));
                playerCoreRenderer.SetPropertyBlock(mpb);
            }

            if (playerTrail != null)
            {
                Color trailStart = Color.Lerp(phaseCore, phaseMid, 0.35f);
                playerTrail.startColor = trailStart;
                playerTrail.endColor = new Color(phaseEdge.r, phaseEdge.g, phaseEdge.b, 0f);
            }

            if (playerLightTrail != null)
            {
                Color lightTrailStart = Color.Lerp(new Color(1f, 0.82f, 0.3f, 1f), phaseMid, 0.22f);
                lightTrailStart.a = 0.2f;
                Color lightTrailEnd = new Color(phaseEdge.r, phaseEdge.g, phaseEdge.b, 0f);
                playerLightTrail.startColor = lightTrailStart;
                playerLightTrail.endColor = lightTrailEnd;
            }

            if (haloMat != null)
            {
                Color aura = new Color(phaseMid.r, phaseMid.g, phaseMid.b, 0.14f + phaseFlash * 0.05f);
                SetColorSafe(haloMat, "_BaseColor", aura);
                SetColorSafe(haloMat, "_Color", aura);
                SetColorSafe(haloMat, "_TintColor", aura);
            }

            if (playerLightMat != null)
            {
                Color localWarm = Color.Lerp(new Color(1f, 0.76f, 0.24f, 1f), phaseMid, 0.34f);
                SetColorSafe(playerLightMat, "_BaseColor", localWarm);
                SetColorSafe(playerLightMat, "_CoreColor", Color.Lerp(phaseCore, Color.white, 0.25f));
                SetColorSafe(playerLightMat, "_OuterColor", Color.Lerp(localWarm, phaseEdge, 0.2f));
                SetColorSafe(playerLightMat, "_MidColor", Color.Lerp(localWarm, phaseMid, 0.45f));
                SetFloatSafe(playerLightMat, "_Glow", 0.74f + phaseFlash * 0.12f);
                SetFloatSafe(playerLightMat, "_Intensity", 0.88f + phaseFlash * 0.18f);
                SetColorSafe(playerLightMat, "_Color", localWarm);
            }

            if (playerLightRenderer != null)
            {
                playerLightRenderer.enabled = false;
            }

            if (playerGlowRenderer != null)
            {
                float shellScale = 0.56f + Mathf.PingPong(Time.time * 1.3f, 0.04f) + phaseFlash * 0.02f;
                playerGlowRenderer.transform.localScale = Vector3.one * shellScale;
            }

            if (playerAuraRenderer != null)
            {
                float auraScale = 0.84f + Mathf.PingPong(Time.time * 0.85f, 0.05f) + phaseFlash * 0.025f;
                playerAuraRenderer.transform.localScale = Vector3.one * auraScale;
            }

            if (playerOrbitRing != null)
            {
                float width = 0.045f + phaseFlash * 0.02f;
                playerOrbitRing.startWidth = width;
                playerOrbitRing.endWidth = width;
                Color ringColor = Color.Lerp(phaseCore, phaseMid, 0.5f);
                ringColor.a = 0.9f;
                playerOrbitRing.startColor = ringColor;
                playerOrbitRing.endColor = ringColor;
                playerOrbitRing.transform.Rotate(Vector3.up, 130f * Time.deltaTime, Space.Self);
            }

            if (sparkTrail != null)
            {
                var main = sparkTrail.main;
                Color sparkColor = Color.Lerp(phaseCore, phaseMid, 0.4f);
                sparkColor.a = 1f;
                main.startColor = sparkColor;
            }

            UpdateWakeDecals(renderStep, playerPos, phaseMid);

            if (playerChargeText != null)
            {
                playerChargeText.text = runState.Phase == PCRPhase.A ? "+" : "-";
                playerChargeText.color = phaseCore;
                if (mainCamera != null)
                {
                    playerChargeText.transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward, Vector3.up);
                }
            }

            bool showWorldTelegraphs = debugShowTelegraphs && !IsMenu && !IsCountdown && !tutorialOpen;
            for (int i = 0; i < eventViews.Count; i++)
            {
                eventViews[i].Refresh(runState.Phase, showWorldTelegraphs, runState.StepIndex);
            }

            for (int i = 0; i < decisionViews.Count; i++)
            {
                decisionViews[i].Refresh(runState.StepIndex, runState.Phase, showWorldTelegraphs);
            }
        }

        private void UpdateEffects()
        {
            phaseFlash = Mathf.MoveTowards(phaseFlash, 0f, Time.deltaTime * 3f);
            shiftFlash = Mathf.MoveTowards(shiftFlash, 0f, Time.deltaTime * 4f);
            deathFlash = Mathf.MoveTowards(deathFlash, 0f, Time.deltaTime * 2f);
            phaseIndicatorTimer = Mathf.MoveTowards(phaseIndicatorTimer, 0f, Time.deltaTime);
            shiftIndicatorTimer = Mathf.MoveTowards(shiftIndicatorTimer, 0f, Time.deltaTime);
        }

        private void UpdateCamera()
        {
            if (mainCamera == null || level == null || playerRoot == null)
            {
                return;
            }

            float simStep = Mathf.Max(0.05f, level.SimStepSec);
            float stepAlpha = Mathf.Clamp01(accumulator / simStep);
            float renderStep = Mathf.Clamp(currentStep + stepAlpha, 0f, level.TotalSteps - 1f);
            float lookaheadStep = renderStep + debugLookahead;
            PCRSimulation.EvaluateCenterline(level, lookaheadStep, out Vector3 lookPoint, out Vector3 lookTangent);
            float targetYaw = Mathf.Atan2(lookTangent.x, lookTangent.z) * Mathf.Rad2Deg;
            Vector3 targetPos = playerRoot.transform.position - lookTangent.normalized * 1.6f + Vector3.up * debugCameraHeight;

            float currentYaw = mainCamera.transform.eulerAngles.y;
            float limitedYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, 60f * Time.deltaTime);
            Quaternion targetRot = Quaternion.Euler(90f, limitedYaw, 0f);
            mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, targetRot, Time.deltaTime * 7f);
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetPos, Time.deltaTime * 5f);
            mainCamera.orthographic = true;
            mainCamera.orthographicSize = Mathf.Lerp(mainCamera.orthographicSize, debugOrthoSize, Time.deltaTime * 6f);
        }

        private PCRLevelDoc BuildRuntimeLevelDoc(int seed)
        {
            var doc = ScriptableObject.CreateInstance<PCRLevelDoc>();
            doc.Seed = seed;
            doc.TargetDurationSec = levelDoc != null ? levelDoc.TargetDurationSec : 90f;
            doc.SegmentCount = levelDoc != null ? levelDoc.SegmentCount : 14;
            doc.SegmentLibrary = levelDoc != null ? levelDoc.SegmentLibrary : null;

            var diff = ScriptableObject.CreateInstance<PCRDifficultyProfileSO>();
            if (levelDoc != null && levelDoc.DifficultyProfile != null)
            {
                diff.StartSpeed = levelDoc.DifficultyProfile.StartSpeed;
                diff.EndSpeed = levelDoc.DifficultyProfile.EndSpeed;
                diff.MinLaneCount = levelDoc.DifficultyProfile.MinLaneCount;
                diff.MaxLaneCount = levelDoc.DifficultyProfile.MaxLaneCount;
            }
            else
            {
                diff.StartSpeed = 7.5f;
                diff.EndSpeed = 10.5f;
                diff.MinLaneCount = 2;
                diff.MaxLaneCount = 5;
            }

            if (debugLaneOverride > 1)
            {
                diff.MinLaneCount = debugLaneOverride;
                diff.MaxLaneCount = debugLaneOverride;
            }

            var pacing = ScriptableObject.CreateInstance<PCRPacingProfileSO>();
            if (levelDoc != null && levelDoc.PacingProfile != null)
            {
                pacing.SegmentCountMin = levelDoc.PacingProfile.SegmentCountMin;
                pacing.SegmentCountMax = levelDoc.PacingProfile.SegmentCountMax;
                pacing.EventCadenceMinSec = levelDoc.PacingProfile.EventCadenceMinSec;
                pacing.EventCadenceMaxSec = levelDoc.PacingProfile.EventCadenceMaxSec;
                pacing.MaxNoTapSurvivalSec = levelDoc.PacingProfile.MaxNoTapSurvivalSec;
                pacing.MaxDecisionGapSec = levelDoc.PacingProfile.MaxDecisionGapSec;
                pacing.MinRequiredTapPer10Sec = levelDoc.PacingProfile.MinRequiredTapPer10Sec;
                pacing.MaxSamePhaseRunSec = levelDoc.PacingProfile.MaxSamePhaseRunSec;
                pacing.DecisionLeadTimeSec = levelDoc.PacingProfile.DecisionLeadTimeSec;
            }
            else
            {
                pacing.SegmentCountMin = 12;
                pacing.SegmentCountMax = 16;
                pacing.EventCadenceMinSec = 1f;
                pacing.EventCadenceMaxSec = 1.4f;
                pacing.MaxNoTapSurvivalSec = 9f;
                pacing.MaxDecisionGapSec = 2f;
                pacing.MinRequiredTapPer10Sec = 4f;
                pacing.MaxSamePhaseRunSec = 3f;
                pacing.DecisionLeadTimeSec = 1f;
            }

            pacing.TargetDurationSec = 90f;
            pacing.SimStepSec = debugSimStep;
            pacing.EmptyGapThresholdSec = debugEmptyGapThreshold;
            pacing.LaneShiftTargetSec = debugLaneShiftTarget;
            pacing.MaxNoTapSurvivalSec = Mathf.Clamp(debugMaxNoTapSurvival, 6f, 14f);
            pacing.MaxDecisionGapSec = Mathf.Clamp(debugMaxDecisionGap, 1f, 2.5f);
            pacing.MinRequiredTapPer10Sec = Mathf.Clamp(debugMinRequiredTapPer10Sec, 1f, 10f);
            pacing.MaxSamePhaseRunSec = Mathf.Clamp(debugMaxSamePhaseRunSec, 1.5f, 5f);
            pacing.DecisionLeadTimeSec = Mathf.Clamp(debugDecisionLeadTimeSec, 0.4f, 2f);

            doc.DifficultyProfile = diff;
            doc.PacingProfile = pacing;
            doc.VisualProfile = levelDoc != null ? levelDoc.VisualProfile : null;
            return doc;
        }

        private bool TryBuildEmergencyLevel(int seed, out PCRBuildResult result, out int selectedSeed)
        {
            result = null;
            selectedSeed = seed;
            const int emergencyAttempts = 6;
            for (int i = 0; i < emergencyAttempts; i++)
            {
                int candidateSeed = seed + 10007 + i * 97;
                PCRLevelDoc emergencyDoc = BuildRuntimeLevelDoc(candidateSeed);
                if (emergencyDoc == null || emergencyDoc.PacingProfile == null)
                {
                    continue;
                }

                emergencyDoc.PacingProfile.EventCadenceMinSec = Mathf.Min(emergencyDoc.PacingProfile.EventCadenceMinSec, 1.1f);
                emergencyDoc.PacingProfile.EventCadenceMaxSec = Mathf.Min(emergencyDoc.PacingProfile.EventCadenceMaxSec, 1.5f);
                emergencyDoc.PacingProfile.MaxDecisionGapSec = Mathf.Max(emergencyDoc.PacingProfile.MaxDecisionGapSec, 2.2f);
                emergencyDoc.PacingProfile.MinRequiredTapPer10Sec = Mathf.Min(emergencyDoc.PacingProfile.MinRequiredTapPer10Sec, 2f);
                emergencyDoc.PacingProfile.MaxNoTapSurvivalSec = Mathf.Max(emergencyDoc.PacingProfile.MaxNoTapSurvivalSec, 12f);
                emergencyDoc.PacingProfile.MaxSamePhaseRunSec = Mathf.Max(emergencyDoc.PacingProfile.MaxSamePhaseRunSec, 4.5f);
                emergencyDoc.PacingProfile.DecisionLeadTimeSec = Mathf.Max(emergencyDoc.PacingProfile.DecisionLeadTimeSec, 1.1f);

                PCRBuildResult candidate = PCRLevelBuilder.Build(emergencyDoc, candidateSeed);
                if (candidate?.Level == null)
                {
                    continue;
                }

                if (candidate.Validation != null && candidate.Validation.IsValid)
                {
                    result = candidate;
                    selectedSeed = candidateSeed;
                    return true;
                }
            }

            return false;
        }

        private void EnsureCamera()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                GameObject cameraObject = GameObject.Find("Main Camera");
                if (cameraObject == null)
                {
                    cameraObject = new GameObject("Main Camera");
                    cameraObject.tag = "MainCamera";
                    mainCamera = cameraObject.AddComponent<Camera>();
                }
                else
                {
                    mainCamera = cameraObject.GetComponent<Camera>();
                    if (mainCamera == null)
                    {
                        mainCamera = cameraObject.AddComponent<Camera>();
                    }
                }
            }

            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.008f, 0.002f, 0.02f, 1f);
            mainCamera.nearClipPlane = 0.01f;
            mainCamera.farClipPlane = 600f;
            mainCamera.allowHDR = true;

            UniversalAdditionalCameraData cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            cameraData.renderPostProcessing = true;
            cameraData.requiresColorTexture = true;
            cameraData.requiresDepthTexture = false;
            cameraData.volumeLayerMask = ~0;
            cameraData.volumeTrigger = mainCamera.transform;
        }

        private void EnsureMaterials()
        {
            Shader traceShader = Shader.Find("FluxOut/PCR/Trace");
            Shader iconShader = Shader.Find("FluxOut/PCR/SDFIcon");
            Shader playerLightShader = Shader.Find("FluxOut/PCR/PlayerRadial");
            Shader fallbackOpaque = FindFirstShader(
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Sprites/Default",
                "UI/Default");
            Shader fallbackTransparent = FindFirstShader(
                "Legacy Shaders/Particles/Additive",
                "Particles/Standard Unlit",
                "Legacy Shaders/Particles/Alpha Blended",
                "Unlit/Transparent",
                "Sprites/Default");
            Shader fallback = fallbackOpaque != null ? fallbackOpaque : fallbackTransparent;
            if (fallback == null)
            {
                Debug.LogError("PCRGameController: no fallback shader found. Disabling controller.");
                enabled = false;
                return;
            }

            if (traceShader != null && !traceShader.isSupported)
            {
                Debug.LogWarning("FluxOut/PCR/Trace shader is unsupported on this device. Using fallback shader.");
                traceShader = null;
            }

            if (iconShader != null && !iconShader.isSupported)
            {
                Debug.LogWarning("FluxOut/PCR/SDFIcon shader is unsupported on this device. Using fallback shader.");
                iconShader = null;
            }

            if (playerLightShader != null && !playerLightShader.isSupported)
            {
                Debug.LogWarning("FluxOut/PCR/PlayerRadial shader is unsupported on this device. Using fallback shader.");
                playerLightShader = null;
            }

            if (traceShader == null)
            {
                Debug.LogWarning("FluxOut/PCR/Trace shader not found in player build. Using fallback shader.");
            }

            if (iconShader == null)
            {
                Debug.LogWarning("FluxOut/PCR/SDFIcon shader not found in player build. Using fallback shader.");
            }

            if (playerLightShader == null)
            {
                Debug.LogWarning("FluxOut/PCR/PlayerRadial shader not found in player build. Using fallback shader.");
            }

            traceMatLive = new Material(traceShader != null ? traceShader : fallback);
            SetColorSafe(traceMatLive, "_BaseColor", new Color(0.3f, 0.62f, 1f, 1f));
            SetColorSafe(traceMatLive, "_CoreColor", new Color(0.62f, 0.82f, 1f, 1f));
            SetColorSafe(traceMatLive, "_MidColor", new Color(0.34f, 0.56f, 0.98f, 1f));
            SetColorSafe(traceMatLive, "_EdgeColor", new Color(0.16f, 0.06f, 0.3f, 1f));
            SetFloatSafe(traceMatLive, "_Glow", 1.02f);
            SetFloatSafe(traceMatLive, "_PulseSpeed", 1.4f);
            SetFloatSafe(traceMatLive, "_CoreWidth", 0.47f);
            SetFloatSafe(traceMatLive, "_EdgeSoftness", 0.18f);
            SetFloatSafe(traceMatLive, "_PulseContrast", 0.75f);
            SetFloatSafe(traceMatLive, "_FlowTiling", 18f);
            SetColorSafe(traceMatLive, "_Color", new Color(0.34f, 0.74f, 1f, 1f));

            traceMatDead = new Material(traceShader != null ? traceShader : fallback);
            SetColorSafe(traceMatDead, "_BaseColor", new Color(0.18f, 0.06f, 0.08f, 1f));
            SetColorSafe(traceMatDead, "_CoreColor", new Color(0.44f, 0.16f, 0.2f, 1f));
            SetColorSafe(traceMatDead, "_MidColor", new Color(0.28f, 0.1f, 0.14f, 1f));
            SetColorSafe(traceMatDead, "_EdgeColor", new Color(0.12f, 0.04f, 0.06f, 1f));
            SetFloatSafe(traceMatDead, "_Glow", 0.28f);
            SetFloatSafe(traceMatDead, "_PulseSpeed", 0.2f);
            SetFloatSafe(traceMatDead, "_CoreWidth", 0.42f);
            SetFloatSafe(traceMatDead, "_EdgeSoftness", 0.2f);
            SetFloatSafe(traceMatDead, "_PulseContrast", 0.02f);
            SetFloatSafe(traceMatDead, "_FlowTiling", 12f);
            SetColorSafe(traceMatDead, "_Color", new Color(0.18f, 0.06f, 0.08f, 1f));

            traceMatGlow = new Material(traceShader != null ? traceShader : fallback);
            SetColorSafe(traceMatGlow, "_BaseColor", new Color(0.36f, 0.38f, 1f, 1f));
            SetColorSafe(traceMatGlow, "_CoreColor", new Color(0.68f, 0.72f, 1f, 1f));
            SetColorSafe(traceMatGlow, "_MidColor", new Color(0.36f, 0.4f, 0.96f, 1f));
            SetColorSafe(traceMatGlow, "_EdgeColor", new Color(0.16f, 0.08f, 0.32f, 1f));
            SetFloatSafe(traceMatGlow, "_Glow", 1.2f);
            SetFloatSafe(traceMatGlow, "_PulseSpeed", 0.95f);
            SetFloatSafe(traceMatGlow, "_CoreWidth", 0.5f);
            SetFloatSafe(traceMatGlow, "_EdgeSoftness", 0.22f);
            SetFloatSafe(traceMatGlow, "_PulseContrast", 0.7f);
            SetFloatSafe(traceMatGlow, "_FlowTiling", 11f);
            SetColorSafe(traceMatGlow, "_Color", new Color(0.28f, 0.56f, 1f, 1f));

            iconMatBase = new Material(iconShader != null ? iconShader : (fallbackTransparent != null ? fallbackTransparent : fallback));
            SetColorSafe(iconMatBase, "_BaseColor", Color.white);
            SetColorSafe(iconMatBase, "_Color", Color.white);
            SetFloatSafe(iconMatBase, "_Glow", 0.92f);
            SetFloatSafe(iconMatBase, "_Outline", 0.14f);
            SetFloatSafe(iconMatBase, "_FillBoost", 1.36f);
            SetFloatSafe(iconMatBase, "_PassiveDim", 0.45f);

            playerMat = new Material(traceShader != null ? traceShader : fallback);
            SetColorSafe(playerMat, "_BaseColor", new Color(0.32f, 0.82f, 1f, 1f));
            SetColorSafe(playerMat, "_CoreColor", new Color(0.94f, 0.98f, 1f, 1f));
            SetColorSafe(playerMat, "_MidColor", new Color(0.3f, 0.8f, 1f, 1f));
            SetColorSafe(playerMat, "_EdgeColor", new Color(0.1f, 0.24f, 0.56f, 1f));
            SetFloatSafe(playerMat, "_Glow", 1.45f);
            SetFloatSafe(playerMat, "_PulseSpeed", 1.55f);
            SetFloatSafe(playerMat, "_CoreWidth", 0.52f);
            SetFloatSafe(playerMat, "_EdgeSoftness", 0.15f);
            SetFloatSafe(playerMat, "_PulseContrast", 0.62f);
            SetFloatSafe(playerMat, "_FlowTiling", 7f);
            SetColorSafe(playerMat, "_Color", new Color(0.32f, 0.82f, 1f, 1f));

            Shader haloShader = FindFirstShader("Legacy Shaders/Particles/Alpha Blended", "Particles/Standard Unlit", "Unlit/Transparent", "Unlit/Color");
            haloMat = new Material(haloShader != null ? haloShader : (fallbackTransparent != null ? fallbackTransparent : fallback));
            SetColorSafe(haloMat, "_BaseColor", new Color(0.56f, 0.92f, 1f, 0.32f));
            SetColorSafe(haloMat, "_Color", new Color(0.56f, 0.92f, 1f, 0.32f));
            SetColorSafe(haloMat, "_TintColor", new Color(0.56f, 0.92f, 1f, 0.32f));

            sparkMat = new Material(haloShader != null ? haloShader : (fallbackTransparent != null ? fallbackTransparent : fallback));
            SetColorSafe(sparkMat, "_BaseColor", new Color(1f, 0.8f, 0.32f, 0.68f));
            SetColorSafe(sparkMat, "_Color", new Color(1f, 0.8f, 0.32f, 0.68f));
            SetColorSafe(sparkMat, "_TintColor", new Color(1f, 0.8f, 0.32f, 0.68f));

            Shader lightFallback = traceShader != null ? traceShader : (fallbackTransparent != null ? fallbackTransparent : fallback);
            playerLightMat = new Material(playerLightShader != null ? playerLightShader : lightFallback);
            SetColorSafe(playerLightMat, "_CoreColor", new Color(1f, 0.95f, 0.56f, 1f));
            SetColorSafe(playerLightMat, "_OuterColor", new Color(1f, 0.66f, 0.24f, 1f));
            SetFloatSafe(playerLightMat, "_Intensity", 0.92f);
            SetFloatSafe(playerLightMat, "_Radius", 0.72f);
            SetFloatSafe(playerLightMat, "_Feather", 0.44f);
            SetFloatSafe(playerLightMat, "_Pulse", 0.08f);
            SetFloatSafe(playerLightMat, "_PulseSpeed", 1.05f);
            SetColorSafe(playerLightMat, "_BaseColor", new Color(1f, 0.78f, 0.2f, 1f));
            SetColorSafe(playerLightMat, "_MidColor", new Color(1f, 0.68f, 0.28f, 1f));
            SetColorSafe(playerLightMat, "_EdgeColor", new Color(0.12f, 0.08f, 0.02f, 1f));
            SetFloatSafe(playerLightMat, "_Glow", 0.84f);
            SetFloatSafe(playerLightMat, "_CoreWidth", 0.5f);
            SetFloatSafe(playerLightMat, "_EdgeSoftness", 0.38f);
            SetFloatSafe(playerLightMat, "_PulseContrast", 0.28f);
            SetFloatSafe(playerLightMat, "_FlowTiling", 4f);
            SetColorSafe(playerLightMat, "_Color", new Color(1f, 0.78f, 0.2f, 1f));

            Shader wakeShader = Shader.Find("FluxOut/PCR/Wake");
            if (wakeShader != null && !wakeShader.isSupported)
            {
                wakeShader = null;
            }

            wakeMat = new Material(wakeShader != null ? wakeShader : (haloShader != null ? haloShader : (fallbackTransparent != null ? fallbackTransparent : fallback)));
            SetColorSafe(wakeMat, "_TintColor", new Color(0.22f, 0.88f, 1f, 0.56f));
            SetColorSafe(wakeMat, "_Color", new Color(0.22f, 0.88f, 1f, 0.56f));
            SetColorSafe(wakeMat, "_BaseColor", new Color(0.22f, 0.88f, 1f, 0.56f));
            SetFloatSafe(wakeMat, "_Intensity", 1.12f);
            SetFloatSafe(wakeMat, "_Softness", 0.42f);
            SetFloatSafe(wakeMat, "_ForwardFade", 0.72f);
        }

        private static Shader FindFirstShader(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i]))
                {
                    continue;
                }

                Shader shader = Shader.Find(names[i]);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static void SetColorSafe(Material material, string propertyName, Color value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetFloatSafe(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetVectorSafe(Material material, string propertyName, Vector4 value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetVector(propertyName, value);
            }
        }

        private void EnsureUiFonts()
        {
            if (menuTitleFont != null && menuBodyFont != null)
            {
                return;
            }

            if (Event.current == null || GUI.skin == null)
            {
                return;
            }

            // Avoid runtime font-name lookup warnings on Android devices.
            menuTitleFont = GUI.skin.font;
            menuBodyFont = GUI.skin.font;
        }

        private void UpdateBackgroundParallax(Vector3 playerPos, Color phaseColor, bool phaseA)
        {
            if (parallaxLayers.Count == 0)
            {
                return;
            }

            Color cool = new Color(0.14f, 0.42f, 0.62f, 1f);
            Color warm = new Color(0.62f, 0.3f, 0.14f, 1f);
            Color phaseTint = phaseA ? Color.Lerp(cool, phaseColor, 0.38f) : Color.Lerp(warm, phaseColor, 0.38f);
            float t = Time.time;
            for (int i = 0; i < parallaxLayers.Count; i++)
            {
                ParallaxLayerState layer = parallaxLayers[i];
                if (layer.Material == null)
                {
                    continue;
                }

                Vector2 offset = new Vector2(
                    playerPos.x * layer.OffsetScale + t * layer.ScrollVelocity.x,
                    playerPos.z * layer.OffsetScale * 0.22f + t * layer.ScrollVelocity.y);
                SetVectorSafe(layer.Material, "_ParallaxOffset", new Vector4(offset.x, offset.y, 0f, 0f));

                if (layer.LayerTransform != null)
                {
                    Vector3 basePos = layer.BasePosition;
                    layer.LayerTransform.position = new Vector3(
                        basePos.x - playerPos.x * layer.WorldParallax,
                        basePos.y,
                        basePos.z - playerPos.z * layer.WorldParallax * 0.24f);
                }

                if (layer.Material.HasProperty("_AccentA"))
                {
                    Color accentA = Color.Lerp(new Color(0.09f, 0.26f, 0.45f, 1f), phaseTint, layer.PhaseBlend);
                    layer.Material.SetColor("_AccentA", accentA);
                }

                if (layer.Material.HasProperty("_AccentB"))
                {
                    Color accentB = Color.Lerp(new Color(0.08f, 0.2f, 0.34f, 1f), phaseTint, layer.PhaseBlend * 0.72f);
                    layer.Material.SetColor("_AccentB", accentB);
                }
            }
        }

        private static float ComputeUiScale()
        {
            float dpi = Screen.dpi;
            if (dpi > 0f)
            {
                return Mathf.Clamp(dpi / 210f, 1f, 2.2f);
            }

            float shortSide = Mathf.Min(Screen.width, Screen.height);
            return Mathf.Clamp(shortSide / 1080f, 1f, 1.7f);
        }

        private void ApplyGuiScale(float scale)
        {
            guiScale = Mathf.Max(0.1f, scale);
            GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTexture);
            GUI.color = prev;
        }

        private void EnsureWorldRoot()
        {
            if (worldRoot != null)
            {
                return;
            }

            worldRoot = new GameObject("PCR_World");
            worldRoot.transform.SetParent(transform, false);
            traceRoot = new GameObject("TraceRoot");
            traceRoot.transform.SetParent(worldRoot.transform, false);
            elementRoot = new GameObject("ElementRoot");
            elementRoot.transform.SetParent(worldRoot.transform, false);
        }

        private void ClearRuntimeObjects()
        {
            for (int i = 0; i < runtimeObjects.Count; i++)
            {
                if (runtimeObjects[i] != null)
                {
                    Destroy(runtimeObjects[i]);
                }
            }

            runtimeObjects.Clear();
            eventViews.Clear();
            decisionViews.Clear();
            parallaxLayers.Clear();
            wakeDecals.Clear();
            wakeDecalCursor = 0;
            if (playerRoot != null)
            {
                Destroy(playerRoot);
            }
        }

        private void BuildTraceVisuals()
        {
            for (int lane = 0; lane < level.MaxLaneCount; lane++)
            {
                bool inRun = false;
                bool segmentDead = false;
                int runStart = 0;
                for (int step = 0; step < level.TotalSteps; step++)
                {
                    bool laneActive = lane < PCRSimulation.GetLaneCountAtStep(level, step);
                    bool dead = laneActive && PCRSimulation.IsDeadTrace(level, step, lane);
                    if (!inRun && laneActive)
                    {
                        inRun = true;
                        segmentDead = dead;
                        runStart = step;
                    }

                    bool boundary = inRun && (!laneActive || dead != segmentDead || step == level.TotalSteps - 1);
                    if (!boundary)
                    {
                        continue;
                    }

                    int runEnd = step == level.TotalSteps - 1 && laneActive && dead == segmentDead ? step : step - 1;
                    if (runEnd - runStart >= 1)
                    {
                        GameObject go = new GameObject($"Trace_L{lane}_{runStart}_{runEnd}");
                        go.transform.SetParent(traceRoot.transform, false);
                        var lr = go.AddComponent<LineRenderer>();
                        lr.alignment = LineAlignment.View;
                        lr.textureMode = LineTextureMode.Tile;
                        lr.numCapVertices = 3;
                        lr.numCornerVertices = 2;
                        lr.shadowCastingMode = ShadowCastingMode.Off;
                        lr.receiveShadows = false;
                        lr.startWidth = 0.36f;
                        lr.endWidth = 0.36f;
                        lr.widthMultiplier = 1f;
                        lr.material = segmentDead ? traceMatDead : traceMatLive;
                        Color laneColor = segmentDead ? new Color(0.14f, 0.14f, 0.16f, 0.9f) : new Color(0.26f, 0.96f, 0.44f, 0.94f);
                        lr.startColor = laneColor;
                        lr.endColor = laneColor;
                        int pointCount = runEnd - runStart + 1;
                        lr.positionCount = pointCount;
                        for (int p = 0; p < pointCount; p++)
                        {
                            int stepIndex = runStart + p;
                            lr.SetPosition(p, GetLanePointAtStep(stepIndex, lane));
                        }

                        runtimeObjects.Add(go);

                        if (segmentDead)
                        {
                            BuildDeadLaneOverlay(runStart, runEnd, lane);
                        }
                        else
                        {
                            GameObject glowGo = new GameObject($"TraceGlow_L{lane}_{runStart}_{runEnd}");
                            glowGo.transform.SetParent(traceRoot.transform, false);
                            var glow = glowGo.AddComponent<LineRenderer>();
                            glow.alignment = LineAlignment.View;
                            glow.textureMode = LineTextureMode.Tile;
                            glow.numCapVertices = 3;
                            glow.numCornerVertices = 2;
                            glow.shadowCastingMode = ShadowCastingMode.Off;
                            glow.receiveShadows = false;
                            glow.startWidth = 0.78f;
                            glow.endWidth = 0.78f;
                            glow.widthMultiplier = 1f;
                            glow.material = traceMatGlow != null ? traceMatGlow : traceMatLive;
                            Color glowColor = new Color(0.44f, 1f, 0.58f, 0.28f);
                            glow.startColor = glowColor;
                            glow.endColor = glowColor;
                            glow.positionCount = pointCount;
                            for (int p = 0; p < pointCount; p++)
                            {
                                int stepIndex = runStart + p;
                                glow.SetPosition(p, GetLanePointAtStep(stepIndex, lane));
                            }

                            runtimeObjects.Add(glowGo);
                        }
                    }

                    inRun = laneActive;
                    segmentDead = dead;
                    runStart = step;
                }
            }

            BuildBackgroundParallax();
        }

        private void BuildDeadLaneOverlay(int runStart, int runEnd, int lane)
        {
            int pointCount = runEnd - runStart + 1;
            if (pointCount < 2)
            {
                return;
            }

            var deadGlowGo = new GameObject($"TraceDeadGlow_L{lane}_{runStart}_{runEnd}");
            deadGlowGo.transform.SetParent(traceRoot.transform, false);
            var deadGlow = deadGlowGo.AddComponent<LineRenderer>();
            deadGlow.alignment = LineAlignment.View;
            deadGlow.textureMode = LineTextureMode.Tile;
            deadGlow.numCapVertices = 3;
            deadGlow.numCornerVertices = 2;
            deadGlow.shadowCastingMode = ShadowCastingMode.Off;
            deadGlow.receiveShadows = false;
            deadGlow.startWidth = 0.64f;
            deadGlow.endWidth = 0.64f;
            deadGlow.widthMultiplier = 1f;
            deadGlow.material = traceMatDead;
            Color deadGlowColor = new Color(0.18f, 0.18f, 0.2f, 0.24f);
            deadGlow.startColor = deadGlowColor;
            deadGlow.endColor = deadGlowColor;
            deadGlow.positionCount = pointCount;
            for (int p = 0; p < pointCount; p++)
            {
                int stepIndex = runStart + p;
                deadGlow.SetPosition(p, GetLanePointAtStep(stepIndex, lane));
            }

            runtimeObjects.Add(deadGlowGo);
            BuildDeadLaneMarkers(runStart, runEnd, lane);
        }

        private void BuildDeadLaneMarkers(int runStart, int runEnd, int lane)
        {
            if (iconMatBase == null)
            {
                return;
            }

            int markerEverySteps = 12;
            for (int step = runStart + markerEverySteps / 2; step <= runEnd; step += markerEverySteps)
            {
                var marker = CreatePrimitiveNoCollider($"DeadMarker_L{lane}_{step}", PrimitiveType.Quad, traceRoot.transform);
                marker.transform.position = GetLanePointAtStep(step, lane) + Vector3.up * 0.05f;
                marker.transform.rotation = Quaternion.LookRotation(Vector3.up, level.Tangents[Mathf.Clamp(step, 0, level.TotalSteps - 1)]);
                marker.transform.localScale = new Vector3(0.52f, 0.52f, 1f);
                var renderer = marker.GetComponent<MeshRenderer>();
                var markerMat = new Material(iconMatBase);
                SetFloatSafe(markerMat, "_IconType", 5f);
                SetFloatSafe(markerMat, "_PhaseActive", 0.58f);
                SetFloatSafe(markerMat, "_ArrowDir", 1f);
                SetFloatSafe(markerMat, "_Glow", 0.64f);
                SetColorSafe(markerMat, "_BaseColor", new Color(0.96f, 0.36f, 0.52f, 1f));
                SetColorSafe(markerMat, "_Color", new Color(0.96f, 0.36f, 0.52f, 1f));
                renderer.sharedMaterial = markerMat;
                runtimeObjects.Add(marker);
            }
        }

        private void BuildBackgroundParallax()
        {
            float zEnd = level.StepDistances[level.TotalSteps - 1] + 20f;
            parallaxLayers.Clear();
            Shader bgShader = Shader.Find("FluxOut/PCR/Background");
            Shader fallback = FindFirstShader(
                "Legacy Shaders/Particles/Alpha Blended",
                "Particles/Standard Unlit",
                "Unlit/Transparent",
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Hidden/InternalErrorShader");

            CreateParallaxLayer(
                "BgLayer_FarNebula",
                y: -0.34f,
                zCenter: zEnd * 0.5f,
                scaleX: 280f,
                scaleZ: zEnd + 120f,
                top: new Color(0.036f, 0.062f, 0.106f, 1f),
                bottom: new Color(0.01f, 0.016f, 0.032f, 1f),
                accentA: new Color(0.16f, 0.34f, 0.54f, 1f),
                accentB: new Color(0.13f, 0.27f, 0.44f, 1f),
                patternScale: 4.6f,
                patternStrength: 0.28f,
                detailStrength: 0.26f,
                driftSpeed: 0.08f,
                vignette: 0.46f,
                alpha: 0.68f,
                depth: 0.08f,
                worldParallax: 0.028f,
                scrollVelocity: new Vector2(0.007f, -0.003f),
                bgShader: bgShader,
                fallback: fallback);

            CreateParallaxLayer(
                "BgLayer_MidPCB",
                y: -0.26f,
                zCenter: zEnd * 0.5f,
                scaleX: 236f,
                scaleZ: zEnd + 88f,
                top: new Color(0.072f, 0.118f, 0.19f, 1f),
                bottom: new Color(0.024f, 0.034f, 0.052f, 1f),
                accentA: new Color(0.24f, 0.5f, 0.74f, 1f),
                accentB: new Color(0.2f, 0.36f, 0.56f, 1f),
                patternScale: 9.6f,
                patternStrength: 0.36f,
                detailStrength: 0.32f,
                driftSpeed: 0.16f,
                vignette: 0.54f,
                alpha: 0.6f,
                depth: 0.42f,
                worldParallax: 0.064f,
                scrollVelocity: new Vector2(0.016f, -0.006f),
                bgShader: bgShader,
                fallback: fallback);

            CreateParallaxLayer(
                "BgLayer_NearFog",
                y: -0.19f,
                zCenter: zEnd * 0.5f,
                scaleX: 196f,
                scaleZ: zEnd + 72f,
                top: new Color(0.078f, 0.126f, 0.2f, 1f),
                bottom: new Color(0.028f, 0.04f, 0.06f, 1f),
                accentA: new Color(0.26f, 0.6f, 0.82f, 1f),
                accentB: new Color(0.2f, 0.44f, 0.64f, 1f),
                patternScale: 6.2f,
                patternStrength: 0.24f,
                detailStrength: 0.24f,
                driftSpeed: 0.2f,
                vignette: 0.6f,
                alpha: 0.38f,
                depth: 0.78f,
                worldParallax: 0.12f,
                scrollVelocity: new Vector2(0.03f, -0.012f),
                bgShader: bgShader,
                fallback: fallback);
        }

        private void CreateParallaxLayer(
            string name,
            float y,
            float zCenter,
            float scaleX,
            float scaleZ,
            Color top,
            Color bottom,
            Color accentA,
            Color accentB,
            float patternScale,
            float patternStrength,
            float detailStrength,
            float driftSpeed,
            float vignette,
            float alpha,
            float depth,
            float worldParallax,
            Vector2 scrollVelocity,
            Shader bgShader,
            Shader fallback)
        {
            var layer = CreatePrimitiveNoCollider(name, PrimitiveType.Quad, traceRoot.transform);
            layer.transform.position = new Vector3(0f, y, zCenter);
            layer.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            layer.transform.localScale = new Vector3(scaleX, scaleZ, 1f);

            var renderer = layer.GetComponent<MeshRenderer>();
            bool useBgShader = bgShader != null && bgShader.isSupported;
            Material mat = new Material(useBgShader ? bgShader : fallback);
            SetColorSafe(mat, "_TopColor", top);
            SetColorSafe(mat, "_BottomColor", bottom);
            SetColorSafe(mat, "_AccentA", accentA);
            SetColorSafe(mat, "_AccentB", accentB);
            SetFloatSafe(mat, "_PatternScale", patternScale);
            SetFloatSafe(mat, "_PatternStrength", patternStrength);
            SetFloatSafe(mat, "_DetailStrength", detailStrength);
            SetFloatSafe(mat, "_DriftSpeed", driftSpeed);
            SetFloatSafe(mat, "_Vignette", vignette);
            SetFloatSafe(mat, "_Alpha", alpha);
            SetFloatSafe(mat, "_LayerDepth", depth);
            SetVectorSafe(mat, "_ParallaxOffset", Vector4.zero);
            Color fallbackColor = Color.Lerp(bottom, Color.Lerp(top, accentA, 0.42f), 0.72f);
            fallbackColor.a = Mathf.Clamp(alpha, 0.34f, 0.95f);
            SetColorSafe(mat, "_Color", fallbackColor);
            renderer.sharedMaterial = mat;
            runtimeObjects.Add(layer);

            parallaxLayers.Add(new ParallaxLayerState
            {
                Material = mat,
                LayerTransform = layer.transform,
                BasePosition = layer.transform.position,
                ScrollVelocity = scrollVelocity,
                OffsetScale = Mathf.Lerp(0.03f, 0.12f, depth),
                PhaseBlend = Mathf.Lerp(0.07f, 0.18f, depth),
                WorldParallax = worldParallax
            });
        }

        private void BuildElements()
        {
            var laneStepCounts = new Dictionary<int, int>();
            var laneStepOrders = new Dictionary<int, int>();
            for (int i = 0; i < level.Events.Count; i++)
            {
                PCREventRuntime countEvt = level.Events[i];
                if (countEvt.Type != PCRElementType.MuxSwitch)
                {
                    continue;
                }

                int countKey = (countEvt.StepIndex << 4) ^ (countEvt.LaneIndex & 0xF);
                laneStepCounts.TryGetValue(countKey, out int existing);
                laneStepCounts[countKey] = existing + 1;
            }

            for (int i = 0; i < level.Events.Count; i++)
            {
                PCREventRuntime evt = level.Events[i];
                if (evt.Type != PCRElementType.MuxSwitch)
                {
                    continue;
                }

                int denseKey = (evt.StepIndex << 4) ^ (evt.LaneIndex & 0xF);
                laneStepCounts.TryGetValue(denseKey, out int densityAtSlot);
                laneStepOrders.TryGetValue(denseKey, out int slotOrder);
                laneStepOrders[denseKey] = slotOrder + 1;
                bool crowdedSlot = densityAtSlot > 1;
                int tangentStep = Mathf.Clamp(evt.StepIndex, 0, level.TotalSteps - 1);
                Vector3 tangent = level.Tangents[tangentStep];
                if (tangent.sqrMagnitude < 0.0001f)
                {
                    tangent = Vector3.forward;
                }

                tangent.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                float centeredOrder = slotOrder - (densityAtSlot - 1) * 0.5f;
                Vector3 slotSpread = crowdedSlot
                    ? tangent * (centeredOrder * 0.42f) + right * (Mathf.Sign(centeredOrder) * 0.14f)
                    : Vector3.zero;
                Vector3 iconPos = evt.WorldPosition + slotSpread;

                var go = CreatePrimitiveNoCollider($"Evt_{evt.Type}_{evt.EventId}", PrimitiveType.Quad, elementRoot.transform);
                go.transform.position = iconPos + Vector3.up * 0.14f;
                go.transform.rotation = Quaternion.LookRotation(Vector3.up, tangent);
                go.transform.localScale = evt.IsField ? new Vector3(2.15f, 2.15f, 1f) : new Vector3(1.38f, 1.38f, 1f);

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = new Material(iconMatBase);
                var view = go.AddComponent<PCREventView>();
                view.Initialize(evt, renderer, level, runState.Phase);
                eventViews.Add(view);
                runtimeObjects.Add(go);

                Color keyColor = GetElementKeyColor(evt.Type);
                if (!crowdedSlot)
                {
                    var baseMarker = CreatePrimitiveNoCollider($"EvtBase_{evt.Type}_{evt.EventId}", PrimitiveType.Cube, elementRoot.transform);
                    baseMarker.transform.position = iconPos + Vector3.up * 0.04f;
                    baseMarker.transform.rotation = go.transform.rotation;
                    baseMarker.transform.localScale = evt.IsField ? new Vector3(0.34f, 0.1f, 1.64f) : new Vector3(0.26f, 0.1f, 0.88f);
                    var baseRenderer = baseMarker.GetComponent<MeshRenderer>();
                    var baseMat = new Material(traceMatDead);
                    Color baseMarkerColor = evt.IsField
                        ? Color.Lerp(keyColor, Color.white, 0.12f)
                        : Color.Lerp(keyColor, new Color(0.14f, 0.1f, 0.2f, 1f), 0.5f);
                    SetColorSafe(baseMat, "_BaseColor", baseMarkerColor);
                    SetColorSafe(baseMat, "_Color", baseMarkerColor);
                    SetFloatSafe(baseMat, "_Glow", evt.IsField ? 0.42f : 0.24f);
                    baseRenderer.sharedMaterial = baseMat;
                    runtimeObjects.Add(baseMarker);

                    if (evt.IsField)
                    {
                        var ring = CreatePrimitiveNoCollider($"EvtRing_{evt.Type}_{evt.EventId}", PrimitiveType.Quad, elementRoot.transform);
                        ring.transform.position = iconPos + Vector3.up * 0.03f;
                        ring.transform.rotation = go.transform.rotation;
                        ring.transform.localScale = new Vector3(2.56f, 2.56f, 1f);
                        var ringRenderer = ring.GetComponent<MeshRenderer>();
                        var ringMat = new Material(traceMatGlow != null ? traceMatGlow : traceMatLive);
                        Color ringColor = new Color(keyColor.r, keyColor.g, keyColor.b, 0.22f);
                        SetColorSafe(ringMat, "_BaseColor", ringColor);
                        SetColorSafe(ringMat, "_Color", ringColor);
                        SetFloatSafe(ringMat, "_Glow", 0.84f);
                        ringRenderer.sharedMaterial = ringMat;
                        runtimeObjects.Add(ring);
                    }
                }

            }
        }

        private void BuildDecisionTelegraphs()
        {
            if (level.Decisions == null || level.Decisions.Count == 0 || iconMatBase == null)
            {
                return;
            }

            const int maxTelegraphs = 20;
            const int minDecisionStepGap = 5;
            int builtCount = 0;
            int lastStep = -999;
            var occupiedSlots = new HashSet<int>();
            for (int i = 0; i < level.Decisions.Count; i++)
            {
                PCRDecisionWindow decision = level.Decisions[i];
                if (decision.RequiredMask == PCRPhaseMask.None)
                {
                    continue;
                }

                int decisionStep = Mathf.Clamp(decision.StepIndex, 1, level.TotalSteps - 2);
                if (decisionStep - lastStep < minDecisionStepGap)
                {
                    continue;
                }

                int telegraphStep = Mathf.Clamp(decisionStep - Mathf.Max(1, decision.LeadSteps), 0, level.TotalSteps - 1);
                if (telegraphStep <= 1)
                {
                    continue;
                }

                int laneCount = PCRSimulation.GetLaneCountAtStep(level, telegraphStep);
                int lane = decision.LaneIndex >= 0 ? Mathf.Clamp(decision.LaneIndex, 0, laneCount - 1) : Mathf.Clamp((laneCount - 1) / 2, 0, laneCount - 1);
                int slotKey = (telegraphStep << 4) ^ (lane & 0xF);
                if (!occupiedSlots.Add(slotKey))
                {
                    continue;
                }

                Vector3 pos = GetLanePointAtStep(telegraphStep, lane) + Vector3.up * 0.24f;

                var go = CreatePrimitiveNoCollider($"Decision_{decisionStep}_{decision.Reason}", PrimitiveType.Quad, elementRoot.transform);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(Vector3.up, level.Tangents[Mathf.Clamp(telegraphStep, 0, level.TotalSteps - 1)]);
                go.transform.localScale = new Vector3(1.18f, 1.18f, 1f);

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = new Material(iconMatBase);
                var view = go.AddComponent<PCRDecisionTelegraphView>();
                view.Initialize(decision, renderer);
                decisionViews.Add(view);
                runtimeObjects.Add(go);
                lastStep = decisionStep;
                builtCount++;
                if (builtCount >= maxTelegraphs)
                {
                    break;
                }
            }
        }

        private void BuildFieldZone(PCREventRuntime evt, Vector3 origin, Color color)
        {
            var zone = CreatePrimitiveNoCollider($"Field_{evt.Type}_{evt.EventId}", PrimitiveType.Quad, elementRoot.transform);
            zone.transform.position = origin + Vector3.up * 0.02f;
            zone.transform.rotation = Quaternion.LookRotation(Vector3.up, level.Tangents[Mathf.Clamp(evt.StepIndex, 0, level.TotalSteps - 1)]);
            zone.transform.localScale = new Vector3(2.34f, 1.68f, 1f);
            var renderer = zone.GetComponent<MeshRenderer>();
            Shader alphaShader = FindFirstShader(
                "Legacy Shaders/Particles/Alpha Blended",
                "Particles/Standard Unlit",
                "Unlit/Transparent",
                "Unlit/Color",
                "Universal Render Pipeline/Unlit",
                "Hidden/InternalErrorShader");
            var mat = new Material(alphaShader != null ? alphaShader : traceMatLive.shader);
            SetColorSafe(mat, "_BaseColor", color);
            SetColorSafe(mat, "_Color", color);
            SetColorSafe(mat, "_TintColor", color);
            renderer.sharedMaterial = mat;
            runtimeObjects.Add(zone);
        }

        private static Color GetElementKeyColor(PCRElementType type)
        {
            return type == PCRElementType.MuxSwitch
                ? new Color(0.76f, 0.95f, 1f, 1f)
                : Color.white;
        }

        private void BuildPlayer()
        {
            if (traceMatLive == null || playerMat == null || haloMat == null)
            {
                EnsureMaterials();
            }

            try
            {
                playerRoot = new GameObject("PlayerElectron");
                playerRoot.transform.SetParent(worldRoot.transform, false);

                var core = CreatePrimitiveNoCollider("Core", PrimitiveType.Sphere, playerRoot.transform);
                core.transform.localScale = Vector3.one * 0.32f;
                playerCoreRenderer = core.GetComponent<Renderer>();
                if (playerCoreRenderer != null)
                {
                    playerCoreRenderer.sharedMaterial = playerMat != null ? playerMat : traceMatLive;
                }

                var glowShell = CreatePrimitiveNoCollider("GlowShell", PrimitiveType.Sphere, playerRoot.transform);
                glowShell.transform.localScale = Vector3.one * 0.54f;
                playerGlowRenderer = glowShell.GetComponent<Renderer>();
                if (playerGlowRenderer != null)
                {
                    playerGlowRenderer.sharedMaterial = haloMat != null ? haloMat : traceMatGlow;
                }

                var auraShell = CreatePrimitiveNoCollider("AuraShell", PrimitiveType.Sphere, playerRoot.transform);
                auraShell.transform.localScale = Vector3.one * 0.8f;
                playerAuraRenderer = auraShell.GetComponent<Renderer>();
                if (playerAuraRenderer != null)
                {
                    playerAuraRenderer.sharedMaterial = haloMat != null ? haloMat : traceMatGlow;
                }

                var lightDisc = CreatePrimitiveNoCollider("LocalLightDisc", PrimitiveType.Quad, playerRoot.transform);
                lightDisc.transform.localPosition = new Vector3(0f, -0.1f, -0.86f);
                lightDisc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                lightDisc.transform.localScale = new Vector3(2.1f, 6.5f, 1f);
                playerLightRenderer = lightDisc.GetComponent<Renderer>();
                if (playerLightRenderer != null)
                {
                    playerLightRenderer.sharedMaterial = playerLightMat != null ? playerLightMat : traceMatGlow;
                    playerLightRenderer.enabled = false;
                }

                BuildPlayerOrbitRing();

                var charge = new GameObject("ChargeSign");
                charge.transform.SetParent(playerRoot.transform, false);
                charge.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                playerChargeText = charge.AddComponent<TextMesh>();
                playerChargeText.anchor = TextAnchor.MiddleCenter;
                playerChargeText.alignment = TextAlignment.Center;
                playerChargeText.fontSize = 128;
                playerChargeText.characterSize = 0.085f;
                playerChargeText.text = "+";
                playerChargeText.color = Color.white;

                playerTrail = playerRoot.AddComponent<TrailRenderer>();
                playerTrail.time = 0.45f;
                playerTrail.minVertexDistance = 0.02f;
                playerTrail.startWidth = 0.18f;
                playerTrail.endWidth = 0.015f;
                playerTrail.material = traceMatLive;

                var lightTrailAnchor = new GameObject("LightTrailAnchor");
                lightTrailAnchor.transform.SetParent(playerRoot.transform, false);
                lightTrailAnchor.transform.localPosition = new Vector3(0f, -0.03f, -0.66f);
                playerLightTrail = lightTrailAnchor.AddComponent<TrailRenderer>();
                playerLightTrail.time = 0.62f;
                playerLightTrail.minVertexDistance = 0.024f;
                playerLightTrail.startWidth = 0.28f;
                playerLightTrail.endWidth = 0.02f;
                playerLightTrail.material = traceMatGlow != null ? traceMatGlow : traceMatLive;

                var sparkTrailGo = new GameObject("SparkTrail");
                sparkTrailGo.transform.SetParent(playerRoot.transform, false);
                sparkTrailGo.transform.localPosition = new Vector3(0f, -0.01f, -0.56f);
                sparkTrail = sparkTrailGo.AddComponent<ParticleSystem>();
                ConfigureSparkTrail(sparkTrail);
                sparkTrail.Play();

                var phaseBurstGo = new GameObject("PhaseBurst");
                phaseBurstGo.transform.SetParent(playerRoot.transform, false);
                phaseBurst = phaseBurstGo.AddComponent<ParticleSystem>();
                ConfigureParticle(phaseBurst, new ParticleSettings
                {
                    StartSize = 0.2f,
                    Lifetime = 0.26f,
                    Speed = 3.2f,
                    MaxParticles = 100
                });

                var shiftBurstGo = new GameObject("ShiftBurst");
                shiftBurstGo.transform.SetParent(playerRoot.transform, false);
                shiftBurst = shiftBurstGo.AddComponent<ParticleSystem>();
                ConfigureParticle(shiftBurst, new ParticleSettings
                {
                    StartSize = 0.16f,
                    Lifetime = 0.2f,
                    Speed = 3.6f,
                    MaxParticles = 120
                });

                var deathBurstGo = new GameObject("DeathBurst");
                deathBurstGo.transform.SetParent(playerRoot.transform, false);
                deathBurst = deathBurstGo.AddComponent<ParticleSystem>();
                ConfigureParticle(deathBurst, new ParticleSettings
                {
                    StartSize = 0.28f,
                    Lifetime = 0.34f,
                    Speed = 4.2f,
                    MaxParticles = 130
                });

                BuildWakePool();
            }
            catch (Exception ex)
            {
                Debug.LogError($"PCRGameController: BuildPlayer failed, using minimal fallback. {ex}");
                BuildMinimalPlayerFallback();
            }
        }

        private void BuildMinimalPlayerFallback()
        {
            if (playerRoot != null)
            {
                Destroy(playerRoot);
            }

            playerRoot = new GameObject("PlayerElectron_Fallback");
            playerRoot.transform.SetParent(worldRoot.transform, false);
            var core = CreatePrimitiveNoCollider("CoreFallback", PrimitiveType.Sphere, playerRoot.transform);
            core.transform.localScale = Vector3.one * 0.34f;
            playerCoreRenderer = core.GetComponent<Renderer>();
            if (playerCoreRenderer != null)
            {
                playerCoreRenderer.sharedMaterial = playerMat != null ? playerMat : traceMatLive;
            }

            playerGlowRenderer = null;
            playerAuraRenderer = null;
            playerLightRenderer = null;
            playerOrbitRing = null;
            playerChargeText = null;
            playerTrail = null;
            playerLightTrail = null;
            sparkTrail = null;
            phaseBurst = null;
            shiftBurst = null;
            deathBurst = null;
            wakeDecals.Clear();
        }

        private void BuildPlayerOrbitRing()
        {
            var ringGo = new GameObject("OrbitRing");
            ringGo.transform.SetParent(playerRoot.transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            playerOrbitRing = ringGo.AddComponent<LineRenderer>();
            playerOrbitRing.alignment = LineAlignment.TransformZ;
            playerOrbitRing.shadowCastingMode = ShadowCastingMode.Off;
            playerOrbitRing.receiveShadows = false;
            playerOrbitRing.textureMode = LineTextureMode.Stretch;
            playerOrbitRing.loop = true;
            playerOrbitRing.positionCount = 36;
            playerOrbitRing.startWidth = 0.08f;
            playerOrbitRing.endWidth = 0.08f;
            playerOrbitRing.material = traceMatGlow != null ? traceMatGlow : traceMatLive;
            const float radius = 0.58f;
            for (int i = 0; i < playerOrbitRing.positionCount; i++)
            {
                float t = i / (float)playerOrbitRing.positionCount * Mathf.PI * 2f;
                playerOrbitRing.SetPosition(i, new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius));
            }
        }

        private void BuildWakePool()
        {
            wakeDecals.Clear();
            wakeDecalCursor = 0;
            if (!enableWakeDecals || traceRoot == null)
            {
                return;
            }

            var wakeRoot = new GameObject("WakePool");
            wakeRoot.transform.SetParent(traceRoot.transform, false);
            runtimeObjects.Add(wakeRoot);
            for (int i = 0; i < WakePoolSize; i++)
            {
                var quad = CreatePrimitiveNoCollider($"Wake_{i}", PrimitiveType.Quad, wakeRoot.transform);
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(0.84f, 2.36f, 1f);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = wakeMat != null ? wakeMat : (haloMat != null ? haloMat : traceMatGlow);
                renderer.enabled = false;
                runtimeObjects.Add(quad);
                wakeDecals.Add(new PCRWakeDecal
                {
                    Transform = quad.transform,
                    Renderer = renderer,
                    Block = new MaterialPropertyBlock(),
                    Lifetime = 0.64f
                });
            }
        }

        private void UpdateWakeDecals(float renderStep, Vector3 playerPos, Color phaseColor)
        {
            if (!enableWakeDecals || wakeDecals.Count == 0)
            {
                lastWakeSpawnPos = Vector3.positiveInfinity;
                return;
            }

            for (int i = 0; i < wakeDecals.Count; i++)
            {
                PCRWakeDecal decal = wakeDecals[i];
                if (!decal.Active || decal.Renderer == null)
                {
                    continue;
                }

                decal.Age += Time.deltaTime;
                float t = Mathf.Clamp01(decal.Age / Mathf.Max(0.1f, decal.Lifetime));
                if (t >= 1f)
                {
                    decal.SetActive(false);
                    continue;
                }

                decal.Transform.localScale = Vector3.Lerp(decal.StartScale, decal.StartScale * 1.24f, t);
                decal.Transform.position += decal.Drift * Time.deltaTime;
                float alpha = Mathf.Pow(1f - t, 1.8f) * 0.46f;
                ApplyWakeColor(decal, new Color(decal.Color.r, decal.Color.g, decal.Color.b, alpha));
            }

            bool shouldSpawn = IsPlaying && !runState.Dead;
            if (!shouldSpawn)
            {
                lastWakeSpawnPos = Vector3.positiveInfinity;
                return;
            }

            if (!float.IsFinite(lastWakeSpawnPos.x))
            {
                lastWakeSpawnPos = playerPos;
            }

            Vector3 a = new Vector3(lastWakeSpawnPos.x, 0f, lastWakeSpawnPos.z);
            Vector3 b = new Vector3(playerPos.x, 0f, playerPos.z);
            if (Vector3.Distance(a, b) < WakeSpawnDistance)
            {
                return;
            }

            int step = Mathf.Clamp(Mathf.RoundToInt(renderStep), 0, level.TotalSteps - 1);
            Vector3 tangent = level.Tangents[step];
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.forward;
            }

            SpawnWake(playerPos, tangent.normalized, phaseColor);
            lastWakeSpawnPos = playerPos;
        }

        private void SpawnWake(Vector3 position, Vector3 tangent, Color phaseColor)
        {
            if (wakeDecals.Count == 0)
            {
                return;
            }

            PCRWakeDecal decal = wakeDecals[wakeDecalCursor];
            wakeDecalCursor = (wakeDecalCursor + 1) % wakeDecals.Count;
            decal.Active = true;
            decal.Age = 0f;
            decal.Lifetime = 0.62f + UnityEngine.Random.Range(-0.08f, 0.08f);
            decal.Color = Color.Lerp(new Color(1f, 0.88f, 0.38f, 1f), phaseColor, 0.62f);
            decal.StartScale = new Vector3(0.78f, 2.34f, 1f);
            Vector3 tangentFlat = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, tangentFlat);
            decal.Transform.position = position - tangentFlat * 0.58f + Vector3.up * 0.014f;
            decal.Transform.rotation = rotation;
            decal.Transform.localScale = decal.StartScale;
            decal.Drift = -tangentFlat * (0.18f + UnityEngine.Random.Range(0f, 0.14f));
            decal.Renderer.enabled = true;
            ApplyWakeColor(decal, new Color(decal.Color.r, decal.Color.g, decal.Color.b, 0.44f));
        }

        private static void ApplyWakeColor(PCRWakeDecal decal, Color color)
        {
            if (decal.Renderer == null)
            {
                return;
            }

            decal.Block.Clear();
            decal.Block.SetColor("_Color", color);
            decal.Block.SetColor("_BaseColor", color);
            decal.Block.SetColor("_TintColor", color);
            decal.Block.SetColor("_CoreColor", Color.Lerp(color, Color.white, 0.3f));
            decal.Block.SetColor("_OuterColor", new Color(color.r * 0.26f, color.g * 0.26f, color.b * 0.26f, color.a));
            decal.Block.SetFloat("_Intensity", 1.1f);
            decal.Block.SetFloat("_Glow", 0.82f);
            decal.Renderer.SetPropertyBlock(decal.Block);
        }

        private static GameObject CreatePrimitiveNoCollider(string name, PrimitiveType type, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh = GetBuiltinPrimitiveMesh(type);
            renderer.sharedMaterial = null;
            return go;
        }

        private static Mesh GetBuiltinPrimitiveMesh(PrimitiveType type)
        {
            string meshPath = type switch
            {
                PrimitiveType.Sphere => "New-Sphere.fbx",
                PrimitiveType.Cube => "Cube.fbx",
                PrimitiveType.Quad => "Quad.fbx",
                PrimitiveType.Capsule => "New-Capsule.fbx",
                PrimitiveType.Cylinder => "New-Cylinder.fbx",
                PrimitiveType.Plane => "New-Plane.fbx",
                _ => "Quad.fbx"
            };

            Mesh mesh = Resources.GetBuiltinResource<Mesh>(meshPath);
            if (mesh != null)
            {
                return mesh;
            }

            // Fallback if a mesh name changes between Unity versions.
            return Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        }

        private void SetupPost()
        {
            if (postVolume == null)
            {
                var go = new GameObject("PCR_PostVolume");
                go.transform.SetParent(worldRoot.transform, false);
                postVolume = go.AddComponent<Volume>();
                postVolume.isGlobal = true;
                postVolume.priority = 100f;
                postVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
                runtimeObjects.Add(go);
            }

            if (!postVolume.profile.TryGet(out Bloom bloom))
            {
                bloom = postVolume.profile.Add<Bloom>(true);
            }

            bloom.active = true;
            bloom.intensity.Override(Mathf.Lerp(0.62f, 1.24f, Mathf.Clamp01(debugBloomStrength)));
            bloom.threshold.Override(0.64f);
            bloom.scatter.Override(0.76f);
            if (!postVolume.profile.TryGet(out Vignette vignette))
            {
                vignette = postVolume.profile.Add<Vignette>(true);
            }

            vignette.active = true;
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.38f);
        }

        private void BuildStepCacheIfNeeded(PCRLevelRuntime runtimeLevel)
        {
            runtimeLevel.EventsByStep = new List<PCREventRuntime>[runtimeLevel.TotalSteps];
            for (int i = 0; i < runtimeLevel.TotalSteps; i++)
            {
                runtimeLevel.EventsByStep[i] = new List<PCREventRuntime>();
            }

            for (int i = 0; i < runtimeLevel.Events.Count; i++)
            {
                int step = Mathf.Clamp(runtimeLevel.Events[i].StepIndex, 0, runtimeLevel.TotalSteps - 1);
                runtimeLevel.EventsByStep[step].Add(runtimeLevel.Events[i]);
            }

            runtimeLevel.DecisionsByStep = new List<PCRDecisionWindow>[runtimeLevel.TotalSteps];
            for (int i = 0; i < runtimeLevel.TotalSteps; i++)
            {
                runtimeLevel.DecisionsByStep[i] = new List<PCRDecisionWindow>();
            }

            if (runtimeLevel.Decisions == null)
            {
                runtimeLevel.Decisions = new List<PCRDecisionWindow>();
                return;
            }

            for (int i = 0; i < runtimeLevel.Decisions.Count; i++)
            {
                int step = Mathf.Clamp(runtimeLevel.Decisions[i].StepIndex, 0, runtimeLevel.TotalSteps - 1);
                runtimeLevel.DecisionsByStep[step].Add(runtimeLevel.Decisions[i]);
            }
        }

        private void ApplyDensityMutations(PCRLevelRuntime runtimeLevel, int seed)
        {
            var random = new System.Random(seed + 1337);
            MutateDensityByType(runtimeLevel, PCRElementType.DiodeGate, densityGate, random);
            MutateDensityByType(runtimeLevel, PCRElementType.MuxSwitch, densityMux, random);
            MutateDensityByType(runtimeLevel, PCRElementType.InductorCoupler, densityInductor, random);
            MutateDensityByType(runtimeLevel, PCRElementType.SparkGap, densityArc, random);
            MutateDensityByType(runtimeLevel, PCRElementType.Capacitor, densityCap, random);
            MutateDensityByType(runtimeLevel, PCRElementType.Amplifier, densityAmp, random);
            MutateDensityByType(runtimeLevel, PCRElementType.GroundClamp, densityClamp, random);
        }

        private static void MutateDensityByType(PCRLevelRuntime runtimeLevel, PCRElementType type, float density, System.Random random)
        {
            density = Mathf.Clamp(density, 0.25f, 2f);
            if (Mathf.Approximately(density, 1f))
            {
                return;
            }

            if (density < 1f)
            {
                float removeChance = 1f - density;
                for (int i = runtimeLevel.Events.Count - 1; i >= 0; i--)
                {
                    if (runtimeLevel.Events[i].Type != type)
                    {
                        continue;
                    }

                    if (random.NextDouble() < removeChance)
                    {
                        runtimeLevel.Events.RemoveAt(i);
                    }
                }

                return;
            }

            float addChance = density - 1f;
            int baseId = runtimeLevel.Events.Count + 1;
            int originalCount = runtimeLevel.Events.Count;
            for (int i = 0; i < originalCount; i++)
            {
                if (runtimeLevel.Events[i].Type != type || random.NextDouble() > addChance)
                {
                    continue;
                }

                PCREventRuntime clone = runtimeLevel.Events[i];
                clone.EventId = baseId++;
                int[] candidateOffsets = { 1, -1, 2, -2 };
                int start = random.Next(0, candidateOffsets.Length);
                bool placed = false;
                for (int c = 0; c < candidateOffsets.Length; c++)
                {
                    int offset = candidateOffsets[(start + c) % candidateOffsets.Length];
                    int candidateStep = Mathf.Clamp(clone.StepIndex + offset, 0, runtimeLevel.TotalSteps - 1);
                    if (!IsDensityCloneStepSafe(runtimeLevel, clone, candidateStep))
                    {
                        continue;
                    }

                    clone.StepIndex = candidateStep;
                    placed = true;
                    break;
                }

                if (!placed)
                {
                    continue;
                }

                runtimeLevel.Events.Add(clone);
            }
        }

        private static bool IsDensityCloneStepSafe(PCRLevelRuntime runtimeLevel, PCREventRuntime clone, int stepIndex)
        {
            int laneCount = PCRSimulation.GetLaneCountAtStep(runtimeLevel, stepIndex);
            if (clone.LaneIndex < 0 || clone.LaneIndex >= laneCount)
            {
                return false;
            }

            for (int i = 0; i < runtimeLevel.Events.Count; i++)
            {
                PCREventRuntime other = runtimeLevel.Events[i];
                if (other.LaneIndex != clone.LaneIndex)
                {
                    continue;
                }

                int diff = Mathf.Abs(other.StepIndex - stepIndex);
                if (diff == 0)
                {
                    return false;
                }

                if (clone.IsField || other.IsField)
                {
                    continue;
                }

                int minGap = IsDensityHazardType(clone.Type) || IsDensityHazardType(other.Type) ? 3 : 2;
                if (diff < minGap)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDensityHazardType(PCRElementType type)
        {
            return type == PCRElementType.DiodeGate || type == PCRElementType.SparkGap;
        }

        private Vector3 GetLanePointAtStep(int step, int lane)
        {
            Vector3 center = level.CenterlinePoints[step];
            Vector3 tangent = level.Tangents[step];
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            int laneCount = PCRSimulation.GetLaneCountAtStep(level, step);
            float offset = (lane - (laneCount - 1) * 0.5f) * level.LaneSpacing;
            return center + right * offset + Vector3.up * 0.01f;
        }

        private Vector3 GetPlayerWorldPosition()
        {
            return playerRoot != null ? playerRoot.transform.position : Vector3.zero;
        }

        private static void ConfigureParticle(ParticleSystem ps, ParticleSettings settings)
        {
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = settings.Lifetime;
            main.startSize = settings.StartSize;
            main.startSpeed = settings.Speed;
            main.maxParticles = settings.MaxParticles;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.05f;
        }

        private void ConfigureSparkTrail(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 0.5f;
            main.startSize = 0.056f;
            main.startSpeed = 2.45f;
            main.maxParticles = 520;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(1f, 0.92f, 0.55f, 1f);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 32f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.014f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.98f, 0.86f), 0f),
                    new GradientColorKey(new Color(1f, 0.76f, 0.28f), 0.45f),
                    new GradientColorKey(new Color(0.28f, 0.86f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.52f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 0.74f;
            trails.lifetime = 0.16f;
            trails.minVertexDistance = 0.03f;
            trails.dieWithParticles = true;
            trails.sizeAffectsWidth = true;
            trails.worldSpace = true;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.material = sparkMat != null ? sparkMat : (haloMat != null ? haloMat : traceMatGlow);
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.velocityScale = 0.1f;
                renderer.lengthScale = 1.12f;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sortMode = ParticleSystemSortMode.Distance;
                renderer.sortingFudge = 1.25f;
                renderer.trailMaterial = sparkMat != null ? sparkMat : renderer.material;
            }
        }

        private static void EmitParticleBurst(ParticleSystem ps, Vector3 position, Color color)
        {
            if (ps == null)
            {
                return;
            }

            var main = ps.main;
            main.startColor = color;
            ps.transform.position = position;
            ps.Emit(18);
        }

        private void EmitSparkTrailBurst(int count, Color color)
        {
            if (sparkTrail == null || count <= 0)
            {
                return;
            }

            var emit = new ParticleSystem.EmitParams
            {
                startColor = color,
                startSize = 0.095f,
                startLifetime = 0.28f
            };
            sparkTrail.Emit(emit, count);
        }

        private void DrawHud()
        {
            if (level == null || IsMenu)
            {
                return;
            }

            EnsureUiFonts();
            Rect panel = new Rect(12f, 12f, 280f, 124f);
            DrawRect(panel, new Color(0f, 0f, 0f, 0.28f));
            DrawRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.18f, 0.64f, 0.92f, 0.9f));

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = new Color(0.86f, 0.95f, 1f, 1f);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 8f, 220f, 24f), $"FluxOut  //  {level.Seed}", titleStyle);

            Color phaseColor = runState.Phase == PCRPhase.A ? new Color(0.4f, 1f, 0.46f, 1f) : new Color(1f, 0.58f, 0.18f, 1f);
            DrawRect(new Rect(panel.x + panel.width - 48f, panel.y + 8f, 36f, 36f), phaseColor);
            var phaseBadgeStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            phaseBadgeStyle.normal.textColor = Color.black;
            GUI.Label(new Rect(panel.x + panel.width - 48f, panel.y + 8f, 36f, 36f), runState.Phase == PCRPhase.A ? "↑" : "↓", phaseBadgeStyle);

            var chipStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            chipStyle.normal.textColor = new Color(0.86f, 0.95f, 1f, 1f);

            DrawRect(new Rect(panel.x + 10f, panel.y + 38f, 82f, 26f), new Color(0f, 0f, 0f, 0.34f));
            GUI.Label(new Rect(panel.x + 10f, panel.y + 38f, 82f, 26f), $"T {timerRemaining:0.0}s", chipStyle);

            DrawRect(new Rect(panel.x + 98f, panel.y + 38f, 96f, 26f), new Color(0f, 0f, 0f, 0.34f));
            GUI.Label(new Rect(panel.x + 98f, panel.y + 38f, 96f, 26f), $"{currentStep}/{level.TotalSteps}", chipStyle);

            string decisionHint = GetDecisionHintText();
            DrawRect(new Rect(panel.x + 10f, panel.y + 68f, panel.width - 20f, 22f), new Color(0f, 0f, 0f, 0.24f));
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
            hintStyle.normal.textColor = new Color(0.8f, 0.9f, 1f, 0.95f);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 69f, panel.width - 28f, 20f), decisionHint, hintStyle);

            DrawRect(new Rect(panel.x + 10f, panel.y + 94f, panel.width - 20f, 24f), new Color(0f, 0f, 0f, 0.26f));
            GUI.Label(new Rect(panel.x + 14f, panel.y + 97f, panel.width - 26f, 18f), "YOU flip phase. Matte lane means signal lost.", hintStyle);

            var tutorialButton = new GUIStyle(GUI.skin.button)
            {
                font = menuBodyFont,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            if (GUI.Button(new Rect(UiWidth - 130f, 12f, 118f, 34f), "TUTORIAL", tutorialButton))
            {
                OpenTutorial();
            }
        }

        private string GetDecisionHintText()
        {
            if (level == null || level.Decisions == null || level.Decisions.Count == 0)
            {
                return "Decision: --";
            }

            int step = Mathf.Clamp(runState.StepIndex, 0, level.TotalSteps - 1);
            for (int i = 0; i < level.Decisions.Count; i++)
            {
                PCRDecisionWindow window = level.Decisions[i];
                if (window.StepIndex < step)
                {
                    continue;
                }

                int delta = Mathf.Max(0, window.StepIndex - step);
                string phaseText = window.RequiredMask switch
                {
                    PCRPhaseMask.A => "↑",
                    PCRPhaseMask.B => "↓",
                    PCRPhaseMask.Both => "↕",
                    _ => "--"
                };
                string reasonText = window.Reason switch
                {
                    _ => "Route prefers"
                };
                return $"Decision in {delta} steps: {reasonText} {phaseText}";
            }

            return "Decision: --";
        }

        private void DrawActionIndicators()
        {
            if (level == null || IsMenu || IsCountdown)
            {
                return;
            }

            if (phaseIndicatorTimer > 0.01f)
            {
                float alpha = Mathf.Clamp01(phaseIndicatorTimer / PhaseIndicatorDurationSec);
                Color phaseColor = runState.Phase == PCRPhase.A ? new Color(0.38f, 1f, 0.44f, alpha) : new Color(1f, 0.56f, 0.16f, alpha);
                GUI.color = new Color(0f, 0f, 0f, 0.45f * alpha);
                GUI.DrawTexture(new Rect(UiWidth * 0.5f - 170f, 22f, 340f, 64f), WhiteTexture);
                var phaseStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 30
                };
                phaseStyle.normal.textColor = phaseColor;
                GUI.color = Color.white;
                GUI.Label(new Rect(UiWidth * 0.5f - 170f, 22f, 340f, 64f), $"PHASE {(runState.Phase == PCRPhase.A ? "↑" : "↓")}", phaseStyle);
            }

            if (shiftIndicatorTimer > 0.01f)
            {
                float alpha = Mathf.Clamp01(shiftIndicatorTimer / ShiftIndicatorDurationSec);
                GUI.color = new Color(0f, 0f, 0f, 0.42f * alpha);
                GUI.DrawTexture(new Rect(UiWidth * 0.5f - 170f, 90f, 340f, 52f), WhiteTexture);
                var shiftStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 24
                };
                shiftStyle.normal.textColor = new Color(0.86f, 0.95f, 1f, alpha);
                GUI.color = Color.white;
                GUI.Label(new Rect(UiWidth * 0.5f - 170f, 90f, 340f, 52f), shiftIndicatorText, shiftStyle);
            }

            if (TryGetUpcomingDecision(runState.StepIndex, out PCRDecisionWindow upcoming, out int deltaSteps))
            {
                float leadAlpha = Mathf.Clamp01(1f - deltaSteps / Mathf.Max(1f, upcoming.LeadSteps));
                Color c = upcoming.RequiredMask switch
                {
                    PCRPhaseMask.A => new Color(0.38f, 1f, 0.44f, leadAlpha),
                    PCRPhaseMask.B => new Color(1f, 0.58f, 0.16f, leadAlpha),
                    _ => new Color(0.88f, 0.94f, 1f, leadAlpha)
                };
                GUI.color = new Color(0f, 0f, 0f, 0.42f * leadAlpha);
                GUI.DrawTexture(new Rect(UiWidth * 0.5f - 200f, 146f, 400f, 48f), WhiteTexture);
                var dStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 22
                };
                dStyle.normal.textColor = c;
                string req = upcoming.RequiredMask == PCRPhaseMask.Both ? "↕" : (upcoming.RequiredMask == PCRPhaseMask.A ? "↑" : "↓");
                string prepText = $"SET {req}  /  ROUTE";
                GUI.color = Color.white;
                GUI.Label(new Rect(UiWidth * 0.5f - 200f, 146f, 400f, 48f), $"PREP {prepText}", dStyle);
            }

            GUI.color = Color.white;
        }

        private bool TryGetUpcomingDecision(int currentStep, out PCRDecisionWindow window, out int deltaSteps)
        {
            deltaSteps = 0;
            window = default;
            if (level == null || level.Decisions == null || level.Decisions.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < level.Decisions.Count; i++)
            {
                PCRDecisionWindow candidate = level.Decisions[i];
                if (candidate.StepIndex < currentStep)
                {
                    continue;
                }

                int delta = candidate.StepIndex - currentStep;
                if (delta <= candidate.LeadSteps)
                {
                    window = candidate;
                    deltaSteps = delta;
                    return true;
                }
            }

            return false;
        }

        private void DrawFlowPanel()
        {
            if (level == null)
            {
                DrawBootFailurePanel();
                return;
            }

            if (tutorialOpen)
            {
                DrawTutorialPanel();
                return;
            }

            if (IsMenu)
            {
                DrawMainMenuPanel();
                return;
            }

            if (IsCountdown)
            {
                DrawCountdownPanel();
                return;
            }

            if (IsPaused)
            {
                DrawPausePanel();
                return;
            }

            if (IsDead)
            {
                DrawDeathPanel();
                return;
            }

            if (IsFinished)
            {
                DrawFinishPanel();
            }
        }

        private void DrawBootFailurePanel()
        {
            EnsureUiFonts();
            Rect panelRect = new Rect(UiWidth * 0.5f - 300f, UiHeight * 0.5f - 170f, 600f, 340f);
            DrawRect(panelRect, new Color(0.02f, 0.02f, 0.04f, 0.9f));
            DrawRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 3f), new Color(1f, 0.34f, 0.42f, 0.92f));

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuTitleFont,
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            titleStyle.normal.textColor = new Color(0.96f, 0.84f, 0.86f, 1f);
            GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 16f, panelRect.width - 32f, 52f), "BOOT FAILURE", titleStyle);

            string message = string.IsNullOrWhiteSpace(bootErrorMessage)
                ? "Runtime level build failed. Rebuild with a new seed."
                : bootErrorMessage;
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 18,
                alignment = TextAnchor.UpperCenter,
                wordWrap = true
            };
            bodyStyle.normal.textColor = new Color(0.86f, 0.92f, 0.98f, 1f);
            GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 78f, panelRect.width - 48f, 110f), message, bodyStyle);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = menuBodyFont,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                fixedHeight = 56f
            };
            GUILayout.BeginArea(new Rect(panelRect.x + 44f, panelRect.y + panelRect.height - 132f, panelRect.width - 88f, 104f));
            if (GUILayout.Button("REBUILD", buttonStyle))
            {
                BuildAndStartLevel(debugSeed, true);
            }

            if (GUILayout.Button("NEW SEED", buttonStyle))
            {
                debugSeed++;
                BuildAndStartLevel(debugSeed, true);
            }

            GUILayout.EndArea();
        }

        private void DrawMainMenuPanel()
        {
            EnsureUiFonts();
            float panelWidth = Mathf.Min(760f, UiWidth - 48f);
            float panelHeight = Mathf.Min(500f, UiHeight - 56f);
            Rect panelRect = new Rect((UiWidth - panelWidth) * 0.5f, (UiHeight - panelHeight) * 0.5f, panelWidth, panelHeight);

            float pulse = 0.5f + Mathf.Sin(Time.time * 1.8f) * 0.5f;
            Color frameColor = Color.Lerp(new Color(0.16f, 0.6f, 0.92f, 1f), new Color(0.12f, 0.84f, 1f, 1f), pulse * 0.45f);
            DrawRect(new Rect(panelRect.x - 8f, panelRect.y - 8f, panelRect.width + 16f, panelRect.height + 16f), new Color(0.02f, 0.08f, 0.14f, 0.58f));
            DrawRect(panelRect, new Color(0.01f, 0.02f, 0.05f, 0.88f));
            DrawRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 3f), frameColor);
            DrawRect(new Rect(panelRect.x, panelRect.yMax - 3f, panelRect.width, 3f), frameColor);
            DrawRect(new Rect(panelRect.x, panelRect.y, 3f, panelRect.height), frameColor);
            DrawRect(new Rect(panelRect.xMax - 3f, panelRect.y, 3f, panelRect.height), frameColor);

            float stripeY = panelRect.y + 54f;
            for (int i = 0; i < 8; i++)
            {
                float x = panelRect.x + 24f + i * (panelRect.width - 48f) / 8f;
                DrawRect(new Rect(x, stripeY, 42f, 2f), new Color(0.2f, 0.8f, 1f, 0.12f + 0.07f * Mathf.PingPong(Time.time * 0.8f + i * 0.16f, 1f)));
            }

            GUILayout.BeginArea(new Rect(panelRect.x + 26f, panelRect.y + 14f, panelRect.width - 52f, panelRect.height - 26f));
            var title = new GUIStyle(GUI.skin.label)
            {
                font = menuTitleFont,
                fontSize = 66,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            title.normal.textColor = new Color(0.9f, 0.98f, 1f, 1f);
            GUILayout.Label("FLUXOUT", title);

            var subtitle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            subtitle.normal.textColor = new Color(0.55f, 0.86f, 1f, 1f);
            GUILayout.Label("PHASE CIRCUIT RUNNER  //  ELECTRIC CORE", subtitle);
            GUILayout.Space(8f);

            var desc = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 19,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            desc.normal.textColor = new Color(0.84f, 0.93f, 1f, 1f);
            GUILayout.Label("Tap only flips phase (A/B). Route arrows decide lane shifts. Stay on glow lanes and avoid matte lanes for 90 seconds.", desc);
            GUILayout.Space(18f);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = menuBodyFont,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                fixedHeight = 56f
            };
            buttonStyle.normal.textColor = new Color(0.92f, 0.98f, 1f, 1f);
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;
            buttonStyle.padding = new RectOffset(14, 14, 10, 10);

            if (GUILayout.Button("START RUN", buttonStyle))
            {
                if (level == null)
                {
                    BuildAndStartLevel(debugSeed);
                }
                else
                {
                    StartRunCountdown();
                }
            }

            if (GUILayout.Button("RUN TUTORIAL", buttonStyle))
            {
                OpenTutorial();
            }

            if (GUILayout.Button("NEW SEED RUN", buttonStyle))
            {
                BuildNewSeedAndPlay();
            }

            if (GUILayout.Button("REBUILD LEVEL", buttonStyle))
            {
                BuildAndStartLevel(debugSeed, true);
            }

            GUILayout.Space(8f);
            var footer = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            footer.normal.textColor = new Color(0.66f, 0.82f, 0.95f, 1f);
            GUILayout.Label($"SEED {debugSeed}   //   MENU VOL {menuMusicVolume:0.00}   //   RUN VOL {backgroundMusicVolume:0.00}", footer);
            GUILayout.EndArea();
        }

        private void DrawCountdownPanel()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, UiWidth, UiHeight), WhiteTexture);
            GUI.color = Color.white;

            string text = countdownDisplayValue > 0 ? countdownDisplayValue.ToString() : "GO";
            float scale = 1f + Mathf.PingPong(Time.time * 1.8f, 0.14f);
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(120f * scale),
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = new Color(0.86f, 0.97f, 1f, 1f);
            GUI.Label(new Rect(0, UiHeight * 0.5f - 140f, UiWidth, 220f), text, style);

            var sub = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 22
            };
            sub.normal.textColor = new Color(0.76f, 0.9f, 1f, 1f);
            GUI.Label(new Rect(0, UiHeight * 0.5f + 46f, UiWidth, 50f), "Signal Syncing...", sub);
        }

        private void DrawTutorialPanel()
        {
            Rect panelRect = new Rect(UiWidth * 0.5f - 340f, UiHeight * 0.5f - 250f, 680f, 500f);
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(panelRect, WhiteTexture);
            GUI.color = Color.white;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            titleStyle.normal.textColor = new Color(0.86f, 0.96f, 1f, 1f);
            GUI.Label(new Rect(panelRect.x + 20f, panelRect.y + 16f, panelRect.width - 40f, 34f), "RUN TUTORIAL", titleStyle);
            GUI.Label(
                new Rect(panelRect.x + 24f, panelRect.y + 52f, panelRect.width - 48f, 44f),
                "Tap / Space only flips phase. Arrows route you. Matte lane means signal lost.");

            Rect scrollRect = new Rect(panelRect.x + 20f, panelRect.y + 96f, panelRect.width - 40f, 338f);
            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 18f, TutorialEntries.Length * 80f + 8f);
            tutorialScroll = GUI.BeginScrollView(scrollRect, tutorialScroll, contentRect);

            var iconStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            iconStyle.normal.textColor = Color.black;
            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            nameStyle.normal.textColor = new Color(0.95f, 0.98f, 1f, 1f);
            var descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true
            };
            descStyle.normal.textColor = new Color(0.83f, 0.9f, 0.96f, 1f);

            for (int i = 0; i < TutorialEntries.Length; i++)
            {
                float y = 8f + i * 80f;
                Rect row = new Rect(4f, y, contentRect.width - 8f, 72f);
                GUI.color = new Color(0f, 0f, 0f, 0.34f);
                GUI.DrawTexture(row, WhiteTexture);
                GUI.color = Color.white;

                TutorialEntry entry = TutorialEntries[i];
                Rect iconRect = new Rect(row.x + 10f, row.y + 10f, 52f, 52f);
                GUI.color = entry.Color;
                GUI.DrawTexture(iconRect, WhiteTexture);
                GUI.color = Color.white;
                GUI.Label(iconRect, entry.Icon, iconStyle);
                GUI.Label(new Rect(row.x + 74f, row.y + 8f, row.width - 84f, 24f), entry.Name, nameStyle);
                GUI.Label(new Rect(row.x + 74f, row.y + 30f, row.width - 84f, 34f), entry.Description, descStyle);
            }

            GUI.EndScrollView();

            GUILayout.BeginArea(new Rect(panelRect.x + 22f, panelRect.y + panelRect.height - 56f, panelRect.width - 44f, 40f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Close"))
            {
                CloseTutorial();
            }

            GUILayout.Space(8f);
            GUI.enabled = !IsMenu;
            if (GUILayout.Button("Restart Run"))
            {
                CloseTutorial();
                RestartRun();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void OpenTutorial()
        {
            if (tutorialOpen)
            {
                return;
            }

            tutorialResumeState = flowState;
            tutorialOpen = true;
            tutorialScroll = Vector2.zero;
            if (flowState == PCRFlowState.Playing)
            {
                flowState = PCRFlowState.Paused;
            }
        }

        private void CloseTutorial()
        {
            if (!tutorialOpen)
            {
                return;
            }

            tutorialOpen = false;
            flowState = tutorialResumeState;
        }

        private void DrawPausePanel()
        {
            EnsureUiFonts();
            Rect panelRect = new Rect(UiWidth * 0.5f - 250f, UiHeight * 0.5f - 130f, 500f, 260f);
            DrawRect(panelRect, new Color(0.01f, 0.03f, 0.06f, 0.86f));
            DrawRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 3f), new Color(0.18f, 0.72f, 1f, 0.9f));

            var title = new GUIStyle(GUI.skin.label)
            {
                font = menuTitleFont,
                fontSize = 44,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            title.normal.textColor = new Color(0.86f, 0.96f, 1f, 1f);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 10f, panelRect.width, 52f), "PAUSED", title);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = menuBodyFont,
                fontSize = 23,
                fontStyle = FontStyle.Bold,
                fixedHeight = 54f
            };

            GUILayout.BeginArea(new Rect(panelRect.x + 26f, panelRect.y + 64f, panelRect.width - 52f, panelRect.height - 78f));
            GUILayout.Space(6f);
            if (GUILayout.Button("RESUME", buttonStyle))
            {
                flowState = PCRFlowState.Playing;
            }

            if (GUILayout.Button("RESTART", buttonStyle))
            {
                RestartRun();
            }

            if (GUILayout.Button("MAIN MENU", buttonStyle))
            {
                EnterMainMenu();
            }

            GUILayout.EndArea();
        }

        private void DrawDeathPanel()
        {
            EnsureUiFonts();
            Rect panelRect = new Rect(UiWidth * 0.5f - 270f, UiHeight * 0.5f - 170f, 540f, 340f);
            DrawRect(panelRect, new Color(0.04f, 0.02f, 0.03f, 0.9f));
            DrawRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 3f), new Color(1f, 0.34f, 0.42f, 0.95f));

            GUILayout.BeginArea(new Rect(panelRect.x + 24f, panelRect.y + 18f, panelRect.width - 48f, panelRect.height - 36f));
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuTitleFont,
                fontSize = 38,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            titleStyle.normal.textColor = new Color(1f, 0.72f, 0.72f, 1f);
            GUILayout.Label($"SIGNAL LOST - {deathReasonText}", titleStyle);
            var infoStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter
            };
            infoStyle.normal.textColor = new Color(0.9f, 0.95f, 1f, 1f);
            GUILayout.Label($"Time Left: {timerRemaining:0.0}s", infoStyle);
            GUILayout.Space(10f);
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = menuBodyFont,
                fontSize = 23,
                fontStyle = FontStyle.Bold,
                fixedHeight = 56f
            };

            if (GUILayout.Button("RESTART", buttonStyle))
            {
                RestartRun();
            }

            if (GUILayout.Button("NEW SEED", buttonStyle))
            {
                BuildNewSeedAndPlay();
            }

            if (GUILayout.Button("MAIN MENU", buttonStyle))
            {
                EnterMainMenu();
            }

            GUILayout.EndArea();
        }

        private void DrawFinishPanel()
        {
            EnsureUiFonts();
            Rect panelRect = new Rect(UiWidth * 0.5f - 280f, UiHeight * 0.5f - 190f, 560f, 380f);
            DrawRect(panelRect, new Color(0.01f, 0.03f, 0.06f, 0.9f));
            DrawRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 3f), new Color(0.2f, 0.86f, 1f, 0.95f));

            GUILayout.BeginArea(new Rect(panelRect.x + 24f, panelRect.y + 18f, panelRect.width - 48f, panelRect.height - 36f));
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuTitleFont,
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            titleStyle.normal.textColor = new Color(0.88f, 0.98f, 1f, 1f);
            GUILayout.Label("RUN COMPLETE", titleStyle);
            var infoStyle = new GUIStyle(GUI.skin.label)
            {
                font = menuBodyFont,
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
            infoStyle.normal.textColor = new Color(0.82f, 0.92f, 1f, 1f);
            GUILayout.Label($"Meaningful Avg: {validation.AverageMeaningfulIntervalSec:0.00}s", infoStyle);
            GUILayout.Label($"Shift Avg: {validation.AverageShiftIntervalSec:0.00}s", infoStyle);
            GUILayout.Label($"Max Decision Gap: {validation.MaxDecisionGapSec:0.00}s", infoStyle);
            GUILayout.Label($"Req Tap Rate: {validation.MinRequiredTapRatePer10Sec:0.00}/10s", infoStyle);
            GUILayout.Label($"No-Tap Survival: {validation.NoTapSurvivalSec:0.00}s", infoStyle);
            GUILayout.Space(8f);
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = menuBodyFont,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                fixedHeight = 52f
            };

            if (GUILayout.Button("RESTART", buttonStyle))
            {
                RestartRun();
            }

            if (GUILayout.Button("NEW SEED", buttonStyle))
            {
                BuildNewSeedAndPlay();
            }

            if (GUILayout.Button("MAIN MENU", buttonStyle))
            {
                EnterMainMenu();
            }

            GUILayout.EndArea();
        }

        private void DrawScreenFlash()
        {
            Color color = Color.clear;
            if (deathFlash > 0.01f)
            {
                color += new Color(1f, 1f, 1f, deathFlash * 0.34f);
            }

            if (phaseFlash > 0.01f)
            {
                Color phaseColor = runState.Phase == PCRPhase.A ? new Color(0.24f, 0.88f, 1f, 1f) : new Color(1f, 0.58f, 0.2f, 1f);
                color += new Color(phaseColor.r, phaseColor.g, phaseColor.b, phaseFlash * 0.1f);
            }

            if (shiftFlash > 0.01f)
            {
                color += new Color(0.9f, 0.95f, 1f, shiftFlash * 0.06f);
            }

            if (color.a <= 0f)
            {
                return;
            }

            GUI.color = color;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), WhiteTexture);
            GUI.color = Color.white;
        }

        private void DrawDebugPanel()
        {
            GUILayout.BeginArea(new Rect(UiWidth - 370, 14, 356, UiHeight - 20), GUI.skin.box);
            GUILayout.Label("PCR Debug");
            GUILayout.Label($"Valid: {validation.IsValid} Retry: {validation.RetryIndex}");
            GUILayout.Label(validation.IsValid ? "Validator Passed" : $"Validator Fail: {validation.FailureReason}");
            GUILayout.Label($"NoTapSurvival: {validation.NoTapSurvivalSec:0.00}s");
            GUILayout.Label($"DecisionGap: {validation.MaxDecisionGapSec:0.00}s");
            GUILayout.Label($"ReqTapRate: {validation.MinRequiredTapRatePer10Sec:0.00}/10s");
            GUILayout.Label($"SamePhaseRun: {validation.MaxSamePhaseRunSec:0.00}s");

            GUILayout.Space(4f);
            GUILayout.Label($"Seed: {debugSeed}");
            string seedText = GUILayout.TextField(debugSeed.ToString(), 10);
            if (int.TryParse(seedText, out int parsedSeed))
            {
                debugSeed = parsedSeed;
            }

            debugSpeedMultiplier = SliderRow("Speed", debugSpeedMultiplier, 0.4f, 2f);
            debugSimStep = SliderRow("SimStep", debugSimStep, 0.08f, 0.2f);
            debugLookahead = SliderRow("Lookahead", debugLookahead, 6f, 20f);
            debugOrthoSize = SliderRow("Ortho", debugOrthoSize, 5f, 12f);
            debugCameraHeight = SliderRow("CamHeight", debugCameraHeight, 14f, 28f);
            debugBloomStrength = SliderRow("Bloom", debugBloomStrength, 0f, 2f);
            debugLaneShiftSmooth = SliderRow("LaneEase", debugLaneShiftSmooth, 0.06f, 0.18f);
            debugEmptyGapThreshold = SliderRow("EmptyGap", debugEmptyGapThreshold, 1f, 2.5f);
            debugLaneShiftTarget = SliderRow("ShiftTarget", debugLaneShiftTarget, 0.8f, 1.6f);
            debugMaxNoTapSurvival = SliderRow("NoTapCap", debugMaxNoTapSurvival, 6f, 14f);
            debugMaxDecisionGap = SliderRow("DecisionGapMax", debugMaxDecisionGap, 1f, 2.5f);
            debugMinRequiredTapPer10Sec = SliderRow("MinTap/10s", debugMinRequiredTapPer10Sec, 1f, 10f);
            debugMaxSamePhaseRunSec = SliderRow("MaxSamePhase", debugMaxSamePhaseRunSec, 1.5f, 5f);
            debugDecisionLeadTimeSec = SliderRow("DecisionLead", debugDecisionLeadTimeSec, 0.4f, 2f);

            GUILayout.Label($"Lane Override: {debugLaneOverride} (0=auto)");
            debugLaneOverride = Mathf.RoundToInt(GUILayout.HorizontalSlider(debugLaneOverride, 0, 5));

            GUILayout.Space(4f);
            GUILayout.Label("Core Mode");
            GUILayout.Label("Only route arrows are active.");
            GUILayout.Label("Death source: matte lane (signal lost).");

            debugShowTelegraphs = GUILayout.Toggle(debugShowTelegraphs, "Show Telegraphs");
            if (GUILayout.Button("Rebuild Level"))
            {
                BuildAndStartLevel(debugSeed);
            }

            if (GUILayout.Button(IsPaused ? "Resume" : "Pause"))
            {
                TogglePause();
            }

            if (GUILayout.Button("Step Once"))
            {
                flowState = PCRFlowState.Paused;
                if (!IsDead && !IsFinished && level != null && currentStep < level.TotalSteps)
                {
                    SimTick();
                }
            }

            GUILayout.EndArea();
        }

        private static float SliderRow(string label, float value, float min, float max)
        {
            GUILayout.Label($"{label}: {value:0.00}");
            return GUILayout.HorizontalSlider(value, min, max);
        }

        private readonly struct TutorialEntry
        {
            public readonly string Icon;
            public readonly string Name;
            public readonly string Description;
            public readonly Color Color;

            public TutorialEntry(string icon, string name, string description, Color color)
            {
                Icon = icon;
                Name = name;
                Description = description;
                Color = color;
            }
        }

        private enum PCRFlowState
        {
            MainMenu = 0,
            Countdown = 1,
            Playing = 2,
            Paused = 3,
            Dead = 4,
            Finished = 5
        }

        private struct ParticleSettings
        {
            public float StartSize;
            public float Lifetime;
            public float Speed;
            public int MaxParticles;
        }

        private sealed class ParallaxLayerState
        {
            public Material Material;
            public Transform LayerTransform;
            public Vector3 BasePosition;
            public Vector2 ScrollVelocity;
            public float OffsetScale;
            public float PhaseBlend;
            public float WorldParallax;
        }

        private sealed class PCRWakeDecal
        {
            public Transform Transform;
            public Renderer Renderer;
            public MaterialPropertyBlock Block;
            public bool Active;
            public float Age;
            public float Lifetime;
            public Vector3 StartScale;
            public Vector3 Drift;
            public Color Color;

            public void SetActive(bool active)
            {
                Active = active;
                if (Renderer != null)
                {
                    Renderer.enabled = active;
                }
            }
        }

        private void OnDestroy()
        {
            if (backgroundMusicRoutine != null)
            {
                StopCoroutine(backgroundMusicRoutine);
                backgroundMusicRoutine = null;
            }

            if (backgroundMusicSource != null)
            {
                backgroundMusicSource.Stop();
            }

            if (backgroundMusicClip != null)
            {
                Destroy(backgroundMusicClip);
                backgroundMusicClip = null;
            }
        }
    }

    public sealed class PCREventView : MonoBehaviour
    {
        private PCREventRuntime evt;
        private Renderer targetRenderer;
        private MaterialPropertyBlock coreMpb;
        private MaterialPropertyBlock motionMpbA;
        private MaterialPropertyBlock motionMpbB;
        private Renderer motionRendererA;
        private Renderer motionRendererB;
        private Transform phaseBadgeRoot;
        private Renderer phaseBadgeCore;
        private Renderer phaseBadgeStripeTop;
        private Renderer phaseBadgeStripeBottom;
        private TextMesh phaseBadgeText;
        private MaterialPropertyBlock phaseBadgeMpb;
        private float pulseOffset;
        private Vector3 initialScale;
        private Vector3 motionBaseScaleA;
        private Vector3 motionBaseScaleB;
        private Vector3 motionBasePosA;
        private Vector3 motionBasePosB;
        private float jitterSeed;

        public void Initialize(PCREventRuntime eventRuntime, Renderer renderer, PCRLevelRuntime level, PCRPhase currentPhase)
        {
            evt = eventRuntime;
            targetRenderer = renderer;
            coreMpb = new MaterialPropertyBlock();
            motionMpbA = new MaterialPropertyBlock();
            motionMpbB = new MaterialPropertyBlock();
            pulseOffset = (evt.EventId % 11) * 0.13f;
            jitterSeed = (evt.EventId % 17) * 0.19f;
            initialScale = transform.localScale;

            Shader fallback = Shader.Find("Unlit/Color") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader");
            Material baseMat = renderer.sharedMaterial != null ? renderer.sharedMaterial : new Material(fallback);
            motionRendererA = CreateOverlay("MotionA", transform, baseMat, new Vector3(0f, 0f, 0.012f), initialScale * 0.9f);
            motionRendererB = CreateOverlay("MotionB", transform, baseMat, new Vector3(0f, 0f, 0.016f), initialScale * 0.62f);
            if (motionRendererA != null)
            {
                motionBaseScaleA = motionRendererA.transform.localScale;
                motionBasePosA = motionRendererA.transform.localPosition;
            }

            if (motionRendererB != null)
            {
                motionBaseScaleB = motionRendererB.transform.localScale;
                motionBasePosB = motionRendererB.transform.localPosition;
            }

            if (NeedsPhaseBadge(evt.Type))
            {
                var badgeRootGo = new GameObject("PhaseBadge");
                phaseBadgeRoot = badgeRootGo.transform;
                phaseBadgeRoot.SetParent(transform, false);
                phaseBadgeRoot.localPosition = new Vector3(0f, 0.34f, 0f);
                phaseBadgeRoot.localRotation = Quaternion.identity;
                phaseBadgeMpb = new MaterialPropertyBlock();

                Material badgeMat = new Material(fallback);
                if (badgeMat.HasProperty("_Color"))
                {
                    badgeMat.SetColor("_Color", Color.white);
                }

                phaseBadgeCore = CreateOverlay(
                    "BadgeCore",
                    phaseBadgeRoot,
                    badgeMat,
                    new Vector3(0f, 0f, 0f),
                    new Vector3(0.22f, 0.13f, 1f));
                phaseBadgeStripeTop = CreateOverlay(
                    "BadgeStripeTop",
                    phaseBadgeRoot,
                    badgeMat,
                    new Vector3(0f, 0.045f, 0.006f),
                    new Vector3(0.14f, 0.024f, 1f));
                phaseBadgeStripeBottom = CreateOverlay(
                    "BadgeStripeBottom",
                    phaseBadgeRoot,
                    badgeMat,
                    new Vector3(0f, -0.045f, 0.006f),
                    new Vector3(0.14f, 0.024f, 1f));

                var badgeTextGo = new GameObject("BadgeText");
                badgeTextGo.transform.SetParent(phaseBadgeRoot, false);
                badgeTextGo.transform.localPosition = new Vector3(0f, 0f, 0.012f);
                phaseBadgeText = badgeTextGo.AddComponent<TextMesh>();
                phaseBadgeText.anchor = TextAnchor.MiddleCenter;
                phaseBadgeText.alignment = TextAlignment.Center;
                phaseBadgeText.fontSize = 64;
                phaseBadgeText.characterSize = 0.055f;
                phaseBadgeText.text = string.Empty;
                phaseBadgeText.color = Color.white;
            }

            Refresh(currentPhase, true, evt.StepIndex);
        }

        public void Refresh(PCRPhase phase, bool visible, int currentStep)
        {
            if (targetRenderer == null)
            {
                return;
            }

            int stepDelta = evt.StepIndex - currentStep;
            bool showElement = visible && stepDelta >= -1 && stepDelta <= 8;
            float active01 = ComputeActive01(evt, phase);
            float pulse = Mathf.Lerp(0.95f, 1.09f, Mathf.PingPong(Time.time * 2.2f + pulseOffset, 1f));
            transform.localScale = initialScale * Mathf.Lerp(0.9f, pulse, active01);

            targetRenderer.enabled = showElement;
            if (!showElement)
            {
                if (motionRendererA != null)
                {
                    motionRendererA.enabled = false;
                }

                if (motionRendererB != null)
                {
                    motionRendererB.enabled = false;
                }

                SetBadgeVisibility(false);

                return;
            }

            Color baseColor = ComputeBaseColor(evt, phase, active01);
            float currentArrow = phase == PCRPhase.A ? evt.RouteDeltaA : evt.RouteDeltaB;
            ApplyIcon(targetRenderer, coreMpb, MapIconType(evt.Type), baseColor, active01, currentArrow, pulseOffset, Mathf.Lerp(0.65f, 1.95f, active01));
            UpdateMotionLayers(phase, active01, currentArrow);
            UpdatePhaseBadge(stepDelta);
        }

        private static bool NeedsPhaseBadge(PCRElementType type)
        {
            return type == PCRElementType.DiodeGate
                   || type == PCRElementType.SparkGap
                   || type == PCRElementType.MuxSwitch
                   || type == PCRElementType.InductorCoupler;
        }

        private static int MapIconType(PCRElementType type)
        {
            return type switch
            {
                PCRElementType.DiodeGate => 0,
                PCRElementType.Inverter => 1,
                PCRElementType.MuxSwitch => 2,
                PCRElementType.Capacitor => 3,
                PCRElementType.Amplifier => 4,
                PCRElementType.GroundClamp => 5,
                PCRElementType.SparkGap => 6,
                PCRElementType.InductorCoupler => 7,
                _ => 2
            };
        }

        private static Renderer CreateOverlay(string name, Transform parent, Material materialTemplate, Vector3 localPos, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (filter.sharedMesh == null)
            {
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }

            var renderer = go.AddComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(materialTemplate);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return renderer;
        }

        private static void ApplyIcon(
            Renderer renderer,
            MaterialPropertyBlock mpb,
            int iconType,
            Color color,
            float phaseActive,
            float arrowDir,
            float pulse,
            float glow)
        {
            if (renderer == null)
            {
                return;
            }

            mpb.Clear();
            mpb.SetFloat("_IconType", iconType);
            mpb.SetFloat("_PhaseActive", phaseActive);
            mpb.SetFloat("_ArrowDir", arrowDir);
            mpb.SetFloat("_PulseOffset", pulse);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_Color", color);
            mpb.SetFloat("_Glow", glow * 0.46f);
            renderer.SetPropertyBlock(mpb);
        }

        private static float ComputeActive01(PCREventRuntime evt, PCRPhase phase)
        {
            return evt.Type switch
            {
                PCRElementType.DiodeGate => phase == evt.PhaseParam ? 1f : 0.12f,
                PCRElementType.SparkGap => phase == evt.PhaseParam ? 1f : 0.35f,
                PCRElementType.MuxSwitch => 1f,
                PCRElementType.InductorCoupler => 1f,
                _ => 0.9f
            };
        }

        private static Color ComputeBaseColor(PCREventRuntime evt, PCRPhase phase, float active01)
        {
            Color phaseA = new Color(0.32f, 1f, 0.42f, 1f);
            Color phaseB = new Color(1f, 0.56f, 0.16f, 1f);
            Color neutral = new Color(0.95f, 0.95f, 0.95f, 1f);
            Color col = evt.Type switch
            {
                PCRElementType.DiodeGate => phase == evt.PhaseParam ? new Color(0.62f, 1f, 0.72f, 1f) : new Color(1f, 0.35f, 0.38f, 1f),
                PCRElementType.SparkGap => phase == evt.PhaseParam ? new Color(1f, 0.34f, 0.45f, 1f) : new Color(0.4f, 1f, 0.52f, 1f),
                PCRElementType.GroundClamp => new Color(0.72f, 0.9f, 0.74f, 1f),
                PCRElementType.Capacitor => new Color(0.65f, 0.78f, 1f, 1f),
                PCRElementType.Amplifier => new Color(1f, 0.9f, 0.36f, 1f),
                PCRElementType.Inverter => new Color(1f, 0.82f, 0.38f, 1f),
                PCRElementType.MuxSwitch => phase == PCRPhase.A ? phaseA : phaseB,
                PCRElementType.InductorCoupler => phase == PCRPhase.A ? phaseA : phaseB,
                _ => phase == PCRPhase.A ? phaseA : phaseB
            };
            return Color.Lerp(neutral * 0.2f, col, active01);
        }

        private void SetBadgeVisibility(bool visible)
        {
            if (phaseBadgeCore != null)
            {
                phaseBadgeCore.enabled = visible;
            }

            if (phaseBadgeStripeTop != null)
            {
                phaseBadgeStripeTop.enabled = visible;
            }

            if (phaseBadgeStripeBottom != null)
            {
                phaseBadgeStripeBottom.enabled = visible;
            }

            if (phaseBadgeText != null)
            {
                phaseBadgeText.gameObject.SetActive(visible);
            }
        }

        private void ApplyBadgeColor(Renderer renderer, Color color, float glow)
        {
            if (renderer == null || phaseBadgeMpb == null)
            {
                return;
            }

            phaseBadgeMpb.Clear();
            phaseBadgeMpb.SetColor("_Color", color);
            phaseBadgeMpb.SetColor("_BaseColor", color);
            phaseBadgeMpb.SetColor("_TintColor", color);
            phaseBadgeMpb.SetFloat("_Glow", glow);
            renderer.SetPropertyBlock(phaseBadgeMpb);
        }

        private void UpdateMotionLayers(PCRPhase phase, float active01, float currentArrow)
        {
            float t = Time.time + pulseOffset;
            if (motionRendererA != null)
            {
                motionRendererA.enabled = true;
            }

            if (motionRendererB != null)
            {
                motionRendererB.enabled = true;
            }

            switch (evt.Type)
            {
                case PCRElementType.DiodeGate:
                    Color gateColor = phase == evt.PhaseParam ? new Color(0.66f, 1f, 0.72f, 1f) : new Color(1f, 0.36f, 0.4f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 0, gateColor, 1f, currentArrow >= 0f ? 1f : -1f, pulseOffset + 0.22f, 0.95f);
                    ApplyIcon(motionRendererB, motionMpbB, 2, gateColor, 1f, currentArrow >= 0f ? 1f : -1f, pulseOffset + 0.52f, 0.72f);
                    if (motionRendererA != null)
                    {
                        motionRendererA.transform.localRotation = Quaternion.identity;
                    }

                    if (motionRendererB != null)
                    {
                        motionRendererB.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 6.4f) * 12f);
                        motionRendererB.transform.localScale = motionBaseScaleB * (0.9f + Mathf.PingPong(t * 1.6f, 0.18f));
                    }
                    break;

                case PCRElementType.Inverter:
                    Color invColor = new Color(1f, 0.84f, 0.4f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 1, invColor, 1f, 1f, pulseOffset + 0.16f, 1.05f);
                    ApplyIcon(motionRendererB, motionMpbB, 1, invColor, 0.82f, -1f, pulseOffset + 0.66f, 0.75f);
                    if (motionRendererA != null)
                    {
                        motionRendererA.transform.localRotation = Quaternion.Euler(0f, 0f, t * 180f);
                    }

                    if (motionRendererB != null)
                    {
                        motionRendererB.transform.localRotation = Quaternion.Euler(0f, 0f, -t * 220f);
                        motionRendererB.transform.localScale = motionBaseScaleB * (0.92f + Mathf.PingPong(t * 1.8f, 0.14f));
                    }
                    break;

                case PCRElementType.SparkGap:
                    bool lethalNow = phase == evt.PhaseParam;
                    Color arcColor = lethalNow ? new Color(1f, 0.38f, 0.46f, 1f) : new Color(0.48f, 0.9f, 1f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 6, arcColor, lethalNow ? 1f : 0.72f, 1f, pulseOffset + 0.18f, lethalNow ? 1.3f : 0.86f);
                    ApplyIcon(motionRendererB, motionMpbB, 6, arcColor, 0.9f, -1f, pulseOffset + 0.57f, 0.84f);
                    if (motionRendererA != null)
                    {
                        motionRendererA.transform.localScale = motionBaseScaleA * (1f + Mathf.PingPong(t * 2.2f, 0.12f));
                    }

                    if (motionRendererB != null)
                    {
                        float jitter = (Mathf.PerlinNoise(jitterSeed, t * 11f) - 0.5f) * 0.16f;
                        motionRendererB.transform.localPosition = motionBasePosB + new Vector3(jitter, 0f, jitter * 0.08f);
                        motionRendererB.transform.localRotation = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(jitterSeed + 2f, t * 13f) - 0.5f) * 34f);
                    }
                    break;

                case PCRElementType.MuxSwitch:
                    float inactiveDirMux = currentArrow >= 0f ? -1f : 1f;
                    Color muxOn = phase == PCRPhase.A ? new Color(0.36f, 1f, 0.46f, 1f) : new Color(1f, 0.62f, 0.2f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 2, muxOn, 1f, currentArrow, pulseOffset + 0.2f, 1.18f);
                    ApplyIcon(motionRendererB, motionMpbB, 2, muxOn * 0.65f, 0.62f, inactiveDirMux, pulseOffset + 0.49f, 0.68f);
                    if (motionRendererB != null)
                    {
                        motionRendererB.transform.localScale = motionBaseScaleB * (0.92f + Mathf.PingPong(t * 1.5f, 0.12f));
                    }
                    break;

                case PCRElementType.InductorCoupler:
                    Color indColor = phase == PCRPhase.A ? new Color(0.38f, 1f, 0.5f, 1f) : new Color(1f, 0.66f, 0.24f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 7, indColor, 1f, currentArrow, pulseOffset + 0.14f, 1.08f);
                    ApplyIcon(motionRendererB, motionMpbB, 2, indColor, 0.86f, currentArrow, pulseOffset + 0.42f, 0.74f);
                    if (motionRendererB != null)
                    {
                        motionRendererB.transform.localPosition = motionBasePosB + new Vector3(Mathf.Sin(t * 3.4f) * 0.09f, 0f, 0f);
                        motionRendererB.transform.localRotation = Quaternion.Euler(0f, 0f, t * 55f);
                    }
                    break;

                case PCRElementType.Capacitor:
                    Color capColor = new Color(0.64f, 0.8f, 1f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 3, capColor, 0.88f + Mathf.PingPong(t * 1.2f, 0.12f), 1f, pulseOffset + 0.08f, 0.92f);
                    ApplyIcon(motionRendererB, motionMpbB, 3, capColor * 0.82f, 0.74f, -1f, pulseOffset + 0.31f, 0.64f);
                    if (motionRendererB != null)
                    {
                        motionRendererB.transform.localScale = motionBaseScaleB * (0.88f + Mathf.PingPong(t * 1.8f, 0.2f));
                    }
                    break;

                case PCRElementType.Amplifier:
                    Color ampColor = new Color(1f, 0.9f, 0.46f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 4, ampColor, 1f, 1f, pulseOffset + 0.08f, 1.18f);
                    ApplyIcon(motionRendererB, motionMpbB, 4, ampColor * 0.76f, 0.76f, -1f, pulseOffset + 0.44f, 0.66f);
                    if (motionRendererB != null)
                    {
                        motionRendererB.transform.localScale = motionBaseScaleB * (1.02f + Mathf.PingPong(t * 1.5f, 0.3f));
                    }
                    break;

                case PCRElementType.GroundClamp:
                    Color clampColor = new Color(0.72f, 0.98f, 0.78f, 1f);
                    ApplyIcon(motionRendererA, motionMpbA, 5, clampColor, 1f, 1f, pulseOffset + 0.12f, 0.94f);
                    ApplyIcon(motionRendererB, motionMpbB, 5, clampColor * 0.78f, 0.82f, -1f, pulseOffset + 0.48f, 0.62f);
                    if (motionRendererB != null)
                    {
                        motionRendererB.transform.localRotation = Quaternion.Euler(0f, 0f, t * 46f);
                        motionRendererB.transform.localScale = motionBaseScaleB * (1.05f + Mathf.PingPong(t * 1.1f, 0.18f));
                    }
                    break;

                default:
                    ApplyIcon(motionRendererA, motionMpbA, MapIconType(evt.Type), Color.white, active01, currentArrow, pulseOffset + 0.12f, 0.84f);
                    ApplyIcon(motionRendererB, motionMpbB, MapIconType(evt.Type), Color.white * 0.8f, active01, -currentArrow, pulseOffset + 0.44f, 0.58f);
                    break;
            }
        }

        private void UpdatePhaseBadge(int stepDelta)
        {
            if (phaseBadgeCore == null)
            {
                return;
            }

            bool show = stepDelta >= -1 && stepDelta <= 7;
            SetBadgeVisibility(show);
            if (!show)
            {
                return;
            }

            if (phaseBadgeRoot != null && Camera.main != null)
            {
                phaseBadgeRoot.rotation = Quaternion.LookRotation(Camera.main.transform.forward, Vector3.up);
            }

            Color phaseAColor = new Color(0.36f, 1f, 0.44f, 0.96f);
            Color phaseBColor = new Color(1f, 0.6f, 0.2f, 0.96f);
            bool lethalPhaseBadge = evt.Type == PCRElementType.SparkGap;
            PCRPhaseMask mask = GetBadgeMaskForEvent(evt);

            Color coreColor = mask switch
            {
                PCRPhaseMask.A => phaseAColor,
                PCRPhaseMask.B => phaseBColor,
                _ => new Color(0.88f, 0.94f, 1f, 0.96f)
            };
            if (lethalPhaseBadge)
            {
                coreColor = Color.Lerp(coreColor, new Color(1f, 0.26f, 0.3f, coreColor.a), 0.44f);
            }

            ApplyBadgeColor(phaseBadgeCore, coreColor, 0.7f);

            bool topOn = mask == PCRPhaseMask.A || mask == PCRPhaseMask.Both;
            bool bottomOn = mask == PCRPhaseMask.B || mask == PCRPhaseMask.Both;
            if (phaseBadgeStripeTop != null)
            {
                phaseBadgeStripeTop.enabled = topOn;
                if (topOn)
                {
                    ApplyBadgeColor(phaseBadgeStripeTop, phaseAColor, 0.9f);
                }
            }

            if (phaseBadgeStripeBottom != null)
            {
                phaseBadgeStripeBottom.enabled = bottomOn;
                if (bottomOn)
                {
                    ApplyBadgeColor(phaseBadgeStripeBottom, phaseBColor, 0.9f);
                }
            }

            if (phaseBadgeText != null)
            {
                string maskArrow = mask switch
                {
                    PCRPhaseMask.A => "↑",
                    PCRPhaseMask.B => "↓",
                    _ => "↕"
                };
                phaseBadgeText.text = lethalPhaseBadge ? $"X{maskArrow}" : maskArrow;
                phaseBadgeText.color = mask switch
                {
                    PCRPhaseMask.A => phaseAColor,
                    PCRPhaseMask.B => phaseBColor,
                    _ => new Color(0.88f, 0.94f, 1f, 1f)
                };
            }
        }

        private static PCRPhaseMask GetBadgeMaskForEvent(PCREventRuntime evt)
        {
            return evt.Type switch
            {
                PCRElementType.DiodeGate => evt.PhaseParam == PCRPhase.A ? PCRPhaseMask.A : PCRPhaseMask.B,
                PCRElementType.SparkGap => evt.PhaseParam == PCRPhase.A ? PCRPhaseMask.A : PCRPhaseMask.B,
                PCRElementType.MuxSwitch => PCRPhaseMask.Both,
                PCRElementType.InductorCoupler => PCRPhaseMask.Both,
                _ => PCRPhaseMask.Both
            };
        }
    }

    public sealed class PCRDecisionTelegraphView : MonoBehaviour
    {
        private PCRDecisionWindow decision;
        private Renderer targetRenderer;
        private MaterialPropertyBlock mpb;
        private Transform badgeRoot;
        private Renderer badgeCore;
        private Renderer badgeStripeTop;
        private Renderer badgeStripeBottom;
        private TextMesh badgeText;
        private MaterialPropertyBlock badgeMpb;
        private Color baseColor;

        public void Initialize(PCRDecisionWindow window, Renderer renderer)
        {
            decision = window;
            targetRenderer = renderer;
            mpb = new MaterialPropertyBlock();
            badgeMpb = new MaterialPropertyBlock();
            Shader badgeShader = Shader.Find("Unlit/Color")
                                ?? Shader.Find("Universal Render Pipeline/Unlit")
                                ?? Shader.Find("Sprites/Default")
                                ?? Shader.Find("Hidden/InternalErrorShader");
            Material badgeMat = new Material(badgeShader);
            if (badgeMat.HasProperty("_Color"))
            {
                badgeMat.SetColor("_Color", Color.white);
            }

            var badgeGo = new GameObject("DecisionBadge");
            badgeRoot = badgeGo.transform;
            badgeRoot.SetParent(transform, false);
            badgeRoot.localPosition = new Vector3(0f, 0.34f, 0f);
            badgeRoot.localRotation = Quaternion.identity;
            badgeCore = CreateBadgePart("Core", badgeRoot, badgeMat, Vector3.zero, new Vector3(0.24f, 0.14f, 1f));
            badgeStripeTop = CreateBadgePart("StripeTop", badgeRoot, badgeMat, new Vector3(0f, 0.035f, 0.006f), new Vector3(0.16f, 0.024f, 1f));
            badgeStripeBottom = CreateBadgePart("StripeBottom", badgeRoot, badgeMat, new Vector3(0f, -0.035f, 0.006f), new Vector3(0.16f, 0.024f, 1f));
            var badgeTextGo = new GameObject("BadgeText");
            badgeTextGo.transform.SetParent(badgeRoot, false);
            badgeTextGo.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            badgeText = badgeTextGo.AddComponent<TextMesh>();
            badgeText.anchor = TextAnchor.MiddleCenter;
            badgeText.alignment = TextAlignment.Center;
            badgeText.fontSize = 68;
            badgeText.characterSize = 0.064f;
            badgeText.text = string.Empty;
            badgeText.color = Color.white;

            baseColor = decision.RequiredMask switch
            {
                PCRPhaseMask.A => new Color(0.38f, 1f, 0.44f, 1f),
                PCRPhaseMask.B => new Color(1f, 0.58f, 0.16f, 1f),
                _ => new Color(0.86f, 0.94f, 1f, 1f)
            };

            Refresh(0, PCRPhase.A, true);
        }

        public void Refresh(int currentStep, PCRPhase phase, bool visible)
        {
            if (targetRenderer == null)
            {
                return;
            }

            int revealStep = Mathf.Max(0, decision.StepIndex - Mathf.Max(1, decision.LeadSteps));
            int delta = decision.StepIndex - currentStep;
            bool active = visible && currentStep >= revealStep && delta >= 0 && delta <= decision.LeadSteps + 2;
            targetRenderer.enabled = active;
            SetBadgeVisible(active);

            if (!active)
            {
                return;
            }

            float t = Mathf.Clamp01((decision.LeadSteps - Mathf.Max(0, delta)) / Mathf.Max(1f, decision.LeadSteps));
            float alpha = Mathf.Lerp(0.2f, 1f, t);
            float glow = Mathf.Lerp(0.3f, 0.84f, t);
            mpb.SetFloat("_IconType", decision.Reason switch
            {
                PCRDecisionReason.Gate => 0f,
                PCRDecisionReason.Arc => 6f,
                _ => 2f
            });
            mpb.SetFloat("_PhaseActive", 1f);
            float decisionArrow = decision.RequiredMask switch
            {
                PCRPhaseMask.A => 1f,
                PCRPhaseMask.B => -1f,
                _ => phase == PCRPhase.A ? 1f : -1f
            };
            mpb.SetFloat("_ArrowDir", decisionArrow);
            mpb.SetColor("_BaseColor", new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
            mpb.SetColor("_Color", new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
            mpb.SetFloat("_Glow", glow);
            targetRenderer.SetPropertyBlock(mpb);
            UpdateBadge(alpha);
            if (badgeRoot != null && Camera.main != null)
            {
                badgeRoot.rotation = Quaternion.LookRotation(Camera.main.transform.forward, Vector3.up);
            }
        }

        private static Renderer CreateBadgePart(string name, Transform parent, Material matTemplate, Vector3 localPos, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (filter.sharedMesh == null)
            {
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(matTemplate);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void SetBadgeVisible(bool visible)
        {
            if (badgeCore != null)
            {
                badgeCore.enabled = visible;
            }

            if (badgeStripeTop != null)
            {
                badgeStripeTop.enabled = visible;
            }

            if (badgeStripeBottom != null)
            {
                badgeStripeBottom.enabled = visible;
            }

            if (badgeText != null)
            {
                badgeText.gameObject.SetActive(visible);
            }
        }

        private void ApplyBadgeColor(Renderer renderer, Color color)
        {
            if (renderer == null || badgeMpb == null)
            {
                return;
            }

            badgeMpb.Clear();
            badgeMpb.SetColor("_Color", color);
            badgeMpb.SetColor("_BaseColor", color);
            badgeMpb.SetColor("_TintColor", color);
            badgeMpb.SetFloat("_Glow", 0.8f);
            renderer.SetPropertyBlock(badgeMpb);
        }

        private void UpdateBadge(float alpha)
        {
            if (badgeCore == null)
            {
                return;
            }

            Color aColor = new Color(0.38f, 1f, 0.44f, alpha);
            Color bColor = new Color(1f, 0.58f, 0.16f, alpha);
            Color bothColor = new Color(0.88f, 0.94f, 1f, alpha);

            Color coreColor = decision.RequiredMask switch
            {
                PCRPhaseMask.A => aColor,
                PCRPhaseMask.B => bColor,
                _ => bothColor
            };
            ApplyBadgeColor(badgeCore, coreColor);

            bool topOn = decision.RequiredMask == PCRPhaseMask.A || decision.RequiredMask == PCRPhaseMask.Both;
            bool bottomOn = decision.RequiredMask == PCRPhaseMask.B || decision.RequiredMask == PCRPhaseMask.Both;
            if (badgeStripeTop != null)
            {
                badgeStripeTop.enabled = topOn;
                if (topOn)
                {
                    ApplyBadgeColor(badgeStripeTop, aColor);
                }
            }

            if (badgeStripeBottom != null)
            {
                badgeStripeBottom.enabled = bottomOn;
                if (bottomOn)
                {
                    ApplyBadgeColor(badgeStripeBottom, bColor);
                }
            }

            if (badgeText != null)
            {
                badgeText.text = decision.RequiredMask switch
                {
                    PCRPhaseMask.A => "↑",
                    PCRPhaseMask.B => "↓",
                    _ => "↕"
                };
                badgeText.color = decision.RequiredMask switch
                {
                    PCRPhaseMask.A => aColor,
                    PCRPhaseMask.B => bColor,
                    _ => bothColor
                };
            }
        }
    }
}
