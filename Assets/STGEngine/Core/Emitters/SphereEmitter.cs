using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// Sphere emitter: distributes bullets uniformly on a sphere surface
    /// using Fibonacci sphere algorithm for even spacing.
    /// </summary>
    [TypeTag("sphere")]
    public class SphereEmitter : IEmitter
    {
        public string TypeName => "sphere";

        public int Count { get; set; } = 24;

        /// <summary>Sphere radius (offset from center).</summary>
        public float Radius { get; set; } = 0.5f;

        /// <summary>Initial speed for all bullets.</summary>
        public float Speed { get; set; } = 4f;

        public SphereEmitter() { }

        public BulletSpawnData Evaluate(int index, float time)
        {
            // Fibonacci sphere for uniform distribution
            float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
            float theta = 2f * Mathf.PI * index / goldenRatio;
            float phi = Mathf.Acos(1f - 2f * (index + 0.5f) / Count);

            float x = Mathf.Sin(phi) * Mathf.Cos(theta);
            float y = Mathf.Cos(phi);
            float z = Mathf.Sin(phi) * Mathf.Sin(theta);
            var dir = new Vector3(x, y, z);

            return new BulletSpawnData
            {
                Position = dir * Radius,
                Direction = dir,
                Speed = Speed
            };
        }
    }
}
