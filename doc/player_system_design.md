# 玩家系统完善设计文档

> Phase 7：玩家系统 + 射击 + 道具拾取闭环
> 目标定位：编辑器试玩级
> 风格：可配置底层 + 东方系预设

---

## 一、设计范围

| 包含 | 不包含 |
|------|--------|
| PlayerProfile 数据模型（YAML 可配置） | 关卡选择 / 存档 / 续关 |
| PlayerState 扩展（Power/Score/Bomb/碎片） | 完整 Game Over 画面 |
| 死亡/复活流程 | Option 副炮（后期扩展） |
| Bomb 系统（消弹+无敌） | 特殊弹幕效果 Bomb |
| 浮游炮射击（追踪弹，普通/低速双模式） | AI 射击 |
| 道具拾取闭环（Power/Score/碎片→状态） | 道具商店 / 装备系统 |
| 边界约束 | 2D 模式适配 |
| 试玩 HUD | 完整 UI 系统 |
| 尺寸标准统一化 | |

---

## 二、尺寸标准

基准：1 Unity 单位 = 1 米。

| 实体 | 尺寸 | 说明 |
|------|------|------|
| 玩家视觉球体直径 | 1.6 | 女性身高基准 |
| 玩家被弹判定半径 | 0.08 | 拳头大小，东方系极小判定点 |
| 玩家擦弹判定半径 | 0.5 | 手臂展开范围 |
| 普通弹幕半径 | 0.3 ~ 0.5 | 足球~沙滩球 |
| 大玉弹幕半径 | 1.0 ~ 1.5 | 比玩家大 |
| 米粒弹半径 | 0.1 ~ 0.2 | 乒乓球 |
| Boss 视觉 | 4.0 ~ 6.0 | 明显大于玩家 |
| 小怪视觉 | 1.5 ~ 2.5 | 与玩家相当或略大 |
| 道具 | 0.5 ~ 1.0 | 容易看到和拾取 |
| 沙盒边界 halfExtents | (40, 40, 40) | 80m 立方体 |
| 玩家移速 | 14 m/s | 匹配大场景 |
| 玩家低速移速 | ~4.6 m/s | 0.33x |

这些值作为初始参考，全部可通过 PlayerProfile YAML 覆盖，后期调整。

在 Core 层新增 `WorldScale.cs` 集中定义常量，供 PlayerProfile 默认值引用。

---

## 三、PlayerProfile 数据模型

放在 `Core/DataModel/PlayerProfile.cs`，纯 C# 类，YAML 序列化。

```csharp
[TypeTag("player_profile")]
public class PlayerProfile
{
    public string Id { get; set; }
    public string Name { get; set; }

    // ── 移动 ──
    public float MoveSpeed { get; set; } = 14f;
    public float SlowMultiplier { get; set; } = 0.33f;

    // ── 判定 ──
    public float HitboxRadius { get; set; } = 0.08f;
    public float GrazeRadius { get; set; } = 0.5f;
    public float VisualScale { get; set; } = 1.6f;

    // ── 生存 ──
    public int InitialLives { get; set; } = 3;
    public int InitialBombs { get; set; } = 3;
    public float InvincibleDuration { get; set; } = 2f;
    public float RespawnInvincibleDuration { get; set; } = 3f;

    // ── Power ──
    public float MaxPower { get; set; } = 4.0f;
    public float InitialPower { get; set; } = 1.0f;
    public float PowerPerSmallItem { get; set; } = 0.05f;
    public float PowerPerLargeItem { get; set; } = 1.0f;
    public float DeathPowerLoss { get; set; } = 0.5f;

    // ── Power 段位 → 浮游炮数量 ──
    public List<PowerTier> PowerTiers { get; set; } = new()
    {
        new PowerTier { Threshold = 0f,   OptionCount = 2 },
        new PowerTier { Threshold = 1.0f, OptionCount = 2 },
        new PowerTier { Threshold = 2.0f, OptionCount = 4 },
        new PowerTier { Threshold = 3.0f, OptionCount = 4 },
        new PowerTier { Threshold = 4.0f, OptionCount = 6 },
    };

    // 每个段位对应的浮游炮偏移（相对玩家局部坐标）
    public List<List<Vector3>> OptionOffsetsByTier { get; set; } = new()
    {
        // 2 个
        new() { new(-1.2f, 0.3f, 0.5f), new(1.2f, 0.3f, 0.5f) },
        // 4 个
        new() { new(-1.2f, 0.3f, 0.5f), new(1.2f, 0.3f, 0.5f),
                 new(-2.0f, -0.2f, 0f),  new(2.0f, -0.2f, 0f) },
        // 6 个
        new() { new(-1.2f, 0.3f, 0.5f), new(1.2f, 0.3f, 0.5f),
                 new(-2.0f, -0.2f, 0f),  new(2.0f, -0.2f, 0f),
                 new(-1.5f, -0.5f, -0.5f), new(1.5f, -0.5f, -0.5f) },
    };

    // ── 射击（普通模式）──
    public float ShotInterval { get; set; } = 0.067f;
    public float ShotSpeed { get; set; } = 30f;
    public float ShotDamage { get; set; } = 10f;
    public float ShotRadius { get; set; } = 0.15f;
    public int ShotsPerOption { get; set; } = 2;
    public float ShotConeAngle { get; set; } = 30f;
    public float ShotHomingStrength { get; set; } = 8f;

    // ── 射击（低速模式）──
    public float FocusShotInterval { get; set; } = 0.05f;
    public float FocusShotSpeed { get; set; } = 50f;
    public float FocusShotDamage { get; set; } = 15f;
    public int FocusShotsPerOption { get; set; } = 1;
    public float FocusShotConeAngle { get; set; } = 8f;
    public float FocusShotHomingStrength { get; set; } = 3f;

    // ── Bomb ──
    public float BombDuration { get; set; } = 3f;
    public float BombInvincibleDuration { get; set; } = 5f;
    public float BombClearRadius { get; set; } = 30f;

    // ── 道具拾取 ──
    public float ItemCollectRadius { get; set; } = 1.5f;
    public AutoCollectTrigger AutoCollectMode { get; set; } = AutoCollectTrigger.HighPower;
    public int LifeFragmentsPerLife { get; set; } = 3;
    public int BombFragmentsPerBomb { get; set; } = 3;
    public int BasePointItemValue { get; set; } = 10000;

    // ── 预设 ──
    public static PlayerProfile TouhouDefault => new()
    {
        Id = "touhou_default",
        Name = "东方系默认",
    };
}

[TypeTag("power_tier")]
public class PowerTier
{
    public float Threshold { get; set; }
    public int OptionCount { get; set; }
}

public enum AutoCollectTrigger
{
    Manual,     // 不自动回收，只靠走过去拾取
    HighPower,  // Power 满时自动回收
}
```

