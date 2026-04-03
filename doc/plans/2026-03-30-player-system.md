# Phase 7: 玩家系统实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完善玩家系统，实现编辑器试玩级的完整游戏循环（射击、Bomb、道具拾取、死亡复活）。

**Architecture:** Profile-Driven 架构——所有玩家参数集中在 PlayerProfile YAML 中，运行时读取驱动行为。浮游炮射击系统独立于弹幕系统，通过 IOptionVisual 接口预留渲染扩展。道具拾取通过 ItemEffects 静态方法闭环写入 PlayerState。

**Tech Stack:** Unity 2022 LTS / URP / C# / YamlDotNet / UI Toolkit

**Spec:** `doc/player_system_design.md`

---

## 文件结构

### 新增文件

| 文件 | 职责 |
|------|------|
| `Assets/STGEngine/Core/WorldScale.cs` | 尺寸常量集中定义 |
| `Assets/STGEngine/Core/DataModel/PlayerProfile.cs` | 玩家配置数据模型（YAML 序列化） |
| `Assets/STGEngine/Runtime/Player/PlayerShotSystem.cs` | 浮游炮射击 + 追踪弹 + 命中检测 |
| `Assets/STGEngine/Runtime/Player/IOptionVisual.cs` | 浮游炮渲染接口 |
| `Assets/STGEngine/Runtime/Player/SphereOptionVisual.cs` | 球体占位实现 |
| `Assets/STGEngine/Runtime/Player/ItemEffects.cs` | 道具效果应用（纯静态方法） |

### 修改文件

| 文件 | 变更 |
|------|------|
| `Runtime/Player/PlayerState.cs` | 新增 Power/Score/Bomb/碎片/死亡字段 + FromProfile 工厂 |
| `Runtime/Player/PlayerController.cs` | Profile 驱动初始化，边界约束，Bomb，死亡流程，射击集成 |
| `Runtime/Player/SimulatedPlayer.cs` | Profile 驱动初始化，边界约束 |
| `Runtime/Preview/ItemPreviewSystem.cs` | CheckPickup / SpawnDeathDrop / 动态回收目标 |
| `Editor/Scene/PatternSandboxSetup.cs` | Profile 加载，HUD 扩展，Game Over |

---

## Task 1: WorldScale 常量 + PlayerProfile 数据模型

**Files:**
- Create: `Assets/STGEngine/Core/WorldScale.cs`
- Create: `Assets/STGEngine/Core/DataModel/PlayerProfile.cs`

**依赖:** 无（纯数据层，不依赖任何运行时代码）

- [ ] **Step 1: 创建 WorldScale.cs**

在 `Assets/STGEngine/Core/` 下创建 `WorldScale.cs`。定义所有尺寸常量：

```csharp
namespace STGEngine.Core
{
    /// <summary>
    /// 世界尺寸标准。1 unit = 1 meter。
    /// 所有值为初始参考，可通过 PlayerProfile 覆盖。
    /// </summary>
    public static class WorldScale
    {
        public const float PlayerVisualDiameter = 1.6f;
        public const float PlayerHitboxRadius   = 0.08f;
        public const float PlayerGrazeRadius    = 0.5f;

        public const float BulletSmallRadius    = 0.15f;
        public const float BulletNormalRadius   = 0.4f;
        public const float BulletLargeRadius    = 1.2f;

        public const float BossVisualScale      = 5.0f;
        public const float EnemyVisualScale     = 2.0f;

        public const float ItemSmallRadius      = 0.3f;
        public const float ItemLargeRadius      = 0.5f;

        public const float DefaultBoundaryHalf  = 40f;

        public const float PlayerMoveSpeed      = 14f;
        public const float PlayerSlowMultiplier = 0.33f;
    }
}
```

- [ ] **Step 2: 创建 PlayerProfile.cs**

在 `Assets/STGEngine/Core/DataModel/` 下创建 `PlayerProfile.cs`。包含 PowerTier 类和 AutoCollectTrigger 枚举。所有字段使用 WorldScale 常量作为默认值。完整代码见 spec 第三节。

关键点：
- `[TypeTag("player_profile")]` 注册到 TypeRegistry
- `PowerTier` 类加 `[TypeTag("power_tier")]`
- `OptionOffsetsByTier` 使用 `List<List<Vector3>>`，YamlDotNet 需要能序列化
- `TouhouDefault` 静态属性返回使用所有默认值的实例

- [ ] **Step 3: 验证编译**

