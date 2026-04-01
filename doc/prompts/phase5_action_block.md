# Phase 5：ActionEvent 功能块系统设计

> 为 Timeline 引入通用脚本事件机制，统一管理非弹幕/非敌人的游戏流程控制。

---

## 一、设计目标

在现有 `SpawnPatternEvent` / `SpawnWaveEvent` 之外，新增 `ActionEvent` 事件族，覆盖：
- 演出控制（标题显示、屏幕特效、背景切换、BGM/SE）
- 游戏逻辑（消弹、道具掉落、自动回收、结算）
- 流程控制（等待条件、分支跳转）

**使用范围**：MidStage 和 BossFight 均可使用（用户已确认）。

---

## 二、架构决策

### 2.1 数据层：继承 TimelineEvent

```
TimelineEvent (abstract)
├── SpawnPatternEvent    [TypeTag("spawn_pattern")]
├── SpawnWaveEvent       [TypeTag("spawn_wave")]
└── ActionEvent          [TypeTag("action")]
    ├── ActionType (enum) — 区分具体功能
    └── Params (IActionParams) — 强类型参数接口
```

**为什么用单一 ActionEvent + ActionType 枚举，而不是每种功能一个子类？**
- 避免类爆炸（11+ 种功能 = 11+ 个子类 + 11 个 TypeTag）
- 序列化统一：YAML 中只需一个 `type: action`，用 `action_type` 字段区分
- 编辑器 UI 统一：一个 ActionBlock 类 + 按 ActionType 动态渲染属性面板
- 新增功能类型只需扩展枚举 + 参数定义，不需要改序列化/Block/Layer 代码

### 2.2 参数存储：强类型参数类

每种 ActionType 对应一个实现 `IActionParams` 的参数类。反序列化时根据 `action_type` 字段决定实例化哪个参数类。

```csharp
public interface IActionParams { }

// 注册表：ActionType → IActionParams 具体类型
// ActionParamsRegistry.Resolve(ActionType) → Type
```

### 2.3 在 BossFight 中的位置

BossFight segment 当前只有 `SpellCardIds` 列表。ActionEvent 需要能插入符卡之间。

**方案：BossFight segment 复用 `Events` 列表**

`TimelineSegment.Events` 当前仅 MidStage 使用。扩展为 BossFight 也使用：
- BossFightLayer 在 `RebuildBlockList` 时，除了构建 SpellCardBlock/TransitionBlock，也从 `Events` 中提取 ActionEvent 构建 ActionBlock
- ActionEvent 的 `StartTime` 在 BossFight 中表示相对于 segment 起始的绝对时间
- 编辑器中 ActionBlock 与 SpellCardBlock 共存于同一轨道，按时间排列

这样不需要改 TimelineSegment 的数据结构，只需要让 BossFight 也读取 Events。

### 2.4 阻塞机制：时间轴冻结模型

#### 问题

部分 ActionEvent 会破坏绝对时间逻辑——它们的实际时长在设计时不可确定：
- 对话（等待玩家按键确认，可能无超时）
- 等待条件（等待敌人全灭/Boss 血量降到阈值）
- 结算（显示固定时长，但可能需要玩家确认跳过）

这和 BossFight 中 SpellCard 的 TimeLimit 是同类问题（Boss 可能提前被击破），但方向相反：
- SpellCard 提前结束 → 后续符卡**顺移提前**
- 阻塞事件 → 后续事件**暂停等待**

#### 核心原则

**阻塞事件不改变任何其他事件的 StartTime 数据。** 它在运行时"冻结时间轴推进"：

1. 时间轴推进到阻塞 ActionEvent 的 StartTime
2. 全局时间停止增长（`CurrentTime` 冻结）
3. 阻塞事件自身执行（显示对话/等待条件/播放结算）
4. 阻塞解除（玩家确认/条件满足/超时到达）
5. 时间轴恢复推进

#### 两种阻塞形态

根据是否有超时上限，阻塞事件在编辑器中有两种视觉表示：

