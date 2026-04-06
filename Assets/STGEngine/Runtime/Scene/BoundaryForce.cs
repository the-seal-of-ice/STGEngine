using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 软边界力场。当玩家接近通路边缘时施加渐变推力，
    /// 柔和地约束玩家在通路内活动。不是硬墙，而是阻力渐增。
    /// 边界宽度取玩家前后一段距离内的最窄值，防止在宽窄交界处穿过障碍物。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/BoundaryForce")]
    public class BoundaryForce : MonoBehaviour
    {
        [Header("Lateral Boundary (Left/Right)")]
        [SerializeField, Tooltip("横向自由区比例（有效宽度的多少比例内无推力）")]
        private float _innerRatio = 0.95f;

        [SerializeField, Tooltip("横向最大推力强度（m/s）")]
        private float _lateralMaxForce = 15f;

        [SerializeField, Tooltip("横向推力指数")]
        private float _lateralExponent = 3f;

        [Header("Lookahead")]
        [SerializeField, Tooltip("前后采样距离（米），取此范围内最窄宽度作为边界")]
        private float _lookaheadDistance = 30f;

        [SerializeField, Tooltip("前后采样点数")]
        private int _lookaheadSamples = 5;

        [Header("Vertical Boundary (Up/Down)")]
        [SerializeField, Tooltip("地面以下推力（强，防止穿地）")]
        private float _groundForce = 200f;

        [SerializeField, Tooltip("上方自由区高度（米）")]
        private float _ceilingHeight = 15f;

        [SerializeField, Tooltip("上方推力强度")]
        private float _ceilingForce = 20f;

        [Header("Hard Limits")]
        [SerializeField, Tooltip("横向硬限制倍率（相对于有效半宽的倍数）")]
        private float _hardLimitRatio = 1.15f;

        private PlayerAnchorController _player;
        private ChunkGenerator _generator;
        private bool _initialized;

        /// <summary>初始化，绑定到玩家控制器和生成器。</summary>
        public void Initialize(PlayerAnchorController player, ChunkGenerator generator)
        {
            _player = player;
            _generator = generator;
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized || _player == null || _generator == null) return;

            // 取玩家前后一段距离内的最窄宽度，防止宽窄交界处穿过障碍物
            float effectiveWidth = GetMinWidthInRange();
            float halfWidth = effectiveWidth * 0.5f;
            float freeHalfW = halfWidth * _innerRatio;

            Vector2 offset = _player.LocalOffset;
            Vector2 force = Vector2.zero;

            // --- 横向边界（左右）---
            if (Mathf.Abs(offset.x) > freeHalfW)
            {
                float maxDist = halfWidth * _hardLimitRatio;
                float range = maxDist - freeHalfW;
                if (range > 0.01f)
                {
                    float depth = (Mathf.Abs(offset.x) - freeHalfW) / range;
                    depth = Mathf.Clamp01(depth);
                    float strength = Mathf.Pow(depth, _lateralExponent) * _lateralMaxForce;
                    force.x = -Mathf.Sign(offset.x) * strength;
                }
            }

            // --- 地面约束（Y 下边界）---
            float groundLevel = 0.8f; // 玩家球体半径
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

            // 硬限制
            Vector2 clamped = _player.LocalOffset;
            float hardLimit = halfWidth * _hardLimitRatio;
            clamped.x = Mathf.Clamp(clamped.x, -hardLimit, hardLimit);
            clamped.y = Mathf.Clamp(clamped.y, groundLevel - 0.1f, _ceilingHeight + 5f);
            _player.LocalOffset = clamped;
        }

        /// <summary>
        /// 在玩家前后 _lookaheadDistance 范围内采样，返回最窄的通路宽度。
        /// 确保边界不会比前方即将到达的窄通路更宽。
        /// </summary>
        private float GetMinWidthInRange()
        {
            float playerDist = _generator.Scroll.TotalScrolled;
            var profile = _player.CurrentAnchor;
            float minWidth = profile.Width;

            float splineLen = _generator.PlayerAnchor.Width; // fallback
            // 实际采样
            for (int i = 0; i <= _lookaheadSamples; i++)
            {
                float t = (float)i / _lookaheadSamples;
                float sampleDist = playerDist - _lookaheadDistance * 0.3f + _lookaheadDistance * t;
                if (sampleDist < 0f) continue;

                var sample = _generator.Scroll.CurrentSpeed > 0.01f
                    ? GetProfileSample(sampleDist)
                    : profile;

                if (sample.Width < minWidth)
                    minWidth = sample.Width;
            }

            return minWidth;
        }

        private PathSample GetProfileSample(float dist)
        {
            // 通过 PlayerAnchorController 的 CurrentAnchor 获取 profile 引用不方便，
            // 直接从 ChunkGenerator 获取
            return _generator.GetProfileSample(dist);
        }
    }
}
