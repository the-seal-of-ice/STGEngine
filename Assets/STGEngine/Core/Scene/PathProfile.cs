// Assets/STGEngine/Core/Scene/PathProfile.cs
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 通路轮廓定义。描述通路随滚动距离的形态变化。
    /// 所有曲线的 X 轴为滚动距离（米），非时间。
    /// </summary>
    public class PathProfile
    {
        /// <summary>通路宽度（米）随滚动距离变化。窄通道 ~15m，Boss 战场 ~60m。</summary>
        public SerializableCurve WidthCurve { get; set; } = new((0, 20f), (100, 20f));

        /// <summary>通路高度（米）随滚动距离变化。3D 纵向活动范围。</summary>
        public SerializableCurve HeightCurve { get; set; } = new((0, 20f), (100, 20f));

        /// <summary>场景流动速度（m/s）。可变速：Boss 前减速，道中加速。</summary>
        public SerializableCurve ScrollSpeed { get; set; } = new((0, 10f), (100, 10f));

        /// <summary>通路中心线横向偏移（米）。让通路有蜿蜒感而非死直。</summary>
        public SerializableCurve DriftCurve { get; set; } = new((0, 0f), (100, 0f));

        /// <summary>通路总长度（米）。等于曲线 X 轴的最大值。</summary>
        public float TotalLength { get; set; } = 1000f;

        /// <summary>
        /// 在指定滚动距离处采样通路形态。
        /// </summary>
        public PathSample SampleAt(float distance)
        {
            float d = UnityEngine.Mathf.Clamp(distance, 0f, TotalLength);
            return new PathSample
            {
                Width = WidthCurve.Evaluate(d),
                Height = HeightCurve.Evaluate(d),
                Speed = ScrollSpeed.Evaluate(d),
                Drift = DriftCurve.Evaluate(d)
            };
        }
    }

    /// <summary>
    /// 通路在某一滚动距离处的采样结果。
    /// </summary>
    public struct PathSample
    {
        /// <summary>通路宽度（米）。</summary>
        public float Width;
        /// <summary>通路高度（米）。</summary>
        public float Height;
        /// <summary>场景流动速度（m/s）。</summary>
        public float Speed;
        /// <summary>通路中心线横向偏移（米）。</summary>
        public float Drift;
    }
}