| 形态 | 数据条件 | 编辑器表现 | 典型场景 |
|------|---------|-----------|---------|
| **阻塞节点** (Marker) | `Duration = 0`, `Timeout = 0` | 竖线 + 菱形标记，零宽度 | 对话（无超时）、玩家确认 |
| **阻塞块** (Block) | `Duration > 0` 或 `Timeout > 0` | 正常块，宽度 = 超时上限，带 ⏸ 标记 | WaitCondition（有超时）、ScoreTally |

#### 阻塞标注规则

ActionEvent 新增字段：

```csharp
/// <summary>
/// Whether this action freezes timeline progression until completed.
/// When true, CurrentTime stops advancing at this event's StartTime.
/// </summary>
public bool Blocking { get; set; } = false;

/// <summary>
/// Maximum wait time before auto-resuming (seconds). 0 = no timeout (infinite wait).
/// Only meaningful when Blocking = true.
/// Displayed as block width in the editor; 0 = marker (zero-width).
/// </summary>
public float Timeout { get; set; } = 0f;
```

各 ActionType 的默认阻塞行为：

| ActionType | Blocking 默认 | Timeout 默认 | 说明 |
|-----------|:---:|:---:|------|
| ShowTitle | false | 0 | 非阻塞，叠加显示。可手动设为阻塞（符卡宣言等待确认） |
| ScreenEffect | false | 0 | 非阻塞，纯视觉 |
| BgmControl | false | 0 | 非阻塞，即时触发 |
| SePlay | false | 0 | 非阻塞，即时触发 |
| BackgroundSwitch | false | 0 | 非阻塞，即时触发 |
| BulletClear | false | 0 | 非阻塞，即时触发 |
| ItemDrop | false | 0 | 非阻塞，即时触发 |
| AutoCollect | false | 0 | 非阻塞，即时触发 |
| **ScoreTally** | **true** | **3.0** | **⚠ 阻塞**，有超时上限（结算动画时长） |
| **WaitCondition** | **true** | **30.0** | **⚠ 阻塞**，有超时上限（可设为 0 = 无限等待） |
| **BranchJump** | false | 0 | 非阻塞，瞬时判断跳转 |

> **⚠ 破坏绝对时间的类型**：ScoreTally、WaitCondition，以及任何被手动设为 `Blocking=true` 的事件。编辑器中这些事件带特殊视觉标记（⏸ 图标 + 斜线填充）。

#### 运行时阻塞流程

```
TimelinePlaybackController.Tick(dt):
  if (_blockingEvent != null):
    // 时间冻结，不推进 CurrentTime
    _blockingElapsed += dt
    if 阻塞解除条件满足 OR (_blockingEvent.Timeout > 0 AND _blockingElapsed >= _blockingEvent.Timeout):
      _blockingEvent = null
      _blockingElapsed = 0
      // 恢复正常推进
    return

  // 正常推进 CurrentTime
  CurrentTime += dt
  // 检查是否到达新的阻塞事件
  foreach ActionEvent in segment.Events:
    if ae.Blocking AND CurrentTime >= ae.StartTime AND 未执行过:
      _blockingEvent = ae
      _blockingElapsed = 0
      执行阻塞事件（显示对话/等待条件/结算）
      return
```

#### 编辑器中的视觉表示

```
非阻塞 ActionEvent（Duration > 0）：
0s    5s    10s
|─────|─────|
[🎵 BGM Play  ]     ← 普通块，正常宽度

非阻塞 ActionEvent（Duration = 0，瞬时）：
0s    5s    10s
|─────|─────|
      💥              ← 菱形标记点，零宽度

阻塞 ActionEvent（有超时上限）：
0s    5s    10s
|─────|─────|
[⏸ Wait ////]        ← 块 + ⏸图标 + 斜线填充，宽度=Timeout

阻塞 ActionEvent（无超时，Timeout=0）：
0s    5s    10s
|─────|─────|
      ⏸◆              ← 菱形标记 + ⏸图标，零宽度，特殊颜色（红色边框）
```

### 2.5 镜头控制

暂不纳入 ActionBlock 体系，后续单独设计（用户已确认）。

---

## 三、ActionType 完整定义

### 3.1 演出控制类

