using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 相机脚本预设模板。提供常用镜头运动的快速配置。
    /// </summary>
    public class CameraPreset
    {
        /// <summary>预设名称。</summary>
        public string Name { get; set; }

        /// <summary>预设描述。</summary>
        public string Description { get; set; }

        /// <summary>预设模板参数。</summary>
        public CameraScriptParams Template { get; set; }
    }

    /// <summary>
    /// 内置相机预设集合。
    /// </summary>
    public static class CameraPresets
    {
        public static IReadOnlyList<CameraPreset> BuiltIn { get; } = new List<CameraPreset>
        {
            new CameraPreset
            {
                Name = "Boss 登场",
                Description = "从远景推到中景，以边界中心为参考",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.BoundaryCenter,
                    FrameMode = CameraFrameMode.SplineAxes,
                    BoundaryCenterHeight = 8f,
                    BlendIn = 0.5f,
                    BlendOut = 0.5f,
                    MotionTransition = MotionTransitionType.SmoothBlend,
                    ScreenTransition = ScreenTransitionType.Cut,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe { Time = 0f, PositionOffset = new Vector3(0, 15, -25), Rotation = Quaternion.Euler(25, 0, 0), FOV = 50f },
                        new CameraKeyframe { Time = 2f, PositionOffset = new Vector3(0, 8, -12), Rotation = Quaternion.Euler(20, 0, 0), FOV = 60f }
                    }
                }
            },
            new CameraPreset
            {
                Name = "Boss 聚焦",
                Description = "以 Boss 为参考中心的环绕运镜",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.Boss,
                    FrameMode = CameraFrameMode.WorldAxes,
                    BlendIn = 0.3f,
                    BlendOut = 0.3f,
                    MotionTransition = MotionTransitionType.SmoothBlend,
                    ScreenTransition = ScreenTransitionType.Cut,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe { Time = 0f, PositionOffset = new Vector3(5, 3, -8), Rotation = Quaternion.Euler(15, -20, 0), FOV = 55f },
                        new CameraKeyframe { Time = 1.5f, PositionOffset = new Vector3(-5, 3, -8), Rotation = Quaternion.Euler(15, 20, 0), FOV = 55f },
                        new CameraKeyframe { Time = 3f, PositionOffset = new Vector3(0, 5, -10), Rotation = Quaternion.Euler(20, 0, 0), FOV = 60f }
                    }
                }
            },
            new CameraPreset
            {
                Name = "跳切到玩家",
                Description = "直接跳切到玩家视角",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.Player,
                    FrameMode = CameraFrameMode.SplineAxes,
                    BlendIn = 0f,
                    BlendOut = 0.3f,
                    MotionTransition = MotionTransitionType.Cut,
                    ScreenTransition = ScreenTransitionType.Cut,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe { Time = 0f, PositionOffset = new Vector3(0, 10, -8), Rotation = Quaternion.Euler(30, 0, 0), FOV = 60f }
                    }
                }
            },
            new CameraPreset
            {
                Name = "平滑转向 Boss",
                Description = "从玩家视角平滑过渡到 Boss 视角",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.Boss,
                    FrameMode = CameraFrameMode.WorldAxes,
                    BlendIn = 1f,
                    BlendOut = 0.5f,
                    MotionTransition = MotionTransitionType.SpeedRamp,
                    ScreenTransition = ScreenTransitionType.CrossFade,
                    TransitionDuration = 1f,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe { Time = 0f, PositionOffset = new Vector3(0, 4, -10), Rotation = Quaternion.Euler(15, 0, 0), FOV = 55f },
                        new CameraKeyframe { Time = 2f, PositionOffset = new Vector3(0, 4, -10), Rotation = Quaternion.Euler(15, 0, 0), FOV = 55f }
                    }
                }
            },
            new CameraPreset
            {
                Name = "俯瞰全场",
                Description = "以边界中心为参考的高位俯视",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.BoundaryCenter,
                    FrameMode = CameraFrameMode.SplineAxes,
                    BoundaryCenterHeight = 12f,
                    BlendIn = 0.8f,
                    BlendOut = 0.8f,
                    MotionTransition = MotionTransitionType.SmoothBlend,
                    ScreenTransition = ScreenTransitionType.FadeToBlack,
                    TransitionDuration = 0.8f,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe { Time = 0f, PositionOffset = new Vector3(0, 25, -5), Rotation = Quaternion.Euler(70, 0, 0), FOV = 50f },
                        new CameraKeyframe { Time = 3f, PositionOffset = new Vector3(0, 25, -5), Rotation = Quaternion.Euler(70, 0, 0), FOV = 50f }
                    }
                }
            }
        };

        /// <summary>
        /// 将预设模板应用到目标 CameraScriptParams（深拷贝关键帧）。
        /// </summary>
        public static void ApplyPreset(CameraPreset preset, CameraScriptParams target)
        {
            if (preset?.Template == null || target == null) return;

            var src = preset.Template;
            target.ReferenceTarget = src.ReferenceTarget;
            target.FrameMode = src.FrameMode;
            target.TargetId = src.TargetId ?? "";
            target.FixedWorldPosition = src.FixedWorldPosition;
            target.BoundaryCenterHeight = src.BoundaryCenterHeight;
            target.BlendIn = src.BlendIn;
            target.BlendOut = src.BlendOut;
            target.ScreenTransition = src.ScreenTransition;
            target.MotionTransition = src.MotionTransition;
            target.TransitionDuration = src.TransitionDuration;

            target.Keyframes.Clear();
            if (src.Keyframes != null)
            {
                foreach (var kf in src.Keyframes)
                {
                    target.Keyframes.Add(new CameraKeyframe
                    {
                        Time = kf.Time,
                        PositionOffset = kf.PositionOffset,
                        Rotation = kf.Rotation,
                        FOV = kf.FOV,
                        Easing = kf.Easing
                    });
                }
            }
        }
    }
}
