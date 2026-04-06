using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 软边界力场。当玩家接近通路边缘时施加渐变推力，
    /// 柔和地约束玩家在通路内活动。不是硬墙，而是阻力渐增。
    /// 同时将玩家约束在地面附近（Y 方向软边界）。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/BoundaryForce")]
    public class BoundaryForce : MonoBehaviour
    {
        [Header("Lateral Boundary (Left/Right)")]
        [SerializeField, Tooltip("横向自由区比例（通路宽度的多少比例内无推力）")]
        private float _innerRatio = 0.95f;

        [SerializeField, Tooltip("横向最大推力强度（m/s）")]
        private float _lateralMaxForce = 15f;

        [SerializeField, Tooltip("横向推力指数")]
        private float _lateralExponent = 3f;

        [Header("Vertical Boundary (Up/Down)")]
        [SerializeField, Tooltip("地面以下推力（强，防止穿地）")]
        private float _groundForce = 50f;

        [SerializeField, Tooltip("上方自由区高度（米）")]
        private float _ceilingHeight = 15f;

        [SerializeField, Tooltip("上方推力强度")]
        private float _ceilingForce = 20f;

        [Header("Hard Limits")]
        [SerializeField, Tooltip("横向硬限制倍率（相对于通路半宽的倍数）")]
        private float _hardLimitRatio = 1.3f;

        private PlayerAnchorController _player;
        private bool _initialized;

        /// <summary>初始化，绑定到玩家控制器。</summary>
        public void Initialize(PlayerAnchorController player)
        {
            _player = player;
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized || _player == null) return;

            var anchor = _player.CurrentAnchor;
            float halfWidth = anchor.Width * 0.5f;
            float freeHalfW = halfWidth * _innerRatio;

            Vector2 offset = _player.LocalOffset;
            Vector2 force = Vector2.zero;

            // --- 横向边界（左右）---
            if (Mathf.Abs(offset.x) > freeHalfW)
            {
                float maxDist = halfWidth * _hardLimitRatio;
                float depth = (Mathf.Abs(offset.x) - freeHalfW) / (maxDist - freeHalfW);
                depth = Mathf.Clamp01(depth);
                float strength = Mathf.Pow(depth, _lateralExponent) * _lateralMaxForce;
                force.x = -Mathf.Sign(offset.x) * strength;
            }

            // --- 地面约束（Y 下边界）---
            if (offset.y < 0f)
            {
                // 地面以下：强推力把玩家推回地面
                force.y = -offset.y * _groundForce;
            }
            else if (offset.y > _ceilingHeight)
            {
                // 天花板：柔和推回
                float depth = (offset.y - _ceilingHeight) / 5f;
                force.y = -Mathf.Min(depth, 1f) * _ceilingForce;
            }

            // 应用推力
            if (force.sqrMagnitude > 0f)
            {
                _player.LocalOffset += force * Time.deltaTime;
            }

            // 硬限制
            Vector2 clamped = _player.LocalOffset;
            float hardLimit = halfWidth * _hardLimitRatio;
            clamped.x = Mathf.Clamp(clamped.x, -hardLimit, hardLimit);
            clamped.y = Mathf.Clamp(clamped.y, -0.5f, _ceilingHeight + 5f);
            _player.LocalOffset = clamped;
        }
    }
}
