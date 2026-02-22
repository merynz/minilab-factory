# ZebraDash Milestone-2 Presentation + Flow Stabilization

## Scope
- Presentation/readability stabilization for Level01/Level02.
- Parallax seam/jitter reduction with deterministic time-based motion.
- Obstacle lifecycle hardening (spawn -> active -> post-hit -> despawn).
- HUD tuning visibility for planner + timing + pooling diagnostics.

## Changed Files

1. `Games/Game_Arcade_ZebraDash/UnityProject/Assets/Runtime/ZebraDash/ParallaxSystem.cs`
- Reworked layer model with overlap-based tiling to hide seam boundaries.
- Added deterministic double-precision scroll accumulator (`ScrollX` as `double`).
- Added constrained beat/bar/accent envelopes with small modulation ranges.
- Normalized layer speeds to fixed ratios for stable depth perception.
- Added subpixel snapping on parallax transform updates to reduce shimmer/jitter.

2. `Games/Game_Arcade_ZebraDash/UnityProject/Assets/Runtime/ZebraDash/ObstacleKinematics.cs`
- Added explicit lifecycle state machine: `Spawned`, `Active`, `PostHit`, `Despawned`.
- Introduced deterministic post-hit timing (`postHitSec`) and `despawnAtSec`.
- Added viewport culling guard after hit to prevent lingering off-screen obstacles.
- Added alpha lifecycle handling (telegraph/commit/post-hit fade) on obstacle material.
- Improved obstacle base scale for readability (non-hold/fakeout enlarged).

3. `Games/Game_Arcade_ZebraDash/UnityProject/Assets/Runtime/ZebraDash/ObstacleSpawner.cs`
- Added min-visible clamp for travel time:
  - `minVisibleSec = clamp(0.90 * beatSec, 0.45, 0.85)`.
- Passed `beatSec` into kinematics configure for lifecycle calculations.
- Exposed pool diagnostics: `ActiveCount`, `PoolCount`, `CreatedCount`.
- Increased hazard render sorting order and lane-tinted hazard color blending.

4. `Games/Game_Arcade_ZebraDash/UnityProject/Assets/Runtime/ZebraDash/LevelRunner.cs`
- Added presentation debug outputs:
  - `TwoBarPlanDebug`, `NextHazardTimingDebug`.
  - obstacle pool counters surfaced to HUD.
- Added hazard timing diagnostics (travel/min-visible/telegraph/approach summary).
- Extended grid debug line with planner fallback step.

5. `Games/Game_Arcade_ZebraDash/UnityProject/Assets/Runtime/ZebraDash/ZebraDashFlowController.cs`
- Moved parallax tick from `Update` to `LateUpdate` (camera/layer ordering stability).
- Strengthened lane rails + hitline readability (color/alpha/sorting updates).
- Added beat-driven lane/hitline pulse feedback.
- Tightened safe-area fallback thresholds to avoid tiny/collapsed UI regions.
- HUD expanded with two-bar plan, hazard timing, and pool counters.

6. `Games/Game_Arcade_ZebraDash/UnityProject/Assets/Runtime/ZebraDash/GameplayPatternGenerator.cs`
- Added fallback phase marker propagation to grid debug (`fallbackStep`).
- Kept deterministic planner phase reporting for tuning visibility.

## Acceptance Mapping

- Seam/jitter reduction:
  - Parallax overlap + double accumulator + subpixel snap + LateUpdate tick.
- Readability:
  - Stronger lane rails/hitline pulse + larger hazard silhouette + lane tinting.
- Lifecycle correctness:
  - Explicit obstacle state machine + deterministic post-hit despawn + viewport cull.
- Debug/tuning:
  - Two-bar planner view + hazard timing summary + pool/active counters.

## Notes
- This change set is presentation/lifecycle focused and keeps DSP-time hit invariants intact.
- Final visual validation requires on-device pass on both `level01_electro` and `level02_robo`.
