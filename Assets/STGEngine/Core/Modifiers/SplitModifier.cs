using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Simulation modifier: splits a bullet into multiple bullets after SplitTime.
    /// The original bullet continues; new bullets spread at SpreadAngle intervals.
    /// BulletEvaluator queries ShouldSplit() and GetSplitDirections() to spawn children.
    /// Step() only tracks elapsed time; actual splitting is handled by the evaluator.
    /// </summary>
    [TypeTag("split")]
    public class SplitModifier : ISimulationModifier
    {
        public string TypeName => "split";
        public bool RequiresSimulation => true;

        /// <summary>Time in seconds after which the bullet splits.</summary>
        public float SplitTime { get; set; } = 1f;

        /// <summary>Number of child bullets to spawn (excluding the original).</summary>
        public int SplitCount { get; set; } = 3;

        /// <summary>Total spread angle in degrees for child bullets.</summary>
        public float SpreadAngle { get; set; } = 60f;

        // Internal state
        private float _elapsed;
        private bool _hasSplit;

        public SplitModifier() { }

        public void Step(float dt, ref Vector3 position, ref Vector3 velocity)
        {
            _elapsed += dt;
            // Movement is handled by the base evaluator; this modifier only tracks time.
            position += velocity * dt;
        }

        /// <summary>
        /// Check if this bullet should split at the current elapsed time.
        /// Returns true only once (the first frame after SplitTime).
        /// </summary>
        public bool ShouldSplit()
        {
            if (_hasSplit) return false;
            if (_elapsed >= SplitTime)
            {
                _hasSplit = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get the velocity directions for child bullets.
        /// Spreads evenly around the parent's current velocity direction.
        /// </summary>
        public List<Vector3> GetSplitDirections(Vector3 currentVelocity)
        {
            var dirs = new List<Vector3>(SplitCount);
            float speed = currentVelocity.magnitude;
            if (speed < 0.0001f) return dirs;

            var forward = currentVelocity / speed;

            // Find a perpendicular axis
            var up = Vector3.Cross(forward, Vector3.up);
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.Cross(forward, Vector3.right);
            up.Normalize();

            float halfSpread = SpreadAngle * 0.5f;
            float step = SplitCount > 1 ? SpreadAngle / (SplitCount - 1) : 0f;

            for (int i = 0; i < SplitCount; i++)
            {
                float angle = SplitCount > 1 ? -halfSpread + step * i : 0f;
                var rot = Quaternion.AngleAxis(angle, up);
                dirs.Add(rot * forward * speed);
            }

            return dirs;
        }

        public object CaptureState()
        {
            return new SplitState { Elapsed = _elapsed, HasSplit = _hasSplit };
        }

        public void RestoreState(object state)
        {
            var s = (SplitState)state;
            _elapsed = s.Elapsed;
            _hasSplit = s.HasSplit;
        }

        /// <summary>Reset split state (used when creating child bullet instances).</summary>
        public void ResetSplitState()
        {
            _elapsed = 0f;
            _hasSplit = false;
        }

        private struct SplitState
        {
            public float Elapsed;
            public bool HasSplit;
        }
    }
}
