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
            // ── 普通演出（Normal 关键帧） ──

            new CameraPreset
            {
                Name = "Boss 登场演出",
                Description = "普通演出：从远景推到中景，临时接管相机",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.BoundaryCenter,
                    FrameMode = CameraFrameMode.WorldAxes,
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
                Name = "Boss 环绕演出",
                Description = "普通演出：以 Boss 为参考的环绕运镜",
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

            // ── 保持型（Persist 关键帧） ──

            new CameraPreset
            {
                Name = "俯瞰全场(持久)",
                Description = "Persist：拉远拉高俯视，鼠标自由控制玩家朝向",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.BoundaryCenter,
                    FrameMode = CameraFrameMode.WorldAxes,
                    BoundaryCenterHeight = 10f,
                    BlendIn = 0.8f,
                    BlendOut = 0f,
                    MotionTransition = MotionTransitionType.SmoothBlend,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe
                        {
                            Time = 0f,
                            PositionOffset = new Vector3(0, 20, -5),
                            Rotation = Quaternion.Euler(70, 0, 0),
                            FOV = 50f,
                            PersistMode = KeyframePersistMode.Persist,
                            AimMode = PlayerAimMode.FreeMouse
                        }
                    }
                }
            },
            new CameraPreset
            {
                Name = "拉近特写(持久)",
                Description = "Persist：拉近玩家，屏幕中心射击",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.Player,
                    FrameMode = CameraFrameMode.SplineAxes,
                    BlendIn = 0.5f,
                    BlendOut = 0f,
                    MotionTransition = MotionTransitionType.SmoothBlend,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe
                        {
                            Time = 0f,
                            PositionOffset = new Vector3(0, -4, 3),
                            Rotation = Quaternion.identity,
                            FOV = 45f,
                            PersistMode = KeyframePersistMode.Persist,
                            AimMode = PlayerAimMode.ScreenCenter
                        }
                    }
                }
            },
            new CameraPreset
            {
                Name = "锁定Boss(持久)",
                Description = "Persist：玩家朝向锁定 Boss，鼠标控制相机",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.Player,
                    FrameMode = CameraFrameMode.SplineAxes,
                    BlendIn = 0.5f,
                    BlendOut = 0f,
                    MotionTransition = MotionTransitionType.SmoothBlend,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe
                        {
                            Time = 0f,
                            PositionOffset = Vector3.zero,
                            Rotation = Quaternion.identity,
                            FOV = 60f,
                            PersistMode = KeyframePersistMode.Persist,
                            AimMode = PlayerAimMode.LockBoss
                        }
                    }
                }
            },

            // ── 回归 ──

            new CameraPreset
            {
                Name = "回归默认视角",
                Description = "Revert：平滑恢复到默认玩家视角",
                Template = new CameraScriptParams
                {
                    ReferenceTarget = CameraReferenceTarget.Player,
                    BlendIn = 0.5f,
                    BlendOut = 0f,
                    MotionTransition = MotionTransitionType.SmoothBlend,
                    Keyframes = new List<CameraKeyframe>
                    {
                        new CameraKeyframe
                        {
                            Time = 0f,
                            PersistMode = KeyframePersistMode.Revert
                        }
                    }
                }
            },
        };

        /// <summary>
        /// 将预设模板应用到目标 CameraScriptParams（深拷贝关键帧含所有字段）。
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
                        Easing = kf.Easing,
                        PersistMode = kf.PersistMode,
                        ReferenceOverride = kf.ReferenceOverride,
                        FrameModeOverride = kf.FrameModeOverride,
                        TargetIdOverride = kf.TargetIdOverride,
                        FixedPositionOverride = kf.FixedPositionOverride,
                        BoundaryCenterHeightOverride = kf.BoundaryCenterHeightOverride,
                        AimMode = kf.AimMode,
                        AimTargetPosition = kf.AimTargetPosition,
                        AimTargetId = kf.AimTargetId
                    });
                }
            }
        }
    }
}
