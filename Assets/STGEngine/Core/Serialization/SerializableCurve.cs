using System.Collections.Generic;

namespace STGEngine.Core.Serialization
{
    /// <summary>
    /// YAML-serializable representation of an AnimationCurve.
    /// Runtime layer converts this to UnityEngine.AnimationCurve.
    /// </summary>
    public class SerializableCurve
    {
        public List<CurveKeyframe> Keyframes { get; set; } = new();

        /// <summary>Parameterless ctor required by YamlDotNet.</summary>
        public SerializableCurve() { }

        public SerializableCurve(params (float time, float value)[] points)
        {
            foreach (var (t, v) in points)
                Keyframes.Add(new CurveKeyframe { Time = t, Value = v });
        }

        /// <summary>
        /// Evaluate the curve at time t using linear interpolation.
        /// Sufficient for Core layer without AnimationCurve dependency.
        /// </summary>
        public float Evaluate(float t)
        {
            if (Keyframes.Count == 0) return 0f;
            if (Keyframes.Count == 1) return Keyframes[0].Value;

            // Clamp to range
            if (t <= Keyframes[0].Time) return Keyframes[0].Value;
            if (t >= Keyframes[Keyframes.Count - 1].Time)
                return Keyframes[Keyframes.Count - 1].Value;

            // Find segment and lerp
            for (int i = 0; i < Keyframes.Count - 1; i++)
            {
                var a = Keyframes[i];
                var b = Keyframes[i + 1];
                if (t >= a.Time && t <= b.Time)
                {
                    float frac = (t - a.Time) / (b.Time - a.Time);
                    return a.Value + (b.Value - a.Value) * frac;
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