| ActionType | 说明 | 时间模型 | 关键参数 |
|-----------|------|---------|---------|
| `ShowTitle` | 标题/符卡宣言/章节名 | 时间点 + 持续时间 | Text, SubText, AnimationType, ScreenPosition |
| `ScreenEffect` | 屏幕震动/闪白/径向模糊/色调偏移 | 时间点 + 持续时间 | EffectType, Intensity, DecayCurve |
| `BgmControl` | BGM 切换/淡入淡出/停止 | 时间点 | BgmId, FadeInDuration, FadeOutDuration, LoopPoint |
| `SePlay` | 播放音效 | 时间点 | SeId, Volume, Pitch |
| `BackgroundSwitch` | 切换背景层/滚动速度 | 时间点 | BackgroundId, TransitionType, TransitionDuration, ScrollSpeed |

### 3.2 游戏逻辑类

| ActionType | 说明 | 时间模型 | 阻塞 | 关键参数 |
|-----------|------|---------|:---:|---------|
| `BulletClear` | 消弹 | 瞬时 | — | ClearShape, Origin, Radius, ConvertToScore |
| `ItemDrop` | 道具掉落 | 瞬时 | — | ItemType, Count, DropPosition, DropPattern |
| `AutoCollect` | 全屏道具自动回收 | 瞬时 | — | Delay |
| `ScoreTally` | 章节/符卡结算 | 时间点 + 持续时间 | **⚠ 阻塞** | TallyType, DisplayDuration |

### 3.3 流程控制类

| ActionType | 说明 | 时间模型 | 阻塞 | 关键参数 |
|-----------|------|---------|:---:|---------|
| `WaitCondition` | 暂停 timeline 直到条件满足 | 时间点 | **⚠ 阻塞** | ConditionType, TargetValue, Timeout |
| `BranchJump` | 条件分支跳转 | 瞬时 | — | Condition, TargetSegmentId, FallbackSegmentId |

> **⚠ 阻塞标记说明**：标记为"⚠ 阻塞"的类型默认 `Blocking=true`，会冻结时间轴推进。其他类型默认非阻塞，但可手动设为 `Blocking=true`（如对话式 ShowTitle）。详见 §2.4。

---

## 四、已有实体属性扩展

以下功能不独立成块，作为已有数据模型的属性：

### 4.1 SpellCard 扩展

```csharp
// 新增字段
public float InvincibilityDuration { get; set; } = 1.0f;  // 符卡切换时的无敌帧
public Bounds? PlayerBounds { get; set; } = null;          // 玩家移动区域限制，null=不限制
public float TimeScale { get; set; } = 1.0f;              // 时间流速（慢动作演出）
```

### 4.2 TimelineSegment 扩展

```csharp
// 新增字段
public DifficultyFilter Difficulty { get; set; } = DifficultyFilter.All;  // 难度过滤
public int RepeatCount { get; set; } = 1;                                  // 循环次数
public int PowerOverride { get; set; } = -1;                               // -1=不改变
public PathKeyframe[] BossEntrancePath { get; set; } = null;               // Boss 登场路径（BossFight 专用）
```

### 4.3 DifficultyFilter 枚举

```csharp
[Flags]
public enum DifficultyFilter
{
    Easy    = 1,
    Normal  = 2,
    Hard    = 4,
    Lunatic = 8,
    All     = Easy | Normal | Hard | Lunatic
}
```

---

## 五、数据模型详细设计

### 5.1 核心类

```csharp
// ═══ Core/Timeline/ActionEvent.cs ═══

[TypeTag("action")]
public class ActionEvent : TimelineEvent
{
    /// <summary>Duration of this action. 0 = instant (point event / marker).</summary>
    public override float Duration { get; set; } = 0f;

    /// <summary>What kind of action this is.</summary>
    public ActionType ActionType { get; set; }

    /// <summary>Type-specific parameters. Concrete type determined by ActionType.</summary>
    public IActionParams Params { get; set; }

    /// <summary>
    /// Whether this action freezes timeline progression until completed.
    /// When true, CurrentTime stops advancing at this event's StartTime.
    /// Default depends on ActionType (ScoreTally/WaitCondition default true).
    /// </summary>
    public bool Blocking { get; set; } = false;

    /// <summary>
    /// Maximum wait time before auto-resuming (seconds). 0 = no timeout (infinite wait).
    /// Only meaningful when Blocking = true.
    /// Editor display: Timeout > 0 → block with width=Timeout; Timeout = 0 → marker (zero-width).
    /// </summary>
    public float Timeout { get; set; } = 0f;
}
```

