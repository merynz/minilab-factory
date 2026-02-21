using UnityEngine;

namespace MiniLab.Core.Rhythm
{
    public static class MetronomeClipFactory
    {
        public static AudioClip Create(float bpm, int bars = 8, int sampleRate = 44100)
        {
            bpm = Mathf.Clamp(bpm, 40f, 220f);
            int beats = Mathf.Max(4, bars * 4);
            float secPerBeat = 60f / bpm;
            float durationSec = beats * secPerBeat;
            int totalSamples = Mathf.CeilToInt(durationSec * sampleRate);
            float[] data = new float[totalSamples];

            int clickSamples = Mathf.CeilToInt(sampleRate * 0.03f);
            for (int beat = 0; beat < beats; beat++)
            {
                int start = Mathf.RoundToInt(beat * secPerBeat * sampleRate);
                bool accent = (beat % 4) == 0;
                float freq = accent ? 1200f : 900f;
                float amp = accent ? 0.8f : 0.55f;

                for (int i = 0; i < clickSamples; i++)
                {
                    int idx = start + i;
                    if (idx >= totalSamples)
                    {
                        break;
                    }

                    float t = i / (float)sampleRate;
                    float env = Mathf.Exp(-t * 50f);
                    data[idx] += Mathf.Sin(2f * Mathf.PI * freq * t) * amp * env;
                }
            }

            AudioClip clip = AudioClip.Create("MiniLabMetronome", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
