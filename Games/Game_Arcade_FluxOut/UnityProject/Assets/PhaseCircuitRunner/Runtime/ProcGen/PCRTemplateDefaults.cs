using System.Collections.Generic;
using UnityEngine;

namespace FluxOut.PCR
{
    public static class PCRTemplateDefaults
    {
        public static List<PCRSegmentTemplateSO> CreateRuntimeDefaults()
        {
            var list = new List<PCRSegmentTemplateSO>
            {
                BuildIntro2LaneMuxPulse(),
                BuildIntro3LaneGateChoice(),
                BuildCore3LaneInductorRail(),
                BuildCoreInverterThenGate(),
                BuildCoreMuxWithCapacitor(),
                BuildCoreMuxWithAmplifier(),
                BuildCoreCapAmpCombo(),
                BuildBus3To4Distributor(),
                BuildBus4To5DistributorShort(),
                BuildSpikeArcZone(),
                BuildSpikeGateArcMix(),
                BuildCooldownSafeButShifting()
            };

            return list;
        }

        private static PCRSegmentTemplateSO Template(
            string id,
            int laneStart,
            int laneEnd,
            int lengthUnits,
            PCRCurveStyle curveStyle,
            int[] staticDeadLanes,
            List<PCRElementPlacement> placements,
            int expectedTaps,
            int expectedShifts,
            int intensity,
            params string[] tags)
        {
            var so = ScriptableObject.CreateInstance<PCRSegmentTemplateSO>();
            so.TemplateId = id;
            so.LaneCountStart = laneStart;
            so.LaneCountEnd = laneEnd;
            so.LengthUnits = lengthUnits;
            so.CurveStyle = curveStyle;
            so.StaticDeadLanes.Clear();
            for (int i = 0; i < staticDeadLanes.Length; i++)
            {
                so.StaticDeadLanes.Add(staticDeadLanes[i]);
            }

            so.Placements = placements;
            so.EventBudget.ExpectedTaps = expectedTaps;
            so.EventBudget.ExpectedShifts = expectedShifts;
            so.EventBudget.Intensity = intensity;
            so.DecisionIntensity = intensity;
            so.PhaseDemandBias = 0;
            so.Tags.Clear();
            for (int i = 0; i < tags.Length; i++)
            {
                so.Tags.Add(tags[i]);
            }

            return so;
        }

        private static PCRElementPlacement E(
            PCRElementType type,
            float s01,
            int lane,
            PCRPhase phase = PCRPhase.A,
            int routeDeltaA = 1,
            int routeDeltaB = -1,
            float sideOffset = 0f)
        {
            return new PCRElementPlacement
            {
                Type = type,
                SPosition01 = s01,
                LaneIndex = lane,
                PhaseParam = phase,
                RouteDeltaA = routeDeltaA,
                RouteDeltaB = routeDeltaB,
                SideOffset = sideOffset
            };
        }

