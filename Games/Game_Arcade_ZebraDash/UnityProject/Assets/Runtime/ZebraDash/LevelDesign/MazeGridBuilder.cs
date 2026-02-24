using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZebraDash.LevelDesign
{
    [Serializable]
    public sealed class MazeTileSpec
    {
        public float designX;
        public float centerY;
        public float width;
        public float height;
        public bool isDanger;
        public bool isSolid;
        public bool isDecor;
        public int sectionIndex;
        public string visualKind = "wall";
        public int variant;
    }

    [Serializable]
    public sealed class MazeGapColumn
    {
        public float designX;
        public float gapBottom;
        public float gapTop;
        public int sectionIndex;
    }

    [Serializable]
    public sealed class MazeBeatCue
    {
        public int beatIndex;
        public float timeSec;
        public float designX;
        public bool expectedTap;
        public int sectionIndex;
    }

    [Serializable]
    public sealed class MazeLayout
    {
        public float tileSize = 1f;
        public float durationSec;
        public MazeGapColumn[] columns = Array.Empty<MazeGapColumn>();
        public MazeTileSpec[] tiles = Array.Empty<MazeTileSpec>();
        public MazeBeatCue[] beatCues = Array.Empty<MazeBeatCue>();
    }

    public static class MazeGridBuilder
    {
        private const int GridRows = 11;
        private const int BottomRow = 0;
        private const int TopRow = GridRows - 1;
        private const float CellSize = 1f;
        private const float CenterRow = 5f;
        private const int WarmupBeats = 6;

        private enum GateKind
        {
            None = 0,
            Pit = 1,
            LowCeiling = 2,
            Choke = 3
        }

        private sealed class BeatPlan
        {
            public int BeatIndex;
            public int LocalBeat;
            public int SectionIndex;
            public SectionKind SectionKind;
            public bool ExpectedTap;
            public bool IsWarmup;
            public GateKind Gate;
            public int FloorRow;
            public int CeilingRow;
            public int ColsPerBeat;
            public float BeatSec;
            public float BeatStartX;
            public float BeatStartSec;
            public float SafeFloorY;
            public float SafeCeilingY;
            public MovementProfile Profile;
        }

        private struct SimState
        {
            public float Y;
            public float Vy;
            public float CoyoteSec;
            public bool Grounded;

            public static SimState AtFloor(float floorY)
            {
                return new SimState
                {
                    Y = floorY,
                    Vy = 0f,
                    CoyoteSec = 0f,
                    Grounded = true
                };
            }
        }

        public static MazeLayout Build(LevelOrchestration orchestration, float durationSec, int seed)
        {
            if (orchestration == null || orchestration.sections == null || orchestration.sections.Length == 0)
            {
                return new MazeLayout { durationSec = Mathf.Max(8f, durationSec) };
            }

            MazeLayout fallback = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int attemptSeed = unchecked(seed + (attempt * 7919));
                if (!TryBuildAttempt(orchestration, durationSec, attemptSeed, out MazeLayout layout, out List<BeatPlan> plans))
                {
                    continue;
                }

                fallback ??= layout;
                if (ReachabilitySimulator.HasValidRoute(plans))
                {
                    return layout;
                }
            }

            return fallback ?? new MazeLayout { durationSec = Mathf.Max(8f, durationSec) };
        }

        private static bool TryBuildAttempt(
            LevelOrchestration orchestration,
            float durationSec,
            int seed,
            out MazeLayout layout,
            out List<BeatPlan> beatPlans)
        {
            var columns = new List<MazeGapColumn>(4096);
            var tiles = new List<MazeTileSpec>(26000);
            var beatCues = new List<MazeBeatCue>(2048);
            beatPlans = new List<BeatPlan>(2048);

            float designX = 0f;
            float songTime = 0f;
            float centerRow = CenterRow;
            int globalBeat = 0;
            int lastTapBeat = -99;
            int beatsSinceGate = 0;
            int beatsSinceDecision = 0;

            SimState simState = default;
            bool simStateInitialized = false;

            for (int s = 0; s < orchestration.sections.Length; s++)
            {
                SectionPlan section = orchestration.sections[s];
                if (section == null)
                {
                    continue;
                }

                MovementProfile profile = orchestration.ResolveProfile(section.movementProfileRef) ?? new MovementProfile();
                int colsPerBeat = Mathf.Clamp(Mathf.RoundToInt(profile.ColumnsPerBeat), 1, 6);
                int beatsInSection = Mathf.Max(1, section.barsLength * 4);

                for (int localBeat = 0; localBeat < beatsInSection; localBeat++)
                {
                    bool expectedTap = ResolveExpectedTap(section, profile, seed, globalBeat, localBeat, lastTapBeat, beatsSinceDecision);
                    if (expectedTap)
                    {
                        lastTapBeat = globalBeat;
                    }

                    GateKind gate = ResolveGate(section.sectionType, expectedTap, beatsSinceGate, seed, globalBeat);
                    centerRow = ResolveNextCenterRow(section, centerRow, expectedTap, gate, seed, globalBeat);

                    BeatPlan beatPlan = BuildBeatPlan(
                        section,
                        profile,
                        gate,
                        expectedTap,
                        centerRow,
                        colsPerBeat,
                        designX,
                        songTime,
                        globalBeat,
                        localBeat,
                        seed,
                        simStateInitialized ? simState : SimState.AtFloor(RowToSafeFloorY(3)));

                    if (!simStateInitialized)
                    {
                        simState = SimState.AtFloor(beatPlan.SafeFloorY);
                        simStateInitialized = true;
                    }

                    if (!TrySimulateBeat(simState, beatPlan, expectedTap, out SimState nextState, out _))
                    {
                        BeatPlan recovery = BuildRecoveryPlan(section, profile, expectedTap, colsPerBeat, designX, songTime, globalBeat, localBeat);
                        if (!TrySimulateBeat(simState, recovery, expectedTap, out nextState, out _))
                        {
                            layout = null;
                            return false;
                        }

                        beatPlan = recovery;
                    }

                    simState = nextState;
                    beatsSinceGate = beatPlan.Gate == GateKind.None ? beatsSinceGate + 1 : 0;
                    beatsSinceDecision = (expectedTap || beatPlan.Gate != GateKind.None) ? 0 : beatsSinceDecision + 1;
                    beatPlans.Add(beatPlan);

                    beatCues.Add(new MazeBeatCue
                    {
                        beatIndex = beatPlan.BeatIndex,
                        timeSec = beatPlan.BeatStartSec,
                        designX = beatPlan.BeatStartX,
                        expectedTap = beatPlan.ExpectedTap,
                        sectionIndex = beatPlan.SectionIndex
                    });

                    CreateBeatObjects(tiles, beatPlan, seed);

                    for (int c = 0; c < beatPlan.ColsPerBeat; c++)
                    {
                        float colX = beatPlan.BeatStartX + (c * CellSize);
                        MazeGapColumn gapColumn = BuildGapColumn(colX, beatPlan.SectionIndex, beatPlan.FloorRow, beatPlan.CeilingRow);
                        columns.Add(gapColumn);
                        CreateDenseStaticWalls(tiles, colX, beatPlan.FloorRow, beatPlan.CeilingRow, section, beatPlan.BeatIndex, c);
                    }

                    designX += beatPlan.ColsPerBeat * CellSize;
                    songTime += beatPlan.BeatSec;
                    globalBeat++;
                }
            }

            layout = new MazeLayout
            {
                tileSize = CellSize,
                durationSec = Mathf.Max(durationSec, songTime),
                columns = columns.ToArray(),
                tiles = tiles.ToArray(),
                beatCues = beatCues.ToArray()
            };

            return true;
        }

        private static BeatPlan BuildBeatPlan(
            SectionPlan section,
            MovementProfile profile,
            GateKind gate,
            bool expectedTap,
            float centerRow,
            int colsPerBeat,
            float beatStartX,
            float beatStartSec,
            int beatIndex,
            int localBeat,
            int seed,
            SimState simState)
        {
            BeatPlan selected = null;
            float bestFitness = float.NegativeInfinity;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                int hash = DeterministicHash(seed, beatIndex, (attempt * 53) + localBeat + section.sectionIndex);
                float attemptCenter = Mathf.Clamp(centerRow + ((((hash & 255) / 255f) - 0.5f) * 0.9f), 2f, 8f);
                int targetCenterRow = Mathf.Clamp(Mathf.RoundToInt(attemptCenter), 2, 8);
                int gapRows = ResolveGapRows(section, profile, gate, attempt);
                ResolveGapRows(targetCenterRow, gapRows, out int floorRow, out int ceilingRow);

                var plan = new BeatPlan
                {
                    BeatIndex = beatIndex,
                    LocalBeat = localBeat,
                    SectionIndex = section.sectionIndex,
                    SectionKind = section.sectionType,
                    ExpectedTap = expectedTap,
                    IsWarmup = beatIndex < WarmupBeats,
                    Gate = gate,
                    FloorRow = floorRow,
                    CeilingRow = ceilingRow,
                    ColsPerBeat = colsPerBeat,
                    BeatSec = Mathf.Max(0.18f, profile.BeatSec),
                    BeatStartX = beatStartX,
                    BeatStartSec = beatStartSec,
                    SafeFloorY = RowToSafeFloorY(floorRow),
                    SafeCeilingY = RowToSafeCeilingY(ceilingRow),
                    Profile = profile
                };

                if (!TrySimulateBeat(simState, plan, expectedTap, out _, out float fitness))
                {
                    continue;
                }

                if (fitness > bestFitness)
                {
                    bestFitness = fitness;
                    selected = plan;
                }
            }

            if (selected != null)
            {
                return selected;
            }

            return BuildRecoveryPlan(section, profile, expectedTap, colsPerBeat, beatStartX, beatStartSec, beatIndex, localBeat);
        }

        private static BeatPlan BuildRecoveryPlan(
            SectionPlan section,
            MovementProfile profile,
            bool expectedTap,
            int colsPerBeat,
            float beatStartX,
            float beatStartSec,
            int beatIndex,
            int localBeat)
        {
            int centerRow = Mathf.Clamp(Mathf.RoundToInt(CenterRow), 2, 8);
            ResolveGapRows(centerRow, 7, out int floorRow, out int ceilingRow);
            return new BeatPlan
            {
                BeatIndex = beatIndex,
                LocalBeat = localBeat,
                SectionIndex = section.sectionIndex,
                SectionKind = section.sectionType,
                ExpectedTap = expectedTap,
                IsWarmup = beatIndex < WarmupBeats,
                Gate = GateKind.None,
                FloorRow = floorRow,
                CeilingRow = ceilingRow,
                ColsPerBeat = colsPerBeat,
                BeatSec = Mathf.Max(0.18f, profile.BeatSec),
                BeatStartX = beatStartX,
                BeatStartSec = beatStartSec,
                SafeFloorY = RowToSafeFloorY(floorRow),
                SafeCeilingY = RowToSafeCeilingY(ceilingRow),
                Profile = profile
            };
        }

        private static bool TrySimulateBeat(SimState state, BeatPlan plan, bool expectedTap, out SimState next, out float fitness)
        {
            next = state;
            fitness = 0f;

            float dt = Mathf.Max(0.008f, plan.BeatSec / 12f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(plan.BeatSec / dt), 8, 32);
            dt = plan.BeatSec / steps;

            float y = Mathf.Clamp(state.Y, plan.SafeFloorY, plan.SafeCeilingY);
            float vy = state.Vy;
            bool grounded = state.Grounded;
            float coyote = state.CoyoteSec;
            bool tapConsumed = false;
            float apex = y;
            float closestToCenter = Mathf.Abs(y - ((plan.SafeFloorY + plan.SafeCeilingY) * 0.5f));

            float gravity = Mathf.Max(4f, plan.Profile.GravityUnitsPerSec2);
            float jumpVel = Mathf.Max(1.5f, plan.Profile.JumpVelocityUnitsPerSec);
            float maxRise = Mathf.Max(jumpVel + 2f, jumpVel * 1.5f);
            float maxFall = Mathf.Max(jumpVel + 4f, jumpVel * 2.0f);
            float coyoteSec = Mathf.Clamp(plan.Profile.coyoteSec, 0.03f, 0.18f);

            for (int i = 0; i < steps; i++)
            {
                if (grounded)
                {
                    coyote = coyoteSec;
                }
                else
                {
                    coyote = Mathf.Max(0f, coyote - dt);
                }

                if (expectedTap && !tapConsumed && (grounded || coyote > 0f))
                {
                    vy = jumpVel;
                    grounded = false;
                    coyote = 0f;
                    tapConsumed = true;
                }

                vy -= gravity * dt;
                vy = Mathf.Clamp(vy, -maxFall, maxRise);
                y += vy * dt;

                if (y <= plan.SafeFloorY)
                {
                    y = plan.SafeFloorY;
                    if (vy < 0f)
                    {
                        vy = 0f;
                    }

                    grounded = true;
                }
                else if (y >= plan.SafeCeilingY)
                {
                    y = plan.SafeCeilingY;
                    if (vy > 0f)
                    {
                        vy = 0f;
                    }

                    grounded = false;
                }
                else
                {
                    grounded = false;
                }

                apex = Mathf.Max(apex, y);
                float centerY = (plan.SafeFloorY + plan.SafeCeilingY) * 0.5f;
                closestToCenter = Mathf.Min(closestToCenter, Mathf.Abs(y - centerY));

                if (plan.Gate == GateKind.LowCeiling && !expectedTap)
                {
                    if (y >= plan.SafeCeilingY - 0.06f)
                    {
                        return false;
                    }
                }

                if (plan.Gate == GateKind.Choke)
                {
                    float progress = (i + 1f) / steps;
                    if (progress >= 0.35f && progress <= 0.85f)
                    {
                        if (y <= plan.SafeFloorY + 0.05f || y >= plan.SafeCeilingY - 0.05f)
                        {
                            return false;
                        }
                    }
                }
            }

            if (expectedTap && !tapConsumed)
            {
                return false;
            }

            if (plan.Gate == GateKind.Pit)
            {
                if (apex < plan.SafeFloorY + 0.55f)
                {
                    return false;
                }
            }

            if (plan.SafeCeilingY - plan.SafeFloorY < 1.25f)
            {
                return false;
            }

            next = new SimState
            {
                Y = y,
                Vy = vy,
                Grounded = grounded,
                CoyoteSec = coyote
            };

            float corridorMid = (plan.SafeFloorY + plan.SafeCeilingY) * 0.5f;
            float centerPenalty = Mathf.Abs(y - corridorMid);
            float gateBonus = plan.Gate == GateKind.None ? 0.15f : 0.35f;
            fitness = gateBonus - centerPenalty + (expectedTap ? 0.20f : 0f) - (closestToCenter * 0.08f);
            return true;
        }

        private static MazeGapColumn BuildGapColumn(float designX, int sectionIndex, int floorRow, int ceilingRow)
        {
            float gapBottom = RowCenterToY(floorRow) + (CellSize * 0.5f);
            float gapTop = RowCenterToY(ceilingRow) - (CellSize * 0.5f);
            return new MazeGapColumn
            {
                designX = designX,
                gapBottom = gapBottom,
                gapTop = gapTop,
                sectionIndex = sectionIndex
            };
        }

        private static void CreateDenseStaticWalls(
            List<MazeTileSpec> tiles,
            float designX,
            int floorRow,
            int ceilingRow,
            SectionPlan section,
            int beatIndex,
            int localCol)
        {
            for (int row = BottomRow; row <= floorRow; row++)
            {
                string kind = row == floorRow
                    ? "wall_top"
                    : (row == BottomRow ? "wall_bottom" : "wall_inner");
                AddTile(
                    tiles,
                    designX,
                    row,
                    1f,
                    1f,
                    isDanger: false,
                    isSolid: true,
                    isDecor: false,
                    section.sectionIndex,
                    kind,
                    variant: row);
            }

            for (int row = ceilingRow; row <= TopRow; row++)
            {
                string kind = row == ceilingRow
                    ? "wall_bottom"
                    : (row == TopRow ? "wall_top" : "wall_inner");
                AddTile(
                    tiles,
                    designX,
                    row,
                    1f,
                    1f,
                    isDanger: false,
                    isSolid: true,
                    isDecor: false,
                    section.sectionIndex,
                    kind,
                    variant: row);
            }

            int hash = DeterministicHash(section.sectionIndex, beatIndex, localCol);
            if ((hash & 3) == 0)
            {
                AddTile(tiles, designX, floorRow + 1, 0.18f, 0.18f, false, false, true, section.sectionIndex, "wall_trim", hash & 3);
            }

            if ((hash & 7) == 2)
            {
                AddTile(tiles, designX, ceilingRow - 1, 0.18f, 0.18f, false, false, true, section.sectionIndex, "wall_trim", (hash >> 1) & 3);
            }
        }

        private static void CreateBeatObjects(List<MazeTileSpec> tiles, BeatPlan plan, int seed)
        {
            int hash = DeterministicHash(seed, plan.SectionIndex, plan.BeatIndex);
            int midRow = Mathf.RoundToInt((plan.FloorRow + plan.CeilingRow) * 0.5f);

            AddTile(tiles, plan.BeatStartX + 0.14f, midRow, 0.18f, 0.18f, false, false, true, plan.SectionIndex, "beat_guide_light", plan.BeatIndex & 3);

            if ((plan.BeatIndex % 8) == 0)
            {
                AddTile(tiles, plan.BeatStartX + 0.22f, plan.FloorRow + 1, 0.30f, 0.30f, false, false, true, plan.SectionIndex, "tunnel_ring", hash & 3);
            }

            if (plan.SectionKind == SectionKind.Transition)
            {
                AddTile(tiles, plan.BeatStartX + 0.36f, midRow, 0.64f, 1.65f, false, false, true, plan.SectionIndex, "speed_gate", hash & 3);
                AddTile(tiles, plan.BeatStartX + 1.04f, midRow, 0.64f, 1.65f, false, false, true, plan.SectionIndex, "style_gate", (hash >> 2) & 3);
                return;
            }

            if ((plan.BeatIndex % 16) == 0)
            {
                AddTile(tiles, plan.BeatStartX + 0.30f, plan.FloorRow + 1, 0.44f, 0.44f, false, false, true, plan.SectionIndex, "checkpoint_lite", hash & 3);
            }

            if (plan.SectionKind == SectionKind.Rest)
            {
                if ((plan.BeatIndex & 1) == 0)
                {
                    AddTile(tiles, plan.BeatStartX + 0.55f, plan.FloorRow + 1, 0.28f, 0.28f, false, false, true, plan.SectionIndex, "score_orb", hash & 3);
                }

                return;
            }

            if (plan.IsWarmup)
            {
                if ((plan.BeatIndex % 3) == 0)
                {
                    AddTile(tiles, plan.BeatStartX + 0.58f, plan.FloorRow + 1, 0.32f, 0.32f, false, false, true, plan.SectionIndex, "score_orb", hash & 3);
                }

                return;
            }

            switch (Mathf.Abs(hash % 6))
            {
                case 0:
                    AddTile(tiles, plan.BeatStartX + 0.48f, plan.FloorRow + 1, 0.72f, 0.24f, false, false, true, plan.SectionIndex, "platform_thin", hash & 3);
                    break;
                case 1:
                    AddTile(tiles, plan.BeatStartX + 0.52f, plan.FloorRow + 1, 0.62f, 0.24f, false, false, true, plan.SectionIndex, "spring_pad", hash & 3);
                    break;
                case 2:
                    AddTile(tiles, plan.BeatStartX + 0.54f, plan.FloorRow + 1, 0.64f, 0.24f, false, false, true, plan.SectionIndex, "speed_pad_up", hash & 3);
                    break;
                case 3:
                    AddTile(tiles, plan.BeatStartX + 0.54f, plan.FloorRow + 1, 0.64f, 0.24f, false, false, true, plan.SectionIndex, "speed_pad_down", hash & 3);
                    break;
                case 4:
                    AddTile(tiles, plan.BeatStartX + 0.58f, plan.FloorRow + 2, 0.48f, 0.48f, false, false, true, plan.SectionIndex, "jump_ring", hash & 3);
                    break;
                default:
                    AddTile(tiles, plan.BeatStartX + 0.52f, plan.FloorRow + 1, 0.68f, 0.30f, false, false, true, plan.SectionIndex, "solid_block", hash & 3);
                    break;
            }

            if ((hash & 7) == 3)
            {
                string zone = (hash & 1) == 0 ? "gravity_heavy_zone" : "gravity_light_zone";
                AddTile(tiles, plan.BeatStartX + 0.95f, midRow, 0.78f, 0.30f, false, false, true, plan.SectionIndex, zone, hash & 3);
            }

            switch (plan.Gate)
            {
                case GateKind.Pit:
                    AddTile(tiles, plan.BeatStartX + 1.02f, plan.FloorRow + 1, 1.15f, 0.24f, true, false, false, plan.SectionIndex, "acid_pool", hash & 3);
                    AddTile(tiles, plan.BeatStartX + 1.04f, plan.FloorRow + 1, 0.85f, 0.28f, true, false, false, plan.SectionIndex, "spike_floor", (hash >> 1) & 3);
                    break;

                case GateKind.LowCeiling:
                    AddTile(tiles, plan.BeatStartX + 1.02f, plan.CeilingRow - 1, 0.95f, 0.30f, true, false, false, plan.SectionIndex, "spike_ceiling", hash & 3);
                    if ((hash & 1) == 0)
                    {
                        AddTile(tiles, plan.BeatStartX + 1.42f, plan.CeilingRow - 1, 0.38f, 0.86f, true, false, false, plan.SectionIndex, "spike_wall", (hash >> 2) & 3);
                    }
                    break;

                case GateKind.Choke:
                    AddTile(tiles, plan.BeatStartX + 1.00f, plan.FloorRow + 1, 0.74f, 0.28f, true, false, false, plan.SectionIndex, "spike_floor", hash & 3);
                    AddTile(tiles, plan.BeatStartX + 1.00f, plan.CeilingRow - 1, 0.74f, 0.28f, true, false, false, plan.SectionIndex, "spike_ceiling", (hash >> 1) & 3);
                    if ((hash & 3) == 0)
                    {
                        AddTile(tiles, plan.BeatStartX + 1.38f, midRow, 0.30f, 0.96f, true, false, false, plan.SectionIndex, "needle_gate", (hash >> 3) & 3);
                    }
                    break;
            }

            int hazardRoll = Mathf.Abs(hash % 14);
            if (plan.SectionKind == SectionKind.Drop)
            {
                switch (hazardRoll)
                {
                    case 0:
                    case 1:
                        AddTile(tiles, plan.BeatStartX + 1.40f, midRow, 0.58f, 0.58f, true, false, false, plan.SectionIndex, "saw_static", hash & 3);
                        break;
                    case 2:
                        AddTile(tiles, plan.BeatStartX + 1.38f, midRow, 0.70f, 0.70f, true, false, false, plan.SectionIndex, "rotary_saw_large", hash & 3);
                        break;
                    case 3:
                    case 4:
                        AddTile(tiles, plan.BeatStartX + 1.32f, midRow, 0.56f, 0.56f, true, false, false, plan.SectionIndex, "mine_static", hash & 3);
                        break;
                    case 5:
                        AddTile(tiles, plan.BeatStartX + 1.24f, midRow, 0.56f, 0.56f, true, false, false, plan.SectionIndex, "plasma_orb_static", hash & 3);
                        break;
                    case 6:
                        AddTile(tiles, plan.BeatStartX + 1.25f, midRow, 0.52f, 0.52f, true, false, false, plan.SectionIndex, "electric_arc_static", hash & 3);
                        break;
                    case 7:
                    case 8:
                        AddTile(tiles, plan.BeatStartX + 1.26f, midRow, 0.64f, 0.64f, true, false, false, plan.SectionIndex, "crusher_pillar_static", hash & 3);
                        break;
                    case 9:
                        AddTile(tiles, plan.BeatStartX + 1.26f, midRow, 0.72f, 0.74f, true, false, false, plan.SectionIndex, "piston_ram_static", hash & 3);
                        break;
                    case 10:
                        AddTile(tiles, plan.BeatStartX + 1.30f, midRow, 0.62f, 0.26f, true, false, false, plan.SectionIndex, "sheet_spike_wave", hash & 3);
                        break;
                    case 11:
                        AddTile(tiles, plan.BeatStartX + 1.30f, midRow, 0.58f, 0.22f, true, false, false, plan.SectionIndex, "laser_bar_static", hash & 3);
                        break;
                    default:
                        AddTile(tiles, plan.BeatStartX + 1.14f, plan.FloorRow + 1, 0.86f, 0.34f, true, false, false, plan.SectionIndex, "spike_cluster_dense", hash & 3);
                        break;
                }
            }
            else
            {
                if ((hazardRoll % 5) == 0)
                {
                    AddTile(tiles, plan.BeatStartX + 1.30f, midRow, 0.58f, 0.22f, true, false, false, plan.SectionIndex, "laser_bar_static", hash & 3);
                }
                else if ((hazardRoll % 6) == 1)
                {
                    AddTile(tiles, plan.BeatStartX + 1.25f, midRow, 0.52f, 0.52f, true, false, false, plan.SectionIndex, "electric_arc_static", hash & 3);
                }
                else if ((hazardRoll % 7) == 2)
                {
                    AddTile(tiles, plan.BeatStartX + 1.26f, midRow, 0.64f, 0.64f, true, false, false, plan.SectionIndex, "crusher_pillar_static", hash & 3);
                }
                else if ((hazardRoll % 8) == 3)
                {
                    AddTile(tiles, plan.BeatStartX + 1.28f, midRow, 0.68f, 0.70f, true, false, false, plan.SectionIndex, "piston_ram_static", hash & 3);
                }
            }
        }

        private static int ResolveGapRows(SectionPlan section, MovementProfile profile, GateKind gate, int attempt)
        {
            int baseGap = section.sectionType switch
            {
                SectionKind.Rest => 7,
                SectionKind.Transition => 8,
                SectionKind.Drop => 4,
                _ => 5
            };

            string profileName = profile != null ? profile.name ?? string.Empty : string.Empty;
            if (profileName.IndexOf("TIGHT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                baseGap -= 1;
            }
            else if (profileName.IndexOf("FLOAT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                baseGap += 1;
            }

            if (gate == GateKind.Choke)
            {
                baseGap -= 1;
            }
            else if (gate == GateKind.None)
            {
                baseGap += 1;
            }

            if (attempt >= 6)
            {
                baseGap += 1;
            }

            return Mathf.Clamp(baseGap, 3, 8);
        }

        private static GateKind ResolveGate(SectionKind sectionKind, bool expectedTap, int beatsSinceGate, int seed, int beatIndex)
        {
            if (beatIndex < WarmupBeats)
            {
                return GateKind.None;
            }

            if (sectionKind == SectionKind.Rest || sectionKind == SectionKind.Transition)
            {
                return GateKind.None;
            }

            bool forceGate = beatsSinceGate >= 2;
            int hash = DeterministicHash(seed, beatIndex, beatsSinceGate + (int)sectionKind);
            float roll = (Mathf.Abs(hash) % 1000) / 999f;
            if (!forceGate && roll > 0.68f)
            {
                return GateKind.None;
            }

            if (expectedTap)
            {
                return (hash & 1) == 0 ? GateKind.Pit : GateKind.Choke;
            }

            if (sectionKind == SectionKind.Drop)
            {
                return (hash & 1) == 0 ? GateKind.LowCeiling : GateKind.Choke;
            }

            return GateKind.LowCeiling;
        }

        private static void ResolveGapRows(int centerRow, int gapRows, out int floorRow, out int ceilingRow)
        {
            int half = Mathf.Max(1, gapRows / 2);
            floorRow = centerRow - half;
            ceilingRow = floorRow + gapRows + 1;

            if (floorRow < BottomRow)
            {
                int push = BottomRow - floorRow;
                floorRow += push;
                ceilingRow += push;
            }

            if (ceilingRow > TopRow)
            {
                int push = ceilingRow - TopRow;
                floorRow -= push;
                ceilingRow -= push;
            }

            floorRow = Mathf.Clamp(floorRow, BottomRow, TopRow - 2);
            ceilingRow = Mathf.Clamp(ceilingRow, floorRow + 2, TopRow);
        }

        private static float ResolveNextCenterRow(SectionPlan section, float currentCenter, bool expectedTap, GateKind gate, int seed, int beatIndex)
        {
            if (section.sectionType == SectionKind.Transition)
            {
                return Mathf.Lerp(currentCenter, CenterRow, 0.55f);
            }

            int hash = DeterministicHash(seed, section.sectionIndex, beatIndex);
            float jitter = (((hash & 255) / 255f) - 0.5f) * 0.52f;
            float trend = Mathf.Sin((beatIndex * 0.29f) + section.sectionIndex) * (0.12f + (section.difficultyRamp * 0.12f));
            float gateBias = gate switch
            {
                GateKind.Pit => 0.30f,
                GateKind.LowCeiling => -0.28f,
                GateKind.Choke => 0.04f,
                _ => 0f
            };
            float tapBias = expectedTap ? 0.20f : -0.10f;
            float target = currentCenter + trend + jitter + gateBias + tapBias;
            return Mathf.Clamp(Mathf.Lerp(currentCenter, target, 0.62f), 2f, 8f);
        }

        private static bool ResolveExpectedTap(
            SectionPlan section,
            MovementProfile profile,
            int seed,
            int beatIndex,
            int localBeat,
            int lastTapBeat,
            int beatsSinceDecision)
        {
            if (section.sectionType == SectionKind.Rest || section.sectionType == SectionKind.Transition)
            {
                return false;
            }

            if (beatIndex < WarmupBeats)
            {
                return false;
            }

            int minGapBeats = section.sectionType == SectionKind.Drop ? 1 : 2;
            if (beatIndex - lastTapBeat < minGapBeats)
            {
                return false;
            }

            float beatSec = profile != null ? Mathf.Max(0.18f, profile.BeatSec) : 0.5f;
            float maxNoTapSec = section.sectionType == SectionKind.Drop ? 0.95f : 1.25f;
            int maxGapBeats = Mathf.Clamp(Mathf.RoundToInt(maxNoTapSec / beatSec), 2, 4);
            if (beatIndex - lastTapBeat >= maxGapBeats)
            {
                return true;
            }

            int maxDecisionGapBeats = Mathf.Clamp(Mathf.RoundToInt(1.25f / beatSec), 2, 4);
            if (beatsSinceDecision >= maxDecisionGapBeats)
            {
                return true;
            }

            int beatInBar = localBeat & 3;
            bool baseTap = section.sectionType == SectionKind.Drop
                ? beatInBar == 1 || beatInBar == 2
                : beatInBar == 1 || beatInBar == 3;

            int hash = DeterministicHash(seed, section.sectionIndex, beatIndex);
            float roll = (Mathf.Abs(hash) % 1000) / 999f;
            float density = Mathf.Clamp01(section.densityTarget);

            if (!baseTap)
            {
                return false;
            }

            if (density < 0.30f && roll > 0.60f)
            {
                return false;
            }

            return true;
        }

        private static float RowCenterToY(int row)
        {
            return (row - CenterRow) * CellSize;
        }

        private static float RowToSafeFloorY(int floorRow)
        {
            return RowCenterToY(floorRow) + (CellSize * 0.5f) + 0.16f;
        }

        private static float RowToSafeCeilingY(int ceilingRow)
        {
            return RowCenterToY(ceilingRow) - (CellSize * 0.5f) - 0.18f;
        }

        private static void AddTile(
            List<MazeTileSpec> tiles,
            float x,
            int row,
            float widthCells,
            float heightCells,
            bool isDanger,
            bool isSolid,
            bool isDecor,
            int sectionIndex,
            string kind,
            int variant)
        {
            row = Mathf.Clamp(row, BottomRow, TopRow);
            tiles.Add(new MazeTileSpec
            {
                designX = x,
                centerY = RowCenterToY(row),
                width = Mathf.Max(0.06f, widthCells * CellSize),
                height = Mathf.Max(0.06f, heightCells * CellSize),
                isDanger = isDanger,
                isSolid = isSolid,
                isDecor = isDecor,
                sectionIndex = sectionIndex,
                visualKind = kind,
                variant = variant
            });
        }

        private static int DeterministicHash(int a, int b, int c)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + a;
                hash = (hash * 31) + b;
                hash = (hash * 31) + c;
                return hash;
            }
        }

        private static class ReachabilitySimulator
        {
            public static bool HasValidRoute(IReadOnlyList<BeatPlan> plans)
            {
                if (plans == null || plans.Count == 0)
                {
                    return false;
                }

                SimState state = SimState.AtFloor(plans[0].SafeFloorY);
                int noInputRun = 0;
                for (int i = 0; i < plans.Count; i++)
                {
                    BeatPlan plan = plans[i];
                    bool tap = plan.ExpectedTap;

                    if (!tap && !plan.IsWarmup)
                    {
                        noInputRun++;
                        if (noInputRun > 2)
                        {
                            return false;
                        }
                    }
                    else if (tap)
                    {
                        noInputRun = 0;
                    }

                    if (!TrySimulateBeat(state, plan, tap, out SimState next, out _))
                    {
                        return false;
                    }

                    state = next;
                }

                return true;
            }
        }
    }
}
