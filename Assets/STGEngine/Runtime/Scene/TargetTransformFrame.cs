using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 跟踪指定 Transform 的相机坐标标架。
    /// 用于 Boss、Enemy 等目标。标架模式可配置。
    /// </summary>
    public class TargetTransformFrame : ICameraFrameProvider
    {
        private readonly Transform _target;
        private readonly CameraFrameMode _frameMode;
        private readonly ScrollController _scroll;
        private readonly PathProfile _pathProfile;

        private PathSample _cachedSample;
        private float _cachedDistance = -1f;

        /// <summary>
        /// 创建跟踪指定 Transform 的 frame provider。
        /// </summary>
        /// <param name="target">跟踪目标。</param>
        /// <param name="frameMode">标架模式。</param>
        /// <param name="scroll">SplineAxes 模式需要的滚动控制器（其他模式可为 null）。</param>
        /// <param name="pathProfile">SplineAxes 模式需要的路径配置（其他模式可为 null）。</param>
        public TargetTransformFrame(
            Transform target,
            CameraFrameMode frameMode,
            ScrollController scroll = null,
            PathProfile pathProfile = null)
        {
            _target = target;
            _frameMode = frameMode;
            _scroll = scroll;
            _pathProfile = pathProfile;
        }

        public Vector3 PlayerWorldPosition =>
            _target != null ? _target.position : Vector3.zero;

        public Vector3 FrameRight
        {
            get
            {
                switch (_frameMode)
                {
                    case CameraFrameMode.TargetForward:
                        return _target != null ? _target.right : Vector3.right;
                    case CameraFrameMode.SplineAxes:
                        return HasSplineData() ? GetSample().Normal : Vector3.right;
                    case CameraFrameMode.WorldAxes:
                    default:
                        return Vector3.right;
                }
            }
        }

        public Vector3 FrameUp => Vector3.up;

        public Vector3 FrameForward
        {
            get
            {
                switch (_frameMode)
                {
                    case CameraFrameMode.TargetForward:
                        return _target != null ? _target.forward : Vector3.forward;
                    case CameraFrameMode.SplineAxes:
                        return HasSplineData() ? GetSample().Tangent : Vector3.forward;
                    case CameraFrameMode.WorldAxes:
                    default:
                        return Vector3.forward;
                }
            }
        }

        private bool HasSplineData()
        {
            return _scroll != null && _pathProfile != null;
        }

        private PathSample GetSample()
        {
            float dist = _scroll.TotalScrolled;
            if (dist != _cachedDistance)
            {
                _cachedSample = _pathProfile.SampleAt(dist);
                _cachedDistance = dist;
            }

            return _cachedSample;
        }
    }
}
