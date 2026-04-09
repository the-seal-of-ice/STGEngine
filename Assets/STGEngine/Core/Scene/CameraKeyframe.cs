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

        /// <summary>局部空间旋转（四元数）。无万向锁。</summary>
        public Quaternion Rotation { get; set; } = Quaternion.identity;

        /// <summary>
        /// 欧拉角便捷访问器（编辑器 UI 用）。
        /// 读取时从四元数转换，写入时转回四元数。
        /// </summary>
        public Vector3 RotationEuler
        {
            get => Rotation.eulerAngles;
            set => Rotation = Quaternion.Euler(value);
        }

        /// <summary>视野角度。</summary>
        public float FOV { get; set; } = 60f;

        /// <summary>到下一帧的缓动类型。</summary>
        public EasingType Easing { get; set; } = EasingType.EaseInOut;

        /// <summary>持久化模式。默认 Normal（普通演出关键帧）。</summary>
        public KeyframePersistMode PersistMode { get; set; } = KeyframePersistMode.Normal;

        /// <summary>
        /// 每关键帧参考对象覆盖。为 null 时使用脚本级别的 ReferenceTarget。
        /// 设为具体值时，该关键帧使用独立的参考对象。
        /// </summary>
        public CameraReferenceTarget? ReferenceOverride { get; set; }

        /// <summary>每关键帧参考对象的标架模式覆盖。</summary>
        public CameraFrameMode? FrameModeOverride { get; set; }

        /// <summary>每关键帧 Boss/Enemy 目标 ID 覆盖。</summary>
        public string TargetIdOverride { get; set; }

        /// <summary>每关键帧固定世界坐标覆盖。</summary>
        public Vector3? FixedPositionOverride { get; set; }

        /// <summary>每关键帧边界中心高度覆盖。</summary>
        public float? BoundaryCenterHeightOverride { get; set; }
    }
}
