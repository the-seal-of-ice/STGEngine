using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// Single-point emitter: all bullets fire from origin in a single direction.
    /// </summary>
    [TypeTag("point")]
    public class PointEmitter : IEmitter
    {
        public string TypeName => "point";

        public int Count { get; set; } = 1;

        /// <summary>Initial speed for all bullets.</summary>
        public float Speed { get; set; } = 5f;

        /// <summary>Base firing direction.</summary>
        public Vector3 Direction { get; set; } = Vector3.forward;

        public BulletSpawnData Evaluate(int index, float time)
        {
            return new BulletSpawnData
            {
                Position = Vector3.zero,
                Direction = Direction.normalized,
                Speed = Speed
            };
        }
    }
}
