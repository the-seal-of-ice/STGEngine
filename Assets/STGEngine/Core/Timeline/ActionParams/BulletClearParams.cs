using UnityEngine;

namespace STGEngine.Core.Timeline
{
    public enum ClearShape { FullScreen, Circle, Rectangle, Line }

    /// <summary>
    /// Controls how far the clear wave expands.
    /// </summary>
    public enum ClearRange
    {
        /// <summary>Expand to the specified Radius / Extents, then stop.</summary>
        ToRadius,
        /// <summary>Expand until the wave covers the entire sandbox boundary.</summary>
        ToBoundary,
        /// <summary>Expand infinitely with accelerating speed until all bullets are gone.</summary>
        Infinite
    }

    public class BulletClearParams : IActionParams
    {
        public ClearShape Shape { get; set; } = ClearShape.FullScreen;
        /// <summary>Center point of the clear effect.</summary>
        public Vector3 Origin { get; set; } = Vector3.zero;
        /// <summary>Radius for Circle mode (or max radius for ToBoundary/Infinite).</summary>
        public float Radius { get; set; } = 50f;
        /// <summary>Half-extents for Rectangle mode.</summary>
        public Vector3 Extents { get; set; } = Vector3.one * 25f;
        /// <summary>Convert cleared bullets to score items.</summary>
        public bool ConvertToScore { get; set; } = true;
        /// <summary>Initial expansion speed of the clear wave (units/sec). 0 = instant.</summary>
        public float ExpandSpeed { get; set; } = 30f;
        /// <summary>How far the wave expands.</summary>
        public ClearRange Range { get; set; } = ClearRange.ToBoundary;
    }
}
