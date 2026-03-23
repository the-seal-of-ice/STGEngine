using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Simulation modifier: bounces bullets off a spherical boundary.
    /// When a bullet exceeds BoundaryRadius, its velocity is reflected
    /// and it is pushed back inside. Stops bouncing after MaxBounces.
    /// </summary>
    [TypeTag("bounce")]
    public class BounceModifier : ISimulationModifier
    {
        public string TypeName => "bounce";
        public bool RequiresSimulation => true;

        /// <summary>Radius of the spherical boundary.</summary>
        public float BoundaryRadius { get; set; } = 10f;

        /// <summary>Maximum number of bounces before bullet flies free.</summary>
        public int MaxBounces { get; set; } = 3;

        // Internal state
        private int _bounceCount;
        private Vector3 _lastVelocity;

        public BounceModifier() { }

        public void Step(float dt, ref Vector3 position, ref Vector3 velocity)
        {
            _lastVelocity = velocity;
            // Position advancement is handled by SimulationEvaluator;
            // we check boundary against the *next* position to detect collision.
            var nextPos = position + velocity * dt;

            // Check boundary collision
            if (_bounceCount >= MaxBounces) return;

            float dist = nextPos.magnitude;
            if (dist > BoundaryRadius)
            {
                // Reflect velocity off the sphere normal (pointing inward)
                var normal = -nextPos.normalized;
                velocity = Vector3.Reflect(velocity, normal);

                _bounceCount++;
            }
        }

        public object CaptureState()
        {
            return new BounceState
            {
                BounceCount = _bounceCount,
                LastVelocity = _lastVelocity
            };
        }

        public void RestoreState(object state)
        {
            var s = (BounceState)state;
            _bounceCount = s.BounceCount;
            _lastVelocity = s.LastVelocity;
        }

        private struct BounceState
        {
            public int BounceCount;
            public Vector3 LastVelocity;
        }
    }
}
