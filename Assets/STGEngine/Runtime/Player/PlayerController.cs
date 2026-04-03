using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Preview;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家控制器。Profile 驱动，含死亡/复活状态机与 Bomb 逻辑。
    ///
    /// 移动方向（参考操作逻辑设计文档）：
    /// - WASD：主导平面移动（相对摄像头视角的上下左右）
    /// - Space：次要轴前进（摄像头 forward 方向）
    /// - Shift：次要轴后退
    /// - 左Ctrl：低速模式（精确闪避）
    /// - 鼠标右键：Bomb
    /// </summary>
    [AddComponentMenu("STGEngine/Player Controller")]
    public class PlayerController : MonoBehaviour, IPlayerProvider
    {
        [Header("视觉")]
        [SerializeField] private float _hitboxVisualAlpha = 0.4f;

        private PlayerProfile _profile;
        private PlayerState _state;
        private PlayerCamera _playerCamera;
        private Vector3 _inputDirection;

        private Vector3 _boundaryMin;
        private Vector3 _boundaryMax;

        // 状态机
        private enum PlayerPhase { Normal, Dying, Respawning, Dead }
        private PlayerPhase _phase = PlayerPhase.Normal;
        private float _dyingTimer;
        private const float DyingDuration = 0.5f;

        private System.Func<IReadOnlyList<BulletState>> _bulletStateProvider;
        private float _bulletCollisionRadius = 0.1f;

        public event System.Action OnPlayerHit;
        public event System.Action<int> OnGraze;
        public event System.Action OnPlayerDeath;
        public event System.Action OnBomb;
        public event System.Action OnRespawnClearBullets;

        public PlayerState State => _state;
        public PlayerCamera Camera => _playerCamera;

        // ── IPlayerProvider ──
        Vector3 IPlayerProvider.Position => _state?.Position ?? transform.position;
        Vector3 IPlayerProvider.Forward => _playerCamera != null ? _playerCamera.ViewForward : transform.forward;
        bool IPlayerProvider.IsActive => _state != null && enabled;

        public void Initialize(PlayerProfile profile, PlayerCamera camera,
            System.Func<IReadOnlyList<BulletState>> bulletProvider = null,
            float bulletRadius = 0.1f)
        {
            _profile = profile;
            _playerCamera = camera;
            _bulletStateProvider = bulletProvider;
            _bulletCollisionRadius = bulletRadius;

            _state = PlayerState.FromProfile(profile, transform.position);
            _phase = PlayerPhase.Normal;

            // 边界
            var boundary = FindAnyObjectByType<SandboxBoundary>();
            if (boundary != null)
            {
                var half = boundary.HalfExtents;
                _boundaryMin = -half;
                _boundaryMax = half;
            }
            else
            {
                var h = WorldScale.DefaultBoundaryHalf;
                _boundaryMin = new Vector3(-h, -h, -h);
                _boundaryMax = new Vector3(h, h, h);
            }

            // 摄像头跟随球体
            if (camera != null)
                camera.SetTarget(transform);
        }

        private void Update()
        {
            GatherInput();
        }

        /// <summary>
        /// 逻辑 tick。状态机驱动移动、碰撞、死亡/复活。
        /// </summary>
        public void FixedTick(float dt)
        {
            if (_state == null) return;

            switch (_phase)
            {
                case PlayerPhase.Normal:
                    TickNormal(dt);
                    break;

                case PlayerPhase.Dying:
                    _dyingTimer -= dt;
                    if (_dyingTimer <= 0f)
                        TransitionToRespawnOrDead();
                    break;

                case PlayerPhase.Respawning:
                    TickNormal(dt);
                    if (_phase == PlayerPhase.Respawning && !_state.IsInvincible)
                        _phase = PlayerPhase.Normal;
                    break;

                case PlayerPhase.Dead:
                    break;
            }
        }

        private void TickNormal(float dt)
        {
            // ── 移动 ──
            float speed = _state.IsSlow
                ? _profile.MoveSpeed * _profile.SlowMultiplier
                : _profile.MoveSpeed;
            _state.Position += _inputDirection * speed * dt;

            // ── 边界钳制 ──
            _state.Position = Vector3.Max(_boundaryMin, Vector3.Min(_boundaryMax, _state.Position));
            transform.position = _state.Position;

            // ── 计时器 ──
            _state.TickInvincibility(dt);
            _state.TickBomb(dt);

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
                        EnterDying();
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

        private void EnterDying()
        {
            _state.OnHit();
            OnPlayerHit?.Invoke();

            // Power loss
            _state.Power = Mathf.Max(0f, _state.Power - _profile.DeathPowerLoss);

            if (_state.IsDead)
            {
                _phase = PlayerPhase.Dead;
                OnPlayerDeath?.Invoke();
                return;
            }

            _phase = PlayerPhase.Dying;
            _dyingTimer = DyingDuration;
        }

        private void TransitionToRespawnOrDead()
        {
            _phase = PlayerPhase.Respawning;
            _state.Position = Vector3.zero;
            transform.position = _state.Position;
            _state.IsInvincible = true;
            _state.InvincibleTimer = _state.RespawnInvincibleDuration;
            OnRespawnClearBullets?.Invoke();
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

            // Bomb（鼠标右键）
            if (Input.GetMouseButtonDown(1) && _state.Bombs > 0 && !_state.IsBombing
                && _phase == PlayerPhase.Normal)
            {
                _state.Bombs--;
                _state.IsBombing = true;
                _state.BombTimer = _state.BombDuration;
                _state.IsInvincible = true;
                _state.InvincibleTimer = _state.BombInvincibleDuration;
                OnBomb?.Invoke();
            }
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