Unity 中等待编译完成，确认无错误。特别检查：
- `WorldScale` 在 Core 程序集中可访问
- `PlayerProfile` 的 `[TypeTag]` 被 TypeRegistry 扫描到
- `List<Vector3>` 和 `List<List<Vector3>>` 的 YAML 序列化无问题

- [ ] **Step 4: 提交**

```bash
git add Assets/STGEngine/Core/WorldScale.cs Assets/STGEngine/Core/DataModel/PlayerProfile.cs
git commit -m "feat: add WorldScale constants and PlayerProfile data model"
```

## Task 2: PlayerState 扩展 + FromProfile 工厂

**Files:**
- Modify: `Assets/STGEngine/Runtime/Player/PlayerState.cs`

**依赖:** Task 1（PlayerProfile）

- [ ] **Step 1: 扩展 PlayerState 字段**

在现有 PlayerState 中新增以下字段（保留所有现有字段不变）：

```csharp
// ── Power（新增）──
public float Power;
public float MaxPower;

// ── Score（新增）──
public long Score;
public int PointItemValue;

// ── Bomb 状态（新增）──
public bool IsBombing;
public float BombTimer;
public float BombDuration;
public float BombInvincibleDuration;

// ── 死亡/复活（新增）──
public bool IsDead;
public float RespawnTimer;
public float RespawnInvincibleDuration;

// ── 碎片（新增）──
public int LifeFragments;
public int BombFragments;
```

- [ ] **Step 2: 添加 FromProfile 工厂方法**

```csharp
public static PlayerState FromProfile(PlayerProfile profile, Vector3 spawnPos)
{
    return new PlayerState
    {
        Position = spawnPos,
        Lives = profile.InitialLives,
        Bombs = profile.InitialBombs,
        Power = profile.InitialPower,
        MaxPower = profile.MaxPower,
        PointItemValue = profile.BasePointItemValue,
        HitboxRadius = profile.HitboxRadius,
        GrazeRadius = profile.GrazeRadius,
        InvincibleDuration = profile.InvincibleDuration,
        BombDuration = profile.BombDuration,
        BombInvincibleDuration = profile.BombInvincibleDuration,
        RespawnInvincibleDuration = profile.RespawnInvincibleDuration,
    };
}
```

- [ ] **Step 3: 添加 TickBomb 方法**

```csharp
public void TickBomb(float dt)
{
    if (!IsBombing) return;
    BombTimer -= dt;
    if (BombTimer <= 0f)
    {
        IsBombing = false;
        BombTimer = 0f;
    }
}
```

- [ ] **Step 4: 修改 OnHit 方法**

现有 `OnHit()` 需要增加 IsBombing 检查（Bomb 期间不被弹）：

```csharp
public void OnHit()
{
    if (IsInvincible || IsBombing) return;
    Lives--;
    IsInvincible = true;
    InvincibleTimer = InvincibleDuration;
    IsDead = Lives <= 0;
}
```

- [ ] **Step 5: 验证编译 + 提交**

确认编译无错误。现有 PlayerController 和 SimulatedPlayer 中对 PlayerState 的使用不应受影响（只新增了字段）。

```bash
git add Assets/STGEngine/Runtime/Player/PlayerState.cs
git commit -m "feat: extend PlayerState with Power/Score/Bomb/death fields"
```

## Task 3: PlayerController 重构（Profile 驱动 + 边界 + 死亡/Bomb）

**Files:**
- Modify: `Assets/STGEngine/Runtime/Player/PlayerController.cs`

**依赖:** Task 1, Task 2

这是最大的单个改动。PlayerController 从硬编码参数改为 Profile 驱动，并新增死亡/复活状态机和 Bomb 逻辑。

- [ ] **Step 1: 修改 Initialize 签名**

接受 `PlayerProfile` 参数，用 `PlayerState.FromProfile` 替代手动构造：

```csharp
private PlayerProfile _profile;
private Vector3 _boundaryMin;
private Vector3 _boundaryMax;

// 状态机
private enum PlayerPhase { Normal, Dying, Respawning, Dead }
private PlayerPhase _phase = PlayerPhase.Normal;
private float _dyingTimer;
private const float DyingDuration = 0.5f;

public void Initialize(PlayerProfile profile, PlayerCamera camera,
    System.Func<IReadOnlyList<BulletState>> bulletProvider = null,
    float bulletRadius = 0.1f)
{
    _profile = profile;
    _playerCamera = camera;
    _bulletStateProvider = bulletProvider;
    _bulletCollisionRadius = bulletRadius;

    _state = PlayerState.FromProfile(profile, transform.position);

    // 边界
    var boundary = FindAnyObjectByType<SandboxBoundary>();
    if (boundary != null)
    {
        var center = boundary.transform.position;
        var half = boundary.HalfExtents;
        _boundaryMin = center - half;
        _boundaryMax = center + half;
    }
    else
    {
        _boundaryMin = Vector3.one * -WorldScale.DefaultBoundaryHalf;
        _boundaryMax = Vector3.one * WorldScale.DefaultBoundaryHalf;
    }

    if (camera != null) camera.SetTarget(transform);
}
```

