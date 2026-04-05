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

        private float _playbackSpeed = 1f;
        public float PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                _playbackSpeed = value;
                // Sync speed to all active previewers
                foreach (var active in _activeEvents)
                    active.Previewer.Playback.PlaybackSpeed = value;
            }
        }

        public bool IsPlaying { get; private set; }
        public bool Loop { get; set; } = true;

        /// <summary>Render interpolation alpha from the underlying SimulationLoop.</summary>
        public float Alpha => _simLoop.Alpha;

        /// <summary>
        /// Fixed logic timestep. Proxy for SimulationLoop.FixedDt.
        /// Set this to 1f / tickRate to change simulation precision.
        /// Also propagates to all active previewers.
        /// </summary>
        public float FixedDt
        {
            get => _simLoop.FixedDt;
            set
            {
                _simLoop.FixedDt = value;
                foreach (var active in _activeEvents)
                    active.Previewer.Playback.FixedDt = value;
            }
        }

        /// <summary>Fired on every time change.</summary>
        public event Action<float> OnTimeChanged;

        /// <summary>Fired when play/pause state changes.</summary>
        public event Action<bool> OnPlayStateChanged;

        /// <summary>Fired when playback loops back to the start.</summary>
        public event Action OnLooped;

        /// <summary>Currently active events.</summary>
        public IReadOnlyList<ActiveEvent> ActiveEvents => _activeEvents;

        /// <summary>
        /// Dynamic target provider for PlayerHomingModifier. When set, newly activated
        /// previewers automatically inherit this provider so player-homing bullets
        /// track the live player position.
        /// </summary>
        public System.Func<Vector3> HomingTargetProvider { get; set; }

        private readonly SimulationLoop _simLoop = new();
        private readonly List<ActiveEvent> _activeEvents = new();
        private PreviewerPool _pool;
        private PatternLibrary _library;
        private TimelineSegment _segment;

        /// <summary>The currently loaded segment (for external consumers like ActionEventPreviewController).</summary>
        public TimelineSegment CurrentSegment => _segment;

        // ── Blocking state ──
        private ActionEvent _blockingEvent;
        private float _blockingElapsed;
        private readonly HashSet<string> _executedBlockingIds = new();

        /// <summary>Whether the timeline is currently frozen by a blocking ActionEvent.</summary>
        public bool IsBlocked => _blockingEvent != null;

        /// <summary>The blocking ActionEvent currently freezing the timeline, or null.</summary>
        public ActionEvent BlockingEvent => _blockingEvent;

        /// <summary>
        /// Normalized progress within the current blocking event (0→1).
        /// Used by the UI to animate the playhead inside the blocking block.
        /// 0 = just entered, 1 = timeout reached. Always 0 when not blocked.
        /// For infinite-wait blocks (Duration=0), this stays at 0.
        /// </summary>
        public float BlockingProgress
        {
            get
            {
                if (_blockingEvent == null) return 0f;
                if (_blockingEvent.Duration <= 0f) return 0f; // infinite wait
                return Mathf.Clamp01(_blockingElapsed / _blockingEvent.Duration);
            }
        }

        /// <summary>
        /// External callback to resolve blocking conditions (e.g. AllEnemiesDefeated, PlayerConfirm).
        /// Return true to release the block. Called every tick while blocked.
        /// </summary>
        public Func<ActionEvent, bool> BlockingConditionResolver { get; set; }

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
            // Resume all active previewers
            foreach (var active in _activeEvents)
                active.Previewer.Playback.Play();
            OnPlayStateChanged?.Invoke(true);
        }

        public void Pause()
        {
            IsPlaying = false;
            // Pause all active previewers
            foreach (var active in _activeEvents)
                active.Previewer.Playback.Pause();
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
            _blockingEvent = null;
            _blockingElapsed = 0f;
            // Rebuild executed set: mark all blocking events before seek time as already executed
            _executedBlockingIds.Clear();
            if (_segment?.Events != null)
            {
                foreach (var evt in _segment.Events)
                {
                    if (evt is ActionEvent ae && ae.Blocking && CurrentTime > ae.StartTime + ae.BlockingDelay)
                        _executedBlockingIds.Add(ae.Id);
                }
            }
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

            // ── Blocking: freeze timeline progression ──
            if (_blockingEvent != null)
            {
                _blockingElapsed += deltaTime * PlaybackSpeed;

                bool resolved = false;
                // Check external condition resolver
                if (BlockingConditionResolver != null)
                    resolved = BlockingConditionResolver(_blockingEvent);
                // Check timeout (Duration > 0 = finite wait)
                if (!resolved && _blockingEvent.Duration > 0f && _blockingElapsed >= _blockingEvent.Duration)
                    resolved = true;

                if (resolved)
                {
                    // Block released — playhead stays at StartTime (no time consumed)
                    _blockingEvent = null;
                    _blockingElapsed = 0f;
                    // Resume all active previewers that were paused
                    foreach (var active in _activeEvents)
                        active.Previewer.Playback.Play();
                    // Fire with real CurrentTime (= blocking event's StartTime)
                    OnTimeChanged?.Invoke(CurrentTime);
                }
                else
                {
                    // Still blocked — CurrentTime stays at StartTime.
                    // BlockingProgress is updated for UI consumers to draw progress line.
                    OnTimeChanged?.Invoke(CurrentTime);
                    return;
                }
            }

            _simLoop.Update(deltaTime * PlaybackSpeed, dt =>
            {
                // If we entered blocking state during a previous tick in this frame, skip
                if (_blockingEvent != null) return;

                CurrentTime += dt;

                if (Loop && Duration > 0f && CurrentTime >= Duration)
                {
                    CurrentTime %= Duration;
                    _executedBlockingIds.Clear();
                    RebuildActiveEvents();
                    OnLooped?.Invoke();
                }
                else
                {
                    UpdateActiveEvents();
                }

                // ── Check for new blocking events ──
                if (_segment.Events != null)
                {
                    foreach (var evt in _segment.Events)
                    {
                        if (evt is ActionEvent ae && ae.Blocking
                            && CurrentTime >= ae.StartTime + ae.BlockingDelay
                            && !_executedBlockingIds.Contains(ae.Id))
                        {
                            _executedBlockingIds.Add(ae.Id);
                            _blockingEvent = ae;
                            _blockingElapsed = 0f;
                            // Pause all active previewers while blocked
                            foreach (var active in _activeEvents)
                                active.Previewer.Playback.Pause();
                            OnTimeChanged?.Invoke(CurrentTime);
                            return; // exits lambda; next tick in this frame will see _blockingEvent != null
                        }
                    }
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
            _blockingEvent = null;
            _blockingElapsed = 0f;
            _executedBlockingIds.Clear();
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
            previewer.HomingTargetProvider = HomingTargetProvider;
            previewer.Pattern = spawnEvt.ResolvedPattern;
            previewer.transform.position = spawnEvt.SpawnPosition;

            // Calculate local time within this event
            float localTime = CurrentTime - spawnEvt.StartTime;
            previewer.Playback.Duration = spawnEvt.Duration;
            previewer.Playback.Loop = false;
            previewer.Playback.PlaybackSpeed = PlaybackSpeed;
            previewer.Playback.FixedDt = _simLoop.FixedDt; // inherit tick rate
            previewer.Playback.Seek(Mathf.Max(localTime, 0f));
            previewer.ForceRefresh();

            // Only auto-play the previewer if the timeline itself is playing.
            // When paused (e.g. initial load, seek), just show the frozen frame.
            if (IsPlaying)
                previewer.Playback.Play();

            _activeEvents.Add(new ActiveEvent
            {
                Event = spawnEvt,
                Previewer = previewer
            });
        }

        private bool IsEventActive(SpawnPatternEvent evt, float time)
        {
            float endTime = Duration > 0f ? Mathf.Min(evt.EndTime, Duration) : evt.EndTime;
            return time >= evt.StartTime && time < endTime;
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

        /// <summary>
        /// Refresh the previewer for a specific event whose pattern data has changed.
        /// Re-assigns the pattern and force-refreshes to pick up modified parameters.
        /// </summary>
        public void RefreshEvent(SpawnPatternEvent evt)
        {
            // Update the segment's event so future activations use the new pattern
            if (_segment != null)
            {
                foreach (var segEvt in _segment.Events)
                {
                    if (segEvt is SpawnPatternEvent sp && sp.Id == evt.Id)
                    {
                        sp.ResolvedPattern = evt.ResolvedPattern;
                        break;
                    }
                }
            }

            // Update currently active previewer
            foreach (var active in _activeEvents)
            {
                // Match by event Id (not reference) because LoadPreview creates new event objects
                if (active.Event.Id == evt.Id)
                {
                    // Also update the active event's reference
                    active.Event.ResolvedPattern = evt.ResolvedPattern;

                    var previewer = active.Previewer;
                    previewer.Pattern = evt.ResolvedPattern;
                    previewer.transform.position = evt.SpawnPosition;
                    previewer.Playback.Duration = evt.Duration;

                    float localTime = CurrentTime - evt.StartTime;
                    previewer.Playback.Seek(Mathf.Max(localTime, 0f));
                    previewer.ForceRefresh();

                    if (IsPlaying)
                        previewer.Playback.Play();
                    break;
                }
            }
        }
    }
}
