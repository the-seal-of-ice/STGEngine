using System.Collections.Generic;
using UnityEngine;
using STGEngine.Runtime.Bullet;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家控制器。处理输入、移动、碰撞检测集成。
    /// 
    /// 移动模型（参考操作逻辑设计文档）：
    /// - WASD：主导平面移动（相对摄像头视角的上下左右）
    /// - Space：次要轴前进（摄像头 forward 方向）
    /// - Shift：次要轴后退
    /// - 左Ctrl：低速模式（精确闪避）
    /// 
    /// 碰撞检测在 FixedTick 中执行，与 SimulationLoop 同步。
    /// </summary>
    [AddComponentMenu("STGEngine/Player Controller")]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        [SerializeField] private float _moveSpeed = 8f;
        [SerializeField] private float _slowMultiplier = 0.33f;

        [Header("判定")]
        [SerializeField] private float _hitboxRadius = 0.15f;
        [SerializeField] private float _grazeRadius = 0.8f;

        [Header("视觉")]
        [SerializeField] private float _hitboxVisualAlpha = 0.4f;

        // ── 运行时状态 ──
        private PlayerState _state;
        private PlayerCamera _playerCamera;
        private Vector3 _inputDirection;

        // ── 碰撞数据源（由外部注入） ──
        private System.Func<IReadOnlyList<BulletState>> _bulletStateProvider;
        private float _bulletCollisionRadius = 0.1f;

        // ── 事件 ──
        /// <summary>被弹时触发。</summary>
        public event System.Action OnPlayerHit;
        /// <summary>擦弹时触发（参数：本帧擦弹数）。</summary>
        public event System.Action<int> OnGraze;
        /// <summary>玩家死亡（Lives <= 0）时触发。</summary>
        public event System.Action OnPlayerDeath;

        public PlayerState State => _state;
        public PlayerCamera Camera => _playerCamera;

        /// <summary>
        /// 初始化玩家。由场景管理器调用。
        /// </summary>
        public void Initialize(PlayerCamera camera,
            System.Func<IReadOnlyList<BulletState>> bulletProvider = null,
            float bulletRadius = 0.1f)
        {
            _playerCamera = camera;
            _bulletStateProvider = bulletProvider;
            _bulletCollisionRadius = bulletRadius;

            _state = new PlayerState
            {
                Position = transform.position,
                HitboxRadius = _hitboxRadius,
                GrazeRadius = _grazeRadius
            };

            if (camera != null)
                camera.SetTarget(transform);
        }

        private void Update()
        {
            GatherInput();
        }

        /// <summary>
        /// 逻辑 tick，由 SimulationLoop 的 stepAction 调用。
        /// 在固定时间步中执行移动和碰撞检测。
        /// </summary>
        public void FixedTick(float dt)
        {
            if (_state == null) return;

            // ── 移动 ──
            float speed = _state.IsSlow ? _moveSpeed * _slowMultiplier : _moveSpeed;
            _state.Position += _inputDirection * speed * dt;
            transform.position = _state.Position;

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
                        if (_state.Lives <= 0)
                            OnPlayerDeath?.Invoke();
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

        /// <summary>收集本帧输入，转换为世界空间方向向量。</summary>
        private void GatherInput()
        {
            if (_state == null) return;

            // 低速模式
            _state.IsSlow = Input.GetKey(KeyCode.LeftControl);

            // 主导平面（WASD → 相对摄像头的上下左右）
            float h = 0f, v = 0f;
            if (Input.GetKey(KeyCode.A)) h -= 1f;
            if (Input.GetKey(KeyCode.D)) h += 1f;
            if (Input.GetKey(KeyCode.W)) v += 1f;
            if (Input.GetKey(KeyCode.S)) v -= 1f;

            // 次要轴（Space/Shift → 摄像头 forward 方向）
            float depth = 0f;
            if (Input.GetKey(KeyCode.Space)) depth += 1f;
            if (Input.GetKey(KeyCode.LeftShift)) depth -= 1f;

            // 构建世界空间方向
            if (_playerCamera != null)
            {
                _inputDirection = _playerCamera.ViewRight * h
                                + _playerCamera.ViewUp * v
                                + _playerCamera.ViewForward * depth;
            }
            else
            {
                // Fallback：无摄像头时用世界坐标
                _inputDirection = Vector3.right * h + Vector3.up * v + Vector3.forward * depth;
            }

            if (_inputDirection.sqrMagnitude > 1f)
                _inputDirection.Normalize();
        }

        // ── Gizmos：判定点可视化 ──

        private void OnDrawGizmos()
        {
            if (_state == null) return;

            // 被弹判定（红色小球）
            Gizmos.color = new Color(1f, 0.2f, 0.2f, _hitboxVisualAlpha);
            Gizmos.DrawWireSphere(transform.position, _state.HitboxRadius);

            // 擦弹判径（黄色）
            Gizmos.color = new Color(1f, 1f, 0.3f, _hitboxVisualAlpha * 0.5f);
            Gizmos.DrawWireSphere(transform.position, _state.GrazeRadius);
        }
    }
}
