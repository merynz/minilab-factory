using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    public sealed class BeatClock : MonoBehaviour
    {
        private const string OffsetPrefsPrefixSec = "minilab.rhythm.offset.";
        private const string OffsetPrefsPrefixMs = "TrackOffsetMs_";

        [SerializeField] private double scheduleLeadInSec = 0.15;

        public double DspStartTime { get; private set; }
        public float Bpm { get; private set; }
        public float UserOffsetSec { get; private set; }
        public string OffsetTrackId { get; private set; } = "";
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public float PausedSongTimeSec { get; private set; }

        private AudioSource scheduledSource;
        private bool hasScheduledSource;

        public void StartClock(AudioSource source, float bpm, float userOffsetSec, string trackId = "")
        {
            scheduledSource = source;
            hasScheduledSource = scheduledSource != null && scheduledSource.clip != null;
            Bpm = Mathf.Max(1f, bpm);
            UserOffsetSec = userOffsetSec;
            OffsetTrackId = trackId ?? "";
            DspStartTime = AudioSettings.dspTime + scheduleLeadInSec;
            IsRunning = true;
            IsPaused = false;
            PausedSongTimeSec = 0f;

            if (hasScheduledSource)
            {
                scheduledSource.Stop();
                scheduledSource.PlayScheduled(DspStartTime);
            }
        }

        public void RestartClock()
        {
            if (scheduledSource == null)
            {
                return;
            }

            StartClock(scheduledSource, Bpm, UserOffsetSec, OffsetTrackId);
        }

        public void PauseClock()
        {
            if (!IsRunning || IsPaused)
            {
                return;
            }

            PausedSongTimeSec = SongTimeSec;
            IsPaused = true;
            if (hasScheduledSource && scheduledSource.isPlaying)
            {
                scheduledSource.Pause();
            }
        }

        public void ResumeClock()
        {
            if (!IsRunning || !IsPaused)
            {
                return;
            }

            // Keep DSP authority by anchoring start time to current dspNow and paused song position.
            DspStartTime = AudioSettings.dspTime - (PausedSongTimeSec + UserOffsetSec);
            IsPaused = false;
            if (hasScheduledSource)
            {
                scheduledSource.UnPause();
            }
        }

        public void StopClock()
        {
            IsRunning = false;
            IsPaused = false;
            PausedSongTimeSec = 0f;
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

                if (IsPaused)
                {
                    return PausedSongTimeSec;
                }

                return (float)(AudioSettings.dspTime - DspStartTime) - UserOffsetSec;
            }
        }

        public float NowSeconds => SongTimeSec;

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

            string keyMs = OffsetPrefsPrefixMs + trackId.Trim();
            if (PlayerPrefs.HasKey(keyMs))
            {
                return PlayerPrefs.GetFloat(keyMs, fallbackOffsetSec * 1000f) / 1000f;
            }

            return PlayerPrefs.GetFloat(OffsetPrefsPrefixSec + trackId.Trim(), fallbackOffsetSec);
        }

        public static void SaveTrackOffsetSec(string trackId, float offsetSec)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                return;
            }

            string trimmed = trackId.Trim();
            PlayerPrefs.SetFloat(OffsetPrefsPrefixSec + trimmed, offsetSec);
            PlayerPrefs.SetFloat(OffsetPrefsPrefixMs + trimmed, offsetSec * 1000f);
            PlayerPrefs.Save();
        }

        public static float LoadTrackOffsetMs(string trackId, float fallbackOffsetMs = 0f)
        {
            return LoadTrackOffsetSec(trackId, fallbackOffsetMs / 1000f) * 1000f;
        }

        public static void SaveTrackOffsetMs(string trackId, float offsetMs)
        {
            SaveTrackOffsetSec(trackId, offsetMs / 1000f);
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
