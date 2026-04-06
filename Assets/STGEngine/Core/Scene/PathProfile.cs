using STGEngine.Core.Serialization;
using UnityEngine;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 通路轮廓定义。组合 PathSpline（3D 中心线轨迹）和属性曲线（宽度/高度/速度）。
    /// 所有属性曲线的 X 轴为弧长距离（米）。
    /// </summary>
    public class PathProfile
    {
        /// <summary>通路中心线的 3D 样条线。</summary>
        public PathSpline Spline { get; set; } = new();

        /// <summary>通路宽度（米）随弧长距离变化。窄通道 ~15m，Boss 战场 ~60m。</summary>
        public SerializableCurve WidthCurve { get; set; } = new((0, 20f), (100, 20f));

        /// <summary>通路高度（米）随弧长距离变化。3D 纵向活动范围。</summary>
        public SerializableCurve HeightCurve { get; set; } = new((0, 20f), (100, 20f));

        /// <summary>场景流动速度（m/s）。可变速：Boss 前减速，道中加速。</summary>
        public SerializableCurve ScrollSpeed { get; set; } = new((0, 10f), (100, 10f));

        /// <summary>
        /// 在指定弧长距离处采样通路的完整状态：
        /// 样条线位置/方向 + 宽度/高度/速度。
        /// </summary>
        public PathSample SampleAt(float distance)
        {
            float totalLen = Spline.TotalLength;
            float d = Mathf.Clamp(distance, 0f, totalLen > 0f ? totalLen : 1f);

            SplineSample splineSample = Spline.SampleAtDistance(d);

            return new PathSample
            {
                Position = splineSample.Position,
                Tangent = splineSample.Tangent,
                Normal = splineSample.Normal,
                Width = WidthCurve.Evaluate(d),
                Height = HeightCurve.Evaluate(d),
                Speed = ScrollSpeed.Evaluate(d)
            };
        }
    }

    /// <summary>
    /// 通路在某一弧长距离处的完整采样结果。
    /// </summary>
    public struct PathSample
    {
        /// <summary>样条线上的世界坐标位置（通路中心点）。</summary>
        public Vector3 Position;
        /// <summary>切线方向（归一化，通路前进方向）。</summary>
        public Vector3 Tangent;
        /// <summary>法线方向（归一化，通路右侧方向）。</summary>
        public Vector3 Normal;
        /// <summary>通路宽度（米）。</summary>
        public float Width;
        /// <summary>通路高度（米）。</summary>
        public float Height;
        /// <summary>场景流动速度（m/s）。</summary>
        public float Speed;
    }
}
