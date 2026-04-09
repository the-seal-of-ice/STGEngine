using System;
using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Random;

namespace STGEngine.Runtime.Player
{
    public struct PlayerBullet
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Damage;
        public float Radius;
        public bool Active;
        public float HomingStrength;
    }

    public struct HitTarget
    {
        public Vector3 Position;
        public float Radius;
        public float Health;
        public Action<float> ApplyDamage;
    }

    /// <summary>
    /// 浮游炮射击系统。管理浮游炮位置、发射追踪弹、命中检测。
    /// </summary>
    public class PlayerShotSystem
    {
        private readonly PlayerProfile _profile;
        private readonly List<PlayerBullet> _bullets = new();
        private readonly List<IOptionVisual> _optionVisuals = new();
        private readonly List<Vector3> _optionWorldPositions = new();
        private float _cooldown;
        private int _currentOptionCount;
        private DeterministicRng _rng;

        public event Action<Vector3, float> OnHitTarget;

        public IReadOnlyList<PlayerBullet> Bullets => _bullets;
        public IReadOnlyList<Vector3> OptionPositions => _optionWorldPositions;
        public int CurrentOptionCount => _currentOptionCount;

        public PlayerShotSystem(PlayerProfile profile, int seed = 42)
        {
            _profile = profile;
            _rng = new DeterministicRng(seed);
        }

        /// <summary>
        /// Update option positions based on current Power.
        /// Call every frame.
        /// </summary>
        public void UpdateOptions(float power, Vector3 playerPos,
            Vector3 right, Vector3 up, Vector3 forward, Transform parent, float dt)
        {
            // Determine option count from power tiers
            int newCount = _profile.PowerTiers[0].OptionCount;
            for (int i = _profile.PowerTiers.Count - 1; i >= 0; i--)
            {
                if (power >= _profile.PowerTiers[i].Threshold)
                {
                    newCount = _profile.PowerTiers[i].OptionCount;
                    break;
                }
            }

            // Rebuild visuals if count changed
            if (newCount != _currentOptionCount)
            {
                foreach (var v in _optionVisuals) v.Destroy();
                _optionVisuals.Clear();
                _optionWorldPositions.Clear();

                _currentOptionCount = newCount;
                for (int i = 0; i < newCount; i++)
                {
                    var visual = new SphereOptionVisual();
                    visual.Create(parent, i);
                    _optionVisuals.Add(visual);
                    _optionWorldPositions.Add(Vector3.zero);
                }
            }

            // Update positions
            var offsets = FindOffsets(_currentOptionCount);
            for (int i = 0; i < _currentOptionCount; i++)
            {
                Vector3 local = (i < offsets.Count) ? offsets[i] : Vector3.zero;
                var worldPos = playerPos + right * local.x + up * local.y + forward * local.z;
                _optionWorldPositions[i] = worldPos;
                _optionVisuals[i].UpdateTransform(worldPos, Quaternion.LookRotation(forward), dt);
            }
        }

        /// <summary>
        /// Try to fire bullets from all options.
        /// </summary>
        public void TryShoot(bool isShooting, bool isFocused, Vector3 forward, float dt)
        {
            _cooldown -= dt;
            if (!isShooting || _cooldown > 0f || _currentOptionCount == 0) return;

            float interval = isFocused ? _profile.FocusShotInterval : _profile.ShotInterval;
            float speed    = isFocused ? _profile.FocusShotSpeed    : _profile.ShotSpeed;
            float damage   = isFocused ? _profile.FocusShotDamage   : _profile.ShotDamage;
            int   count    = isFocused ? _profile.FocusShotsPerOption : _profile.ShotsPerOption;
            float cone     = isFocused ? _profile.FocusShotConeAngle : _profile.ShotConeAngle;
            float homing   = isFocused ? _profile.FocusShotHomingStrength : _profile.ShotHomingStrength;

            _cooldown = interval;

            for (int o = 0; o < _currentOptionCount; o++)
            {
                var origin = _optionWorldPositions[o];
                for (int i = 0; i < count; i++)
                {
                    // Cone-shaped random direction
                    float halfCone = cone * 0.5f * Mathf.Deg2Rad;
                    float theta = _rng.Range(0f, Mathf.PI * 2f);
                    float phi = _rng.Range(0f, halfCone);
                    var localDir = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Sin(phi) * Mathf.Sin(theta),
                        Mathf.Cos(phi)
                    );
                    var worldDir = Quaternion.LookRotation(forward) * localDir;

                    _bullets.Add(new PlayerBullet
                    {
                        Position = origin,
                        Velocity = worldDir * speed,
                        Damage = damage,
                        Radius = _profile.ShotRadius,
                        Active = true,
                        HomingStrength = homing,
                    });
                }
            }
        }

        /// <summary>
        /// Move bullets, apply homing, check hits, remove out-of-bounds.
        /// </summary>
        public void FixedTick(float dt, IReadOnlyList<HitTarget> targets,
            Vector3 boundaryMin, Vector3 boundaryMax)
        {
            for (int i = 0; i < _bullets.Count; i++)
            {
                var b = _bullets[i];
                if (!b.Active) continue;

                // Homing: rotate toward nearest target
                if (targets != null && targets.Count > 0 && b.HomingStrength > 0f)
                {
                    Vector3? nearest = FindNearestTarget(b.Position, targets);
                    if (nearest.HasValue)
                    {
                        var toTarget = (nearest.Value - b.Position).normalized;
                        float spd = b.Velocity.magnitude;
                        var newDir = Vector3.RotateTowards(
                            b.Velocity.normalized, toTarget,
                            b.HomingStrength * dt, 0f);
                        b.Velocity = newDir * spd;
                    }
                }

                // Move
                b.Position += b.Velocity * dt;

                // Out of bounds
                if (b.Position.x < boundaryMin.x || b.Position.x > boundaryMax.x ||
                    b.Position.y < boundaryMin.y || b.Position.y > boundaryMax.y ||
                    b.Position.z < boundaryMin.z || b.Position.z > boundaryMax.z)
                {
                    b.Active = false;
                    _bullets[i] = b;
                    continue;
                }

                // Hit detection
                if (targets != null)
                {
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (targets[t].Health <= 0f) continue;
                        float dist = Vector3.Distance(b.Position, targets[t].Position);
                        if (dist <= b.Radius + targets[t].Radius)
                        {
                            targets[t].ApplyDamage?.Invoke(b.Damage);
                            OnHitTarget?.Invoke(b.Position, b.Damage);
                            b.Active = false;
                            break;
                        }
                    }
                }

                _bullets[i] = b;
            }

            // Periodic cleanup (every 60 frames)
            if (Time.frameCount % 60 == 0)
                _bullets.RemoveAll(b => !b.Active);
        }

        /// <summary>Draw player bullets and option positions as Gizmos.</summary>
        public void DrawGizmos()
        {
            // Bullets (cyan)
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            for (int i = 0; i < _bullets.Count; i++)
            {
                if (!_bullets[i].Active) continue;
                Gizmos.DrawSphere(_bullets[i].Position, Mathf.Max(_bullets[i].Radius, 0.2f));
            }

            // Option positions (white wireframe)
            Gizmos.color = new Color(0.8f, 0.9f, 1f, 0.5f);
            for (int i = 0; i < _optionWorldPositions.Count; i++)
            {
                Gizmos.DrawWireSphere(_optionWorldPositions[i], 0.3f);
            }
        }

        /// <summary>Destroy all option visuals and clear bullets.</summary>
        public void Dispose()
        {
            foreach (var v in _optionVisuals) v.Destroy();
            _optionVisuals.Clear();
            _optionWorldPositions.Clear();
            _bullets.Clear();
            _currentOptionCount = 0;
        }

        private List<Vector3> FindOffsets(int optionCount)
        {
            // Find the offset list that matches the option count
            // OptionOffsetsByTier is indexed: [0]=2 options, [1]=4 options, [2]=6 options
            foreach (var offsets in _profile.OptionOffsetsByTier)
            {
                if (offsets.Count == optionCount)
                    return offsets;
            }
            // Fallback: return first available or empty
            return _profile.OptionOffsetsByTier.Count > 0
                ? _profile.OptionOffsetsByTier[0]
                : new List<Vector3>();
        }

        private static Vector3? FindNearestTarget(Vector3 pos, IReadOnlyList<HitTarget> targets)
        {
            float minDist = float.MaxValue;
            Vector3? nearest = null;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Health <= 0f) continue;
                float d = Vector3.Distance(pos, targets[i].Position);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = targets[i].Position;
                }
            }
            return nearest;
        }
    }
}