```csharp
// ═══ Core/Timeline/ActionType.cs ═══

public enum ActionType
{
    // 演出控制
    ShowTitle,
    ScreenEffect,
    BgmControl,
    SePlay,
    BackgroundSwitch,

    // 游戏逻辑
    BulletClear,
    ItemDrop,
    AutoCollect,
    ScoreTally,

    // 流程控制
    WaitCondition,
    BranchJump
}
```

### 5.2 参数接口与注册

```csharp
// ═══ Core/Timeline/ActionParams/IActionParams.cs ═══

/// <summary>Marker interface for action-specific parameter classes.</summary>
public interface IActionParams { }
```

```csharp
// ═══ Core/Timeline/ActionParams/ActionParamsRegistry.cs ═══

/// <summary>
/// Maps ActionType → IActionParams concrete type.
/// Used by serializer to instantiate the correct params class.
/// </summary>
public static class ActionParamsRegistry
{
    private static readonly Dictionary<ActionType, Type> _map = new()
    {
        { ActionType.ShowTitle,        typeof(ShowTitleParams) },
        { ActionType.ScreenEffect,     typeof(ScreenEffectParams) },
        { ActionType.BgmControl,       typeof(BgmControlParams) },
        { ActionType.SePlay,           typeof(SePlayParams) },
        { ActionType.BackgroundSwitch, typeof(BackgroundSwitchParams) },
        { ActionType.BulletClear,      typeof(BulletClearParams) },
        { ActionType.ItemDrop,         typeof(ItemDropParams) },
        { ActionType.AutoCollect,      typeof(AutoCollectParams) },
        { ActionType.ScoreTally,       typeof(ScoreTallyParams) },
        { ActionType.WaitCondition,    typeof(WaitConditionParams) },
        { ActionType.BranchJump,       typeof(BranchJumpParams) },
    };

    public static Type Resolve(ActionType type) =>
        _map.TryGetValue(type, out var t) ? t : null;

    public static IActionParams CreateDefault(ActionType type)
    {
        var t = Resolve(type);
        return t != null ? (IActionParams)Activator.CreateInstance(t) : null;
    }
}
```

### 5.3 各参数类定义

```csharp
// ═══ Core/Timeline/ActionParams/ShowTitleParams.cs ═══

public enum TitleAnimationType { FadeIn, SlideLeft, SlideRight, Expand, TypeWriter }
public enum ScreenPosition { TopCenter, Center, BottomCenter, TopLeft, TopRight }

public class ShowTitleParams : IActionParams
{
    public string Text { get; set; } = "";
    public string SubText { get; set; } = "";           // 副标题（可选）
    public TitleAnimationType Animation { get; set; } = TitleAnimationType.FadeIn;
    public ScreenPosition Position { get; set; } = ScreenPosition.Center;
    public float FadeOutDelay { get; set; } = 2.0f;     // 开始淡出前的停留时间
}
```

```csharp
// ═══ Core/Timeline/ActionParams/ScreenEffectParams.cs ═══

public enum ScreenEffectType { Shake, FlashWhite, FlashRed, RadialBlur, ColorShift }

public class ScreenEffectParams : IActionParams
{
    public ScreenEffectType EffectType { get; set; } = ScreenEffectType.Shake;
    public float Intensity { get; set; } = 1.0f;        // 0~1 归一化强度
    // Duration 由 ActionEvent.Duration 控制
}
```

```csharp
// ═══ Core/Timeline/ActionParams/BgmControlParams.cs ═══

public enum BgmAction { Play, Stop, FadeOut, CrossFade }

public class BgmControlParams : IActionParams
{
    public BgmAction Action { get; set; } = BgmAction.Play;
    public string BgmId { get; set; } = "";              // 音乐资源 ID
    public float FadeInDuration { get; set; } = 1.0f;
    public float FadeOutDuration { get; set; } = 1.0f;
    public float LoopStartTime { get; set; } = 0f;       // 循环起始点（秒）
}
```

