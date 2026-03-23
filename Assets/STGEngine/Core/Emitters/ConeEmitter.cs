using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// Cone emitter: distributes bullets within a cone shape.
    /// Uses Fibonacci spiral on a spherical cap for uniform angular distribution.
    /// </summary>
    [TypeTag("cone")]
    public class ConeEmitter : IEmitter
    {
        public string TypeName => "cone";

        public int Count { get; set; } = 12;

        /// <summary>Initial speed for all bullets.</summary>
        public float Speed { get; set; } = 4f;

        /// <summary>Half-angle of the cone in degrees.</summary>
        public float Angle { get; set; } = 30f;

        /// <summary>Radius offset from center for spawn position.</summary>
        public float Radius { get; set; } = 0f;

        public ConeEmitter() { }

        public BulletSpawnData Evaluate(int index, float time)
        {
            float halfAngleRad = Angle * Mathf.Deg2Rad;

            // Distribute within cone using Fibonacci-like spiral
            float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
            float theta = 2f * Mathf.PI * index / goldenRatio;

            // Map index to polar angle within [0, halfAngle]
            // cosTheta ranges from 1 (center) to cos(halfAngle) (edge)
            float cosMin = Mathf.Cos(halfAngleRad);
            float cosVal = 1f - (1f - cosMin) * (index + 0.5f) / Count;
            float phi = Mathf.Acos(Mathf.Clamp(cosVal, cosMin, 1f));

            float x = Mathf.Sin(phi) * Mathf.Cos(theta);
            float y = Mathf.Cos(phi);
            float z = Mathf.Sin(phi) * Mathf.Sin(theta);
            var dir = new Vector3(x, y, z).normalized;

            return new BulletSpawnData
            {
                Position = dir * Radius,
                Direction = dir,
                Speed = Speed
            };
        }
    }
}
