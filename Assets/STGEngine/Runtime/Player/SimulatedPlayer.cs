using System.Collections.Generic;
using UnityEngine;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Preview;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// AI 模拟玩家。实现 IPlayerProvider，可无缝替代 PlayerController。
    /// 
    /// 由 RandomWalkBrain 驱动移动决策，复用 PlayerState 和 CollisionSystem。
    /// 种子+参数即可完美重放轨迹。
    /// 
    /// 视觉：球体 + 立体箭头（Cone）指示朝向。
    /// </summary>
    [AddComponentMenu("STGEngine/Simulated Player")]
    public class SimulatedPlayer : MonoBehaviour, IPlayerProvider
    {
        [Header("移动")]
        [SerializeField] private float _moveSpeed = 6f;

        [Header("判定")]
        [SerializeField] private float _hitboxRadius = 0.15f;
        [SerializeField] private float _grazeRadius = 0.8f;

        // ── AI 决策 ──
        private RandomWalkBrain _brain;
        private PlayerState _state;
        private Vector3 _forward = Vector3.forward;

        // ── 碰撞数据源 ──
        private System.Func<IReadOnlyList<BulletState>> _bulletStateProvider;
        private float _bulletCollisionRadius = 0.1f;

        // ── 场景边界 ──
        private Vector3 _boundaryMin = new(-10f, -10f, -10f);
        private Vector3 _boundaryMax = new(10f, 10f, 10f);

        // ── 视觉 ──
        private Transform _arrowTransform;

        // ── 事件 ──
        public event System.Action OnPlayerHit;
        public event System.Action<int> OnGraze;

        // ── IPlayerProvider ──
        public Vector3 Position => _state?.Position ?? transform.position;
        public Vector3 Forward => _forward;
        public PlayerState State => _state;
        public bool IsActive => _state != null && enabled;

        /// <summary>AI 决策引擎，暴露以便编辑器调整参数。</summary>
        public RandomWalkBrain Brain => _brain;

        /// <summary>
        /// 初始化 AI 玩家。
        /// </summary>
        public void Initialize(
            RandomWalkBrain brain,
            System.Func<IReadOnlyList<BulletState>> bulletProvider = null,
            float bulletRadius = 0.1f)
        {
            _brain = brain;
            _bulletStateProvider = bulletProvider;
            _bulletCollisionRadius = bulletRadius;

            _state = new PlayerState
            {
                Position = transform.position,
                HitboxRadius = _hitboxRadius,
                GrazeRadius = _grazeRadius
            };

            // 查找场景边界
            var boundary = FindAnyObjectByType<SandboxBoundary>();
            if (boundary != null)
            {
                var center = boundary.transform.position;
                var half = boundary.HalfExtents;
                _boundaryMin = center - half;
                _boundaryMax = center + half;
            }

            _brain.Initialize();
            BuildVisual();
        }

        /// <summary>逻辑 tick。AI 决策 → 移动 → 碰撞检测。</summary>
        public void FixedTick(float dt)
        {
            if (_state == null || _brain == null) return;

            // ── AI 决策 ──
            var moveDir = _brain.Tick(_state.Position, _boundaryMin, _boundaryMax, dt);

            // ── 移动 ──
            _state.Position += moveDir * _moveSpeed * dt;

            // 边界 clamp
            _state.Position = Vector3.Max(_boundaryMin, Vector3.Min(_boundaryMax, _state.Position));

            transform.position = _state.Position;

            // 朝向：移动方向（有移动时更新，停顿时保持上一帧）
            if (moveDir.sqrMagnitude > 0.01f)
                _forward = moveDir.normalized;
            UpdateArrow();

            // ── 无敌计时 ──
            _state.TickInvincibility(dt);

            // ── 碰撞检测 ──
            _state.GrazeThisFrame = 0;
            if (_bulletStateProvider != null)
            {
                var bullets = _bulletStateProvider();
                if (bullets != null && bullets.Count > 0)
                {
                    var result = CollisionSystem.Check(
                        _state.Position,
                        _state.HitboxRadius,
                        _state.GrazeRadius,
                        bullets,
                        _bulletCollisionRadius,
                        _state.IsInvincible);

                    if (result.Hit)
                    {
                        _state.OnHit();
                        OnPlayerHit?.Invoke();
                    }

                    if (result.GrazeCount > 0)
                    {
                        _state.GrazeThisFrame = result.GrazeCount;
                        _state.GrazeTotal += result.GrazeCount;
                        OnGraze?.Invoke(result.GrazeCount);
                    }
                }
            }
        }

        // ── 视觉构建 ──

        private void BuildVisual()
        {
            // 球体
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(transform);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * 0.4f;
            var col = sphere.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            var sphereRend = sphere.GetComponent<Renderer>();
            if (sphereRend != null)
                sphereRend.material.color = new Color(0.2f, 0.9f, 0.8f); // 青色，区分真人蓝色

            // 箭头（Cone = 缩放的 Cylinder）
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arrow.transform.SetParent(transform);
            arrow.transform.localScale = new Vector3(0.12f, 0.3f, 0.12f);
            arrow.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            arrow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var arrowCol = arrow.GetComponent<Collider>();
            if (arrowCol != null) DestroyImmediate(arrowCol);
            var arrowRend = arrow.GetComponent<Renderer>();
            if (arrowRend != null)
                arrowRend.material.color = new Color(1f, 0.8f, 0.2f); // 黄色箭头

            _arrowTransform = arrow.transform;
        }

        private void UpdateArrow()
        {
            if (_arrowTransform == null) return;
            // 箭头指向 _forward 方向
            var rot = Quaternion.LookRotation(_forward, Vector3.up);
            // Cylinder 默认沿 Y 轴，需要旋转 90° 使其沿 Z 轴
            _arrowTransform.rotation = rot * Quaternion.Euler(90f, 0f, 0f);
            _arrowTransform.position = transform.position + _forward * 0.35f;
        }

        private void OnDrawGizmos()
        {
            if (_state == null) return;
            // 被弹判定（红色）
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, _state.HitboxRadius);
            // 擦弹判定（黄色）
            Gizmos.color = new Color(1f, 1f, 0.3f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, _state.GrazeRadius);
            // 朝向线
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawRay(transform.position, _forward * 1.5f);
        }
    }
}
