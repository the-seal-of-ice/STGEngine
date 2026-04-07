using UnityEngine;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 为 CameraScriptPlayer 提供玩家位置和局部坐标标架。
    /// 关键帧的 PositionOffset (right, up, forward) 通过此标架转换为世界坐标。
    /// </summary>
    public interface ICameraFrameProvider
    {
        /// <summary>玩家当前世界位置（偏移基准点）。</summary>
        Vector3 PlayerWorldPosition { get; }

        /// <summary>局部坐标系 Right 方向（世界空间单位向量）。</summary>
        Vector3 FrameRight { get; }

        /// <summary>局部坐标系 Up 方向（世界空间单位向量）。</summary>
        Vector3 FrameUp { get; }

        /// <summary>局部坐标系 Forward 方向（世界空间单位向量）。</summary>
        Vector3 FrameForward { get; }
    }
}