移除旧的 `[SerializeField] _hitboxRadius / _grazeRadius / _moveSpeed / _slowMultiplier` 字段，改从 `_profile` 读取。

- [ ] **Step 2: 重写 FixedTick 为状态机**

```csharp
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
            if (_dyingTimer <= 0f) TransitionToRespawnOrDead();
            break;
        case PlayerPhase.Respawning:
            TickNormal(dt); // 复活后正常移动，但处于无敌
            break;
        case PlayerPhase.Dead:
            // 不做任何事，等待外部重置
            break;
    }
}
```

- [ ] **Step 3: 实现 TickNormal**

从现有 FixedTick 逻辑提取，新增边界 clamp 和 Bomb 输入：

```csharp
private void TickNormal(float dt)
{
    // 移动
    float speed = _state.IsSlow ? _profile.MoveSpeed * _profile.SlowMultiplier : _profile.MoveSpeed;
    _state.Position += _inputDirection * speed * dt;
    // 边界 clamp
    _state.Position = Vector3.Max(_boundaryMin, Vector3.Min(_boundaryMax, _state.Position));
    transform.position = _state.Position;

    // 无敌/Bomb 计时
    _state.TickInvincibility(dt);
    _state.TickBomb(dt);

    // 碰撞检测（现有逻辑不变）
    _state.GrazeThisFrame = 0;
    if (_bulletStateProvider != null) { /* 现有碰撞检测代码 */ }

    // 被弹处理改为触发状态机
    // if (result.Hit) → EnterDying()
}
```

- [ ] **Step 4: 实现 EnterDying / TransitionToRespawnOrDead**

```csharp
private void EnterDying()
{
    _state.OnHit();
    OnPlayerHit?.Invoke();

    // Power 掉落
    _state.Power = Mathf.Max(0f, _state.Power - _profile.DeathPowerLoss);

    if (_state.IsDead)
    {
        _phase = PlayerPhase.Dead;
        OnPlayerDeath?.Invoke();
        return;
    }

    _phase = PlayerPhase.Dying;
    _dyingTimer = DyingDuration;
    // 隐藏视觉（后续 Task 中处理）
}

private void TransitionToRespawnOrDead()
{
    _phase = PlayerPhase.Respawning;
    // 回到出生点
    _state.Position = Vector3.zero; // 场景中央
    transform.position = _state.Position;
    // 进入复活无敌
    _state.IsInvincible = true;
    _state.InvincibleTimer = _state.RespawnInvincibleDuration;
    // 触发消弹（通过事件通知外部）
    OnRespawnClearBullets?.Invoke();
}
```

新增事件：`public event System.Action OnRespawnClearBullets;`

- [ ] **Step 5: 实现 Bomb 输入**

在 `GatherInput()` 中新增：

```csharp
// Bomb（鼠标右键）
if (Input.GetMouseButtonDown(1) && _state.Bombs > 0 && !_state.IsBombing)
{
    _state.Bombs--;
    _state.IsBombing = true;
    _state.BombTimer = _state.BombDuration;
    _state.IsInvincible = true;
    _state.InvincibleTimer = _state.BombInvincibleDuration;
    OnBomb?.Invoke();
}
```

新增事件：`public event System.Action OnBomb;`

- [ ] **Step 6: 验证编译 + 提交**

注意：PatternSandboxSetup 中调用 `Initialize` 的地方需要同步修改签名（传入 PlayerProfile）。暂时传 `PlayerProfile.TouhouDefault`，Task 8 中完善。

```bash
git add Assets/STGEngine/Runtime/Player/PlayerController.cs
git commit -m "feat: refactor PlayerController to profile-driven with death/bomb state machine"
```

## Task 4: SimulatedPlayer Profile 驱动适配

**Files:**
- Modify: `Assets/STGEngine/Runtime/Player/SimulatedPlayer.cs`

**依赖:** Task 1, Task 2

