using System;
using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    [Serializable]
    public sealed class BeatGrid
    {
        [SerializeField] private float bpm = 120f;
        [SerializeField] private int barBeats = 4;
        [SerializeField] private int subdiv = 4;

        public BeatGrid()
        {
        }

        public BeatGrid(float bpmValue, int barBeatsValue = 4, int subdivValue = 4)
        {
            Configure(bpmValue, barBeatsValue, subdivValue);
        }

        public float Bpm => Mathf.Max(1f, bpm);
        public int BarBeats => Mathf.Clamp(barBeats, 1, 16);
        public int Subdiv => Mathf.Clamp(subdiv, 1, 16);
        public float BeatSec => 60f / Bpm;
        public float BarSec => BeatSec * BarBeats;
        public float SubSec => BeatSec / Subdiv;

        public void Configure(float bpmValue, int barBeatsValue = 4, int subdivValue = 4)
        {
            bpm = Mathf.Max(1f, bpmValue);
            barBeats = Mathf.Clamp(barBeatsValue, 1, 16);
            subdiv = Mathf.Clamp(subdivValue, 1, 16);
        }

        public float QuantizeToBeat(float timeSec)
        {
            float beat = BeatSec;
            return Mathf.Round(timeSec / beat) * beat;
        }

        public float QuantizeToSub(float timeSec)
        {
            float sub = SubSec;
            return Mathf.Round(timeSec / sub) * sub;
        }

        public float GetPhaseBeat(float timeSec)
        {
            float beat = BeatSec;
            return Mathf.Repeat(timeSec, beat) / beat;
        }

        public float GetPhaseBar(float timeSec)
        {
            float bar = BarSec;
            return Mathf.Repeat(timeSec, bar) / bar;
        }

        public int BeatIndex(float timeSec)
        {
            return Mathf.FloorToInt(timeSec / BeatSec);
        }

        public int BarIndex(float timeSec)
        {
            return Mathf.FloorToInt(timeSec / BarSec);
        }

        public int SubIndex(float timeSec)
        {
            return Mathf.FloorToInt(timeSec / SubSec);
        }

        public float NextBeatTime(float timeSec)
        {
            int idx = BeatIndex(timeSec) + 1;
            return idx * BeatSec;
        }

        public float PrevBeatTime(float timeSec)
        {
            int idx = BeatIndex(timeSec);
            return idx * BeatSec;
        }

        public float NextSubTime(float timeSec)
        {
            int idx = SubIndex(timeSec) + 1;
            return idx * SubSec;
        }

        public float PrevSubTime(float timeSec)
        {
            int idx = SubIndex(timeSec);
            return idx * SubSec;
        }
    }
}
