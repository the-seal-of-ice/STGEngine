using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家位置/状态的统一接口。
    /// 编辑器中由 AI (SimulatedPlayer) 或真人 (PlayerController) 驱动，
    /// 运行时由真人驱动。弹幕系统（HomingModifier 等）通过此接口获取目标。
    /// 
    /// 设计目标：编辑器和运行时零修改切换。
    /// </summary>
    public interface IPlayerProvider
    {
        /// <summary>玩家当前世界位置。</summary>
        Vector3 Position { get; }

        /// <summary>玩家朝向（单位向量）。用于箭头显示和弹幕追踪。</summary>
        Vector3 Forward { get; }

        /// <summary>玩家运行时状态（Lives/Graze/Invincible 等）。</summary>
        PlayerState State { get; }

        /// <summary>玩家是否处于活跃状态。</summary>
        bool IsActive { get; }
    }
}
