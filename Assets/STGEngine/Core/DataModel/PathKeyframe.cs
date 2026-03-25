using UnityEngine;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// A keyframe in a movement path. Used by SpellCard (boss path)
    /// and EnemyInstance (enemy path). Linear interpolation between keyframes.
    /// </summary>
    public class PathKeyframe
    {
        /// <summary>Time in seconds relative to the start of the owning context.</summary>
        public float Time { get; set; }

        /// <summary>World-space position at this keyframe.</summary>
        public Vector3 Position { get; set; }
    }
}
