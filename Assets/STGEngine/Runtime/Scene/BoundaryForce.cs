using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 边界力场。基于障碍物位置拟合的平滑边界曲线做推力和硬限制。
    /// 边界形状贴合障碍物的实际分布，推力平滑无抖动。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/BoundaryForce")]
    public class BoundaryForce : MonoBehaviour
    {
        [Header("Boundary Push")]
        [SerializeField, Tooltip("边界推力开始生效的距离（米，从边界线算起向内）")]
        private float _pushRange = 3f;

        [SerializeField, Tooltip("最大推力强度（m/s）")]
        private float _pushForce = 20f;

        [SerializeField, Tooltip("推力指数")]
        private float _pushExponent = 2.5f;

        [Header("Safety")]
        [SerializeField, Tooltip("边界曲线内缩距离（米），硬限制在曲线内侧这么远")]
        private float _hardMargin = 1.5f;

        [Header("Vertical Boundary")]
        [SerializeField, Tooltip("地面推力强度")]
        private float _groundForce = 200f;

        [SerializeField, Tooltip("上方自由区高度（米）")]
        private float _ceilingHeight = 15f;

        [SerializeField, Tooltip("上方推力强度")]
        private float _ceilingForce = 20f;

        [Header("Rebuild")]
        [SerializeField, Tooltip("边界曲线重建间隔（秒）")]
        private float _rebuildInterval = 0.5f;

        private PlayerAnchorController _player;
        private ChunkGenerator _generator;
        private BoundaryCurveBuilder _curveBuilder;
        private float _rebuildTimer;
        private bool _initialized;

        public void Initialize(PlayerAnchorController player, ChunkGenerator generator)
        {
            _player = player;
            _generator = generator;
            _curveBuilder = new BoundaryCurveBuilder();
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized || _player == null || _generator == null) return;

            // 定期重建边界曲线
            _rebuildTimer -= Time.deltaTime;
            if (_rebuildTimer <= 0f)
            {
                _curveBuilder.Rebuild(_generator.ActiveChunks);
                _rebuildTimer = _rebuildInterval;
            }

            float playerDist = _generator.Scroll.TotalScrolled;
            var anchor = _player.CurrentAnchor;
            float fallbackHalfW = anchor.Width * 0.5f;

            // 查询当前弧长处的左右边界
            float leftBound = _curveBuilder.SampleAt(_curveBuilder.LeftBoundary, playerDist, fallbackHalfW);
            float rightBound = _curveBuilder.SampleAt(_curveBuilder.RightBoundary, playerDist, fallbackHalfW);

            Vector2 offset = _player.LocalOffset;
            Vector2 force = Vector2.zero;

            // --- 左侧边界（offset.x < 0）---
            float leftLimit = leftBound - _hardMargin;
            if (offset.x < 0f)
            {
                float distToBound = leftLimit - Mathf.Abs(offset.x);
                if (distToBound < _pushRange)
                {
                    float depth = 1f - Mathf.Clamp01(distToBound / _pushRange);
                    float strength = Mathf.Pow(depth, _pushExponent) * _pushForce;
                    force.x += strength; // 向右推
                }
            }

            // --- 右侧边界（offset.x > 0）---
            float rightLimit = rightBound - _hardMargin;
            if (offset.x > 0f)
            {
                float distToBound = rightLimit - offset.x;
                if (distToBound < _pushRange)
                {
                    float depth = 1f - Mathf.Clamp01(distToBound / _pushRange);
                    float strength = Mathf.Pow(depth, _pushExponent) * _pushForce;
                    force.x -= strength; // 向左推
                }
            }

            // --- 地面约束 ---
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

            // 硬限制
            Vector2 clamped = _player.LocalOffset;
            clamped.x = Mathf.Clamp(clamped.x, -leftLimit, rightLimit);
            clamped.y = Mathf.Clamp(clamped.y, groundLevel - 0.1f, _ceilingHeight + 5f);
            _player.LocalOffset = clamped;
        }
    }
}