```csharp
// ═══ Core/Timeline/ActionParams/SePlayParams.cs ═══

public class SePlayParams : IActionParams
{
    public string SeId { get; set; } = "";               // 音效资源 ID
    public float Volume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
}
```

```csharp
// ═══ Core/Timeline/ActionParams/BackgroundSwitchParams.cs ═══

public enum BgTransitionType { Cut, CrossFade, SlideUp, SlideDown }

public class BackgroundSwitchParams : IActionParams
{
    public string BackgroundId { get; set; } = "";       // 背景资源 ID
    public BgTransitionType Transition { get; set; } = BgTransitionType.CrossFade;
    public float TransitionDuration { get; set; } = 1.0f;
    public float ScrollSpeedX { get; set; } = 0f;
    public float ScrollSpeedY { get; set; } = -0.5f;    // 默认向下滚动
}
```

```csharp
// ═══ Core/Timeline/ActionParams/BulletClearParams.cs ═══

public enum ClearShape { FullScreen, Circle, Rectangle, Line }

public class BulletClearParams : IActionParams
{
    public ClearShape Shape { get; set; } = ClearShape.FullScreen;
    public Vector3 Origin { get; set; } = Vector3.zero;  // 消弹中心点
    public float Radius { get; set; } = 50f;             // Circle 模式的半径
    public Vector3 Extents { get; set; } = Vector3.one * 25f; // Rectangle 模式的半尺寸
    public bool ConvertToScore { get; set; } = true;     // 弹幕转化为得分道具
    public float ExpandSpeed { get; set; } = 30f;        // 消弹扩散速度（0=瞬间）
}
```

```csharp
// ═══ Core/Timeline/ActionParams/ItemDropParams.cs ═══

public enum ItemType { PowerSmall, PowerLarge, PointItem, BombFragment, LifeFragment, FullPower }
public enum DropPattern { AtPosition, FromBoss, RandomSpread, ArcSpread }

public class ItemDropParams : IActionParams
{
    public ItemType Type { get; set; } = ItemType.PointItem;
    public int Count { get; set; } = 1;
    public DropPattern Pattern { get; set; } = DropPattern.FromBoss;
    public Vector3 Position { get; set; } = Vector3.zero; // AtPosition 模式使用
    public float SpreadRadius { get; set; } = 3f;         // RandomSpread/ArcSpread 的范围
}
```

```csharp
// ═══ Core/Timeline/ActionParams/AutoCollectParams.cs ═══

public class AutoCollectParams : IActionParams
{
    public float Delay { get; set; } = 0f;               // 延迟触发（秒）
}
```

```csharp
// ═══ Core/Timeline/ActionParams/ScoreTallyParams.cs ═══

public enum TallyType { SpellCardBonus, ChapterClear, StageClear }

public class ScoreTallyParams : IActionParams
{
    public TallyType Type { get; set; } = TallyType.ChapterClear;
    public float DisplayDuration { get; set; } = 3.0f;   // 结算画面显示时长
    // 阻塞行为由 ActionEvent.Blocking (默认true) + Timeout (默认=DisplayDuration) 控制
}
```

```csharp
// ═══ Core/Timeline/ActionParams/WaitConditionParams.cs ═══

public enum WaitConditionType
{
    AllEnemiesDefeated,   // 所有敌人被击破
    BossHealthBelow,      // Boss 血量低于阈值
    TimeElapsed,          // 等待固定时间（冗余但方便）
    PlayerConfirm         // 等待玩家按键确认
}

public class WaitConditionParams : IActionParams
{
    public WaitConditionType Condition { get; set; } = WaitConditionType.AllEnemiesDefeated;
    public float TargetValue { get; set; } = 0f;         // BossHealthBelow 的阈值百分比
    // 超时由 ActionEvent.Timeout 控制（默认30s），不在此重复
}
```

