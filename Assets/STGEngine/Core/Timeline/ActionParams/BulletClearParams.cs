using UnityEngine;

namespace STGEngine.Core.Timeline
{
    public enum ClearShape { FullScreen, Circle, Rectangle, Line }

    public class BulletClearParams : IActionParams
    {
        public ClearShape Shape { get; set; } = ClearShape.FullScreen;
        /// <summary>Center point of the clear effect.</summary>
        public Vector3 Origin { get; set; } = Vector3.zero;
        /// <summary>Radius for Circle mode.</summary>
        public float Radius { get; set; } = 50f;
        /// <summary>Half-extents for Rectangle mode.</summary>
        public Vector3 Extents { get; set; } = Vector3.one * 25f;
        /// <summary>Convert cleared bullets to score items.</summary>
        public bool ConvertToScore { get; set; } = true;
        /// <summary>Expansion speed of the clear wave. 0 = instant.</summary>
        public float ExpandSpeed { get; set; } = 30f;
    }
}
