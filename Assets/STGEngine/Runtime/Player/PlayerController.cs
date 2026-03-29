using System.Collections.Generic;
using UnityEngine;
using STGEngine.Runtime.Bullet;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家控制器。处理输入、移动、碰撞检测集成。
    /// 
    /// 核心模型：WASD 移动的是摄像头，玩家球体始终位于屏幕固定位置
    /// （视角中心向下偏一定角度、固定距离处）。球体世界位置由摄像头推算。
    /// 
    /// 移动方向（参考操作逻辑设计文档）：
    /// - WASD：主导平面移动（相对摄像头视角的上下左右）
    /// - Space：次要轴前进（摄像头 forward 方向）
    /// - Shift：次要轴后退
    /// - 左Ctrl：低速模式（精确闪避）
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
        public event System.Action OnPlayerHit;
        public event System.Action<int> OnGraze;
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

            // 摄像头初始化：用当前摄像头位置作为起点
            if (camera != null)
                camera.Initialize(camera.transform.position);

            // 球体初始位置由摄像头推算
            var startPos = camera != null ? camera.ComputePlayerWorldPos() : transform.position;
            _state = new PlayerState
            {
                Position = startPos,
                HitboxRadius = _hitboxRadius,
                GrazeRadius = _grazeRadius
            };
            transform.position = startPos;
        }

        private void Update()
        {
            GatherInput();
        }

        /// <summary>
        /// 逻辑 tick。移动摄像头，球体位置由摄像头推算。
        /// </summary>
        public void FixedTick(float dt)
        {
            if (_state == null) return;

            // ── 移动摄像头 ──
            float speed = _state.IsSlow ? _moveSpeed * _slowMultiplier : _moveSpeed;
            if (_playerCamera != null)
            {
                _playerCamera.MoveCamera(_inputDirection * speed * dt);
                // 球体位置由摄像头推算（始终在屏幕固定位置）
                _state.Position = _playerCamera.ComputePlayerWorldPos();
            }
            else
            {
                _state.Position += _inputDirection * speed * dt;
            }
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

            _state.IsSlow = Input.GetKey(KeyCode.LeftControl);

            float h = 0f, v = 0f;
            if (Input.GetKey(KeyCode.A)) h -= 1f;
            if (Input.GetKey(KeyCode.D)) h += 1f;
            if (Input.GetKey(KeyCode.W)) v += 1f;
            if (Input.GetKey(KeyCode.S)) v -= 1f;

            float depth = 0f;
            if (Input.GetKey(KeyCode.Space)) depth += 1f;
            if (Input.GetKey(KeyCode.LeftShift)) depth -= 1f;

            if (_playerCamera != null)
            {
                _inputDirection = _playerCamera.ViewRight * h
                                + _playerCamera.ViewUp * v
                                + _playerCamera.ViewForward * depth;
            }
            else
            {
                _inputDirection = Vector3.right * h + Vector3.up * v + Vector3.forward * depth;
            }

            if (_inputDirection.sqrMagnitude > 1f)
                _inputDirection.Normalize();
        }

        private void OnDrawGizmos()
        {
            if (_state == null) return;
            Gizmos.color = new Color(1f, 0.2f, 0.2f, _hitboxVisualAlpha);
            Gizmos.DrawWireSphere(transform.position, _state.HitboxRadius);
            Gizmos.color = new Color(1f, 1f, 0.3f, _hitboxVisualAlpha * 0.5f);
            Gizmos.DrawWireSphere(transform.position, _state.GrazeRadius);
        }
    }
}
