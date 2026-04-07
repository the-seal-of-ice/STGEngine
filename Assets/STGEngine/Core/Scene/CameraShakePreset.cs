using System;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 镜头震动预设参数。
    /// </summary>
    [Serializable]
    public class CameraShakePreset
    {
        /// <summary>震动持续时间（秒）。</summary>
        public float Duration { get; set; } = 0.5f;

        /// <summary>最大振幅（米）。</summary>
        public float Amplitude { get; set; } = 0.3f;

        /// <summary>频率（Hz）。</summary>
        public float Frequency { get; set; } = 25f;

        /// <summary>衰减速率（1 = 线性衰减到 0）。</summary>
        public float DecayRate { get; set; } = 1f;
    }
}
