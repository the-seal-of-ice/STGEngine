using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Formula modifier: adds sinusoidal wave motion perpendicular to flight direction,
    /// driven by travel distance instead of global time. Each bullet oscillates
    /// independently based on how far it has traveled, producing a snake-like trajectory.
    /// BulletEvaluator passes travel distance (not time) as the t parameter.
    /// </summary>
    [TypeTag("wave_independent")]
    public class IndependentWaveModifier : IFormulaModifier
    {
        public string TypeName => "wave_independent";
        public bool RequiresSimulation => false;

        /// <summary>Wave amplitude (fixed per bullet).</summary>
        public float Amplitude { get; set; } = 0.3f;

        /// <summary>Wavelength: travel distance for one full sine cycle.</summary>
        public float Wavelength { get; set; } = 2f;

        /// <summary>Wave axis: "perpendicular" or "vertical".</summary>
        public string Axis { get; set; } = "perpendicular";

        public IndependentWaveModifier() { }

        /// <summary>
        /// Compute lateral offset. The t parameter is expected to be the bullet's
        /// travel distance (supplied by BulletEvaluator), not global time.
        /// </summary>
        public Vector3 Evaluate(float t, Vector3 basePosition, Vector3 baseDirection)
        {
            if (Wavelength <= 0f) return Vector3.zero;

            float wave = Amplitude * Mathf.Sin(2f * Mathf.PI * t / Wavelength);

            Vector3 perp;
            if (Axis == "vertical")
            {
                perp = Vector3.up;
            }
            else
            {
                perp = Vector3.Cross(baseDirection, Vector3.up);
                if (perp.sqrMagnitude < 0.001f)
                    perp = Vector3.Cross(baseDirection, Vector3.right);
                perp.Normalize();
            }

            return perp * wave;
        }
    }
}
