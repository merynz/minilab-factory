using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    public sealed class BeatClock : MonoBehaviour
    {
        private const string OffsetPrefsPrefix = "minilab.rhythm.offset.";

        [SerializeField] private double scheduleLeadInSec = 0.15;

        public double DspStartTime { get; private set; }
        public float Bpm { get; private set; }
        public float UserOffsetSec { get; private set; }
        public string OffsetTrackId { get; private set; } = "";
        public bool IsRunning { get; private set; }

        private AudioSource scheduledSource;

        public void StartClock(AudioSource source, float bpm, float userOffsetSec, string trackId = "")
        {
            scheduledSource = source;
            Bpm = Mathf.Max(1f, bpm);
            UserOffsetSec = userOffsetSec;
            OffsetTrackId = trackId ?? "";
            DspStartTime = AudioSettings.dspTime + scheduleLeadInSec;
            IsRunning = true;

            if (scheduledSource != null && scheduledSource.clip != null)
            {
                scheduledSource.Stop();
                scheduledSource.PlayScheduled(DspStartTime);
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

                return (float)(AudioSettings.dspTime - DspStartTime) - UserOffsetSec;
            }
        }

        public double DspNow => AudioSettings.dspTime;

        public float BeatFloat => SongTimeSec * Bpm / 60f;

        public int BeatIndex => Mathf.FloorToInt(BeatFloat);

        public int BarIndex => Mathf.FloorToInt(BeatFloat / 4f);

        public float SecondsPerBeat => 60f / Mathf.Max(1f, Bpm);

        public float BeatToTimeSec(int beatIndex)
        {
            return beatIndex * SecondsPerBeat;
        }

        public float NextBeatTimeSec
        {
            get
            {
                int next = Mathf.FloorToInt(BeatFloat) + 1;
                return BeatToTimeSec(next);
            }
        }

        public float NextBeatDeltaSec => NextBeatTimeSec - SongTimeSec;

        public static float LoadTrackOffsetSec(string trackId, float fallbackOffsetSec = 0f)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                return fallbackOffsetSec;
            }

            return PlayerPrefs.GetFloat(OffsetPrefsPrefix + trackId.Trim(), fallbackOffsetSec);
        }

        public static void SaveTrackOffsetSec(string trackId, float offsetSec)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                return;
            }

            PlayerPrefs.SetFloat(OffsetPrefsPrefix + trackId.Trim(), offsetSec);
            PlayerPrefs.Save();
        }

        public void UpdateOffset(float offsetSec, bool persist = false)
        {
            UserOffsetSec = offsetSec;
            if (persist && !string.IsNullOrWhiteSpace(OffsetTrackId))
            {
                SaveTrackOffsetSec(OffsetTrackId, offsetSec);
            }
        }
    }
}
