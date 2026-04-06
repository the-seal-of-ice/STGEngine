using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景分块的运行时表示。每个 Chunk 对应样条线上一段弧长区间，
    /// 几何体沿样条线生成，天然贴合曲线。
    /// </summary>
    public class Chunk
    {
        /// <summary>Chunk 在序列中的索引（0, 1, 2...）。</summary>
        public int Index { get; set; }

        /// <summary>该 Chunk 起始处的弧长距离（米）。</summary>
        public float StartDistance { get; set; }

        /// <summary>该 Chunk 终点处的弧长距离（米）。</summary>
        public float EndDistance { get; set; }

        /// <summary>该 Chunk 的弧长长度（米）。</summary>
        public float Length => EndDistance - StartDistance;

        /// <summary>Chunk 的根 GameObject。</summary>
        public GameObject Root { get; set; }

        /// <summary>地面 mesh 的 GameObject（Root 的子物体）。</summary>
        public GameObject Ground { get; set; }

        /// <summary>Chunk 是否处于活跃状态。</summary>
        public bool IsActive { get; set; }

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
