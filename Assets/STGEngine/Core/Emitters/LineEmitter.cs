using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// Line emitter: distributes bullets evenly along a line segment.
    /// Bullets fire outward perpendicular to the line (or in a specified direction).
    /// </summary>
    [TypeTag("line")]
    public class LineEmitter : IEmitter
    {
        public string TypeName => "line";

        public int Count { get; set; } = 8;

        /// <summary>Initial speed for all bullets.</summary>
        public float Speed { get; set; } = 4f;

        /// <summary>Start point of the line segment.</summary>
        public Vector3 StartPoint { get; set; } = new Vector3(-2f, 0f, 0f);

        /// <summary>End point of the line segment.</summary>
        public Vector3 EndPoint { get; set; } = new Vector3(2f, 0f, 0f);

        public LineEmitter() { }

        public BulletSpawnData Evaluate(int index, float time)
        {
            // Lerp position along the line
            float t = Count > 1 ? (float)index / (Count - 1) : 0.5f;
            var pos = Vector3.Lerp(StartPoint, EndPoint, t);

            // Direction: perpendicular to line in XZ plane, default forward
            var lineDir = (EndPoint - StartPoint).normalized;
            var dir = Vector3.Cross(lineDir, Vector3.up);
            if (dir.sqrMagnitude < 0.001f)
                dir = Vector3.Cross(lineDir, Vector3.right);
            dir.Normalize();

            return new BulletSpawnData
            {
                Position = pos,
                Direction = dir,
                Speed = Speed
            };
        }
    }
}
