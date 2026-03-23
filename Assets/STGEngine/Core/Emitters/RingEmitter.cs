using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// Ring emitter: distributes bullets evenly around a circle.
    /// </summary>
    [TypeTag("ring")]
    public class RingEmitter : IEmitter
    {
        public string TypeName => "ring";

        public int Count { get; set; } = 12;

        /// <summary>Ring radius (offset from center).</summary>
        public float Radius { get; set; } = 0.5f;

        /// <summary>Initial speed for all bullets.</summary>
        public float Speed { get; set; } = 4f;

        public BulletSpawnData Evaluate(int index, float time)
        {
            float angle = (2f * Mathf.PI * index) / Count;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            var dir = new Vector3(x, 0f, z);

            return new BulletSpawnData
            {
                Position = dir * Radius,
                Direction = dir,
                Speed = Speed
            };
        }
    }
}
