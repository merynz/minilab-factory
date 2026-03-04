using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluxOut.PCR
{
    public enum PCRPhase
    {
        A = 0,
        B = 1
    }

    public enum PCRElementType
    {
        DiodeGate = 0,
        Inverter = 1,
        MuxSwitch = 2,
        Capacitor = 3,
        Amplifier = 4,
        GroundClamp = 5,
        InductorCoupler = 6,
        SparkGap = 7
    }

    public enum PCRCurveStyle
    {
        Straight = 0,
        GentleS = 1,
        GentleC = 2,
        TurnLeft = 3,
        TurnRight = 4
    }

    public enum PCRDeathType
    {
        None = 0,
        DeadTrace = 1,
        ClosedGate = 2,
        ArcZone = 3
    }

    [Flags]
    public enum PCRPhaseMask
    {
        None = 0,
        A = 1,
        B = 2,
        Both = A | B
    }

    public enum PCRDecisionReason
    {
        Gate = 0,
        Arc = 1,
        Routing = 2
    }

    [Serializable]
    public sealed class PCRElementPlacement
    {
        public PCRElementType Type = PCRElementType.MuxSwitch;
        [Range(-1, 8)] public int LaneIndex = -1;
        [Range(0f, 1f)] public float SPosition01 = 0.5f;
        [Range(-3f, 3f)] public float SideOffset = 0f;
        public PCRPhase PhaseParam = PCRPhase.A;
        public int RouteDeltaA = 1;
        public int RouteDeltaB = -1;
    }

    [Serializable]
    public sealed class PCRSegmentEventBudget
    {
        [Min(0)] public int ExpectedTaps = 1;
        [Min(0)] public int ExpectedShifts = 2;
        [Range(1, 5)] public int Intensity = 2;
    }

    [Serializable]
    public struct PCRDecisionWindow
    {
        public int StepIndex;
        public PCRPhaseMask RequiredMask;
        public PCRDecisionReason Reason;
        public int LeadSteps;
        public int LaneIndex;
    }

    [CreateAssetMenu(fileName = "PCRSegmentTemplate", menuName = "FluxOut/PCR/Segment Template")]
    public sealed class PCRSegmentTemplateSO : ScriptableObject
    {
        public string TemplateId = "Template";
        [Range(2, 5)] public int LaneCountStart = 3;
        [Range(2, 5)] public int LaneCountEnd = 3;
        [Range(6, 16)] public int LengthUnits = 10;
        public PCRCurveStyle CurveStyle = PCRCurveStyle.GentleS;
        public List<int> StaticDeadLanes = new();
        public List<PCRElementPlacement> Placements = new();
        public PCRSegmentEventBudget EventBudget = new();
        [Range(1, 5)] public int DecisionIntensity = 2;
        [Range(-2, 2)] public int PhaseDemandBias = 0;
        public bool HasSafePath = true;
        public bool OptionalRewardBranch = false;
        public List<string> Tags = new();
    }

    [CreateAssetMenu(fileName = "PCRSegmentLibrary", menuName = "FluxOut/PCR/Segment Library")]
    public sealed class PCRSegmentLibrarySO : ScriptableObject
    {
        public List<PCRSegmentTemplateSO> Templates = new();
    }

    [CreateAssetMenu(fileName = "PCRDifficultyProfile", menuName = "FluxOut/PCR/Difficulty Profile")]
    public sealed class PCRDifficultyProfileSO : ScriptableObject
    {
        [Range(7.5f, 14f)] public float StartSpeed = 7.5f;
        [Range(7.5f, 14f)] public float EndSpeed = 10.5f;
        [Range(2, 5)] public int MinLaneCount = 2;
        [Range(2, 5)] public int MaxLaneCount = 5;
    }

    [CreateAssetMenu(fileName = "PCRVisualProfile", menuName = "FluxOut/PCR/Visual Profile")]
    public sealed class PCRVisualProfileSO : ScriptableObject
    {
        public Color BackgroundColor = new(0.02f, 0.04f, 0.07f, 1f);
        public Color LiveTraceColor = new(0.12f, 0.94f, 0.88f, 1f);
        public Color DeadTraceColor = new(0.2f, 0.2f, 0.2f, 1f);
        public Color PhaseAColor = new(0.16f, 0.9f, 1f, 1f);
        public Color PhaseBColor = new(1f, 0.38f, 0.2f, 1f);
        [Range(0f, 2f)] public float BloomStrength = 0.75f;
    }

    [CreateAssetMenu(fileName = "PCRPacingProfile", menuName = "FluxOut/PCR/Pacing Profile")]
    public sealed class PCRPacingProfileSO : ScriptableObject
    {
        [Range(0.08f, 0.2f)] public float SimStepSec = 0.12f;
        [Range(70f, 140f)] public float TargetDurationSec = 90f;
        [Range(1f, 3f)] public float EmptyGapThresholdSec = 2f;
        [Range(0.6f, 2.5f)] public float LaneShiftTargetSec = 1.2f;
        [Range(1f, 1.6f)] public float EventCadenceMinSec = 1f;
        [Range(1f, 1.8f)] public float EventCadenceMaxSec = 1.4f;
        [Range(6f, 14f)] public float MaxNoTapSurvivalSec = 9f;
        [Range(1f, 2.5f)] public float MaxDecisionGapSec = 2f;
        [Range(1f, 10f)] public float MinRequiredTapPer10Sec = 4f;
        [Range(1.5f, 5f)] public float MaxSamePhaseRunSec = 3f;
        [Range(0.4f, 2f)] public float DecisionLeadTimeSec = 1f;
        [Range(10, 20)] public int SegmentCountMin = 12;
        [Range(10, 24)] public int SegmentCountMax = 16;
    }

    [CreateAssetMenu(fileName = "PCRLevelDoc", menuName = "FluxOut/PCR/Level Doc")]
    public sealed class PCRLevelDoc : ScriptableObject
    {
        public int Seed = 4242;
        public float TargetDurationSec = 90f;
        public int SegmentCount = 14;
        public PCRDifficultyProfileSO DifficultyProfile;
        public PCRVisualProfileSO VisualProfile;
        public PCRPacingProfileSO PacingProfile;
        public PCRSegmentLibrarySO SegmentLibrary;
    }

    [Serializable]
    public sealed class PCRSegmentRuntime
    {
        public string TemplateId;
        public int SegmentIndex;
        public int StartStep;
        public int EndStep;
        public int LaneCountStart;
        public int LaneCountEnd;
        public int StaticDeadMask;
        public PCRCurveStyle CurveStyle;
        public float CurveAmplitude;
        public float StartDistance;
        public float EndDistance;
        public float XStart;
        public float XEnd;
    }

    [Serializable]
    public sealed class PCREventRuntime
    {
        public int EventId;
        public PCRElementType Type;
        public int SegmentIndex;
        public int StepIndex;
        public int LaneIndex;
        public float SideOffset;
        public PCRPhase PhaseParam;
        public int RouteDeltaA;
        public int RouteDeltaB;
        public bool IsField;
        public Vector3 WorldPosition;
        public Vector3 WorldNormal;
    }

    [Serializable]
    public sealed class PCRLevelRuntime
    {
        public int Seed;
        public float DurationSec;
        public float SimStepSec;
        public float StartSpeed;
        public float EndSpeed;
        public int TotalSteps;
        public int MaxLaneCount;
        public float LaneSpacing;
        public List<PCRSegmentRuntime> Segments = new();
        public List<PCREventRuntime> Events = new();
        public List<PCREventRuntime>[] EventsByStep;
        public List<PCRDecisionWindow> Decisions = new();
        public List<PCRDecisionWindow>[] DecisionsByStep;
        // Bitmask per step: 1 = safe/glow lane, 0 = dead/matte lane.
        public int[] SafeLaneMaskByStep;
        public float[] StepDistances;
        public Vector3[] CenterlinePoints;
        public Vector3[] Tangents;
    }

    [Serializable]
    public struct PCRModifierState
    {
        public bool DelayNextRouting;
        public bool HasQueuedRoute;
        public int QueuedRouteDelta;
        public int AmplifyNextRouting;
        public int CancelRoutingSteps;
    }

    [Serializable]
    public struct PCRRunState
    {
        public int StepIndex;
        public int LaneIndex;
        public PCRPhase Phase;
        public PCRModifierState Modifiers;
        public bool Dead;
        public PCRDeathType DeathType;
    }

    public struct PCRStepOutput
    {
        public bool Shifted;
        public int AppliedRouteDelta;
        public bool MeaningfulEvent;
        public bool PhaseFlippedByInverter;
        public bool Finished;
        public bool Dead;
        public PCRDeathType DeathType;
    }
}
