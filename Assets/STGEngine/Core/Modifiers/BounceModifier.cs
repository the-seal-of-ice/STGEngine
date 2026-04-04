using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Simulation modifier: bounces bullets off an axis-aligned box boundary.
    /// When a bullet exceeds the box half-extents on any axis, its velocity
    /// component on that axis is reflected and it is pushed back inside.
    /// Stops bouncing after MaxBounces.
    /// </summary>
    [TypeTag("bounce")]
    public class BounceModifier : ISimulationModifier
    {
        public string TypeName => "bounce";
        public bool RequiresSimulation => true;

        /// <summary>Half-extents of the box boundary along each axis.</summary>
        public Vector3 BoundaryHalfExtents { get; set; } = new Vector3(40f, 40f, 40f);

        /// <summary>Maximum number of bounces before bullet flies free.</summary>
        public int MaxBounces { get; set; } = 3;

        // Internal state
        private int _bounceCount;
        private Vector3 _lastVelocity;

        public BounceModifier() { }

        public void Step(float dt, ref Vector3 position, ref Vector3 velocity)
        {
            _lastVelocity = velocity;

            if (_bounceCount >= MaxBounces) return;

            var nextPos = position + velocity * dt;
            var h = BoundaryHalfExtents;
            bool bounced = false;

            // Check each axis independently for AABB reflection
            if (nextPos.x > h.x || nextPos.x < -h.x)
            {
                velocity.x = -velocity.x;
                nextPos.x = Mathf.Clamp(nextPos.x, -h.x, h.x);
                bounced = true;
            }
            if (nextPos.y > h.y || nextPos.y < -h.y)
            {
                velocity.y = -velocity.y;
                nextPos.y = Mathf.Clamp(nextPos.y, -h.y, h.y);
                bounced = true;
            }
            if (nextPos.z > h.z || nextPos.z < -h.z)
            {
                velocity.z = -velocity.z;
                nextPos.z = Mathf.Clamp(nextPos.z, -h.z, h.z);
                bounced = true;
            }

            if (bounced)
            {
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
