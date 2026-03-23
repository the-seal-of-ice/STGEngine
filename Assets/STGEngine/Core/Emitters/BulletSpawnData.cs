using UnityEngine;

namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// Output of an emitter: initial state of a single bullet.
    /// </summary>
    public struct BulletSpawnData
    {
        /// <summary>Offset relative to the emission origin.</summary>
        public Vector3 Position;

        /// <summary>Initial flight direction (unit vector).</summary>
        public Vector3 Direction;

        /// <summary>Initial speed scalar.</summary>
        public float Speed;
    }
}
