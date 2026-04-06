using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Core.Scene
{
    /// <summary>放置区域枚举。</summary>
    public enum PlacementZone
    {
        /// <summary>通路两侧（视觉边界）。</summary>
        Roadside,
        /// <summary>通路内部（危险障碍物）。</summary>
        Interior
    }

    /// <summary>玩家擦过时的反应。</summary>
    public enum ContactResponse
    {
        None,
        /// <summary>摇晃（竹子/树木）。</summary>
        Sway,
        /// <summary>轻推玩家。</summary>
        Nudge
    }

    /// <summary>
    /// 障碍物散布配置。定义一种障碍物风格的散布规则。
    /// </summary>
    public class ObstacleConfig
    {
        /// <summary>预制体变体资源路径列表。运行时从 Resources 加载。</summary>
        public List<string> PrefabVariants { get; set; } = new();

        /// <summary>基础散布密度（个/m²）。</summary>
        public float Density { get; set; } = 0.05f;

        /// <summary>缩放随机范围 (min, max)。</summary>
        public Vector2 ScaleRange { get; set; } = new(0.8f, 1.2f);

        /// <summary>Y 轴旋转随机范围（度）。</summary>
        public Vector2 RotationRange { get; set; } = new(0f, 360f);

        /// <summary>放置区域。</summary>
        public PlacementZone PlacementZone { get; set; } = PlacementZone.Roadside;

        /// <summary>是否为危险障碍物（碰撞掉残机）。</summary>
        public bool IsHazard { get; set; }

        /// <summary>泊松采样最小间距（米）。</summary>
        public float MinSpacing { get; set; } = 3f;

        /// <summary>耐久度（0 = 不可破坏）。</summary>
        public float Durability { get; set; }

        /// <summary>玩家擦过时的反应。</summary>
        public ContactResponse ContactResponse { get; set; } = ContactResponse.None;

        /// <summary>
        /// 障碍物标签（如 "bamboo"、"rock"），用于敌人出生锚点匹配。
        /// </summary>
        public string Tag { get; set; } = "";
    }
}
