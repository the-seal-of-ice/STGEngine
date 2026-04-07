using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 程序化场景的相机坐标标架。
    /// 使用样条线 Frenet 标架（Tangent/Normal/Up），在弯道处自然旋转。
    /// </summary>
    public class SplineCameraFrame : ICameraFrameProvider
    {
        private readonly PlayerAnchorController _anchor;

        public SplineCameraFrame(PlayerAnchorController anchor)
        {
            _anchor = anchor;
        }

        public Vector3 PlayerWorldPosition => _anchor.WorldPosition;

        /// <summary>Right = 样条线法线方向（通路右侧）。</summary>
        public Vector3 FrameRight => _anchor.CurrentAnchor.Normal;

        public Vector3 FrameUp => Vector3.up;

        /// <summary>Forward = 样条线切线方向（通路前进方向）。</summary>
        public Vector3 FrameForward => _anchor.CurrentAnchor.Tangent;
    }
}
