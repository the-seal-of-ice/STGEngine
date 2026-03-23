using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Formula modifier: adds sinusoidal wave motion perpendicular to flight direction.
    /// </summary>
    [TypeTag("wave")]
    public class WaveModifier : IFormulaModifier
    {
        public string TypeName => "wave";
        public bool RequiresSimulation => false;

        /// <summary>Wave amplitude.</summary>
        public float Amplitude { get; set; } = 0.3f;

        /// <summary>Wave frequency in Hz.</summary>
        public float Frequency { get; set; } = 2f;

        /// <summary>Wave axis: "perpendicular" or "vertical".</summary>
        public string Axis { get; set; } = "perpendicular";

        public WaveModifier() { }

        public Vector3 Evaluate(float t, Vector3 basePosition, Vector3 baseDirection)
        {
            float wave = Amplitude * Mathf.Sin(2f * Mathf.PI * Frequency * t);

            // Compute perpendicular axis
            Vector3 perp;
            if (Axis == "vertical")
            {
                perp = Vector3.up;
            }
            else
            {
                // Perpendicular in the horizontal plane
                perp = Vector3.Cross(baseDirection, Vector3.up);
                if (perp.sqrMagnitude < 0.001f)
                    perp = Vector3.Cross(baseDirection, Vector3.right);
                perp.Normalize();
            }

            return perp * wave;
        }
    }
}
