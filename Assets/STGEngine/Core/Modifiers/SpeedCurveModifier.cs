using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Formula modifier: overrides bullet displacement using a speed-over-time curve.
    /// The curve maps time to instantaneous speed; displacement is the integral.
    /// BulletEvaluator passes the base linear position; this modifier replaces it
    /// with curve-integrated displacement along the same direction.
    /// </summary>
    [TypeTag("speed_curve")]
    public class SpeedCurveModifier : IFormulaModifier
    {
        public string TypeName => "speed_curve";
        public bool RequiresSimulation => false;

        /// <summary>Speed curve: time → instantaneous speed.</summary>
        public SerializableCurve SpeedCurve { get; set; } = new();

        public SpeedCurveModifier() { }

        public Vector3 Evaluate(float t, Vector3 basePosition, Vector3 baseDirection)
        {
            // Approximate integral of speed curve via trapezoidal rule
            float distance = IntegrateSpeed(t);
            return baseDirection * distance;
        }

        /// <summary>
        /// Numerically integrate speed curve from 0 to t.
        /// Uses simple trapezoidal rule with fixed step count.
        /// </summary>
        private float IntegrateSpeed(float t)
        {
            if (t <= 0f) return 0f;

            const int steps = 32;
            float dt = t / steps;
            float sum = 0f;
            float prev = SpeedCurve.Evaluate(0f);

            for (int i = 1; i <= steps; i++)
            {
                float curr = SpeedCurve.Evaluate(i * dt);
                sum += (prev + curr) * 0.5f * dt;
                prev = curr;
            }

            return sum;
        }
    }
}