```csharp
// ═══ Core/Timeline/ActionParams/BranchJumpParams.cs ═══

public enum BranchCondition
{
    DifficultyIs,         // 当前难度匹配
    BossHealthBelow,      // Boss 血量低于阈值
    PlayerLivesBelow,     // 玩家残机低于阈值
    Always                // 无条件跳转
}

public class BranchJumpParams : IActionParams
{
    public BranchCondition Condition { get; set; } = BranchCondition.Always;
    public float ConditionValue { get; set; } = 0f;
    public string TargetSegmentId { get; set; } = "";    // 条件满足时跳转目标
    public string FallbackSegmentId { get; set; } = "";  // 条件不满足时的目标（空=继续）
}
```

---

## 六、YAML 序列化格式

### 6.1 ActionEvent 在 Stage YAML 中的格式

```yaml
segments:
- id: midstage_1
  name: Stage 1
  type: mid_stage
  duration: 60
  events:
  # 非阻塞，有持续时间 → 普通块
  - type: action
    id: act_a1b2c3
    start_time: 0
    duration: 3
    action_type: show_title
    params:
      text: "Stage 1"
      sub_text: "Moonlight Descent"
      animation: fade_in
      position: center
      fade_out_delay: 2.0

  # 非阻塞，瞬时 → 菱形标记点
  - type: action
    id: act_d4e5f6
    start_time: 0
    duration: 0
    action_type: bgm_control
    params:
      action: play
      bgm_id: bgm_stage1
      fade_in_duration: 2.0

  - type: spawn_pattern
    id: evt_abc123
    start_time: 3
    duration: 10
    pattern_id: opening_barrage
    spawn_position: {x: 0, y: 5, z: 0}

  # ⚠ 阻塞，有超时上限 → 阻塞块（宽度=30s）
  - type: action
    id: act_wait1
    start_time: 50
    duration: 0
    action_type: wait_condition
    blocking: true
    timeout: 30
    params:
      condition: all_enemies_defeated
      target_value: 0

  # 非阻塞，瞬时 → 菱形标记点
  - type: action
    id: act_g7h8i9
    start_time: 55
    duration: 0
    action_type: bullet_clear
    params:
      shape: full_screen
      convert_to_score: true

  # ⚠ 阻塞，有超时上限 → 阻塞块（宽度=3s）
  - type: action
    id: act_tally1
    start_time: 58
    duration: 0
    action_type: score_tally
    blocking: true
    timeout: 3
    params:
      type: chapter_clear
      display_duration: 3.0

- id: boss_phase_1
  name: Boss Phase 1
  type: boss_fight
  duration: 120
  spell_card_ids: [sc_001, sc_002, sc_003]
  events:
  # BossFight 中的 ActionEvent：按绝对时间触发
  - type: action
    id: act_boss_enter
    start_time: 0
    duration: 2
    action_type: show_title
    params:
      text: "Sakuya Izayoi"
      sub_text: ""
      animation: slide_left
      position: top_center

  # ⚠ 阻塞，无超时 → 阻塞标记点（菱形 + ⏸ + 红色边框）
  - type: action
    id: act_dialog1
    start_time: 0
    duration: 0
    action_type: wait_condition
    blocking: true
    timeout: 0
    params:
      condition: player_confirm
```

### 6.2 序列化实现要点

在 `YamlSerializer` 中扩展：

1. **序列化**（`SerializeStage` → `EmitEvent`）：
   - 检测 `evt is ActionEvent ae`
   - 写入 `type: action`, `action_type: {snake_case}`
   - 仅当 `ae.Blocking` 为 true 时写入 `blocking: true`（默认 false 省略）
   - 仅当 `ae.Timeout > 0` 时写入 `timeout: {value}`（默认 0 省略）
   - 写入 `params:` mapping，按 `ae.Params` 的具体类型反射写入各字段

2. **反序列化**（`MapTimelineEvent`）：
   - 新增 `case "action":` 分支
   - 读取 `action_type` 字段，转为 `ActionType` 枚举
   - 通过 `ActionParamsRegistry.Resolve(actionType)` 获取参数类型
   - 从 `params` mapping 反射实例化参数对象

---

## 七、编辑器集成

### 7.1 ActionBlock（新建）

