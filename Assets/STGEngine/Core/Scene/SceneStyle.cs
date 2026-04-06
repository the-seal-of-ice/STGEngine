// Assets/STGEngine/Core/Scene/SceneStyle.cs
using System;
using System.Collections.Generic;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 场景风格配置。组合通路轮廓和视觉参数。
    /// Phase 1 精简版：仅含 PathProfile 和基础配置。
    /// 后续 Phase 将扩展障碍物、光照、粒子、音效等字段。
    /// </summary>
    public class SceneStyle
    {
        /// <summary>唯一标识符。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>显示名称。</summary>
        public string Name { get; set; } = "New Scene Style";

        /// <summary>通路轮廓定义。</summary>
        public PathProfile PathProfile { get; set; } = new();

        /// <summary>是否生成可见地面。月面虚空等特殊场景设为 false。</summary>
        public bool HasGround { get; set; } = true;

        /// <summary>障碍物配置列表（可混合多种风格）。</summary>
        public List<ObstacleConfig> ObstacleConfigs { get; set; } = new();

        /// <summary>道路内危险物出现频率（个/100m）。</summary>
        public float HazardFrequency { get; set; }
    }
}
