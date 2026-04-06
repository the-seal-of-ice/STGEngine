using System.Collections.Generic;

namespace STGEngine.Core.Serialization
{
    /// <summary>
    /// YAML-serializable representation of an AnimationCurve.
    /// Uses cubic Hermite interpolation for smooth C1-continuous evaluation.
    /// </summary>
    public class SerializableCurve
    {
        public List<CurveKeyframe> Keyframes { get; set; } = new();

        /// <summary>Parameterless ctor required by YamlDotNet.</summary>
        public SerializableCurve() { }

        /// <summary>
        /// Create a curve from (time, value) pairs.
        /// Tangents are auto-computed using Catmull-Rom style for smooth transitions.
        /// </summary>
        public SerializableCurve(params (float time, float value)[] points)
        {
            foreach (var (t, v) in points)
                Keyframes.Add(new CurveKeyframe { Time = t, Value = v });
            AutoComputeTangents();
        }

        /// <summary>
        /// Auto-compute tangents for all keyframes using Catmull-Rom style.
        /// Produces C1-continuous curves (no sudden slope changes at keyframes).
        /// </summary>
        public void AutoComputeTangents()
        {
            for (int i = 0; i < Keyframes.Count; i++)
            {
                float tangent;
                if (i == 0)
                {
                    // First point: forward difference
                    if (Keyframes.Count > 1)
                    {
                        float dt = Keyframes[1].Time - Keyframes[0].Time;
                        tangent = dt > 0.0001f ? (Keyframes[1].Value - Keyframes[0].Value) / dt : 0f;
                    }
                    else tangent = 0f;
                }
                else if (i == Keyframes.Count - 1)
                {
                    // Last point: backward difference
                    float dt = Keyframes[i].Time - Keyframes[i - 1].Time;
                    tangent = dt > 0.0001f ? (Keyframes[i].Value - Keyframes[i - 1].Value) / dt : 0f;
                }
                else
                {
                    // Interior point: Catmull-Rom (average of adjacent slopes)
                    float dt = Keyframes[i + 1].Time - Keyframes[i - 1].Time;
                    tangent = dt > 0.0001f ? (Keyframes[i + 1].Value - Keyframes[i - 1].Value) / dt : 0f;
                }

                Keyframes[i].InTangent = tangent;
                Keyframes[i].OutTangent = tangent;
            }
        }

        /// <summary>
        /// Evaluate the curve at time t using cubic Hermite interpolation.
        /// Produces smooth C1-continuous results (position and first derivative continuous).
        /// </summary>
        public float Evaluate(float t)
        {
            if (Keyframes.Count == 0) return 0f;
            if (Keyframes.Count == 1) return Keyframes[0].Value;

            // Clamp to range
            if (t <= Keyframes[0].Time) return Keyframes[0].Value;
            if (t >= Keyframes[Keyframes.Count - 1].Time)
                return Keyframes[Keyframes.Count - 1].Value;

            // Find segment
            for (int i = 0; i < Keyframes.Count - 1; i++)
            {
                var a = Keyframes[i];
                var b = Keyframes[i + 1];
                if (t >= a.Time && t <= b.Time)
                {
                    float dt = b.Time - a.Time;
                    if (dt < 0.0001f) return a.Value;

                    float frac = (t - a.Time) / dt;

                    // Cubic Hermite interpolation
                    float f2 = frac * frac;
                    float f3 = f2 * frac;

                    float h00 = 2f * f3 - 3f * f2 + 1f;  // value at a
                    float h10 = f3 - 2f * f2 + frac;       // tangent at a
                    float h01 = -2f * f3 + 3f * f2;        // value at b
                    float h11 = f3 - f2;                    // tangent at b

                    return h00 * a.Value
                         + h10 * dt * a.OutTangent
                         + h01 * b.Value
                         + h11 * dt * b.InTangent;
                }
            }

            return Keyframes[Keyframes.Count - 1].Value;
        }
    }

    public class CurveKeyframe
    {
        public float Time { get; set; }
        public float Value { get; set; }
        public float InTangent { get; set; }
        public float OutTangent { get; set; }
    }
}
