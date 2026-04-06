using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 软边界力场。当玩家接近通路边缘时施加渐变推力，
    /// 柔和地约束玩家在通路内活动。不是硬墙，而是阻力渐增。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/BoundaryForce")]
    public class BoundaryForce : MonoBehaviour
    {
        [Header("Boundary Settings")]
        [SerializeField, Tooltip("自由区比例（通路宽度的多少比例内无推力）")]
        private float _innerRatio = 0.8f;

        [SerializeField, Tooltip("最大推力强度（m/s）")]
        private float _maxForce = 30f;

        [SerializeField, Tooltip("推力指数（越大边缘越硬）")]
        private float _forceExponent = 2f;

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
            float halfHeight = anchor.Height * 0.5f;
            float freeHalfW = halfWidth * _innerRatio;
            float freeHalfH = halfHeight * _innerRatio;

            Vector2 offset = _player.LocalOffset;
            Vector2 force = Vector2.zero;

            // 横向边界
            if (Mathf.Abs(offset.x) > freeHalfW)
            {
                float depth = (Mathf.Abs(offset.x) - freeHalfW) / (halfWidth - freeHalfW);
                depth = Mathf.Clamp01(depth);
                float strength = Mathf.Pow(depth, _forceExponent) * _maxForce;
                force.x = -Mathf.Sign(offset.x) * strength;
            }

            // 纵向边界（上下）
            if (Mathf.Abs(offset.y) > freeHalfH)
            {
                float depth = (Mathf.Abs(offset.y) - freeHalfH) / (halfHeight - freeHalfH);
                depth = Mathf.Clamp01(depth);
                float strength = Mathf.Pow(depth, _forceExponent) * _maxForce;
                force.y = -Mathf.Sign(offset.y) * strength;
            }

            // 应用推力到玩家偏移
            if (force.sqrMagnitude > 0f)
            {
                _player.LocalOffset += force * Time.deltaTime;
            }

            // 硬限制：绝对不能超出通路边界
            Vector2 clamped = _player.LocalOffset;
            clamped.x = Mathf.Clamp(clamped.x, -halfWidth, halfWidth);
            clamped.y = Mathf.Clamp(clamped.y, -halfHeight, halfHeight);
            _player.LocalOffset = clamped;
        }
    }
}