        private static PCRSegmentTemplateSO BuildIntro2LaneMuxPulse()
        {
            return Template(
                "Intro_2Lane_MuxPulse",
                2,
                2,
                8,
                PCRCurveStyle.GentleS,
                new int[] { },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.14f, 0),
                    E(PCRElementType.MuxSwitch, 0.32f, 1),
                    E(PCRElementType.MuxSwitch, 0.53f, 0),
                    E(PCRElementType.MuxSwitch, 0.72f, 1),
                    E(PCRElementType.DiodeGate, 0.88f, 0, PCRPhase.A)
                },
                2,
                4,
                2,
                "intro");
        }

        private static PCRSegmentTemplateSO BuildIntro3LaneGateChoice()
        {
            return Template(
                "Intro_3Lane_GateChoice",
                3,
                3,
                9,
                PCRCurveStyle.GentleC,
                new[] { 2 },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.15f, 1),
                    E(PCRElementType.DiodeGate, 0.28f, 0, PCRPhase.A),
                    E(PCRElementType.DiodeGate, 0.30f, 1, PCRPhase.B),
                    E(PCRElementType.MuxSwitch, 0.52f, 0),
                    E(PCRElementType.MuxSwitch, 0.75f, 1),
                    E(PCRElementType.Inverter, 0.88f, 1)
                },
                2,
                4,
                2,
                "intro");
        }

        private static PCRSegmentTemplateSO BuildCore3LaneInductorRail()
        {
            return Template(
                "Core_3Lane_InductorRail",
                3,
                3,
                11,
                PCRCurveStyle.GentleS,
                new int[] { },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.InductorCoupler, 0.16f, 1),
                    E(PCRElementType.MuxSwitch, 0.27f, 2),
                    E(PCRElementType.InductorCoupler, 0.44f, 1),
                    E(PCRElementType.MuxSwitch, 0.58f, 0),
                    E(PCRElementType.InductorCoupler, 0.74f, 2),
                    E(PCRElementType.DiodeGate, 0.89f, 1, PCRPhase.B)
                },
                3,
                5,
                3,
                "core");
        }

        private static PCRSegmentTemplateSO BuildCoreInverterThenGate()
        {
            return Template(
                "Core_InverterThenGate",
                3,
                3,
                10,
                PCRCurveStyle.TurnRight,
                new int[] { },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.18f, 1),
                    E(PCRElementType.Inverter, 0.30f, 1),
                    E(PCRElementType.DiodeGate, 0.42f, 1, PCRPhase.B),
                    E(PCRElementType.MuxSwitch, 0.57f, 2),
                    E(PCRElementType.Inverter, 0.70f, 0),
                    E(PCRElementType.DiodeGate, 0.84f, 0, PCRPhase.A)
                },
                2,
                4,
                3,
                "core");
        }

        private static PCRSegmentTemplateSO BuildCoreMuxWithCapacitor()
        {
            return Template(
                "Core_MuxWithCapacitor",
                3,
                4,
                11,
                PCRCurveStyle.TurnLeft,
                new[] { 0 },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.Capacitor, 0.16f, 1),
                    E(PCRElementType.MuxSwitch, 0.22f, 1),
                    E(PCRElementType.MuxSwitch, 0.41f, 2),
                    E(PCRElementType.Capacitor, 0.53f, 2),
                    E(PCRElementType.MuxSwitch, 0.58f, 2),
                    E(PCRElementType.DiodeGate, 0.76f, 3, PCRPhase.A),
                    E(PCRElementType.MuxSwitch, 0.87f, 1)
                },
                3,
                5,
                3,
                "core", "bus");
        }

        private static PCRSegmentTemplateSO BuildCoreMuxWithAmplifier()
        {
            return Template(
                "Core_MuxWithAmplifier",
                3,
                4,
                10,
                PCRCurveStyle.GentleC,
                new int[] { },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.Amplifier, 0.12f, 1),
                    E(PCRElementType.MuxSwitch, 0.17f, 1),
                    E(PCRElementType.DiodeGate, 0.33f, 3, PCRPhase.B),
                    E(PCRElementType.MuxSwitch, 0.46f, 2),
                    E(PCRElementType.Amplifier, 0.58f, 2),
                    E(PCRElementType.MuxSwitch, 0.64f, 2),
                    E(PCRElementType.Inverter, 0.80f, 1)
                },
                3,
                6,
                4,
                "core");
        }

        private static PCRSegmentTemplateSO BuildCoreCapAmpCombo()
        {
            return Template(
                "Core_CapAmpCombo",
                4,
                4,
                12,
                PCRCurveStyle.GentleS,
                new[] { 0 },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.Capacitor, 0.12f, 1),
                    E(PCRElementType.Amplifier, 0.18f, 1),
                    E(PCRElementType.MuxSwitch, 0.23f, 1),
                    E(PCRElementType.MuxSwitch, 0.40f, 2),
                    E(PCRElementType.GroundClamp, 0.51f, 2),
                    E(PCRElementType.MuxSwitch, 0.56f, 2),
                    E(PCRElementType.InductorCoupler, 0.70f, 3),
                    E(PCRElementType.DiodeGate, 0.86f, 3, PCRPhase.A)
                },
                3,
                6,
                4,
                "core");
        }

        private static PCRSegmentTemplateSO BuildBus3To4Distributor()
        {
            return Template(
                "Bus_3to4_Distributor",
                3,
                4,
                12,
                PCRCurveStyle.TurnRight,
                new[] { 0 },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.14f, 1),
                    E(PCRElementType.InductorCoupler, 0.27f, 2),
                    E(PCRElementType.MuxSwitch, 0.42f, 1),
                    E(PCRElementType.DiodeGate, 0.54f, 2, PCRPhase.B),
                    E(PCRElementType.MuxSwitch, 0.66f, 3),
                    E(PCRElementType.Inverter, 0.79f, 2),
                    E(PCRElementType.MuxSwitch, 0.90f, 1)
                },
                3,
                6,
                4,
                "bus");
        }

        private static PCRSegmentTemplateSO BuildBus4To5DistributorShort()
        {
            return Template(
                "Bus_4to5_DistributorShort",
                4,
                5,
                9,
                PCRCurveStyle.TurnLeft,
                new[] { 0, 4 },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.12f, 2),
                    E(PCRElementType.InductorCoupler, 0.25f, 1),
                    E(PCRElementType.MuxSwitch, 0.39f, 3),
                    E(PCRElementType.Amplifier, 0.50f, 2),
                    E(PCRElementType.MuxSwitch, 0.57f, 2),
                    E(PCRElementType.DiodeGate, 0.73f, 4, PCRPhase.B),
                    E(PCRElementType.MuxSwitch, 0.87f, 1)
                },
                3,
                6,
                4,
                "bus");
        }

        private static PCRSegmentTemplateSO BuildSpikeArcZone()
        {
            return Template(
                "Spike_ArcZone",
                4,
                4,
                10,
                PCRCurveStyle.GentleC,
                new[] { 0 },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.10f, 1),
                    E(PCRElementType.SparkGap, 0.23f, 2, PCRPhase.A),
                    E(PCRElementType.MuxSwitch, 0.37f, 2),
                    E(PCRElementType.SparkGap, 0.52f, 1, PCRPhase.B),
                    E(PCRElementType.MuxSwitch, 0.65f, 1),
                    E(PCRElementType.InductorCoupler, 0.79f, 3),
                    E(PCRElementType.DiodeGate, 0.90f, 3, PCRPhase.A)
                },
                4,
                6,
                5,
                "spike");
        }

        private static PCRSegmentTemplateSO BuildSpikeGateArcMix()
        {
            return Template(
                "Spike_GateArcMix",
                4,
                5,
                11,
                PCRCurveStyle.TurnRight,
                new[] { 0 },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.09f, 2),
                    E(PCRElementType.DiodeGate, 0.20f, 2, PCRPhase.A),
                    E(PCRElementType.SparkGap, 0.31f, 3, PCRPhase.B),
                    E(PCRElementType.Inverter, 0.43f, 2),
                    E(PCRElementType.DiodeGate, 0.54f, 3, PCRPhase.B),
                    E(PCRElementType.MuxSwitch, 0.65f, 3),
                    E(PCRElementType.SparkGap, 0.76f, 1, PCRPhase.A),
                    E(PCRElementType.MuxSwitch, 0.87f, 1)
                },
                4,
                6,
                5,
                "spike");
        }

        private static PCRSegmentTemplateSO BuildCooldownSafeButShifting()
        {
            return Template(
                "Cooldown_SafeButShifting",
                3,
                4,
                9,
                PCRCurveStyle.Straight,
                new int[] { },
                new List<PCRElementPlacement>
                {
                    E(PCRElementType.MuxSwitch, 0.14f, 1),
                    E(PCRElementType.InductorCoupler, 0.33f, 2),
                    E(PCRElementType.MuxSwitch, 0.51f, 1),
                    E(PCRElementType.GroundClamp, 0.67f, 2),
                    E(PCRElementType.MuxSwitch, 0.79f, 2),
                    E(PCRElementType.Inverter, 0.91f, 1)
                },
                2,
                5,
                2,
                "cooldown");
        }
    }
}

