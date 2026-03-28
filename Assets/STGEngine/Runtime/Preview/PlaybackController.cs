using System;
using UnityEngine;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Playback time controller for preview. Manages play/pause/seek/speed/loop.
    /// Internally delegates to SimulationLoop for fixed-timestep advancement.
    /// Concrete previewers subscribe to OnTimeChanged to update visuals.
    /// </summary>
    public class PlaybackController
    {
        public float CurrentTime { get; private set; }
        public float Duration { get; set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPlaying { get; private set; }
        public bool Loop { get; set; } = true;

        /// <summary>Render interpolation alpha from the underlying SimulationLoop.</summary>
        public float Alpha => _simLoop.Alpha;

        /// <summary>
        /// Fixed logic timestep. Proxy for SimulationLoop.FixedDt.
        /// Set this to 1f / tickRate to change simulation precision.
        /// </summary>
        public float FixedDt
        {
            get => _simLoop.FixedDt;
            set => _simLoop.FixedDt = value;
        }

        /// <summary>Fired on every time change (play tick, seek, or step).</summary>
        public event Action<float> OnTimeChanged;

        /// <summary>Fired when play/pause state changes.</summary>
        public event Action<bool> OnPlayStateChanged;

        private readonly SimulationLoop _simLoop = new();

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
        /// Instant seek to a specific time. Formula bullets resolve immediately.
        /// Resets the SimulationLoop accumulator to avoid stale ticks after jump.
        /// </summary>
        public void Seek(float time)
        {
            CurrentTime = Mathf.Max(time, 0f);
            _simLoop.Reset();
            OnTimeChanged?.Invoke(CurrentTime);
        }

        /// <summary>Advance by one logic frame while paused.</summary>
        public void StepFrame()
        {
            Seek(CurrentTime + _simLoop.FixedDt);
        }

        /// <summary>
        /// Call once per Unity Update. Advances time via fixed-timestep loop
        /// when playing; does nothing when paused.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsPlaying) return;

            _simLoop.Update(deltaTime * PlaybackSpeed, dt =>
            {
                CurrentTime += dt;

                if (Loop && Duration > 0f && CurrentTime >= Duration)
                {
                    CurrentTime %= Duration;
                }

                OnTimeChanged?.Invoke(CurrentTime);
            });
        }

        /// <summary>Reset playback to t=0.</summary>
        public void Reset()
        {
            CurrentTime = 0f;
            _simLoop.Reset();
            IsPlaying = false;
            OnTimeChanged?.Invoke(0f);
            OnPlayStateChanged?.Invoke(false);
        }
    }
}