```csharp
// Editor/UI/Timeline/Layers/ActionBlock.cs

public class ActionBlock : ITimelineBlock
{
    private readonly ActionEvent _event;

    public string Id => _event.Id;
    public string DisplayLabel => GetDisplayLabel();
    public float StartTime { get; set; }
    public float Duration { get; set; }
    public Color BlockColor => GetActionColor(_event.ActionType);
    public bool CanMove => true;  // 自由定位（同 EventBlock）
    public float DesignEstimate { get; set; } = -1f;
    public object DataSource => _event;
    public bool IsModified => false;
    public bool HasThumbnail => false;

    /// <summary>Whether this block represents a blocking event.</summary>
    public bool IsBlocking => _event.Blocking;

    /// <summary>
    /// Whether this block should render as a zero-width marker (diamond shape)
    /// instead of a rectangular block.
    /// True when: Duration=0 AND (not blocking OR blocking with Timeout=0).
    /// </summary>
    public bool IsMarker => Duration <= 0f && _event.Timeout <= 0f;

    /// <summary>
    /// Effective display width for blocking events.
    /// Blocking + Timeout > 0 → width = Timeout (shown as hatched block).
    /// Blocking + Timeout = 0 → marker (zero-width, special icon).
    /// </summary>
    public float EffectiveWidth => _event.Blocking && _event.Timeout > 0f
        ? _event.Timeout
        : _event.Duration;

    // 颜色方案：按功能分类
    // 演出类 = 紫色系, 逻辑类 = 橙色系, 流程类 = 青色系
    // 阻塞事件额外叠加斜线填充 + ⏸ 图标
}
```

### 7.2 颜色与图标方案

| 分类 | ActionType | 图标 | 颜色 |
|------|-----------|------|------|
| 演出 | ShowTitle | 📝 | `#9B59B6` 紫色 |
| 演出 | ScreenEffect | ✨ | `#8E44AD` 深紫 |
| 演出 | BgmControl | 🎵 | `#2980B9` 蓝色 |
| 演出 | SePlay | 🔊 | `#3498DB` 浅蓝 |
| 演出 | BackgroundSwitch | 🖼 | `#1ABC9C` 青绿 |
| 逻辑 | BulletClear | 💥 | `#E67E22` 橙色 |
| 逻辑 | ItemDrop | 🎁 | `#F39C12` 金色 |
| 逻辑 | AutoCollect | 🧲 | `#D35400` 深橙 |
| 逻辑 | ScoreTally | 🏆 | `#F1C40F` 黄色 |
| 流程 | WaitCondition | ⏸ | `#16A085` 深青 |
| 流程 | BranchJump | 🔀 | `#27AE60` 绿色 |

### 7.3 MidStageLayer 扩展

`MidStageLayer.RebuildBlockList()` 中，除了为 `SpawnPatternEvent` / `SpawnWaveEvent` 创建 `EventBlock`，新增：
- 检测 `ActionEvent` → 创建 `ActionBlock`
- ActionBlock 与 EventBlock 共存于同一轨道，按 StartTime 排列

右键菜单新增 "Add Action" 子菜单，列出所有 ActionType。

### 7.4 BossFightLayer 扩展

`BossFightLayer.RebuildBlockList()` 中，在构建 SpellCardBlock/TransitionBlock 之后：
- 遍历 `_segment.Events`，为每个 `ActionEvent` 创建 `ActionBlock`
- ActionBlock 按 StartTime 插入到 block 列表的正确位置

### 7.5 属性面板

`BuildPropertiesPanel` 中检测 `ActionBlock`：
- 显示通用字段：ActionType（下拉）、StartTime、Duration
- 显示阻塞字段：Blocking（开关）、Timeout（仅 Blocking=true 时显示）
- 根据 ActionType 动态渲染对应参数类的字段
- 使用 `DataBinder` 绑定参数字段到 UI 控件

### 7.6 预览集成

ActionEvent 在编辑器预览中的表现：
- `ShowTitle`：在 3D 视口上方叠加文字（UI Toolkit overlay）
- `ScreenEffect`：后处理效果预览（Shake = 相机抖动，Flash = 屏幕叠加白色）
- `BulletClear`：显示消弹范围的线框（Circle/Rectangle）
- `BgmControl` / `SePlay`：仅在属性面板显示播放按钮，不影响 3D 视口
- `ItemDrop`：在掉落位置显示道具图标占位符
- 流程控制类：仅在 timeline 上显示标记，不影响 3D 视口

