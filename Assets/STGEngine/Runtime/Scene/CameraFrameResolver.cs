using System;
using UnityEngine;
using STGEngine.Core.Scene;
using STGEngine.Core.Timeline;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 根据 CameraScriptParams 配置创建对应的 ICameraFrameProvider。
    /// </summary>
    public static class CameraFrameResolver
    {
        /// <summary>
        /// 解析 CameraScriptParams 的参考对象配置，创建对应的 frame provider。
        /// </summary>
        /// <param name="scriptParams">相机脚本参数。</param>
        /// <param name="playerAnchor">玩家锚点控制器（Player 模式需要）。</param>
        /// <param name="scroll">滚动控制器（BoundaryCenter 和 SplineAxes 模式需要）。</param>
        /// <param name="pathProfile">路径配置（BoundaryCenter 和 SplineAxes 模式需要）。</param>
        /// <param name="targetLookup">目标查找函数，通过 ID 查找 Transform（Boss/Enemy 模式需要）。</param>
        /// <returns>对应的 ICameraFrameProvider，如果无法解析则返回 null。</returns>
        public static ICameraFrameProvider Resolve(
            CameraScriptParams scriptParams,
            PlayerAnchorController playerAnchor,
            ScrollController scroll,
            PathProfile pathProfile,
            Func<string, Transform> targetLookup)
        {
            if (scriptParams == null)
            {
                return null;
            }

            switch (scriptParams.ReferenceTarget)
            {
                case CameraReferenceTarget.Player:
                    return ResolvePlayerFrame(scriptParams, playerAnchor, scroll, pathProfile);

                case CameraReferenceTarget.BoundaryCenter:
                    return new BoundaryCenterFrame(scroll, pathProfile, scriptParams.BoundaryCenterHeight);

                case CameraReferenceTarget.Boss:
                case CameraReferenceTarget.Enemy:
                {
                    if (targetLookup != null)
                    {
                        Transform target = targetLookup(scriptParams.TargetId);
                        if (target != null)
                        {
                            return new TargetTransformFrame(target, scriptParams.FrameMode, scroll, pathProfile);
                        }
                    }

                    Debug.LogWarning($"[CameraFrameResolver] Failed to resolve target '{scriptParams.TargetId}' for {scriptParams.ReferenceTarget}, fallback to Player.");
                    return ResolvePlayerFrame(scriptParams, playerAnchor, scroll, pathProfile);
                }

                case CameraReferenceTarget.WorldFixed:
                    return new FixedWorldFrame(scriptParams.FixedWorldPosition);

                default:
                    return ResolvePlayerFrame(scriptParams, playerAnchor, scroll, pathProfile);
            }
        }

        private static ICameraFrameProvider ResolvePlayerFrame(
            CameraScriptParams scriptParams,
            PlayerAnchorController playerAnchor,
            ScrollController scroll,
            PathProfile pathProfile)
        {
            if (playerAnchor == null)
            {
                return null;
            }

            switch (scriptParams.FrameMode)
            {
                case CameraFrameMode.WorldAxes:
                    return new TargetTransformFrame(playerAnchor.transform, CameraFrameMode.WorldAxes);
                case CameraFrameMode.TargetForward:
                    return new TargetTransformFrame(playerAnchor.transform, CameraFrameMode.TargetForward);
                case CameraFrameMode.SplineAxes:
                default:
                    return new SplineCameraFrame(playerAnchor);
            }
        }
    }
}
