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
        [Header("Lateral Boundary (Left/Right)")]
        [SerializeField, Tooltip("横向自由区比例（通路宽度的多少比例内无推力）")]
        private float _innerRatio = 0.9f;

        [SerializeField, Tooltip("横向最大推力强度（m/s）")]
        private float _lateralMaxForce = 40f;

        [SerializeField, Tooltip("横向推力指数（越大边缘越硬）")]
        private float _lateralExponent = 2f;

        [Header("Vertical Boundary (Up/Down)")]
        [SerializeField, Tooltip("地面以下推力（强，防止穿地）")]
        private float _groundForce = 200f;

        [SerializeField, Tooltip("上方自由区高度（米）")]
        private float _ceilingHeight = 15f;

        [SerializeField, Tooltip("上方推力强度")]
        private float _ceilingForce = 20f;

        private PlayerAnchorController _player;
        private bool _initialized;

        /// <summary>初始化，绑定到玩家控制器。</summary>
        public void Initialize(PlayerAnchorController player, ChunkGenerator generator)
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
            float absX = Mathf.Abs(offset.x);
            if (absX > freeHalfW)
            {
                // 从自由区边缘到通路边缘的归一化深度
                float edgeZone = halfWidth - freeHalfW;
                if (edgeZone > 0.01f)
                {
                    float depth = Mathf.Clamp01((absX - freeHalfW) / edgeZone);
                    float strength = Mathf.Pow(depth, _lateralExponent) * _lateralMaxForce;
                    force.x = -Mathf.Sign(offset.x) * strength;
                }
            }

            // --- 地面约束（Y 下边界）---
            float groundLevel = 0.8f;
            if (offset.y < groundLevel)
            {
                force.y = (groundLevel - offset.y) * _groundForce;
            }
            else if (offset.y > _ceilingHeight)
            {
                float depth = (offset.y - _ceilingHeight) / 5f;
                force.y = -Mathf.Min(depth, 1f) * _ceilingForce;
            }

            // 应用推力
            if (force.sqrMagnitude > 0f)
            {
                _player.LocalOffset += force * Time.deltaTime;
            }

            // 硬限制：通路边缘就是硬墙
            Vector2 clamped = _player.LocalOffset;
            clamped.x = Mathf.Clamp(clamped.x, -halfWidth, halfWidth);
            clamped.y = Mathf.Clamp(clamped.y, groundLevel - 0.1f, _ceilingHeight + 5f);
            _player.LocalOffset = clamped;
        }
    }
}
