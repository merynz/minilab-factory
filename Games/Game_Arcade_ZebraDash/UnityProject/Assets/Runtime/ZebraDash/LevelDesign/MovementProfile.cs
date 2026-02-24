using System;
using UnityEngine;

namespace ZebraDash.LevelDesign
{
    [Serializable]
    public sealed class MovementProfile
    {
        public string name = "P1_ACTIVE";
        public float bpm = 120f;
        public float tileSize = 1.0f;
        public int colsPerBeat = 2;
        // Backward-compatible field. If colsPerBeat <= 0, this is used.
        public float dxTilesPerBeat = 2f;
        public float jumpCycleBeats = 2f;
        public float apexHeightTiles = 3.0f;
        public float jumpAngleBias = 0f;
        public float jumpBufferSec = 0.08f;
        public float coyoteSec = 0.08f;

        public float BeatSec => 60f / Mathf.Max(1f, bpm);
        public float JumpCycleSec => Mathf.Max(BeatSec * 0.5f, jumpCycleBeats * BeatSec);
        public float ApexHeightUnits => Mathf.Max(tileSize * 1.5f, apexHeightTiles * tileSize);
        public float ColumnsPerBeat => colsPerBeat > 0 ? colsPerBeat : Mathf.Max(1f, dxTilesPerBeat);
        public float ScrollUnitsPerSec => Mathf.Max(2f, (ColumnsPerBeat * tileSize) / Mathf.Max(0.05f, BeatSec));
        public float GravityUnitsPerSec2 => (8f * ApexHeightUnits) / Mathf.Max(0.01f, JumpCycleSec * JumpCycleSec);
        public float JumpVelocityUnitsPerSec => (4f * ApexHeightUnits) / Mathf.Max(0.01f, JumpCycleSec);
    }
}