---

## 八、实施计划

### Step 1：数据模型（Core 层） ✅
### Step 2：YAML 序列化 ✅
### Step 3：编辑器 Block 层 ✅
### Step 4：属性面板 ✅

### Step 5：预览集成 ✅
- [x] ShowTitle overlay（5种动画 + 字体/颜色/偏移/图片 + 实时编辑刷新）
- [x] ScreenEffect 相机震动预览（FreeCameraController.ShakeOffset）
- [x] BulletClear 范围 Gizmos 线框（Circle/Rectangle）
- [x] 阻塞机制（时间轴冻结 + 绿色扫描线 + playhead 停在左边缘）
- [x] BossFight 右键添加/删除 ActionEvent

### Step 6：SpellCard/Segment 属性 UI ✅

### 额外完成项
- [x] Timeout 字段合并到 Duration（简化数据模型）
- [x] 阻塞 playhead 视觉：块内绿色扫描线 + playhead 停在左边缘
- [x] ActionEventPreviewController 统一管理预览效果
- [x] Delete 键 / Copy / Paste / Duplicate 全层级支持
- [x] 弹窗 ClampPopupToScreen（全部 9 个弹窗位置）
- [x] 音频系统：IAudioBackend + UnityAudioBackend + AudioService
- [x] BGM：单轨交叉淡入淡出 + 自定义循环点
- [x] SE：对象池（最大并发可配置 8-64）+ 30ms 同 clip 节流
- [x] SE/BGM 在 StartTime 即触发（不依赖 Duration 范围）
- [x] Seek 回退时清理已触发 ID，音效可重复播放
- [x] SE/BGM 块不可 resize，Duration 自动检测 clip 时长
- [x] SE Loop：Duration snap 到 N × clip 长度，+/- 按钮调整循环次数
- [x] MaxConcurrentSe 作为 Gameplay 设置，ESC 菜单可配置

---

## 九、已知问题与 Gotcha

1. **FreeCameraController 覆盖相机位置**：不能直接修改 camera.transform，必须通过 ShakeOffset 属性叠加偏移
2. **tempSegment 不包含 ActionEvent**：LoadMidStagePreview / LoadStageOverviewPreview / LoadBossFightPreview 创建临时 segment 时必须复制 ActionEvent，否则阻塞/音频不生效
3. **YamlDotNet 嵌套字典类型**：反序列化时嵌套 mapping 返回 Dictionary<object,object>，ConvertToType 需要先 ToStringDict 转换
4. **SetSegment 每帧调用**：必须用 ReferenceEquals 守卫避免每帧 Reset 清除预览状态
5. **SE/BGM 触发时机**：必须在 StartTime 即触发（一次性），不能用 Duration 范围检查，否则会在块末尾才触发
6. **UI Toolkit 默认字体不支持 Unicode 符号**：ActionBlock 标签使用纯 ASCII 大写标签（TITLE/FX/BGM 等）

---

## 十、未来扩展预留

### 10.1 镜头控制系统
预留 `ActionType.CameraControl`，待独立设计。参数类 `CameraControlParams` 包含：
- CameraAction（Zoom/Pan/Follow/Shake/Reset）
- 关键帧列表
- 过渡曲线

### 10.2 自定义脚本事件
预留 `ActionType.CustomScript`，允许用户编写 Lua/VisualScript 脚本：
```csharp
public class CustomScriptParams : IActionParams
{
    public string ScriptId { get; set; } = "";
    public Dictionary<string, string> Arguments { get; set; } = new();
}
```

### 10.3 护盾/屏障系统
如果需要独立的护盾机制（有血量、可被打破），可新增 `ActionType.SpawnShield`。

### 10.4 Boss 形态切换
`ActionType.BossFormChange`：切换 Boss 外观/模型/动画状态。

### 10.5 粒子/VFX 触发
`ActionType.SpawnVfx`：在指定位置播放一次性粒子特效。
