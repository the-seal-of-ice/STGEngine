using System.Collections.Generic;
using UnityEngine;
using STGEngine.Runtime.Bullet;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家控制器。WASD 直接移动球体，摄像头从动跟随。
    /// 
    /// 移动方向（参考操作逻辑设计文档）：
    /// - WASD：主导平面移动（相对摄像头视角的上下左右）
    /// - Space：次要轴前进（摄像头 forward 方向）
    /// - Shift：次要轴后退
    /// - 左Ctrl：低速模式（精确闪避）
    /// </summary>
    [AddComponentMenu("STGEngine/Player Controller")]
    public class PlayerController : MonoBehaviour, IPlayerProvider
    {
        [Header("移动")]
        [SerializeField] private float _moveSpeed = 8f;
        [SerializeField] private float _slowMultiplier = 0.33f;

        [Header("判定")]
        [SerializeField] private float _hitboxRadius = 0.15f;
        [SerializeField] private float _grazeRadius = 0.8f;

        [Header("视觉")]
        [SerializeField] private float _hitboxVisualAlpha = 0.4f;

        private PlayerState _state;
        private PlayerCamera _playerCamera;
        private Vector3 _inputDirection;

        private System.Func<IReadOnlyList<BulletState>> _bulletStateProvider;
        private float _bulletCollisionRadius = 0.1f;

        public event System.Action OnPlayerHit;
        public event System.Action<int> OnGraze;
        public event System.Action OnPlayerDeath;

        public PlayerState State => _state;
        public PlayerCamera Camera => _playerCamera;

        // ── IPlayerProvider ──
        Vector3 IPlayerProvider.Position => _state?.Position ?? transform.position;
        Vector3 IPlayerProvider.Forward => _playerCamera != null ? _playerCamera.ViewForward : transform.forward;
        bool IPlayerProvider.IsActive => _state != null && enabled;

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

            // 摄像头跟随球体
            if (camera != null)
                camera.SetTarget(transform);
        }

        private void Update()
        {
            GatherInput();
        }

        /// <summary>
        /// 逻辑 tick。移动球体，摄像头在 LateUpdate 中自动跟随。
        /// </summary>
        public void FixedTick(float dt)
        {
            if (_state == null) return;

            // ── 移动球体 ──
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