与 Task 3 类似但更简单——AI 不射击、不 Bomb、不处理死亡流程（被弹只扣血+无敌）。

- [ ] **Step 1: 修改 Initialize 签名**

```csharp
public void Initialize(
    RandomWalkBrain brain,
    PlayerProfile profile,
    System.Func<IReadOnlyList<BulletState>> bulletProvider = null,
    float bulletRadius = 0.1f)
{
    _brain = brain;
    _profile = profile;
    _bulletStateProvider = bulletProvider;
    _bulletCollisionRadius = bulletRadius;

    _state = PlayerState.FromProfile(profile, transform.position);

    // 边界（现有逻辑不变，但用 profile 的值作为 fallback）
    var boundary = FindAnyObjectByType<SandboxBoundary>();
    if (boundary != null) { /* 现有代码 */ }

    _brain.Initialize();
    BuildVisual();
}
```

移除 `[SerializeField] _hitboxRadius / _grazeRadius` 字段。

- [ ] **Step 2: FixedTick 中使用 Profile 移速**

```csharp
float speed = _state.IsSlow ? _profile.MoveSpeed * _profile.SlowMultiplier : _profile.MoveSpeed;
_state.Position += moveDir * speed * dt;
```

替换现有的 `_moveSpeed` 引用。

- [ ] **Step 3: 更新 BuildVisual 使用 Profile 尺寸**

```csharp
sphere.transform.localScale = Vector3.one * _profile.VisualScale * 0.25f;
// 0.25 是球体 mesh 到角色视觉的缩放因子
```

- [ ] **Step 4: 验证编译 + 提交**

```bash
git add Assets/STGEngine/Runtime/Player/SimulatedPlayer.cs
git commit -m "feat: adapt SimulatedPlayer to profile-driven initialization"
```

## Task 5: 浮游炮射击系统

**Files:**
- Create: `Assets/STGEngine/Runtime/Player/IOptionVisual.cs`
- Create: `Assets/STGEngine/Runtime/Player/SphereOptionVisual.cs`
- Create: `Assets/STGEngine/Runtime/Player/PlayerShotSystem.cs`

**依赖:** Task 1（PlayerProfile）, Task 2（PlayerState）

- [ ] **Step 1: 创建 IOptionVisual.cs**

```csharp
using UnityEngine;

namespace STGEngine.Runtime.Player
{
    public interface IOptionVisual
    {
        GameObject Create(Transform parent, int optionIndex);
        void UpdateTransform(Vector3 worldPosition, Quaternion rotation, float dt);
        void OnPowerTierChanged(int newOptionCount);
        void Destroy();
    }
}
```

- [ ] **Step 2: 创建 SphereOptionVisual.cs**

```csharp
using UnityEngine;

namespace STGEngine.Runtime.Player
{
    public class SphereOptionVisual : IOptionVisual
    {
        private GameObject _go;

        public GameObject Create(Transform parent, int optionIndex)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _go.transform.SetParent(parent);
            _go.transform.localScale = Vector3.one * 0.5f;
            var col = _go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var rend = _go.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = new Color(0.8f, 0.9f, 1f, 0.7f);
            return _go;
        }

        public void UpdateTransform(Vector3 worldPos, Quaternion rot, float dt)
        {
            if (_go != null)
            {
                _go.transform.position = worldPos;
                _go.transform.rotation = rot;
            }
        }

        public void OnPowerTierChanged(int newOptionCount) { }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
        }
    }
}
```

