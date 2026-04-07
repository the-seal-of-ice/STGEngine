using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 边界中心点的相机坐标标架。
    /// 原点 = 样条线当前弧长处的中心点 + 高度偏移。
    /// 标架 = 样条线 tangent/normal + 世界 up。
    /// </summary>
    public class BoundaryCenterFrame : ICameraFrameProvider
    {
        private readonly ScrollController _scroll;
        private readonly PathProfile _pathProfile;
        private readonly float _heightOffset;

        private PathSample _cachedSample;
        private float _cachedDistance = -1f;

        public BoundaryCenterFrame(ScrollController scroll, PathProfile pathProfile, float heightOffset)
        {
            _scroll = scroll;
            _pathProfile = pathProfile;
            _heightOffset = heightOffset;
        }

        public Vector3 PlayerWorldPosition
        {
            get
            {
                var sample = GetSample();
                return sample.Position + Vector3.up * _heightOffset;
            }
        }

        public Vector3 FrameRight => GetSample().Normal;

        public Vector3 FrameUp => Vector3.up;

        public Vector3 FrameForward => GetSample().Tangent;

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
