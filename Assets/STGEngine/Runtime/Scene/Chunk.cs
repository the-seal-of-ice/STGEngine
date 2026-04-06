// Assets/STGEngine/Runtime/Scene/Chunk.cs
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景分块的运行时表示。每个 Chunk 沿滚动轴（+Z）占据固定长度，
    /// 持有地面 mesh 和从 PathProfile 采样的形态数据。
    /// </summary>
    public class Chunk
    {
        /// <summary>Chunk 在序列中的索引（0, 1, 2...），用于确定性随机种子派生。</summary>
        public int Index { get; set; }

        /// <summary>该 Chunk 起始处的滚动距离（米）。</summary>
        public float StartDistance { get; set; }

        /// <summary>该 Chunk 的长度（米）。</summary>
        public float Length { get; set; }

        /// <summary>Chunk 起始处的 PathProfile 采样。</summary>
        public PathSample StartSample { get; set; }

        /// <summary>Chunk 终点处的 PathProfile 采样。</summary>
        public PathSample EndSample { get; set; }

        /// <summary>Chunk 的根 GameObject。</summary>
        public GameObject Root { get; set; }

        /// <summary>地面 mesh 的 GameObject（Root 的子物体）。</summary>
        public GameObject Ground { get; set; }

        /// <summary>Chunk 是否处于活跃状态。</summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 在 Chunk 内部的归一化位置（0~1）处，用 Hermite 插值采样通路形态。
        /// 保证相邻 Chunk 边界处 C1 连续（位置 + 切线连续）。
        /// </summary>
        public PathSample LerpAt(float t)
        {
            t = Mathf.Clamp01(t);
            float h = t * t * (3f - 2f * t);
            return new PathSample
            {
                Width = Mathf.Lerp(StartSample.Width, EndSample.Width, h),
                Height = Mathf.Lerp(StartSample.Height, EndSample.Height, h),
                Speed = Mathf.Lerp(StartSample.Speed, EndSample.Speed, h),
                Drift = Mathf.Lerp(StartSample.Drift, EndSample.Drift, h)
            };
        }

        /// <summary>停用 Chunk，隐藏 GameObject 并标记为非活跃。</summary>
        public void Deactivate()
        {
            IsActive = false;
            if (Root != null) Root.SetActive(false);
        }

        /// <summary>激活 Chunk，显示 GameObject 并标记为活跃。</summary>
        public void Activate()
        {
            IsActive = true;
            if (Root != null) Root.SetActive(true);
        }
    }
}
