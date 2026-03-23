using System;

namespace STGEngine.Runtime
{
    /// <summary>
    /// Fixed-timestep game loop. Logic ticks at constant dt, decoupled from render framerate.
    /// Enables deterministic replay and future netplay (lockstep with identical dt + input).
    /// </summary>
    public class SimulationLoop
    {
        /// <summary>Fixed logic timestep (default 60fps = 1/60s).</summary>
        public float FixedDt { get; set; } = 1f / 60f;

        /// <summary>Current logic tick count (monotonically increasing).</summary>
        public int TickCount { get; private set; }

        /// <summary>Current simulation time = TickCount * FixedDt.</summary>
        public float SimTime => TickCount * FixedDt;

        /// <summary>Render interpolation alpha (0..1) for smooth visuals between logic ticks.</summary>
        public float Alpha { get; private set; }

        private float _accumulator;

        /// <summary>
        /// Call once per Unity Update. Advances logic by N fixed-dt steps,
        /// then computes render interpolation alpha.
        /// </summary>
        /// <param name="deltaTime">Unscaled frame delta (e.g. Time.deltaTime * playbackSpeed).</param>
        /// <param name="stepAction">Callback invoked per logic tick with fixed dt.</param>
        public void Update(float deltaTime, Action<float> stepAction)
        {
            _accumulator += deltaTime;

            while (_accumulator >= FixedDt)
            {
                stepAction(FixedDt);
                _accumulator -= FixedDt;
                TickCount++;
            }

            Alpha = FixedDt > 0f ? _accumulator / FixedDt : 0f;
        }

        /// <summary>Reset to initial state.</summary>
        public void Reset()
        {
            TickCount = 0;
            _accumulator = 0f;
            Alpha = 0f;
        }
    }
}
