using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 固定世界坐标的相机坐标标架。使用世界轴。
    /// </summary>
    public class FixedWorldFrame : ICameraFrameProvider
    {
        private readonly Vector3 _position;

        public FixedWorldFrame(Vector3 position)
        {
            _position = position;
        }

        public Vector3 PlayerWorldPosition => _position;
        public Vector3 FrameRight => Vector3.right;
        public Vector3 FrameUp => Vector3.up;
        public Vector3 FrameForward => Vector3.forward;
    }
}
