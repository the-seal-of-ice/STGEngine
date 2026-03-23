using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Simulation modifier: steers bullets toward a target position.
    /// After an initial delay, bullets gradually turn toward TargetPosition
    /// at a rate controlled by TurnSpeed (degrees/second).
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

        // Internal state: elapsed time since modifier started
        private float _elapsed;

        public HomingModifier() { }

        public void Step(float dt, ref Vector3 position, ref Vector3 velocity)
        {
            _elapsed += dt;

            if (_elapsed < Delay)
            {
                // Before delay: just move straight
                position += velocity * dt;
                return;
            }

            float speed = velocity.magnitude;
            if (speed < 0.0001f)
            {
                position += velocity * dt;
                return;
            }

            // Compute desired direction
            var toTarget = TargetPosition - position;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                position += velocity * dt;
                return;
            }

            var desiredDir = toTarget.normalized;
            var currentDir = velocity / speed;

            // Rotate toward target, limited by TurnSpeed * dt
            float maxAngle = TurnSpeed * dt;
            var newDir = Vector3.RotateTowards(currentDir, desiredDir, maxAngle * Mathf.Deg2Rad, 0f);

            velocity = newDir * speed;
            position += velocity * dt;
        }

        public object CaptureState()
        {
            return _elapsed;
        }

        public void RestoreState(object state)
        {
            _elapsed = (float)state;
        }
    }
}