---

## 四、PlayerState 扩展

现有字段保留，新增 Power/Score/Bomb 状态/碎片/死亡复活。

```csharp
public class PlayerState
{
    public Vector3 Position;

    // ── 生存 ──
    public int Lives;
    public int Bombs;
    public bool IsInvincible;
    public float InvincibleTimer;
    public float InvincibleDuration;

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

    // ── 擦弹 ──
    public int GrazeTotal;
    public int GrazeThisFrame;

    // ── 输入状态 ──
    public bool IsSlow;

    // ── 判定 ──
    public float HitboxRadius;
    public float GrazeRadius;

    // ── 死亡/复活（新增）──
    public bool IsDead;
    public float RespawnTimer;
    public float RespawnInvincibleDuration;

    // ── 碎片（新增）──
    public int LifeFragments;
    public int BombFragments;

    // ── 从 Profile 初始化 ──
    public static PlayerState FromProfile(PlayerProfile profile, Vector3 spawnPos);
}
```

---

## 五、死亡/复活流程

状态机：

```
Normal → Dying → Respawning → Normal
                              ↘ Dead (Lives=0)
```

### 被弹 → Dying

1. `Lives--`
2. `Power -= DeathPowerLoss`（不低于 0）
3. 在死亡位置生成 Power 掉落道具（`SpawnDeathDrop`）
4. 玩家视觉消失，进入 Dying 状态
5. 持续 0.5 秒

### Dying → Respawning

1. 玩家回到出生点（场景中央偏后）
2. 视觉重新出现
3. 触发全屏消弹（复用 BulletClear 逻辑）
4. 进入无敌（`RespawnInvincibleDuration`）

### Lives = 0 → Dead

1. HUD 显示 "GAME OVER"
2. 暂停时间轴播放
3. 3 秒后自动恢复或按任意键恢复，重置 PlayerState

---

## 六、Bomb 系统

```
鼠标右键 → Bombs > 0 → Bombs-- → IsBombing = true
  → 全屏消弹（复用 BulletClear，半径 = BombClearRadius）
  → 无敌持续 BombInvincibleDuration
  → BombTimer 归零 → IsBombing = false
```

Bomb 期间玩家可正常移动。不做特殊弹幕效果（试玩级）。

---

## 七、射击系统

### 7.1 浮游炮架构

- 射击源是玩家周围的浮游炮（Option），不从玩家身上发射
- 浮游炮位置有 Offset，避开屏幕中心视野
- 浮游炮数量随 Power 段位变化：2 → 4 → 6（偶数递增）
- Offset 是相对玩家朝向的局部坐标，玩家转向时浮游炮跟着转

### 7.2 追踪弹

所有玩家子弹带追踪（Homing）能力，3D 空间中锥形散射：

