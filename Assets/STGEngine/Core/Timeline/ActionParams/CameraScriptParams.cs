using System.Collections.Generic;
using STGEngine.Core.Scene;
using UnityEngine;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// CameraScript ActionEvent 的参数：关键帧序列 + blend 时长。
    /// </summary>
    public class CameraScriptParams : IActionParams
    {
        /// <summary>关键帧列表（按 Time 升序）。</summary>
        public List<CameraKeyframe> Keyframes { get; set; } = new();

        /// <summary>从当前相机状态过渡到第一帧的时长（秒）。</summary>
        public float BlendIn { get; set; } = 0.5f;

        /// <summary>从最后一帧过渡回原相机的时长（秒）。</summary>
        public float BlendOut { get; set; } = 0.5f;

        // ── 参考对象配置 ──

        /// <summary>参考对象类型。默认 Player 保持向后兼容。</summary>
        public CameraReferenceTarget ReferenceTarget { get; set; } = CameraReferenceTarget.Player;

        /// <summary>坐标标架模式。默认 SplineAxes 保持向后兼容。</summary>
        public CameraFrameMode FrameMode { get; set; } = CameraFrameMode.SplineAxes;

        /// <summary>Boss/Enemy 的标识 ID（ReferenceTarget 为 Boss 或 Enemy 时使用）。</summary>
        public string TargetId { get; set; } = "";

        /// <summary>固定世界坐标（ReferenceTarget 为 WorldFixed 时使用）。</summary>
        public Vector3 FixedWorldPosition { get; set; }

        /// <summary>边界中心点的高度偏移（ReferenceTarget 为 BoundaryCenter 时使用）。</summary>
        public float BoundaryCenterHeight { get; set; } = 8f;

        // ── 过渡配置 ──

        /// <summary>画面过渡效果类型。默认 Cut（无过渡）。</summary>
        public ScreenTransitionType ScreenTransition { get; set; } = ScreenTransitionType.Cut;

        /// <summary>运动过渡方式。默认 SmoothBlend。</summary>
        public MotionTransitionType MotionTransition { get; set; } = MotionTransitionType.SmoothBlend;

        /// <summary>过渡持续时间（秒）。</summary>
        public float TransitionDuration { get; set; } = 0.5f;
    }
}
