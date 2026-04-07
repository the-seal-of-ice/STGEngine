using UnityEngine;
using STGEngine.Core.Scene;
using STGEngine.Runtime.Player;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// 编辑器环境的相机坐标标架。
    /// 使用固定世界轴方向（编辑器中无样条线弯曲）。
    /// </summary>
    public class EditorCameraFrame : ICameraFrameProvider
    {
        private readonly FreeCameraController _freeCam;
        private IPlayerProvider _player;

        public EditorCameraFrame(FreeCameraController freeCam)
        {
            _freeCam = freeCam;
        }

        /// <summary>设置活跃玩家（Player 模式进入时调用，退出时传 null）。</summary>
        public void SetPlayer(IPlayerProvider player) => _player = player;

        public Vector3 PlayerWorldPosition =>
            _player != null && _player.IsActive
                ? _player.Position
                : (_freeCam != null ? _freeCam.Pivot : Vector3.zero);

        public Vector3 FrameRight => Vector3.right;
        public Vector3 FrameUp => Vector3.up;
        public Vector3 FrameForward => Vector3.forward;
    }
}
