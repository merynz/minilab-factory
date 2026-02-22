using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    public sealed class BeatClock : MonoBehaviour
    {
        [SerializeField] private double scheduleLeadInSec = 0.20;

        public double DspStartTime { get; private set; }
        public float Bpm { get; private set; }
        public float OffsetSec { get; private set; }
        public bool IsRunning { get; private set; }

        private AudioSource scheduledSource;

        public void StartClock(AudioSource source, float bpm, float offsetSec)
        {
            scheduledSource = source;
            Bpm = Mathf.Max(1f, bpm);
            OffsetSec = offsetSec;
            DspStartTime = AudioSettings.dspTime + scheduleLeadInSec;
            IsRunning = true;

            if (scheduledSource != null && scheduledSource.clip != null)
            {
                scheduledSource.Stop();
                scheduledSource.PlayScheduled(DspStartTime + OffsetSec);
            }
        }

        public void StopClock()
        {
            IsRunning = false;
            if (scheduledSource != null)
            {
                scheduledSource.Stop();
            }
        }

        public float SongTimeSec
        {
            get
            {
                if (!IsRunning)
                {
                    return 0f;
                }

                return (float)(AudioSettings.dspTime - DspStartTime - OffsetSec);
            }
        }

        public float BeatFloat => SongTimeSec * Bpm / 60f;

        public int BeatIndex => Mathf.FloorToInt(BeatFloat);

        public int BarIndex => Mathf.FloorToInt(BeatFloat / 4f);

        public float SecondsPerBeat => 60f / Mathf.Max(1f, Bpm);

        public float BeatToTimeSec(int beatIndex)
        {
            return beatIndex * SecondsPerBeat;
        }
    }
}
