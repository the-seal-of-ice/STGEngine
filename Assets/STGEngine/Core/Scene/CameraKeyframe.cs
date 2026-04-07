using System;
using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Core.Scene
{
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
    }
}
