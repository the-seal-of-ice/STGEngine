using System;
using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Tracks an active event instance during timeline playback.
    /// </summary>
    public class ActiveEvent
    {
        public SpawnPatternEvent Event;
        public PatternPreviewer Previewer;
    }

    /// <summary>
    /// Manages segment-level playback for the timeline editor.
    /// Controls global time, activates/deactivates PatternPreviewers via pool,
    /// and synchronizes each previewer's local time.
    /// </summary>
    public class TimelinePlaybackController
    {
        public float CurrentTime { get; private set; }
        public float Duration { get; set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPlaying { get; private set; }
        public bool Loop { get; set; } = true;

        /// <summary>Render interpolation alpha from the underlying SimulationLoop.</summary>
        public float Alpha => _simLoop.Alpha;

        /// <summary>Fired on every time change.</summary>
        public event Action<float> OnTimeChanged;

        /// <summary>Fired when play/pause state changes.</summary>
        public event Action<bool> OnPlayStateChanged;

        /// <summary>Currently active events.</summary>
        public IReadOnlyList<ActiveEvent> ActiveEvents => _activeEvents;

        private readonly SimulationLoop _simLoop = new();
        private readonly List<ActiveEvent> _activeEvents = new();
        private PreviewerPool _pool;
        private PatternLibrary _library;
        private TimelineSegment _segment;

        /// <summary>
        /// Initialize with pool and library references.
        /// </summary>
        public void Initialize(PreviewerPool pool, PatternLibrary library)
        {
            _pool = pool;
            _library = library;
        }

        /// <summary>
        /// Load a segment for playback. Resolves pattern references and resets time.
        /// </summary>
        public void LoadSegment(TimelineSegment segment)
        {
            Stop();
            _segment = segment;
            Duration = segment?.Duration ?? 0f;

            // Resolve pattern references
            if (_segment != null)
            {
                foreach (var evt in _segment.Events)
                {
                    if (evt is SpawnPatternEvent spawnEvt && spawnEvt.ResolvedPattern == null)
                    {
                        spawnEvt.ResolvedPattern = _library?.Resolve(spawnEvt.PatternId);
                        if (spawnEvt.ResolvedPattern == null)
                            Debug.LogWarning($"[TimelinePlayback] Pattern '{spawnEvt.PatternId}' not found.");
                    }
                }
            }

            Seek(0f);
        }

        public void Play()
        {
            IsPlaying = true;
            OnPlayStateChanged?.Invoke(true);
        }

        public void Pause()
        {
            IsPlaying = false;
            OnPlayStateChanged?.Invoke(false);
        }

        public void TogglePlay()
        {
            if (IsPlaying) Pause(); else Play();
        }

        /// <summary>
        /// Seek to a specific time. Rebuilds active event set.
        /// </summary>
        public void Seek(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, Duration);
            _simLoop.Reset();
            RebuildActiveEvents();
            OnTimeChanged?.Invoke(CurrentTime);
        }

        /// <summary>Advance by one logic frame while paused.</summary>
        public void StepFrame()
        {
            float next = CurrentTime + _simLoop.FixedDt;
            if (Loop && Duration > 0f && next >= Duration)
                next %= Duration;
            Seek(Mathf.Min(next, Duration));
        }

        /// <summary>
        /// Call once per Unity Update.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsPlaying || _segment == null) return;

            _simLoop.Update(deltaTime * PlaybackSpeed, dt =>
            {
                CurrentTime += dt;

                if (Loop && Duration > 0f && CurrentTime >= Duration)
                {
                    CurrentTime %= Duration;
                    RebuildActiveEvents();
                }
                else
                {
                    UpdateActiveEvents();
                }

                if (CurrentTime >= Duration && !Loop)
                {
                    CurrentTime = Duration;
                    Pause();
                }

                OnTimeChanged?.Invoke(CurrentTime);
            });
        }

        /// <summary>Stop playback and release all previewers.</summary>
        public void Stop()
        {
            IsPlaying = false;
            CurrentTime = 0f;
            _simLoop.Reset();
            ReleaseAllActive();
            OnPlayStateChanged?.Invoke(false);
        }

        /// <summary>Reset to t=0.</summary>
        public void Reset()
        {
            Stop();
            OnTimeChanged?.Invoke(0f);
        }

        /// <summary>
        /// Full rebuild: release all, then activate events that overlap CurrentTime.
        /// Used after Seek or loop wrap.
        /// </summary>
        private void RebuildActiveEvents()
        {
            ReleaseAllActive();

            if (_segment == null) return;

            foreach (var evt in _segment.Events)
            {
                if (evt is SpawnPatternEvent spawnEvt && IsEventActive(spawnEvt, CurrentTime))
                {
                    ActivateEvent(spawnEvt);
                }
            }
        }

        /// <summary>
        /// Incremental update: activate newly overlapping events, deactivate expired ones.
        /// </summary>
        private void UpdateActiveEvents()
        {
            if (_segment == null) return;

            // Deactivate expired events
            for (int i = _activeEvents.Count - 1; i >= 0; i--)
            {
                var active = _activeEvents[i];
                if (!IsEventActive(active.Event, CurrentTime))
                {
                    _pool.Release(active.Previewer);
                    _activeEvents.RemoveAt(i);
                }
            }

            // Activate new events
            foreach (var evt in _segment.Events)
            {
                if (evt is SpawnPatternEvent spawnEvt && IsEventActive(spawnEvt, CurrentTime))
                {
                    if (!IsAlreadyActive(spawnEvt))
                        ActivateEvent(spawnEvt);
                }
            }
        }

        private void ActivateEvent(SpawnPatternEvent spawnEvt)
        {
            if (spawnEvt.ResolvedPattern == null) return;

            var previewer = _pool.Acquire();
            previewer.Pattern = spawnEvt.ResolvedPattern;
            previewer.transform.position = spawnEvt.SpawnPosition;

            // Calculate local time within this event
            float localTime = CurrentTime - spawnEvt.StartTime;
            previewer.Playback.Duration = spawnEvt.Duration;
            previewer.Playback.Loop = false;
            previewer.Playback.Seek(Mathf.Max(localTime, 0f));
            previewer.ForceRefresh();
            previewer.Playback.Play();

            _activeEvents.Add(new ActiveEvent
            {
                Event = spawnEvt,
                Previewer = previewer
            });
        }

        private bool IsEventActive(SpawnPatternEvent evt, float time)
        {
            return time >= evt.StartTime && time < evt.EndTime;
        }

        private bool IsAlreadyActive(SpawnPatternEvent evt)
        {
            foreach (var active in _activeEvents)
            {
                if (active.Event == evt) return true;
            }
            return false;
        }

        private void ReleaseAllActive()
        {
            foreach (var active in _activeEvents)
                _pool.Release(active.Previewer);
            _activeEvents.Clear();
        }
    }
}
