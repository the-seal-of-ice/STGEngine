using System;
using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 关键帧持久化模式。
    /// </summary>
    public enum KeyframePersistMode
    {
        /// <summary>普通关键帧（演出期间临时接管相机）。</summary>
        Normal,
        /// <summary>保持：将偏移/旋转/FOV 写入玩家相机，演出结束后持久生效。</summary>
        Persist,
        /// <summary>回归：将玩家相机重置回默认属性。</summary>
        Revert
    }

    /// <summary>
    /// 相机演出关键帧。位置偏移定义在玩家局部坐标系 (x=right, y=up, z=forward)，
    /// 运行时由 ICameraFrameProvider 转换为世界坐标。
    /// </summary>
    [Serializable]
    public class CameraKeyframe
    {
        /// <summary>相对于演出开始的时间（秒）。</summary>
        public float Time { get; set; }

        /// <summary>玩家局部坐标系偏移 (x=right, y=up, z=forward)。</summary>
        public Vector3 PositionOffset { get; set; }

        /// <summary>局部空间欧拉角 (pitch, yaw, roll)。</summary>
        public Vector3 Rotation { get; set; }

        /// <summary>视野角度。</summary>
        public float FOV { get; set; } = 60f;

        /// <summary>到下一帧的缓动类型。</summary>
        public EasingType Easing { get; set; } = EasingType.EaseInOut;

        /// <summary>持久化模式。默认 Normal（普通演出关键帧）。</summary>
        public KeyframePersistMode PersistMode { get; set; } = KeyframePersistMode.Normal;
    }
}
