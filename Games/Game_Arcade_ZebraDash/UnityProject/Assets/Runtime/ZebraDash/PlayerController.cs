using System;
using System.Collections.Generic;
using MiniLab.Core.Rhythm;
using UnityEngine;

namespace ZebraDash
{
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private float laneStep = 1.5f;
        [SerializeField] private int minLane = -1;
        [SerializeField] private int maxLane = 1;
        [SerializeField] private float lerpSpeed = 12f;
        [SerializeField] private float holdSlideDelaySec = 0.12f;

        private int lane;
        private bool pointerHeld;
        private bool holdSlideApplied;
        private float pointerDownSongTime;
        private bool inputEnabled;

        private BeatClock beatClock;
        private BeatEvent[] events = Array.Empty<BeatEvent>();
        private readonly HashSet<int> consumedEventIndices = new HashSet<int>();
        private InputJudge inputJudge = new InputJudge(new JudgeWindows());
        private Action<JudgeOutcome> onJudge;
        private Action<string> onMissed;

        private int activeHoldIndex = -1;

        public void Configure(BeatClock clock, BeatMap beatMap, Action<JudgeOutcome> judgeCallback, Action<string> missCallback)
        {
            beatClock = clock;
            events = BeatMapEventUtils.GetCanonicalEvents(beatMap);
            onJudge = judgeCallback;
            onMissed = missCallback;
            consumedEventIndices.Clear();
            activeHoldIndex = -1;
            pointerHeld = false;
            holdSlideApplied = false;
            inputJudge.SetDeviceOffset(0f);
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
            if (!enabled)
            {
                pointerHeld = false;
                holdSlideApplied = false;
                activeHoldIndex = -1;
            }
        }

        private void Update()
        {
            if (inputEnabled && beatClock != null && beatClock.IsRunning)
            {
                float songTime = beatClock.SongTimeSec;
                CheckMissedEvents(songTime);

                if (Input.GetMouseButtonDown(0))
                {
                    pointerHeld = true;
                    holdSlideApplied = false;
                    pointerDownSongTime = songTime;
                    Jump();
                    EvaluateInput(songTime);
                }

                if (pointerHeld && Input.GetMouseButton(0))
                {
                    if (!holdSlideApplied && (songTime - pointerDownSongTime) >= holdSlideDelaySec)
                    {
                        Slide();
                        holdSlideApplied = true;
                    }

                    TickHold(songTime);
                }

                if (Input.GetMouseButtonUp(0))
                {
                    pointerHeld = false;
                    ValidateHoldRelease(songTime);
                }
            }

            Vector3 target = transform.position;
            target.y = lane * laneStep;
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * lerpSpeed);
        }

        private void EvaluateInput(float songTime)
        {
            JudgeOutcome outcome = inputJudge.EvaluateNearest(
                events,
                consumedEventIndices,
                songTime,
                e => e != null && (e.IsKind("Tap") || e.IsKind("Hold")));

            if (outcome.EventIndex < 0 || outcome.Result == JudgeResult.Miss)
            {
                onJudge?.Invoke(new JudgeOutcome(JudgeResult.Miss, outcome.Event, outcome.EventIndex, outcome.DeltaMs));
                onMissed?.Invoke("Miss");
                return;
            }

            consumedEventIndices.Add(outcome.EventIndex);
            onJudge?.Invoke(outcome);

            if (outcome.Event != null && outcome.Event.IsKind("Hold"))
            {
                activeHoldIndex = outcome.EventIndex;
            }
        }

        private void TickHold(float songTime)
        {
            if (activeHoldIndex < 0 || activeHoldIndex >= events.Length)
            {
                return;
            }

            BeatEvent holdEvent = events[activeHoldIndex];
            if (holdEvent == null)
            {
                activeHoldIndex = -1;
                return;
            }

            float holdEnd = holdEvent.timeSec + Mathf.Max(0.1f, holdEvent.durationSec);
            if (songTime >= holdEnd)
            {
                // Hold maintained through its duration.
                activeHoldIndex = -1;
            }
        }

        private void ValidateHoldRelease(float songTime)
        {
            if (activeHoldIndex < 0 || activeHoldIndex >= events.Length)
            {
                return;
            }

            int holdIndex = activeHoldIndex;
            BeatEvent holdEvent = events[activeHoldIndex];
            activeHoldIndex = -1;
            if (holdEvent == null)
            {
                return;
            }

            float holdEnd = holdEvent.timeSec + Mathf.Max(0.1f, holdEvent.durationSec);
            if (songTime < holdEnd)
            {
                onJudge?.Invoke(new JudgeOutcome(JudgeResult.Miss, holdEvent, holdIndex, 0f));
                onMissed?.Invoke("Hold released early");
            }
        }

        private void CheckMissedEvents(float songTime)
        {
            for (int i = 0; i < events.Length; i++)
            {
                if (consumedEventIndices.Contains(i))
                {
                    continue;
                }

                BeatEvent evt = events[i];
                if (evt == null || (!evt.IsKind("Tap") && !evt.IsKind("Hold")))
                {
                    continue;
                }

                if (inputJudge.IsMissedByTime(evt, songTime))
                {
                    consumedEventIndices.Add(i);
                    onJudge?.Invoke(new JudgeOutcome(JudgeResult.Miss, evt, i, inputJudge.MissWindowSec * 1000f));
                    onMissed?.Invoke("Missed beat event");
                    return;
                }

                // Event list is sorted, so if current one is not missed, later ones won't be either.
                break;
            }
        }

        private void Jump()
        {
            lane = Mathf.Clamp(lane + 1, minLane, maxLane);
        }

        private void Slide()
        {
            lane = Mathf.Clamp(lane - 1, minLane, maxLane);
        }
    }
}