| | 普通模式 | 低速（Focus）模式 |
|---|---|---|
| 锥角 | 30°（广域） | 8°（集中） |
| 弹速 | 30 m/s | 50 m/s |
| 追踪强度 | 8 rad/s（强追踪） | 3 rad/s（弱追踪） |
| 每炮弹数 | 2 | 1 |
| 单发伤害 | 10 | 15 |
| 适用 | 不用瞄准，自动清场 | 精准集火 Boss |

追踪逻辑：每帧用 `Vector3.RotateTowards` 向最近 HitTarget 转向。无目标时直飞。

### 7.3 命中目标接口

```csharp
public struct HitTarget
{
    public Vector3 Position;
    public float Radius;
    public float Health;
    public Action<float> ApplyDamage;
}
```

BossPlaceholder 和 EnemyPlaceholder 提供 HitTarget。

### 7.4 浮游炮渲染接口

```csharp
public interface IOptionVisual
{
    GameObject Create(Transform parent, int optionIndex);
    void UpdateTransform(Vector3 worldPosition, Quaternion rotation, float dt);
    void OnPowerTierChanged(int newOptionCount);
    void Destroy();
}
```

默认实现 `SphereOptionVisual`（球体占位），后期可替换为实际模型。

### 7.5 输入

- 鼠标左键：射击（按住持续）
- 左 Ctrl：低速模式（切换射击模式 + 移速降低）

### 7.6 渲染

玩家子弹数量少（同屏几十颗），复用 BulletRenderer 的 GPU Instancing 批次，用不同颜色区分。

---

## 八、道具拾取系统

### 8.1 现状

ItemPreviewSystem 已有：生成、物理模拟、AutoCollect 动画、Gizmos 渲染。

### 8.2 新增：拾取判定

ItemPreviewSystem 新增 `CheckPickup(playerPos, collectRadius)` 方法，返回 `ItemPickupResult`（各类型拾取计数）。

### 8.3 道具效果

纯静态方法 `ItemEffects.Apply(pickup, state, profile)`：

- PowerSmall/PowerLarge/FullPower → `state.Power`
- PointItem → `state.Score`
- LifeFragment → 累积到阈值 → `state.Lives++`
- BombFragment → 累积到阈值 → `state.Bombs++`

### 8.4 自动回收

`AutoCollectTrigger.HighPower`：Power 达到 MaxPower 时触发全屏回收。
回收目标改为动态跟踪玩家位置（而非固定坐标）。

### 8.5 死亡掉落

玩家死亡时在死亡位置生成 PowerSmall 道具，数量 = `ceil(DeathPowerLoss / PowerPerSmallItem)`。

---

## 九、边界约束

PlayerController 和 SimulatedPlayer 统一在 FixedTick 移动后 clamp 到 SandboxBoundary。
边界在 Initialize 时从场景中的 SandboxBoundary 组件获取。

---

## 十、HUD

扩展现有 PatternSandboxSetup 的右上角 HUD：

```
★ 3  ✦ 3  P 2.35/4.00
Score: 1,234,000
Graze: 142
◆×2 ◇×1
```

显示：Lives / Bombs / Power / Score / Graze / 残机碎片 / Bomb 碎片。

---

## 十一、编辑器集成

### 11.1 Player 按钮流程

1. 加载 PlayerProfile（`STGData/PlayerProfiles/` 下的 YAML，或 TouhouDefault）
2. 创建 PlayerController + PlayerShotSystem + 注入 ItemPreviewSystem
3. 禁用 FreeCameraController，启用 PlayerCamera

### 11.2 AI Sim 按钮

同上但创建 SimulatedPlayer（AI 不射击）。

### 11.3 Game Over

残机归零 → HUD 显示 "GAME OVER" → 暂停播放 → 3 秒后或按键恢复。

---

## 十二、文件结构

```
新增：
  Core/WorldScale.cs                         尺寸常量
  Core/DataModel/PlayerProfile.cs            玩家配置数据模型
  Runtime/Player/PlayerShotSystem.cs         浮游炮射击系统
  Runtime/Player/IOptionVisual.cs            浮游炮渲染接口
  Runtime/Player/SphereOptionVisual.cs       球体占位实现
  Runtime/Player/ItemEffects.cs              道具效果（纯静态方法）

修改：
  Runtime/Player/PlayerState.cs              扩展 Power/Score/Bomb/碎片/死亡
  Runtime/Player/PlayerController.cs         Profile 驱动，边界，Bomb，死亡流程，射击集成
  Runtime/Player/SimulatedPlayer.cs          Profile 驱动，边界
  Runtime/Preview/ItemPreviewSystem.cs       CheckPickup / SpawnDeathDrop / 动态回收目标
  Editor/Scene/PatternSandboxSetup.cs        Profile 加载，HUD 扩展，Game Over
```