- [ ] **Step 3: 创建 PlayerShotSystem.cs 骨架**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core;
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

        public PlayerShotSystem(PlayerProfile profile, int seed = 42) { ... }
        public void UpdateOptions(float power, Vector3 playerPos,
            Vector3 right, Vector3 up, Vector3 forward, Transform parent, float dt) { ... }
        public void TryShoot(bool isShooting, bool isFocused, Vector3 forward) { ... }
        public void FixedTick(float dt, IReadOnlyList<HitTarget> targets,
            Vector3 boundaryMin, Vector3 boundaryMax) { ... }
        public void Dispose() { ... }
    }
}
```

- [ ] **Step 4: 实现 UpdateOptions（浮游炮位置 + Power 段位）**

根据当前 Power 查 PowerTiers 表确定 OptionCount。数量变化时创建/销毁 IOptionVisual。每帧更新浮游炮世界位置：

```csharp
public void UpdateOptions(float power, Vector3 playerPos,
    Vector3 right, Vector3 up, Vector3 forward, Transform parent, float dt)
{
    // 查表确定当前 OptionCount
    int newCount = _profile.PowerTiers[0].OptionCount;
    for (int i = _profile.PowerTiers.Count - 1; i >= 0; i--)
    {
        if (power >= _profile.PowerTiers[i].Threshold)
        {
            newCount = _profile.PowerTiers[i].OptionCount;
            break;
        }
    }

    // 数量变化 → 重建视觉
    if (newCount != _currentOptionCount)
    {
        foreach (var v in _optionVisuals) v.Destroy();
        _optionVisuals.Clear();
        _optionWorldPositions.Clear();

        _currentOptionCount = newCount;
        // 找到匹配的 offset 列表
        var offsets = FindOffsets(newCount);
        for (int i = 0; i < newCount; i++)
        {
            var visual = new SphereOptionVisual();
            visual.Create(parent, i);
            _optionVisuals.Add(visual);
            _optionWorldPositions.Add(Vector3.zero);
        }
    }

    // 更新位置
    var offsets2 = FindOffsets(_currentOptionCount);
    for (int i = 0; i < _currentOptionCount; i++)
    {
        var local = offsets2[i];
        var worldPos = playerPos + right * local.x + up * local.y + forward * local.z;
        _optionWorldPositions[i] = worldPos;
        _optionVisuals[i].UpdateTransform(worldPos, Quaternion.LookRotation(forward), dt);
    }
}
```

- [ ] **Step 5: 实现 TryShoot（锥形散射 + 双模式）**

```csharp
public void TryShoot(bool isShooting, bool isFocused, Vector3 forward)
{
    _cooldown -= Time.fixedDeltaTime;
    if (!isShooting || _cooldown > 0f) return;

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
            // 锥形随机方向
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
```

- [ ] **Step 6: 实现 FixedTick（移动 + 追踪 + 命中检测）**

```csharp
public void FixedTick(float dt, IReadOnlyList<HitTarget> targets,
    Vector3 boundaryMin, Vector3 boundaryMax)
{
    for (int i = 0; i < _bullets.Count; i++)
    {
        var b = _bullets[i];
        if (!b.Active) continue;

        // 追踪：向最近目标转向
        if (targets != null && targets.Count > 0 && b.HomingStrength > 0f)
        {
            var nearest = FindNearest(b.Position, targets);
            if (nearest.HasValue)
            {
                var toTarget = (nearest.Value - b.Position).normalized;
                var speed = b.Velocity.magnitude;
                var newDir = Vector3.RotateTowards(
                    b.Velocity.normalized, toTarget,
                    b.HomingStrength * dt, 0f);
                b.Velocity = newDir * speed;
            }
        }

        // 移动
        b.Position += b.Velocity * dt;

        // 出界检查
        if (b.Position.x < boundaryMin.x || b.Position.x > boundaryMax.x ||
            b.Position.y < boundaryMin.y || b.Position.y > boundaryMax.y ||
            b.Position.z < boundaryMin.z || b.Position.z > boundaryMax.z)
        {
            b.Active = false;
            _bullets[i] = b;
            continue;
        }

        // 命中检测
        if (targets != null)
        {
            for (int t = 0; t < targets.Count; t++)
            {
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

    // 清理 inactive（每 60 帧一次避免频繁 GC）
    if (Time.frameCount % 60 == 0)
        _bullets.RemoveAll(b => !b.Active);
}
```

- [ ] **Step 7: 验证编译 + 提交**

```bash
git add Assets/STGEngine/Runtime/Player/IOptionVisual.cs \
        Assets/STGEngine/Runtime/Player/SphereOptionVisual.cs \
        Assets/STGEngine/Runtime/Player/PlayerShotSystem.cs
git commit -m "feat: add option-based shooting system with homing bullets"
```

## Task 6: 道具拾取闭环

**Files:**
- Create: `Assets/STGEngine/Runtime/Player/ItemEffects.cs`
- Modify: `Assets/STGEngine/Runtime/Preview/ItemPreviewSystem.cs`

**依赖:** Task 1, Task 2

- [ ] **Step 1: 创建 ItemEffects.cs**

```csharp
using UnityEngine;
using STGEngine.Core.DataModel;

namespace STGEngine.Runtime.Player
{
    public struct ItemPickupResult
    {
        public int PowerSmallCount;
        public int PowerLargeCount;
        public int PointItemCount;
        public int BombFragmentCount;
        public int LifeFragmentCount;
        public int FullPowerCount;
    }

    public static class ItemEffects
    {
        public static void Apply(ItemPickupResult pickup, PlayerState state, PlayerProfile profile)
        {
            // Power
            state.Power += pickup.PowerSmallCount * profile.PowerPerSmallItem
                         + pickup.PowerLargeCount * profile.PowerPerLargeItem;
            if (pickup.FullPowerCount > 0)
                state.Power = profile.MaxPower;
            state.Power = Mathf.Min(state.Power, profile.MaxPower);

            // Score
            state.Score += pickup.PointItemCount * state.PointItemValue;

            // 残机碎片
            state.LifeFragments += pickup.LifeFragmentCount;
            if (state.LifeFragments >= profile.LifeFragmentsPerLife)
            {
                state.Lives++;
                state.LifeFragments -= profile.LifeFragmentsPerLife;
            }

            // Bomb 碎片
            state.BombFragments += pickup.BombFragmentCount;
            if (state.BombFragments >= profile.BombFragmentsPerBomb)
            {
                state.Bombs++;
                state.BombFragments -= profile.BombFragmentsPerBomb;
            }
        }
    }
}
```

- [ ] **Step 2: ItemPreviewSystem 新增 CheckPickup**

在 `ItemPreviewSystem.cs` 中新增方法：

```csharp
public ItemPickupResult CheckPickup(Vector3 playerPos, float collectRadius)
{
    var result = new ItemPickupResult();
    for (int i = 0; i < _items.Count; i++)
    {
        var item = _items[i];
        if (!item.Active) continue;

        if (Vector3.Distance(item.Position, playerPos) <= collectRadius)
        {
            item.Active = false;
            _items[i] = item;
            switch (item.Type)
            {
                case ItemType.PowerSmall:   result.PowerSmallCount++; break;
                case ItemType.PowerLarge:   result.PowerLargeCount++; break;
                case ItemType.PointItem:    result.PointItemCount++; break;
                case ItemType.BombFragment: result.BombFragmentCount++; break;
                case ItemType.LifeFragment: result.LifeFragmentCount++; break;
                case ItemType.FullPower:    result.FullPowerCount++; break;
            }
        }
    }
    return result;
}
```

需要在文件顶部添加 `using STGEngine.Runtime.Player;`。

- [ ] **Step 3: ItemPreviewSystem 新增 SpawnDeathDrop**

```csharp
public void SpawnDeathDrop(Vector3 deathPosition, int powerItemCount)
{
    for (int i = 0; i < powerItemCount; i++)
    {
        var scatter = new Vector3(
            _rng.Range(-1f, 1f),
            _rng.Range(0.5f, 1.5f),
            _rng.Range(-1f, 1f)
        );
        _items.Add(new PreviewItem
        {
            Position = deathPosition + scatter * 0.3f,
            Velocity = scatter * 3f + Vector3.up * 4f,
            Type = ItemType.PowerSmall,
            Active = true,
            Elapsed = 0f
        });
    }
}
```

- [ ] **Step 4: 动态回收目标**

将 `_collectTarget` 从 `readonly` 改为可更新：

```csharp
private Vector3 _collectTarget = new(0f, 15f, 0f);

public void SetCollectTarget(Vector3 target)
{
    _collectTarget = target;
}
```

在 AutoCollect 飞行逻辑中已经使用 `_collectTarget`，无需改动。

- [ ] **Step 5: 验证编译 + 提交**

```bash
git add Assets/STGEngine/Runtime/Player/ItemEffects.cs \
        Assets/STGEngine/Runtime/Preview/ItemPreviewSystem.cs
git commit -m "feat: add item pickup detection and effects application"
```

## Task 7: PlayerController 集成射击 + 道具

**Files:**
- Modify: `Assets/STGEngine/Runtime/Player/PlayerController.cs`

**依赖:** Task 3, Task 5, Task 6

将 PlayerShotSystem 和道具拾取集成到 PlayerController 的 FixedTick 循环中。

- [ ] **Step 1: 新增字段和初始化**

```csharp
private PlayerShotSystem _shotSystem;
private ItemPreviewSystem _itemSystem;
private System.Func<IReadOnlyList<HitTarget>> _hitTargetProvider;

// 在 Initialize 中新增：
_shotSystem = new PlayerShotSystem(profile);

// 新增注入方法：
public void SetItemSystem(ItemPreviewSystem itemSystem) { _itemSystem = itemSystem; }
public void SetHitTargetProvider(System.Func<IReadOnlyList<HitTarget>> provider)
    { _hitTargetProvider = provider; }
```

- [ ] **Step 2: GatherInput 新增射击输入**

```csharp
// 射击（鼠标左键）
_isShooting = Input.GetMouseButton(0);
```

新增字段 `private bool _isShooting;`

- [ ] **Step 3: TickNormal 中集成射击和道具**

在 TickNormal 的碰撞检测之后添加：

```csharp
// 射击
if (_shotSystem != null)
{
    _shotSystem.UpdateOptions(_state.Power, _state.Position,
        _playerCamera?.ViewRight ?? Vector3.right,
        _playerCamera?.ViewUp ?? Vector3.up,
        _playerCamera?.ViewForward ?? Vector3.forward,
        transform, dt);
    _shotSystem.TryShoot(_isShooting, _state.IsSlow,
        _playerCamera?.ViewForward ?? Vector3.forward);
    var targets = _hitTargetProvider?.Invoke();
    _shotSystem.FixedTick(dt, targets, _boundaryMin, _boundaryMax);
}

// 道具拾取
if (_itemSystem != null)
{
    var pickup = _itemSystem.CheckPickup(_state.Position, _profile.ItemCollectRadius);
    ItemEffects.Apply(pickup, _state, _profile);

    // 动态回收目标
    _itemSystem.SetCollectTarget(_state.Position);

    // HighPower 自动回收
    if (_profile.AutoCollectMode == AutoCollectTrigger.HighPower
        && _state.Power >= _state.MaxPower)
    {
        _itemSystem.TriggerAutoCollect();
    }
}
```

- [ ] **Step 4: EnterDying 中触发死亡掉落**

```csharp
// 在 EnterDying 中，Power 掉落后：
if (_itemSystem != null)
{
    int dropCount = Mathf.CeilToInt(_profile.DeathPowerLoss / _profile.PowerPerSmallItem);
    _itemSystem.SpawnDeathDrop(_state.Position, dropCount);
}
```

- [ ] **Step 5: 验证编译 + 提交**

```bash
git add Assets/STGEngine/Runtime/Player/PlayerController.cs
git commit -m "feat: integrate shooting and item pickup into PlayerController"
```

## Task 8: 编辑器集成（PatternSandboxSetup + HUD + Game Over）

**Files:**
- Modify: `Assets/STGEngine/Editor/Scene/PatternSandboxSetup.cs`

**依赖:** Task 1~7 全部

这是最后的集成任务，将所有系统连接到编辑器的 Player/AI Sim 按钮。

- [ ] **Step 1: Player 按钮流程适配**

修改现有的 Player 按钮回调，传入 PlayerProfile：

```csharp
private PlayerProfile _playerProfile;

// 在 Setup 或 Awake 中：
_playerProfile = LoadPlayerProfile(); // 尝试从 STGData/PlayerProfiles/ 加载，失败则用 TouhouDefault

private PlayerProfile LoadPlayerProfile()
{
    // 检查 STGData/PlayerProfiles/ 下是否有 YAML
    // 有 → 用 YamlSerializer 加载第一个
    // 没有 → 返回 PlayerProfile.TouhouDefault
    return PlayerProfile.TouhouDefault;
}
```

修改创建 PlayerController 的代码，传入 profile：

```csharp
controller.Initialize(_playerProfile, playerCamera, bulletProvider, bulletRadius);
controller.SetItemSystem(_itemPreviewSystem);
controller.SetHitTargetProvider(GetHitTargets);
controller.OnBomb += HandleBomb;
controller.OnRespawnClearBullets += HandleRespawnClear;
```

- [ ] **Step 2: AI Sim 按钮适配**

修改创建 SimulatedPlayer 的代码，传入 profile：

```csharp
simPlayer.Initialize(brain, _playerProfile, bulletProvider, bulletRadius);
```

- [ ] **Step 3: 实现 HitTarget 提供**

```csharp
private IReadOnlyList<HitTarget> GetHitTargets()
{
    var targets = new List<HitTarget>();

    // Boss
    if (_bossPlaceholder != null && _bossPlaceholder.gameObject.activeSelf)
    {
        targets.Add(new HitTarget
        {
            Position = _bossPlaceholder.transform.position,
            Radius = WorldScale.BossVisualScale * 0.5f,
            Health = 1000f, // 占位
            ApplyDamage = dmg => { /* 后续实现 Boss 扣血 */ }
        });
    }

    // Enemies（遍历活跃的 EnemyPlaceholder）
    // 类似逻辑

    return targets;
}
```

- [ ] **Step 4: Bomb / 复活消弹处理**

```csharp
private void HandleBomb()
{
    // 复用 BulletClear 逻辑：遍历所有活跃 previewer 的 SimulationEvaluator
    // 标记范围内 bullet 为 inactive
    ClearAllBullets();
}

private void HandleRespawnClear()
{
    ClearAllBullets();
}

private void ClearAllBullets()
{
    // 遍历 _previewerPool 中所有活跃 previewer
    // 调用 simEvaluator.ClearBullets(...)
}
```

- [ ] **Step 5: 扩展 HUD**

修改现有 `_playerHudLabel` 的更新逻辑：

```csharp
private void UpdatePlayerHud()
{
    if (_activePlayer == null) return;
    var s = _activePlayer.State;
    _playerHudLabel.text =
        $"★{s.Lives}  ✦{s.Bombs}  P {s.Power:F2}/{s.MaxPower:F2}\n" +
        $"Score: {s.Score:N0}\n" +
        $"Graze: {s.GrazeTotal}" +
        (s.LifeFragments > 0 ? $"  ◆×{s.LifeFragments}" : "") +
        (s.BombFragments > 0 ? $"  ◇×{s.BombFragments}" : "") +
        (s.IsSlow ? "  [SLOW]" : "") +
        (s.IsInvincible ? "  [INV]" : "") +
        (s.IsBombing ? "  [BOMB]" : "");
}
```

- [ ] **Step 6: Game Over 处理**

```csharp
// 在 PlayerController.OnPlayerDeath 回调中：
private void HandlePlayerDeath()
{
    // 显示 GAME OVER
    _playerHudLabel.text = "<color=red>GAME OVER</color>\n" + _playerHudLabel.text;

    // 暂停时间轴
    if (_timelinePlayback != null)
        _timelinePlayback.Pause();

    // 3 秒后恢复
    StartCoroutine(GameOverRecovery());
}

private IEnumerator GameOverRecovery()
{
    yield return new WaitForSeconds(3f);
    // 重置 PlayerState
    var state = PlayerState.FromProfile(_playerProfile, Vector3.zero);
    // 重新初始化...
}
```

- [ ] **Step 7: 玩家子弹渲染**

在 PatternSandboxSetup 的渲染循环中，将 PlayerShotSystem 的子弹提交到 BulletRenderer：

```csharp
// 在 OnRenderObject 或 Update 中：
if (_shotSystem != null)
{
    foreach (var b in _shotSystem.Bullets)
    {
        if (!b.Active) continue;
        _playerBulletBatch.Add(b.Position, b.Radius * 2f, Color.cyan);
    }
    _playerBulletBatch.Draw();
}
```

需要为玩家子弹创建一个独立的 RenderBatch（与弹幕子弹区分颜色）。

- [ ] **Step 8: Play 模式全流程验证**

进入 Play 模式，依次验证：
1. Player 按钮 → 玩家出现，浮游炮可见
2. WASD 移动 + 边界约束
3. 鼠标左键射击 → 追踪弹飞向 Boss
4. 左 Ctrl → 低速 + 集中射击
5. 鼠标右键 → Bomb 消弹
6. 站在弹幕中被弹 → 死亡 → 复活
7. 拾取道具 → HUD 更新
8. ESC 退出

- [ ] **Step 9: 提交**

```bash
git add Assets/STGEngine/Editor/Scene/PatternSandboxSetup.cs
git commit -m "feat: integrate player system into editor (HUD, game over, shooting render)"
```

---

## 验证清单

完成所有 Task 后，在 Play 模式中验证：

- [ ] Player 按钮 → 玩家出现，WASD 移动，边界约束生效
- [ ] 鼠标左键 → 浮游炮发射追踪弹，命中 Boss/Enemy
- [ ] 左 Ctrl → 低速模式，射击切换为集中模式
- [ ] 鼠标右键 → Bomb 消弹 + 无敌
- [ ] 被弹 → 死亡动画 → 复活 → 消弹 → 无敌
- [ ] 残机归零 → GAME OVER → 暂停 → 恢复
- [ ] 拾取 PowerSmall → Power 增加 → 浮游炮数量变化
- [ ] 拾取 LifeFragment ×3 → Lives +1
- [ ] Power MAX → 自动回收道具
- [ ] HUD 实时显示所有状态
- [ ] AI Sim 按钮 → AI 玩家正常运行（不射击）
- [ ] ESC → 退出玩家模式，恢复自由相机
