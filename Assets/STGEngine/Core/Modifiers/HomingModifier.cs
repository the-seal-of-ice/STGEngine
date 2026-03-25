using UnityEngine;
using STGEngine.Core.Random;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// How to resolve the rotation axis when bullet direction is nearly
    /// anti-parallel to the desired homing direction (dot &lt; threshold).
    /// </summary>
    public enum AntiParallelMode
    {
        /// <summary>
        /// Let Unity's RotateTowards pick an arbitrary axis.
        /// Fast but produces visible asymmetry due to floating-point bias.
        /// </summary>
        None,

        /// <summary>
        /// Use a fixed world-space axis (prefer Up, fallback Right).
        /// Deterministic and reproducible, but all bullets break the same way.
        /// </summary>
        Fixed,

        /// <summary>
        /// Each bullet seeds a random perpendicular axis on first encounter.
        /// Produces uniform scatter across large bullet counts. (Default)
        /// </summary>
        Random,
    }

    /// <summary>
    /// Simulation modifier: steers bullets toward a target position.
    /// After an initial delay, bullets gradually turn toward TargetPosition
    /// at a rate controlled by TurnSpeed (degrees/second).
    /// AntiParallelMode controls how the near-180° degeneracy is resolved.
    /// </summary>
    [TypeTag("homing")]
    public class HomingModifier : ISimulationModifier
    {
        public string TypeName => "homing";
        public bool RequiresSimulation => true;

        /// <summary>World-space target position to home toward.</summary>
        public Vector3 TargetPosition { get; set; } = Vector3.zero;

        /// <summary>Turn speed in degrees per second.</summary>
        public float TurnSpeed { get; set; } = 90f;

        /// <summary>Delay in seconds before homing activates.</summary>
        public float Delay { get; set; } = 0.5f;

        /// <summary>How to break symmetry when bullet flies away from target.</summary>
        public AntiParallelMode AntiParallel { get; set; } = AntiParallelMode.Random;

        // Internal state
        private float _elapsed;
        private Vector3 _cachedAxis;
        private bool _axisSeeded;

        /// <summary>
        /// Deterministic RNG injected by SimulationEvaluator.
        /// When set, replaces UnityEngine.Random for anti-parallel axis seeding.
        /// </summary>
        public DeterministicRng Rng { get; set; }

        public HomingModifier() { }

        public void Step(float dt, ref Vector3 position, ref Vector3 velocity)
        {
            _elapsed += dt;

            if (_elapsed < Delay)
                return;

            float speed = velocity.magnitude;
            if (speed < 0.0001f) return;

            var toTarget = TargetPosition - position;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            var desiredDir = toTarget.normalized;
            var currentDir = velocity / speed;

            float maxAngle = TurnSpeed * dt;
            float dot = Vector3.Dot(currentDir, desiredDir);

            Vector3 newDir;
            if (dot < -0.99f && AntiParallel != AntiParallelMode.None)
            {
                var axis = ResolveAntiParallelAxis(currentDir);
                newDir = Quaternion.AngleAxis(maxAngle, axis) * currentDir;
            }
            else
            {
                newDir = Vector3.RotateTowards(currentDir, desiredDir, maxAngle * Mathf.Deg2Rad, 0f);
            }

            velocity = newDir * speed;
        }

        private Vector3 ResolveAntiParallelAxis(Vector3 currentDir)
        {
            if (AntiParallel == AntiParallelMode.Fixed)
            {
                var perp = Vector3.Cross(currentDir, Vector3.up);
                if (perp.sqrMagnitude < 0.0001f)
                    perp = Vector3.Cross(currentDir, Vector3.right);
                return perp.normalized;
            }

            // AntiParallelMode.Random — seed once per bullet instance
            if (!_axisSeeded)
            {
                // Use deterministic RNG if available, fallback to Unity Random
                _cachedAxis = Rng != null ? Rng.OnUnitSphere() : UnityEngine.Random.onUnitSphere;
                _cachedAxis = Vector3.Cross(currentDir, _cachedAxis);
                if (_cachedAxis.sqrMagnitude < 0.0001f)
                    _cachedAxis = Vector3.Cross(currentDir, Vector3.up);
                _cachedAxis.Normalize();
                _axisSeeded = true;
            }
            return _cachedAxis;
        }

        public object CaptureState()
        {
            return (_elapsed, _cachedAxis, _axisSeeded);
        }

        public void RestoreState(object state)
        {
            var (e, axis, seeded) = ((float, Vector3, bool))state;
            _elapsed = e;
            _cachedAxis = axis;
            _axisSeeded = seeded;
        }
    }
}
