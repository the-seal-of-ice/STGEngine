# 3D STG 编辑系统 — 确认设计路线

> 本文档记录所有已确认的架构决策和细化的实施路线。
> 所有决策均经过互动式确认。

---

## 一、已确认的架构决策汇总

### 1.1 基础技术栈

| 决策项 | 选择 | 理由 |
|--------|------|------|
| 运行形态 | 独立 Runtime 编辑器 | 可分发给非程序员，类似 Mario Maker |
| 数据承载 | 纯 C# 类 | Runtime 下 SO 丧失 Undo/Inspector 优势；纯 C# 序列化自由度最高 |
| UI 框架 | UI Toolkit | 现代、支持 Flexbox 布局、Runtime/Editor 通用 |
| 序列化库 | YamlDotNet | 支持多态、字典、注释、无嵌套限制 |
| 渲染管线 | URP | 新项目标准选择，Shader Graph 支持 |
| 弹幕渲染 | GPU Instancing | 万级弹幕性能需求，不创建 GameObject |
| 命名空间 | STGEngine.* | 广义命名，不限于弹幕 |
| 程序集 | 3 层：Core / Runtime / Editor | 编译隔离 + 依赖方向清晰 |

### 1.2 核心系统设计

| 决策项 | 选择 | 说明 |
|--------|------|------|
| 弹幕运动模型 | 混合：公式为主 + 模拟补充 | 基础弹幕 f(t,params)，动态行为逐帧模拟 |
| 弹幕参数化 | 发射器 + 修饰器组合 | Emitter + Modifier[]，新弹幕 = 新组合 |
| 弹幕系统架构 | 统一 3D + 约束层 | 所有弹幕在 3D 空间运动，2D = 正交相机 + 锁 Z |
| 模式定义 | 连续参数轴 + 预设 | 三种模式是参数预设，切换 = 参数渐变 |
| 时间轴模型 | 分段式 + 事件触发 | 道中绝对时间，Boss 事件驱动，段落间触发条件连接 |
| 难度系统 | 基准 + 难度修饰器 | Normal 为基准，其他难度通过参数缩放 + 结构变化 |
| Undo/Redo | Command Pattern | 每个操作封装为 Command，压栈管理 |
| 编辑器入口 | 主菜单分流 | 启动后选择编辑模式或游玩模式 |

### 1.3 项目环境

| 项 | 说明 |
|----|------|
| 项目 | 全新 Unity 项目，不修改现有 DanmakuTest_bullet |
| 首步范围 | B+C 混合：项目骨架 + 弹幕编辑器垂直切片 |

---

## 二、程序集与文件夹结构

```
Assets/
├── STGEngine/
│   ├── Core/                          # STGEngine.Core.asmdef
│   │   │                              # 纯 C# 数据模型 + 序列化
│   │   │                              # 不依赖 UnityEngine（除 Vector3 等值类型）
│   │   ├── DataModel/
│   │   │   ├── Stage.cs               # 关卡顶层
│   │   │   ├── Section.cs             # 段落（道中/Boss）
│   │   │   ├── SpellCard.cs           # 符卡
│   │   │   ├── Wave.cs                # 小怪波次
│   │   │   ├── BulletPattern.cs       # 弹幕模式（Emitter + Modifier[]）
│   │   │   ├── EnemyType.cs           # 小怪类型模板
│   │   │   ├── PlayerProfile.cs       # 玩家参数定义
│   │   │   └── ModeParams.cs          # 模式参数轴定义
│   │   ├── Emitters/
│   │   │   ├── IEmitter.cs            # 发射器接口
│   │   │   ├── PointEmitter.cs
│   │   │   ├── RingEmitter.cs
│   │   │   ├── SphereEmitter.cs
│   │   │   └── LineEmitter.cs
│   │   ├── Modifiers/
│   │   │   ├── IModifier.cs           # 修饰器接口
│   │   │   ├── SpeedCurveModifier.cs
│   │   │   ├── HomingModifier.cs
│   │   │   ├── WaveModifier.cs
│   │   │   ├── SplitModifier.cs
│   │   │   └── BounceModifier.cs
│   │   ├── Timeline/
│   │   │   ├── TimelineSegment.cs     # 时间轴段落
│   │   │   ├── TriggerCondition.cs    # 段落间触发条件
│   │   │   └── TimelineEvent.cs       # 时间轴事件基类
│   │   ├── Difficulty/
│   │   │   ├── DifficultyLevel.cs     # 难度等级枚举
│   │   │   └── DifficultyModifier.cs  # 难度修饰器
│   │   ├── Serialization/
│   │   │   ├── YamlSerializer.cs      # YamlDotNet 封装
│   │   │   ├── TypeConverters/        # 自定义类型转换器
│   │   │   └── SchemaVersion.cs       # 版本迁移
│   │   └── Scripting/
│   │       ├── CameraScript.cs        # 镜头脚本数据
│   │       ├── BoundaryScript.cs      # 边界脚本数据
│   │       └── ModeTransition.cs      # 模式切换事件
│   │
│   ├── Runtime/                       # STGEngine.Runtime.asmdef
│   │   │                              # 依赖 Core + UnityEngine
│   │   │                              # 弹幕渲染、游戏循环、预览
│   │   ├── Bullet/
│   │   │   ├── BulletSystem.cs        # 弹幕系统主循环
│   │   │   ├── BulletPool.cs          # GPU Instancing 池
│   │   │   ├── BulletEvaluator.cs     # 公式求值器 f(t, params)
│   │   │   └── BulletSimulator.cs     # 逐帧模拟器（模拟补充）
│   │   ├── Mode/
│   │   │   ├── ModeController.cs      # 模式参数管理 + 渐变
│   │   │   └── ConstraintLayer.cs     # 2D/3D 约束层
│   │   ├── Camera/
│   │   │   ├── GameCamera.cs          # 游戏相机控制
│   │   │   └── EditorCamera.cs        # 编辑器自由相机
│   │   ├── Scene/
│   │   │   ├── StageRunner.cs         # 关卡运行器
│   │   │   └── SectionRunner.cs       # 段落运行器
│   │   ├── Preview/
│   │   │   ├── PatternPreviewer.cs    # 弹幕模式沙盒预览
│   │   │   └── SpellCardPreviewer.cs  # 符卡预览
│   │   └── Rendering/
│   │       ├── BulletRenderer.cs      # GPU Instancing 渲染
│   │       ├── BulletMaterial.cs      # 弹幕材质管理
│   │       └── Shaders/              # URP Shader / Shader Graph
│   │
│   └── Editor/                        # STGEngine.Editor.asmdef
│       │                              # 依赖 Core + Runtime
│       │                              # UI Toolkit 编辑器界面
│       ├── UI/
│       │   ├── MainMenu/
│       │   │   └── MainMenuView.cs    # 主菜单（编辑/游玩分流）
│       │   ├── Timeline/
│       │   │   ├── TimelineView.cs    # 时间轴面板
│       │   │   ├── TimelineTrack.cs   # 时间轴轨道
│       │   │   └── BGMWaveform.cs     # BGM 波形显示
│       │   ├── PatternEditor/
│       │   │   ├── PatternEditorView.cs    # 弹幕编辑面板
│       │   │   ├── EmitterSelector.cs      # 发射器选择器
│       │   │   └── ModifierStack.cs        # 修饰器堆叠编辑
│       │   ├── PropertyPanel/
│       │   │   └── PropertyPanelView.cs    # 通用属性面板
│       │   ├── AssetLibrary/
│       │   │   └── AssetLibraryView.cs     # 资源库面板
│       │   └── Common/
│       │       ├── Breadcrumb.cs           # 面包屑导航
│       │       └── DifficultyToggle.cs     # 难度切换控件
│       ├── Commands/
│       │   ├── ICommand.cs            # Command 接口
│       │   ├── CommandStack.cs        # Undo/Redo 栈
│       │   ├── ModifyPropertyCommand.cs
│       │   ├── AddModifierCommand.cs
│       │   └── RemoveModifierCommand.cs
│       ├── Validation/
│       │   ├── IValidator.cs          # 验证器接口
│       │   ├── BoundaryValidator.cs   # 边界检查
│       │   └── ValidationPanel.cs     # 验证结果面板
│       └── Styles/
│           ├── EditorTheme.uss        # 全局样式
│           └── TimelineStyles.uss     # 时间轴样式
│
├── Scenes/
│   ├── MainMenu.unity                 # 主菜单场景
│   ├── EditorScene.unity              # 编辑器主场景
│   ├── PlayScene.unity                # 游玩场景
│   └── PatternSandbox.unity           # 弹幕沙盒预览场景
│
├── Resources/
│   └── DefaultPatterns/               # 内置弹幕模板
│
└── Plugins/
    └── YamlDotNet/                    # YamlDotNet DLL
```

### 依赖方向

```
Core（纯数据，无 Unity 依赖*）
  ▲
  │
Runtime（弹幕渲染 + 游戏逻辑，依赖 UnityEngine）
  ▲
  │
Editor（UI + 编辑逻辑，依赖 Core + Runtime + UI Toolkit）

* Core 可引用 UnityEngine.dll 中的值类型（Vector3 等），
  但不依赖 MonoBehaviour / GameObject 等运行时概念。
```

---

## 三、核心数据模型草案

### 3.1 弹幕模式（第一步实现的核心）

```csharp
namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// 一个弹幕模式 = 一个发射器 + N 个修饰器
    /// </summary>
    public class BulletPattern
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>发射器：决定弹幕的初始位置和方向分布</summary>
        public IEmitter Emitter { get; set; }

        /// <summary>修饰器列表：按顺序叠加改变弹幕行为</summary>
        public List<IModifier> Modifiers { get; set; } = new();

        /// <summary>难度修饰器：各难度等级的参数缩放</summary>
        public Dictionary<DifficultyLevel, DifficultyModifier> DifficultyOverrides { get; set; } = new();

        /// <summary>子时间轴：控制发射节奏（何时开始/停止/变化）</summary>
        public PatternTimeline Timeline { get; set; }
    }
}
```

### 3.2 发射器接口

```csharp
namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// 发射器：决定弹幕的初始空间分布
    /// 所有发射器必须能回答：给定索引 i（0~count-1），
    /// 第 i 颗弹幕的初始位置和方向是什么？
    /// </summary>
    public interface IEmitter
    {
        string TypeName { get; }

        /// <summary>本次发射的弹幕数量</summary>
        int Count { get; set; }

        /// <summary>
        /// 计算第 i 颗弹幕的初始状态
        /// </summary>
        BulletSpawnData Evaluate(int index, float time);
    }

    public struct BulletSpawnData
    {
        public Vector3 Position;    // 相对于发射点的偏移
        public Vector3 Direction;   // 初始飞行方向（单位向量）
        public float Speed;         // 初始速度
    }
}
```

### 3.3 修饰器接口

```csharp
namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// 修饰器：叠加在弹幕上改变其运动行为
    /// 分为两类：
    /// - 公式型（FormulaModifier）：可以直接计算任意时刻的状态，支持跳转
    /// - 模拟型（SimulationModifier）：需要逐帧更新，不支持跳转
    /// </summary>
    public interface IModifier
    {
        string TypeName { get; }
        bool RequiresSimulation { get; }  // true = 模拟型，false = 公式型
    }

    /// <summary>公式型修饰器：position = f(t, basePosition, params)</summary>
    public interface IFormulaModifier : IModifier
    {
        Vector3 Evaluate(float t, Vector3 basePosition, Vector3 baseDirection);
    }

    /// <summary>模拟型修饰器：需要逐帧 Step</summary>
    public interface ISimulationModifier : IModifier
    {
        void Step(float dt, ref Vector3 position, ref Vector3 velocity);
    }
}
```

### 3.4 模式参数轴

```csharp
namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// 游戏模式不是离散枚举，而是一组连续参数。
    /// 三种"模式"只是预设组合。
    /// </summary>
    public class ModeParams
    {
        /// <summary>移动自由度：0=完全锁定, 1=完全自由</summary>
        public float MovementFreedom { get; set; } = 1f;

        /// <summary>镜头控制权：0=完全脚本, 1=完全玩家</summary>
        public float CameraControl { get; set; } = 1f;

        /// <summary>投影混合：0=正交, 1=透视</summary>
        public float ProjectionBlend { get; set; } = 1f;

        /// <summary>Z轴约束强度：0=完全自由, 1=完全锁定在平面</summary>
        public float ZAxisConstraint { get; set; } = 0f;

        /// <summary>边界类型参数（由具体边界脚本解释）</summary>
        public float BoundaryDynamism { get; set; } = 0f;

        // --- 预设 ---
        public static ModeParams Traditional2D => new()
        {
            MovementFreedom = 0.8f,
            CameraControl = 0f,
            ProjectionBlend = 0f,    // 正交
            ZAxisConstraint = 1f,    // 锁 Z
            BoundaryDynamism = 0f    // 静态边界
        };

        public static ModeParams Free3D => new()
        {
            MovementFreedom = 1f,
            CameraControl = 1f,
            ProjectionBlend = 1f,    // 透视
            ZAxisConstraint = 0f,    // 自由
            BoundaryDynamism = 0f
        };

        public static ModeParams Scripted3D => new()
        {
            MovementFreedom = 0.3f,
            CameraControl = 0f,
            ProjectionBlend = 1f,    // 透视
            ZAxisConstraint = 0f,
            BoundaryDynamism = 1f    // 动态边界
        };
    }
}
```

### 3.5 时间轴段落

```csharp
namespace STGEngine.Core.Timeline
{
    public enum SegmentType
    {
        MidStage,   // 道中：绝对时间轴
        BossFight   // Boss战：事件驱动时间轴
    }

    /// <summary>
    /// 时间轴段落：关卡由多个段落顺序/条件连接组成
    /// </summary>
    public class TimelineSegment
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public SegmentType Type { get; set; }

        /// <summary>本段落的本地时长（道中段落有意义，Boss段落为预估值）</summary>
        public float Duration { get; set; }

        /// <summary>进入本段落的触发条件（第一个段落为 null）</summary>
        public TriggerCondition EntryTrigger { get; set; }

        /// <summary>本段落内的事件列表</summary>
        public List<TimelineEvent> Events { get; set; } = new();

        /// <summary>本段落关联的模式参数（可选，用于模式切换）</summary>
        public ModeParams ModeOverride { get; set; }
    }

    public class TriggerCondition
    {
        public TriggerType Type { get; set; }
        public Dictionary<string, object> Params { get; set; } = new();
    }

    public enum TriggerType
    {
        Immediate,          // 上一段落结束后立即开始
        TimeElapsed,        // 上一段落开始后 N 秒
        AllEnemiesDefeated, // 上一段落所有敌人被击败
        BossDefeated,       // Boss 被击败
        Custom              // 自定义条件
    }
}
```

---

## 四、垂直切片：弹幕编辑器原型

### 4.1 目标

端到端验证技术栈：C# 数据类 → YamlDotNet 序列化 → UI Toolkit 参数面板 → GPU Instancing 渲染 → 沙盒预览。

### 4.2 范围（严格限定）

只做以下内容，不多不少：

**数据层（Core）：**
- `BulletPattern` 类
- 2 个发射器：`PointEmitter`（单点）、`RingEmitter`（环形）
- 2 个公式型修饰器：`SpeedCurveModifier`（速度曲线）、`WaveModifier`（正弦波动）
- YamlDotNet 序列化/反序列化（含多态类型处理）
- 1 个手写的示例 YAML 文件

**渲染层（Runtime）：**
- `BulletEvaluator`：根据 BulletPattern 计算任意时刻所有弹幕位置
- `BulletRenderer`：GPU Instancing 批量渲染（单一球体 mesh + 颜色参数）
- `PatternPreviewer`：沙盒预览控制器（播放/暂停/时间滑块/速度调节）

**编辑层（Editor）：**
- `PatternEditorView`：UI Toolkit 面板
  - 发射器类型下拉选择
  - 发射器参数（count、radius 等）
  - 修饰器列表（添加/删除/排序）
  - 每个修饰器的参数编辑
- 保存/加载 YAML 按钮
- 3 个基础 Command：修改属性、添加修饰器、删除修饰器

**场景：**
- `PatternSandbox.unity`：中心一个发射点 + 标准球形边界 + 自由相机 + UI 面板

### 4.3 不做的内容（明确排除）

- 时间轴系统
- 符卡/小怪/关卡编辑
- 模式切换
- 难度系统
- BGM 集成
- 2D⟷3D 转化
- 碰撞检测
- 玩家角色

### 4.4 验证目标

完成后应该能回答以下问题：
1. YamlDotNet 在 Unity Runtime 中是否正常工作？多态序列化是否可靠？
2. UI Toolkit 在 Runtime 中的性能和交互体验如何？
3. GPU Instancing 渲染 1000+ 弹幕的帧率是否可接受？
4. 公式型弹幕的时间跳转是否真的能瞬间完成？
5. Command Pattern 的 Undo/Redo 在实际编辑操作中是否流畅？
6. 发射器 + 修饰器的组合模式是否足够表达常见弹幕？

---

## 五、垂直切片之后的路线

### Phase 2：扩展弹幕系统 ✅ 已完成

- ✅ 补充更多发射器：SphereEmitter、LineEmitter、ConeEmitter
- ✅ 补充模拟型修饰器：HomingModifier、BounceModifier、SplitModifier
- ✅ 弹幕视觉多样化：不同形状 mesh、颜色曲线、拖尾效果
- ✅ 碰撞体定义 + 可视化叠加显示

详见 9.5 节（Phase 2 Step 1-6 实施记录）。

### Phase 3：时间轴系统 ✅ 已完成

- ✅ 分段式时间轴数据模型（Stage → Segment[]）
- ✅ 时间轴 UI（UI Toolkit 自绘轨道 TrackAreaView）
- ✅ 面包屑导航（两层 → 后续扩展为递归）
- ✅ 段落间触发条件编辑
- ⏳ BGM 波形叠加（推迟至 Phase 3.5）

详见 9.6-9.8 节。

### Phase 4：符卡 + 小怪编辑器 ✅ 已完成

- ✅ 符卡数据模型（SpellCard + SpellCardPattern + BossPath）
- ✅ 小怪类型模板（EnemyType）+ 实例系统（Wave + EnemyInstance）
- ✅ 资源库面板（AssetLibraryPanel，拖拽添加）
- ✅ 波次编辑器（WaveLayer）
- ✅ BossFight Segment 支持 + 符卡预览器

详见 9.9-9.10 节。

### Phase 5：递归 Timeline 层级 + 缩略图 + Override ✅ 已完成（缺陷修复中）

- ✅ Step 1：递归 Timeline 层级导航（ITimelineLayer 6 层实现）
- ✅ Step 2：块内缩略图系统（弹幕轨迹 + 路径 + 色条）
- ✅ Step 3：Modified/Override 机制（OverrideManager + [M] 标记）
- ⚠️ 操作矩阵审计发现 9 个缺陷待修复（详见 9.14 节）

### Phase 6（下一步）：缺陷修复 + 整关编辑 + 模式系统

- 修复 9.14 节操作矩阵中的 P0-P2 缺陷
- 段落编排（全局时间轴）— 已由 StageLayer 实现基础版
- 模式参数轴的实时预览
- 2D⟷3D 投影预览（正交相机快速预览）
- 难度修饰器编辑 + 难度切换预览

### Phase 7：转化系统 + 打磨

- 自动转化规则（层次1）
- 辅助转化工具（层次2）
- 蒙特卡洛难度指标计算
- 验证系统（实时错误检查）
- 运行时二进制格式导出
- 用户引导 / 示例弹幕库

### 待实现设计想法（不属于特定 Phase，可随时插入）

#### 递归 Timeline 层级架构（FL Studio 风格）

> 将编辑器改造为统一的递归 Timeline 层级结构。
> 每一层都是同一个抽象：时间轴上排列"块"，双击进入下一层。
> 块内显示子层级内容的缩略图快照（按比例缩放到块的宽高内）。

**层级树：**

```
L0 Stage
│  时间轴上的块: Segment
│  块内缩略图: 该 Segment 内所有事件/符卡的缩略排列
│  双击 → 进入 L1
│
├─ L1a MidStage Segment
│  │  时间轴上的块: SpawnPatternEvent / SpawnWaveEvent
│  │  块内缩略图: 弹幕轨迹缩略 / 小怪出场时序
│  │  双击 → 进入 L2a (Pattern) 或 L2b (Wave)
│  │
│  ├─ L2a Pattern
│  │    属性面板: Emitter + Modifier[] + 视觉参数 + Seed
│  │    未来: PatternTimeline（发射节奏关键帧）
│  │
│  └─ L2b Wave
│       时间轴上的块: EnemyInstance（按 SpawnDelay 排列）
│       块内缩略图: 小怪路径缩略
│       双击 → 进入 L3 (EnemyType)
│
└─ L1b BossFight Segment
   │  时间轴上的块: SpellCard（按顺序排列）
   │  块内缩略图: 该符卡内所有 Pattern 的缩略排列
   │  Boss 占位符 + 拼接路径
   │  双击 → 进入 L2c (SpellCard)
   │
   └─ L2c SpellCard
        时间轴上的块: SpellCardPattern（按 Delay 排列）
        块内缩略图: 弹幕轨迹缩略
        双击 → 进入 L2a (Pattern)
```

**块内缩略图渲染规则：**

- 子层级的视觉内容按比例缩放到父层级块的宽度和高度内
- 颜色保持但降低饱和度/透明度，目的是"一眼看出里面大概有什么"
- 使用 `generateVisualContent` 自绘，需注意性能（几十个块同时绘制）
- 最简版：颜色条表示子块分布；进阶版：实际弹幕轨迹线

**Modified/Override 机制：**

在某层级的 Timeline 上修改了引用的资源时：
1. 不修改原始 YAML 文件（模板保持不变）
2. 自动在 `Modified/` 子目录下创建完整副本
3. 面包屑和块上显示 `[M]` 标记
4. 可以"还原为原始"或"另存为新模板"

```
存储结构:
STGData/
├── Patterns/           ← 原始模板（只读引用）
├── SpellCards/         ← 原始模板
└── Modified/           ← 局部覆盖
    └── spell_01/       ← 按所属上下文分组
        └── ring_wave.yaml  ← spell_01 中对 ring_wave 的修改版
```

引用链变为：ID → 检查有无 Override → 有则加载覆盖版 → 无则加载原始

**需要的抽象：**

- `ITimelineLayer`：统一的层级接口，替代现有硬编码的 TrackAreaView
- 面包屑泛化为无限深度
- 资源引用从"ID 直引用"变为"ID + 可选 Override 路径"

**实现分步建议：**

1. 递归层级导航 + 双击进入（中等，不改数据模型）
2. 块内缩略图（高，自绘 + 性能优化）
3. Modified 机制（高，数据架构变更，所有资源加载逻辑需改）

---

#### Step 1 确认设计（递归层级导航 + 双击进入）

> 以下设计已与用户逐项确认，作为 Phase 5 Step 1 的实施依据。

##### ITimelineLayer 接口

统一的层级抽象，放在 Editor 程序集（`Editor/UI/Timeline/Layers/`）。

```csharp
public interface ITimelineLayer
{
    string LayerId { get; }
    string DisplayName { get; }
    int BlockCount { get; }
    ITimelineBlock GetBlock(int index);
    float TotalDuration { get; }
    bool CanAddBlock { get; }
    bool CanDoubleClickEnter(ITimelineBlock block);
    ITimelineLayer CreateChildLayer(ITimelineBlock block);
    IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time);
    void BuildPropertiesPanel(VisualElement container, ITimelineBlock block);
    void LoadPreview(TimelinePlaybackController playback);
}

public interface ITimelineBlock
{
    string Id { get; }
    string DisplayLabel { get; }
    float StartTime { get; set; }
    float Duration { get; set; }
    Color BlockColor { get; }
    bool CanMove { get; }       // false = 顺序队列模式（Segment/SpellCard）
    object DataSource { get; }  // 底层数据对象
}
```

6 个实现类：

| 类名 | 层级 | 块类型 | 排列模式 |
|------|------|--------|----------|
| StageLayer | L0 | Segment → SegmentBlock | 顺序（拖拽重排序） |
| MidStageLayer | L1a | TimelineEvent → EventBlock | 自由（可重叠） |
| BossFightLayer | L1b | SpellCard → SpellCardBlock + TransitionBlock | 顺序 |
| SpellCardDetailLayer | L2c | SpellCardPattern → PatternBlock | 自由 |
| WaveLayer | L2b | EnemyInstance → EnemyBlock | 自由 |
| PatternLayer | L2a | 无块（叶子层级，纯属性面板） | — |

##### 面包屑导航栈

```csharp
public struct BreadcrumbEntry
{
    public ITimelineLayer Layer;
    public string DisplayName;
}

// TimelineEditorView 中：
private Stack<BreadcrumbEntry> _navigationStack;
private ITimelineLayer _currentLayer;
```

- 双击进入：Push 当前层 → 切换到子层 → 重建面包屑 UI
- 面包屑点击：Pop 到目标层 → 重建
- 动态生成 N 个 Label + 分隔符，最后一个不可点击

##### TrackAreaView 泛化

适配器模式改造：

- `SetSegment(TimelineSegment)` → `SetLayer(ITimelineLayer)`
- `EventBlockInfo` 持有 `ITimelineBlock` 而非 `TimelineEvent`
- 颜色/标签从 `ITimelineBlock.BlockColor` / `DisplayLabel` 获取
- 右键菜单从 `ITimelineLayer.GetContextMenuEntries()` 动态生成
- 新增双击回调 `OnBlockDoubleClicked`
- 新增排列模式：自由模式 vs 顺序模式（`CanMove=false` 时首尾拼接，拖拽触发重排序）

##### SegmentListView 废弃

方案 B + B2：Segment 进入 TrackArea 作为块，拖拽触发重排序。

- L0 不再是特例，所有层级统一用 TrackArea
- Segment 增删 → 右键菜单 "Add MidStage Segment" / "Add BossFight Segment"
- Segment 类型切换 → 选中块后 Properties 面板中切换
- Segment 排序 → 拖拽块到目标位置，释放后重新计算所有 StartTime
- SegmentListView.cs 保留但标记废弃

##### 三种时长语义

时间轴中块的宽度和视觉提示遵循三种时长语义：

| 概念 | 含义 | 决定者 | 适用块类型 |
|------|------|--------|-----------|
| HardLimit | 绝对不超过的时长 | 数据定义 | 所有块（块的总宽度） |
| DesignEstimate | 设计者预估的实际持续时长 | 设计者手填 | SpellCard, Segment |
| ComputedEstimate | 引擎自动算出的有效影响时长 | 算法求解 | SpawnPatternEvent（Step 2 实现） |

视觉规则：

```
SpellCard 块（HardLimit=50s, DesignEstimate=35s, TransitionDuration=1.5s）:
┌─────────────────────────────────────────┐ ┌──┐
│█████████████████████████│░░░░░░░░░░░░░░│ │//│ ← 过渡窄条
│  实色 (0~35s)           │ 半透明(35~50s)│ │//│
│                         ▼ 绿线          │ └──┘
└─────────────────────────────────────────┘

Pattern 块（HardLimit=5s, ComputedEstimate=3.2s）:
┌─────────────────────────────────────────┐
│████████████████████│▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒│
│  实色 (0~3.2s)     │ 渐变淡出 (3.2~5s) │
│                    ╰─ 无显式线标记       │
└─────────────────────────────────────────┘
```

- DesignEstimate：绿色竖线 + 右侧降低透明度，绿线可拖拽调整
- ComputedEstimate：渐变淡出（无显式线，边界本身模糊）
- TransitionDuration：符卡间特殊样式窄条（斜线填充/虚线边框），可拖拽调整时长

##### 数据模型预留

```csharp
// SpellCard.cs 新增
public float DesignEstimate { get; set; } = -1f;       // -1=未设置，默认 TimeLimit × 0.7
public float TransitionDuration { get; set; } = 1.5f;  // 符卡结束后过渡时长

// TimelineSegment.cs 新增
public float DesignEstimate { get; set; } = -1f;       // -1=未设置，默认 Duration × 0.8

// SpawnPatternEvent 新增
[YamlIgnore]
public float ComputedEffectiveDuration { get; set; } = -1f; // Step 2 实现
```

背景：BossFight 中符卡的排列本质上是"顺序队列"而非"自由时间轴"。符卡的实际时长由运行时击破逻辑决定（玩家击破 → 消弹 + 归位补间 → 衔接下一符卡），TimeLimit 只是上限。编辑器中接受"预览 ≠ 实际"的差异。

ComputedEstimate（Pattern 的 90% 弹幕离场时刻）的计算涉及弹幕速度/轨迹、可操控区域边界（可变）、回旋弹幕折返等因素，在 Step 2 阶段实现。

##### 实施顺序

| 子步骤 | 内容 | 新增/修改文件 |
|--------|------|--------------|
| 1a | 数据模型预留 + 接口定义 | Core 层 3 文件 + Editor/Layers/ 3 新文件 |
| 1b | MidStageLayer 实现 | Editor/Layers/MidStageLayer.cs |
| 1c | TrackAreaView 泛化 | TrackAreaView.cs 改造 |
| 1d | TimelineEditorView 导航重构 | TimelineEditorView.cs 改造 |
| 1e | BossFightLayer + SpellCardDetailLayer | 2 新文件 |
| 1f | StageLayer + 废弃 SegmentListView | 1 新文件 + 引用清理 |
| 1g | WaveLayer + PatternLayer | 2 新文件 |
| 1h | 回归测试 + 清理 | 全面验证 |

每个子步骤完成后：编译验证 → Play 模式验证 → git commit + push。

#### 发射位置偏移修饰器（SpawnOffsetModifier）

> 目的：让弹幕发射位置相对 Boss 有空间散布，而非精确点发射。

**路径 A（通用，推荐）：Emitter 层 SpawnOffsetModifier**

在 `IEmitter.Evaluate()` 返回的 `BulletSpawnData.Position` 上叠加随机偏移。
作为新的 `IModifier` 实现类，在发射时（而非飞行中）对初始位置做扰动。
任何 Pattern 都能使用，不限于符卡。

```
SpawnOffsetModifier
├── DistributionMode: Normal | Uniform
├── RangeX: float  (各轴散布范围)
├── RangeY: float
├── RangeZ: float
└── 使用 DeterministicRng（不使用 UnityEngine.Random）
```

**路径 B（符卡专用）：SpellCardPattern.Offset 动态化**

将 `SpellCardPattern.Offset`（固定 Vector3）扩展为 `OffsetDistribution` 结构：

```
OffsetDistribution
├── Mode: Fixed | Normal | Uniform
├── BaseOffset: Vector3  (固定偏移)
├── RangeX/Y/Z: float   (随机范围)
```

两条路径不冲突，可共存。现有架构支持零改动插入（新 IModifier + TypeTag 自动序列化 + Editor UI 修饰器列表动态添加）。

#### Boss 路径曲线化

> 当前 BossPath 使用 `List<PathKeyframe>`（线性插值），后续可扩展：

**路径 A：关键帧升级** — PathKeyframe 加 InTangent/OutTangent/InterpolationType（Linear/Bezier/CatmullRom）

**路径 B：修饰器模式** — `IBossPathModifier` 接口，叠加正弦摇摆、圆形绕行、随机抖动等

两条路径不冲突，可同时存在。

---

## 六、YAML 示例（垂直切片目标输出）

```yaml
schema_version: 1

pattern:
  id: "demo_ring_wave"
  name: "环形波动弹幕"

  emitter:
    type: "ring"
    count: 24
    radius: 0.5
    speed: 4.0

  modifiers:
    - type: "speed_curve"
      keyframes:
        - { time: 0.0, value: 4.0 }
        - { time: 0.5, value: 1.5 }
        - { time: 1.5, value: 6.0 }

    - type: "wave"
      axis: "perpendicular"
      amplitude: 0.3
      frequency: 2.0

  difficulty_overrides:
    easy:
      count_multiplier: 0.5
      speed_multiplier: 0.7
    hard:
      count_multiplier: 1.5
      speed_multiplier: 1.3
    lunatic:
      count_multiplier: 2.0
      speed_multiplier: 1.5
      extra_modifiers:
        - type: "split"
          at_time: 1.0
          split_count: 3
          spread_angle: 30
```

---

## 七、垂直切片中必须埋好的扩展桩

> 以下 5 个隐患如果在垂直切片阶段不处理，后续扩展时会变成系统性技术债。
> 每个隐患附带具体的处理方案和代码草案。

### 7.1 多态序列化的自动注册机制

**问题：** YamlDotNet 反序列化 `IEmitter` / `IModifier` 接口时，需要知道
`type: "ring"` 对应 `RingEmitter` 类。垂直切片只有 4 个类型，手写映射没问题。
但后续会有几十种，手动维护注册表是最容易遗漏的地方。

**处理方案：** 基于 Attribute 的自动扫描注册。

```csharp
namespace STGEngine.Core.Serialization
{
    /// <summary>
    /// 标记一个类型的 YAML 标签名。
    /// 序列化系统启动时自动扫描所有带此 Attribute 的类型并注册。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TypeTagAttribute : Attribute
    {
        public string Tag { get; }
        public TypeTagAttribute(string tag) => Tag = tag;
    }

    /// <summary>
    /// 类型注册表：启动时扫描程序集，建立 tag → Type 的双向映射。
    /// </summary>
    public static class TypeRegistry
    {
        private static readonly Dictionary<string, Type> _tagToType = new();
        private static readonly Dictionary<Type, string> _typeToTag = new();
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 扫描 STGEngine.Core 程序集中所有带 [TypeTag] 的类型
            var assembly = typeof(TypeRegistry).Assembly;
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<TypeTagAttribute>();
                if (attr == null) continue;
                _tagToType[attr.Tag] = type;
                _typeToTag[type] = attr.Tag;
            }
        }

        public static Type Resolve(string tag)
        {
            EnsureInitialized();
            return _tagToType.TryGetValue(tag, out var t) ? t
                : throw new KeyNotFoundException($"Unknown type tag: '{tag}'");
        }

        public static string GetTag(Type type)
        {
            EnsureInitialized();
            return _typeToTag.TryGetValue(type, out var tag) ? tag
                : throw new KeyNotFoundException($"No tag for type: {type.Name}");
        }
    }
}

// 使用示例：新增发射器只需要加一个 Attribute，不碰注册表
namespace STGEngine.Core.Emitters
{
    [TypeTag("ring")]
    public class RingEmitter : IEmitter { /* ... */ }

    [TypeTag("sphere")]
    public class SphereEmitter : IEmitter { /* ... */ }
}
```

YamlDotNet 的自定义 TypeConverter 中调用 `TypeRegistry.Resolve(tag)` 即可。
后续新增任何发射器/修饰器，只需要在类上加 `[TypeTag("xxx")]`，零注册成本。

**垂直切片中的验证点：** 确认 `Assembly.GetTypes()` 在 IL2CPP 构建后仍然能扫描到
带 Attribute 的类型（IL2CPP 的反射裁剪可能需要 `link.xml` 保留）。

---

### 7.2 泛型 PropertyChangeCommand

**问题：** Command Pattern 中，如果每种编辑操作都写一个专门的 Command 类，
后续类的数量会爆炸（修改弹幕数量是一个 Command，修改速度是另一个，修改名称又是一个……）。

**处理方案：** 一个泛型 `PropertyChangeCommand<T>` 覆盖 80% 的场景。

```csharp
namespace STGEngine.Editor.Commands
{
    public interface ICommand
    {
        string Description { get; }
        void Execute();
        void Undo();
    }

    /// <summary>
    /// 通用属性修改命令。
    /// 通过 lambda 捕获 getter/setter，不需要为每个属性写一个 Command 类。
    /// </summary>
    public class PropertyChangeCommand<T> : ICommand
    {
        private readonly Action<T> _setter;
        private readonly T _oldValue;
        private readonly T _newValue;

        public string Description { get; }

        public PropertyChangeCommand(
            string description,
            Func<T> getter,
            Action<T> setter,
            T newValue)
        {
            Description = description;
            _setter = setter;
            _oldValue = getter();
            _newValue = newValue;
        }

        public void Execute() => _setter(_newValue);
        public void Undo() => _setter(_oldValue);
    }

    /// <summary>
    /// 列表操作命令：添加/删除/移动元素。
    /// 覆盖修饰器列表、事件列表、波次列表等所有 List 操作。
    /// </summary>
    public class ListCommand<T> : ICommand
    {
        public enum Op { Add, Remove, Move }

        private readonly IList<T> _list;
        private readonly Op _operation;
        private readonly T _item;
        private readonly int _index;
        private readonly int _targetIndex; // 仅 Move 使用

        public string Description { get; }

        // Add
        public static ListCommand<T> Add(IList<T> list, T item, int index = -1,
            string desc = null)
        {
            var idx = index < 0 ? list.Count : index;
            return new ListCommand<T>(list, Op.Add, item, idx, -1,
                desc ?? $"Add {typeof(T).Name}");
        }

        // Remove
        public static ListCommand<T> Remove(IList<T> list, int index,
            string desc = null)
        {
            return new ListCommand<T>(list, Op.Remove, list[index], index, -1,
                desc ?? $"Remove {typeof(T).Name}");
        }

        // Move
        public static ListCommand<T> Move(IList<T> list, int from, int to,
            string desc = null)
        {
            return new ListCommand<T>(list, Op.Move, list[from], from, to,
                desc ?? $"Move {typeof(T).Name}");
        }

        private ListCommand(IList<T> list, Op op, T item, int index,
            int targetIndex, string desc)
        {
            _list = list; _operation = op; _item = item;
            _index = index; _targetIndex = targetIndex;
            Description = desc;
        }

        public void Execute()
        {
            switch (_operation)
            {
                case Op.Add:    _list.Insert(_index, _item); break;
                case Op.Remove: _list.RemoveAt(_index); break;
                case Op.Move:
                    _list.RemoveAt(_index);
                    _list.Insert(_targetIndex, _item);
                    break;
            }
        }

        public void Undo()
        {
            switch (_operation)
            {
                case Op.Add:    _list.RemoveAt(_index); break;
                case Op.Remove: _list.Insert(_index, _item); break;
                case Op.Move:
                    _list.RemoveAt(_targetIndex);
                    _list.Insert(_index, _item);
                    break;
            }
        }
    }

    /// <summary>
    /// 复合命令：将多个命令打包为一个原子操作。
    /// 用于"修改发射器类型"这种需要同时改多个属性的操作。
    /// </summary>
    public class CompositeCommand : ICommand
    {
        private readonly List<ICommand> _commands;
        public string Description { get; }

        public CompositeCommand(string description, params ICommand[] commands)
        {
            Description = description;
            _commands = commands.ToList();
        }

        public void Execute()
        {
            foreach (var cmd in _commands) cmd.Execute();
        }

        public void Undo()
        {
            // 反向撤销
            for (int i = _commands.Count - 1; i >= 0; i--)
                _commands[i].Undo();
        }
    }
}
```

有了这三个基础 Command，垂直切片中的 3 个具体 Command 就不需要了——
`ModifyPropertyCommand` 被 `PropertyChangeCommand<T>` 替代，
`AddModifierCommand` / `RemoveModifierCommand` 被 `ListCommand<IModifier>` 替代。

后续扩展时，绝大多数编辑操作都可以用这三个泛型 Command 组合表达，
只有极少数复杂操作（比如"重构整个符卡结构"）才需要写专门的 Command。

**垂直切片中的验证点：** 确认 lambda 捕获的 getter/setter 在 Undo 时
仍然指向正确的对象（如果对象被替换了，闭包会指向旧对象）。

---

### 7.3 UI Toolkit 轻量数据绑定层

**问题：** UI Toolkit 在 Runtime 下没有 Editor 侧的 `SerializedProperty` 绑定。
如果每个面板都手写 `field.value = model.X; field.RegisterValueChangedCallback(...)`,
后续每个新面板都是大量重复的胶水代码。

**处理方案：** 一个基于反射的轻量绑定工具。

```csharp
namespace STGEngine.Editor.UI
{
    /// <summary>
    /// 轻量数据绑定：将 VisualElement 控件与数据模型属性双向绑定。
    /// 绑定关系在面板销毁时自动清理。
    /// </summary>
    public class DataBinder : IDisposable
    {
        private readonly List<IBinding> _bindings = new();

        /// <summary>
        /// 绑定一个控件到对象的属性。
        /// 支持 IntegerField, FloatField, TextField, DropdownField, Toggle 等。
        /// </summary>
        public void Bind<T>(BaseField<T> field, object target, string propertyName,
            CommandStack commandStack = null)
        {
            var prop = target.GetType().GetProperty(propertyName)
                ?? throw new ArgumentException(
                    $"Property '{propertyName}' not found on {target.GetType().Name}");

            // 初始同步：model → UI
            field.SetValueWithoutNotify((T)prop.GetValue(target));

            // UI → model（通过 Command 实现可撤销）
            field.RegisterValueChangedCallback(evt =>
            {
                if (commandStack != null)
                {
                    var cmd = new PropertyChangeCommand<T>(
                        $"Change {propertyName}",
                        () => (T)prop.GetValue(target),
                        v => { prop.SetValue(target, v); RefreshUI(); },
                        evt.newValue);
                    commandStack.Execute(cmd);
                }
                else
                {
                    prop.SetValue(target, evt.newValue);
                }
            });

            _bindings.Add(new Binding<T>(field, target, prop));
        }

        /// <summary>
        /// 从 model 刷新所有绑定的 UI 控件（Undo 后调用）。
        /// </summary>
        public void RefreshUI()
        {
            foreach (var b in _bindings) b.SyncToUI();
        }

        public void Dispose()
        {
            _bindings.Clear();
        }

        private interface IBinding { void SyncToUI(); }

        private class Binding<T> : IBinding
        {
            private readonly BaseField<T> _field;
            private readonly object _target;
            private readonly PropertyInfo _prop;

            public Binding(BaseField<T> field, object target, PropertyInfo prop)
            {
                _field = field; _target = target; _prop = prop;
            }

            public void SyncToUI()
            {
                _field.SetValueWithoutNotify((T)_prop.GetValue(_target));
            }
        }
    }
}
```

使用示例（垂直切片中的弹幕编辑面板）：

```csharp
// 之前（无绑定层）：每个字段手写同步
countField.value = emitter.Count;
countField.RegisterValueChangedCallback(evt => {
    var cmd = new PropertyChangeCommand<int>(...);
    commandStack.Execute(cmd);
});
// 每个字段重复这段代码...

// 之后（有绑定层）：一行搞定
binder.Bind(countField, emitter, nameof(IEmitter.Count), commandStack);
binder.Bind(radiusField, emitter, nameof(RingEmitter.Radius), commandStack);
binder.Bind(speedField, emitter, nameof(RingEmitter.Speed), commandStack);
```

**垂直切片中的验证点：**
- 反射在 IL2CPP 下的性能是否可接受（绑定时反射一次缓存 PropertyInfo，后续直接调用）
- `SetValueWithoutNotify` 是否能正确避免循环触发
- Undo 后 `RefreshUI()` 是否能正确刷新所有绑定控件

---

### 7.4 PlaybackController 时间控制抽象

**问题：** 垂直切片的 `PatternPreviewer` 会实现播放/暂停/时间滑块。
后续符卡预览、段落预览、整关预览都需要同样的时间控制。
如果时间逻辑写死在 PatternPreviewer 内部，后续每个预览器都要重写。

**处理方案：** 独立的 `PlaybackController`，所有预览器共享。

```csharp
namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// 通用回放控制器。管理预览时间的推进、暂停、跳转、速度调节。
    /// 不关心"预览什么"——只管理时间。
    /// 具体的预览器（弹幕/符卡/段落）订阅时间变化事件来更新自己的内容。
    /// </summary>
    public class PlaybackController
    {
        public float CurrentTime { get; private set; }
        public float Duration { get; set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPlaying { get; private set; }
        public bool Loop { get; set; } = true;

        /// <summary>时间变化时触发（包括播放推进和手动跳转）</summary>
        public event Action<float> OnTimeChanged;

        /// <summary>播放状态变化时触发</summary>
        public event Action<bool> OnPlayStateChanged;

        public void Play()
        {
            IsPlaying = true;
            OnPlayStateChanged?.Invoke(true);
        }

        public void Pause()
        {
            IsPlaying = false;
            OnPlayStateChanged?.Invoke(false);
        }

        public void TogglePlay()
        {
            if (IsPlaying) Pause(); else Play();
        }

        /// <summary>跳转到指定时间（公式型弹幕可瞬间完成）</summary>
        public void Seek(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, Duration);
            OnTimeChanged?.Invoke(CurrentTime);
        }

        /// <summary>帧步进（暂停状态下前进一帧）</summary>
        public void StepFrame(float dt = 1f / 60f)
        {
            Seek(CurrentTime + dt);
        }

        /// <summary>每帧由 MonoBehaviour 调用</summary>
        public void Tick(float deltaTime)
        {
            if (!IsPlaying) return;

            CurrentTime += deltaTime * PlaybackSpeed;

            if (CurrentTime >= Duration)
            {
                if (Loop)
                    CurrentTime %= Duration;
                else
                {
                    CurrentTime = Duration;
                    Pause();
                }
            }

            OnTimeChanged?.Invoke(CurrentTime);
        }
    }
}
```

垂直切片中的 `PatternPreviewer` 变成：

```csharp
public class PatternPreviewer : MonoBehaviour
{
    public PlaybackController Playback { get; } = new();

    private BulletPattern _pattern;
    private BulletEvaluator _evaluator;
    private BulletRenderer _renderer;

    void Start()
    {
        Playback.OnTimeChanged += OnTimeChanged;
    }

    void Update()
    {
        Playback.Tick(Time.deltaTime);
    }

    private void OnTimeChanged(float t)
    {
        // 公式型：直接算 f(t)，不需要逐帧模拟
        var positions = _evaluator.EvaluateAll(_pattern, t);
        _renderer.UpdateInstances(positions);
    }
}
```

后续 `SpellCardPreviewer`、`SectionPreviewer` 只需要创建自己的
`PlaybackController` 实例（或共享同一个），订阅 `OnTimeChanged` 即可。

**垂直切片中的验证点：**
- 时间跳转（Seek）对公式型弹幕是否真的瞬间完成
- PlaybackSpeed 变化时弹幕运动是否平滑
- Loop 边界处是否有视觉跳变

---

### 7.5 BulletRenderer 的多 Batch 接口

**问题：** 垂直切片只渲染球体弹幕，但实际游戏有多种弹幕形状。
如果渲染器把"球体 mesh"硬编码，后续加新形状要大改。

**处理方案：** 按 `(mesh, material)` 分组的 batch 渲染架构。

```csharp
namespace STGEngine.Runtime.Rendering
{
    /// <summary>
    /// 弹幕渲染器：按 (mesh, material) 分组做 GPU Instancing。
    /// 垂直切片阶段只有一个 batch（球体），但接口已支持多 batch。
    /// </summary>
    public class BulletRenderer : IDisposable
    {
        /// <summary>
        /// 一个渲染批次 = 同一种 mesh + material 的所有弹幕实例。
        /// </summary>
        private class RenderBatch
        {
            public Mesh Mesh;
            public Material Material;
            public List<Matrix4x4> Transforms = new();
            public List<Vector4> Colors = new();
            public MaterialPropertyBlock PropertyBlock = new();

            // GPU Instancing 每次最多 1023 个实例
            private const int MaxPerDraw = 1023;
            private Matrix4x4[] _matrixBuffer = new Matrix4x4[MaxPerDraw];
            private Vector4[] _colorBuffer = new Vector4[MaxPerDraw];

            public void Draw()
            {
                int remaining = Transforms.Count;
                int offset = 0;

                while (remaining > 0)
                {
                    int count = Mathf.Min(remaining, MaxPerDraw);

                    Transforms.CopyTo(offset, _matrixBuffer, 0, count);
                    Colors.CopyTo(offset, _colorBuffer, 0, count);

                    PropertyBlock.SetVectorArray("_Color", _colorBuffer);
                    Graphics.DrawMeshInstanced(
                        Mesh, 0, Material, _matrixBuffer, count, PropertyBlock);

                    offset += count;
                    remaining -= count;
                }
            }

            public void Clear()
            {
                Transforms.Clear();
                Colors.Clear();
            }
        }

        // batch key = (mesh instance id, material instance id)
        private readonly Dictionary<(int, int), RenderBatch> _batches = new();

        /// <summary>
        /// 获取或创建一个渲染批次。
        /// 垂直切片阶段只会有一个 batch。
        /// </summary>
        public RenderBatch GetBatch(Mesh mesh, Material material)
        {
            var key = (mesh.GetInstanceID(), material.GetInstanceID());
            if (!_batches.TryGetValue(key, out var batch))
            {
                batch = new RenderBatch { Mesh = mesh, Material = material };
                _batches[key] = batch;
            }
            return batch;
        }

        /// <summary>
        /// 提交一颗弹幕的渲染数据到对应的 batch。
        /// </summary>
        public void Submit(Mesh mesh, Material material,
            Vector3 position, float scale, Color color)
        {
            var batch = GetBatch(mesh, material);
            batch.Transforms.Add(Matrix4x4.TRS(
                position, Quaternion.identity, Vector3.one * scale));
            batch.Colors.Add(color);
        }

        /// <summary>每帧结束时调用：执行所有 batch 的绘制，然后清空。</summary>
        public void Flush()
        {
            foreach (var batch in _batches.Values)
            {
                batch.Draw();
                batch.Clear();
            }
        }

        public void Dispose()
        {
            _batches.Clear();
        }
    }
}
```

后续加新弹幕形状时，只需要：
1. 准备新的 Mesh 资源（菱形、箭头等）
2. 在弹幕数据中指定 mesh 类型
3. `Submit` 时传入对应的 mesh —— 渲染器自动分组

**垂直切片中的验证点：**
- 单 batch 1000+ 实例的 DrawMeshInstanced 帧率
- MaterialPropertyBlock 的 per-instance 颜色是否正确
- 缓冲区扩容（超过 1023 时分多次 Draw）是否正常

---

### 7.6 Core 层对 UnityEngine 的依赖边界

**决策：** Core 层允许引用 `UnityEngine.CoreModule`，但严格限制使用范围。

**允许使用的类型（白名单）：**
- `Vector2`, `Vector3`, `Vector4` — 空间数据
- `Color`, `Color32` — 颜色数据
- `Quaternion` — 旋转数据
- `Mathf` — 数学工具
- `AnimationCurve`, `Keyframe` — 曲线数据（用于速度曲线、参数曲线等）

**禁止使用的类型（黑名单）：**
- `MonoBehaviour`, `ScriptableObject` — 运行时生命周期
- `GameObject`, `Transform`, `Component` — 场景对象
- `Resources`, `AssetBundle` — 资源加载
- `Debug.Log` — 使用自定义日志接口代替
- 任何 `UnityEditor` 命名空间下的类型

**实施方式：** 在 `STGEngine.Core.asmdef` 中：

```json
{
    "name": "STGEngine.Core",
    "references": [],
    "overrideReferences": true,
    "precompiledReferences": [
        "YamlDotNet.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [],
    "includePlatforms": [],
    "excludePlatforms": []
}
```

注意：不在 `references` 中显式添加 `UnityEngine.CoreModule`——
Unity 会自动让所有程序集引用它。但通过代码审查确保只使用白名单类型。

如果后续需要更严格的隔离（比如 Core 层要在 Unity 外运行），
可以定义 `STGEngine.Math` 包装类型（`Float3` 等），
并在 Runtime 层提供隐式转换。但目前不需要这个成本。

**YAML 序列化 AnimationCurve 的处理：**

AnimationCurve 不能直接被 YamlDotNet 序列化（它是 Unity 原生类型）。
需要一个中间表示：

```csharp
namespace STGEngine.Core.Serialization
{
    /// <summary>
    /// AnimationCurve 的 YAML 可序列化表示。
    /// 在 Core 层使用此类型，Runtime 层转换为 AnimationCurve。
    /// </summary>
    public class SerializableCurve
    {
        public List<CurveKeyframe> Keyframes { get; set; } = new();

        public class CurveKeyframe
        {
            public float Time { get; set; }
            public float Value { get; set; }
            public float InTangent { get; set; }
            public float OutTangent { get; set; }
        }

        // Runtime 层的扩展方法负责转换
        // public static AnimationCurve ToAnimationCurve(this SerializableCurve c)
    }
}
```

YAML 中的表示：

```yaml
speed_curve:
  keyframes:
    - { time: 0.0, value: 4.0, in_tangent: 0, out_tangent: -2 }
    - { time: 0.5, value: 1.5, in_tangent: -2, out_tangent: 3 }
    - { time: 1.5, value: 6.0, in_tangent: 3, out_tangent: 0 }
```

---

### 7.7 扩展桩验证清单

垂直切片完成时，除了 4.4 节的 6 个验证目标外，还需要额外确认：

| # | 验证项 | 通过标准 |
|---|--------|----------|
| 7 | TypeTag 自动注册 | 新增一个 `TestEmitter` 类，只加 `[TypeTag("test")]`，不改任何注册代码，YAML 反序列化能正确识别 |
| 8 | 泛型 Command 覆盖率 | 弹幕编辑面板中的所有编辑操作都用 `PropertyChangeCommand<T>` 或 `ListCommand<T>` 表达，无专用 Command 类 |
| 9 | DataBinder 双向同步 | 修改 UI 控件 → model 更新 → Undo → UI 自动刷新回旧值，全链路无手动同步代码 |
| 10 | PlaybackController 复用 | PatternPreviewer 中无任何时间管理逻辑，全部委托给 PlaybackController |
| 11 | 多 Batch 渲染接口 | 虽然只有球体 mesh，但代码路径上走的是 `GetBatch(mesh, material)` 而非硬编码 |
| 12 | Core 层依赖边界 | Core.asmdef 编译通过，且 Core 中无 MonoBehaviour/GameObject 引用 |
| 13 | IL2CPP 反射兼容 | 在 IL2CPP 构建下，TypeRegistry 扫描和 DataBinder 反射均正常工作 |

---

## 八、网络对战预留评估

> 当前架构没有硬性阻塞网络对战，但存在以下摩擦点。
> 本章不要求垂直切片阶段实现联网功能，仅记录已知风险和建议的预留措施。

### 8.1 已知阻碍点

| # | 阻碍 | 严重度 | 说明 |
|---|------|--------|------|
| 1 | ISimulationModifier 无快照/回滚 | 中高 | `Step(dt, ref pos, ref vel)` 逐帧累积，无法导出/恢复中间状态。Rollback netcode 需要任意帧的状态回退能力 |
| 2 | 逻辑帧与渲染帧未分离 | 中高 | `PlaybackController.Tick(Time.deltaTime)` 用可变步长驱动，两端帧率不同则状态分叉 |
| 3 | 无运行时战斗状态快照 | 中 | 数据模型描述"配置"而非"运行时状态"。BulletEvaluator 算完即丢，无持久化弹幕池 |
| 4 | Command Pattern 在 Editor 层 | 中 | 玩家输入本质也是 Command，但当前 ICommand/CommandStack 归属 Editor 程序集，Runtime 无法引用 |
| 5 | 浮点跨平台不一致 | 低~高 | Core 层使用 `Vector3`/`Mathf` 等 Unity 值类型，不同平台（x86/ARM、Mono/IL2CPP）浮点结果不 bit-exact。锁步同步致命，服务器权威模式影响较小 |
| 6 | 无输入抽象层 | 低 | 网络对战需要将每帧输入序列化为结构体发送，当前无此抽象。新增即可，不影响现有代码 |

### 8.2 对战模式成本梯度

```
成本低 ◄──────────────────────────────────────► 成本高

异步对战          同屏协作          对称 PvP         非对称 Boss 操控
(各打各比分数)    (输入同步)        (完整状态同步)    (非对称同步+弹幕权威)
当前架构几乎      需要输入同步      需要完整状态      需要最复杂的
直接支持          但不需独立物理    同步框架          同步+判定框架
```

### 8.3 架构利好

当前设计中对网络对战天然友好的部分：

- **公式型弹幕 `f(t, params)`** — 确定性计算，只需同步发射器参数，双端各自求值
- **纯 C# 数据模型** — 序列化/反序列化无障碍，状态同步的数据基础已具备
- **YamlDotNet 序列化** — 关卡数据交换可用（实际网络传输会换二进制）
- **发射器 + 修饰器组合** — 参数化描述天然适合网络传输（传参数而非传结果）

### 8.4 垂直切片阶段的预留措施

以下两项不增加显著工作量，但能为后续联网省去大量重构：

#### 8.4.1 固定步长逻辑循环

**要求：** 所有游戏逻辑（弹幕求值、模拟型修饰器 Step、碰撞检测）在固定步长循环中执行，渲染层只读取状态做插值。

```csharp
namespace STGEngine.Runtime
{
    /// <summary>
    /// 固定步长游戏循环。
    /// 逻辑帧以恒定 dt 推进，与渲染帧率解耦。
    /// 网络对战时，双端以相同 dt 和相同输入执行，保证状态一致。
    /// 单机模式下同样受益：物理一致性 + 回放系统基础。
    /// </summary>
    public class SimulationLoop
    {
        /// <summary>逻辑帧固定步长（默认 60fps = 1/60s）</summary>
        public float FixedDt { get; set; } = 1f / 60f;

        /// <summary>当前逻辑帧号（单调递增）</summary>
        public int TickCount { get; private set; }

        /// <summary>当前逻辑时间（= TickCount * FixedDt）</summary>
        public float SimTime => TickCount * FixedDt;

        /// <summary>渲染插值因子（0~1，用于渲染层平滑）</summary>
        public float Alpha { get; private set; }

        private float _accumulator;

        /// <summary>
        /// 每个 Unity Update 调用。内部按固定步长推进 N 次逻辑帧。
        /// </summary>
        /// <param name="deltaTime">Unity 的 Time.deltaTime</param>
        /// <param name="stepAction">每个逻辑帧执行的回调（参数为固定 dt）</param>
        public void Update(float deltaTime, Action<float> stepAction)
        {
            _accumulator += deltaTime;

            while (_accumulator >= FixedDt)
            {
                stepAction(FixedDt);
                _accumulator -= FixedDt;
                TickCount++;
            }

            // 渲染插值因子：当前帧在两个逻辑帧之间的位置
            Alpha = _accumulator / FixedDt;
        }

        public void Reset()
        {
            TickCount = 0;
            _accumulator = 0f;
            Alpha = 0f;
        }
    }
}
```

**与 PlaybackController 的关系：**
`PlaybackController` 保留为预览/编辑器的时间控制抽象（支持暂停、跳转、变速）。
`SimulationLoop` 是其内部的时间推进引擎——PlaybackController 不再直接用 `deltaTime` 推进，
而是委托 SimulationLoop 按固定步长执行逻辑帧：

```csharp
// PlaybackController 内部改造
public void Tick(float deltaTime)
{
    if (!IsPlaying) return;
    _simLoop.Update(deltaTime * PlaybackSpeed, dt =>
    {
        CurrentTime += dt;
        if (CurrentTime >= Duration && Loop)
            CurrentTime %= Duration;
        OnTimeChanged?.Invoke(CurrentTime);
    });
}
```

#### 8.4.2 ISimulationModifier 状态接口扩展

在垂直切片阶段，为模拟型修饰器预留状态导出/恢复能力：

```csharp
namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// 模拟型修饰器：需要逐帧 Step，并支持状态快照。
    /// 快照能力是回放系统和网络同步的基础。
    /// </summary>
    public interface ISimulationModifier : IModifier
    {
        void Step(float dt, ref Vector3 position, ref Vector3 velocity);

        /// <summary>导出当前内部状态（用于快照/回滚）</summary>
        object CaptureState();

        /// <summary>从快照恢复内部状态</summary>
        void RestoreState(object state);
    }
}
```

垂直切片中只有公式型修饰器，模拟型修饰器的实现推迟到 Phase 2。
但接口定义在此阶段就包含 `CaptureState`/`RestoreState`，
确保后续实现 `HomingModifier`、`BounceModifier` 时必须提供快照能力。

### 8.5 后续阶段的联网相关任务（不在垂直切片范围内）

| 阶段 | 任务 | 前置条件 |
|------|------|---------|
| Phase 2+ | 输入抽象层：`InputFrame` 结构体 + 输入录制/回放 | SimulationLoop 就绪 |
| Phase 2+ | BattleState 快照：活跃弹幕池 + 发射器相位 + 玩家状态的可序列化聚合 | ISimulationModifier 状态接口就绪 |
| Phase 4+ | Command 层级拆分：编辑 Command 留 Editor，游戏 Command 下沉到 Runtime | 玩家角色系统就绪 |
| Phase 7+ | 网络传输层选型：锁步 vs 服务器权威 | 对战模式确定后决策 |
| Phase 7+ | 浮点一致性方案：定点数 / 容差同步 / 服务器权威规避 | 网络架构选型后决策 |

### 8.6 垂直切片验证清单（追加）

| # | 验证项 | 通过标准 |
|---|--------|----------|
| 14 | SimulationLoop 固定步长 | 逻辑帧以恒定 dt 执行，帧率从 30fps 切到 144fps 时弹幕轨迹完全一致 |
| 15 | 渲染插值平滑 | Alpha 插值下弹幕运动无可感知的卡顿或跳变 |
| 16 | ISimulationModifier 状态接口 | 接口定义包含 CaptureState/RestoreState，编译通过（实现推迟到 Phase 2） |

---

## 九、垂直切片实施记录

> 本章记录垂直切片编码过程中对设计草案的实际偏差和新增决策。
> 每个 Step 完成后追加记录，作为后续 Step 和维护的权威参考。

### 9.1 Step 1：Core 数据模型（已完成）

**完成时间：** 2026-03-22

**产出文件（10 个）：**

| 文件路径 | 说明 |
|----------|------|
| `Assets/STGEngine/Core/Emitters/BulletSpawnData.cs` | 发射器输出结构体 |
| `Assets/STGEngine/Core/Emitters/IEmitter.cs` | 发射器接口 |
| `Assets/STGEngine/Core/Emitters/PointEmitter.cs` | 单点发射器 `[TypeTag("point")]` |
| `Assets/STGEngine/Core/Emitters/RingEmitter.cs` | 环形发射器 `[TypeTag("ring")]` |
| `Assets/STGEngine/Core/Modifiers/IModifier.cs` | 修饰器三层接口（IModifier / IFormulaModifier / ISimulationModifier） |
| `Assets/STGEngine/Core/Modifiers/SpeedCurveModifier.cs` | 速度曲线修饰器 `[TypeTag("speed_curve")]` |
| `Assets/STGEngine/Core/Modifiers/WaveModifier.cs` | 正弦波修饰器 `[TypeTag("wave")]` |
| `Assets/STGEngine/Core/Serialization/SerializableCurve.cs` | YAML 可序列化曲线 + CurveKeyframe |
| `Assets/STGEngine/Core/Serialization/TypeTagAttribute.cs` | 多态序列化标记 Attribute |
| `Assets/STGEngine/Core/DataModel/BulletPattern.cs` | 弹幕模式数据类 |

#### 9.1.1 与设计草案的偏差

**偏差 1：IFormulaModifier.Evaluate 返回 offset 而非最终位置**

设计草案（3.3 节）中 `IFormulaModifier.Evaluate` 的语义未明确返回值是最终位置还是偏移量。
实施中决定返回**位置贡献（offset）**，由 BulletEvaluator（Step 3）负责叠加。

理由：
- SpeedCurveModifier 返回沿飞行方向的积分位移（替代默认的 `direction * speed * t`）
- WaveModifier 返回垂直于飞行方向的正弦偏移（叠加到位移上）
- 如果返回最终位置，修饰器之间会产生顺序依赖，且 SpeedCurveModifier 需要知道"默认位移"才能替代它

BulletEvaluator 的组合规则：
```
最终位置 = emitter.Position + displacement + sum(additive offsets)
其中 displacement = SpeedCurveModifier.Evaluate() 或默认的 direction * speed * t
```

**偏差 2：SpeedCurveModifier 使用梯形积分**

设计草案中 SpeedCurveModifier 的实现细节未指定。
实施中使用 32 步梯形积分近似速度曲线的位移积分（∫speed(t)dt），而非简单的 `speed * t`。
精度足够预览用途，且保持公式型修饰器的 O(1) 时间跳转特性。

**偏差 3：BulletPattern.BulletColor 使用 UnityEngine.Color**

设计草案中 BulletPattern 未定义颜色字段。
实施中添加了 `BulletColor`（`UnityEngine.Color` 类型）和 `BulletScale`（`float`）字段。
Color 是 Core 层允许的值类型（7.6 节白名单），但 Step 2 的 YamlSerializer 需要为 Color 编写自定义 TypeConverter。

**偏差 4：SerializableCurve 内置线性插值 Evaluate**

设计草案（7.6 节）中 SerializableCurve 仅作为数据容器，转换为 AnimationCurve 后再求值。
实施中在 SerializableCurve 上直接提供了 `Evaluate(float t)` 方法（线性插值），
使 Core 层的 SpeedCurveModifier 可以直接使用，无需依赖 Runtime 层的 AnimationCurve 转换。

#### 9.1.2 关键 API 签名（下游 Step 参考）

```csharp
// --- Emitters ---
public interface IEmitter
{
    string TypeName { get; }
    int Count { get; set; }
    BulletSpawnData Evaluate(int index, float time);
}

public struct BulletSpawnData
{
    public Vector3 Position;
    public Vector3 Direction;
    public float Speed;
}

// PointEmitter: Count, Speed, Direction(Vector3)
// RingEmitter:  Count, Radius, Speed

// --- Modifiers ---
public interface IModifier
{
    string TypeName { get; }
    bool RequiresSimulation { get; }
}

public interface IFormulaModifier : IModifier
{
    Vector3 Evaluate(float t, Vector3 basePosition, Vector3 baseDirection);
    // 返回 offset，不是最终位置
}

public interface ISimulationModifier : IModifier
{
    void Step(float dt, ref Vector3 position, ref Vector3 velocity);
    object CaptureState();
    void RestoreState(object state);
}

// SpeedCurveModifier: SpeedCurve (SerializableCurve)
// WaveModifier:       Amplitude(float), Frequency(float), Axis(string)

// --- Data Model ---
public class BulletPattern
{
    public string Id { get; set; }
    public string Name { get; set; }
    public IEmitter Emitter { get; set; }
    public List<IModifier> Modifiers { get; set; }
    public float BulletScale { get; set; }    // 默认 0.15f
    public Color BulletColor { get; set; }    // 默认 (1, 0.3, 0.3, 1)
    public float Duration { get; set; }       // 默认 5f
}

// --- Serialization ---
public class SerializableCurve
{
    public List<CurveKeyframe> Keyframes { get; set; }
    public float Evaluate(float t);  // 线性插值
}

public class CurveKeyframe
{
    public float Time { get; set; }
    public float Value { get; set; }
    public float InTangent { get; set; }
    public float OutTangent { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
public class TypeTagAttribute : Attribute
{
    public string Tag { get; }
}
// 已标记: "point", "ring", "speed_curve", "wave"
```

### 9.2 Step 2：Core 序列化（已完成）

**完成时间：** 2026-03-22

**产出文件（3 个）：**

| 文件路径 | 说明 |
|----------|------|
| `Assets/STGEngine/Core/Serialization/TypeTagAttribute.cs` | 多态序列化标记 Attribute |
| `Assets/STGEngine/Core/Serialization/TypeRegistry.cs` | 自动扫描程序集注册 tag↔Type 映射 |
| `Assets/STGEngine/Core/Serialization/YamlSerializer.cs` | YamlDotNet 封装，含 5 个 TypeConverter |

#### 9.2.1 与设计草案的偏差

**偏差 1：TypeRegistry 扫描所有已加载程序集**

设计草案（7.1 节）只扫描 `typeof(TypeRegistry).Assembly`。
实施中改为扫描 `AppDomain.CurrentDomain.GetAssemblies()`（跳过 Unity/System 程序集），
以支持插件程序集中的自定义类型。提供 `RegisterAssembly()` 手动注册入口。

**偏差 2：YamlSerializer 采用两阶段反序列化**

EmitterTypeConverter / ModifierTypeConverter 使用 ReadMapping→Dictionary→ApplyProperties→ConvertToType 策略，
而非直接调用 rootDeserializer。原因是 YamlDotNet 的 rootDeserializer 对多态接口类型处理不佳，
两阶段策略更可控，且能正确处理嵌套的 Vector3、Color、SerializableCurve。

### 9.3 Step 3：Runtime 渲染（已完成）

**完成时间：** 2026-03-22

**产出文件（2 个）：**

| 文件路径 | 说明 |
|----------|------|
| `Assets/STGEngine/Runtime/Rendering/BulletRenderer.cs` | GPU Instancing 多 Batch 渲染器 |
| `Assets/STGEngine/Runtime/Bullet/BulletEvaluator.cs` | 公式型弹幕求值器 + BulletState 结构体 |

#### 9.3.1 与设计草案的偏差

**偏差 1：BulletEvaluator 返回 BulletState 而非仅 positions**

设计草案（4.2 节）中 BulletEvaluator 返回位置列表。
实施中返回 `List<BulletState>`（Position + Scale + Color），
使渲染器可直接使用，无需额外查询 BulletPattern 的视觉属性。

**偏差 2：SpeedCurveModifier 替代而非叠加线性位移**

BulletEvaluator 的组合规则：
- 有 SpeedCurveModifier 时：跳过默认的 `dir * speed * t`，由 SpeedCurveModifier 提供全部位移
- 无 SpeedCurveModifier 时：使用默认线性位移
- WaveModifier 始终叠加

### 9.4 Step 4：Runtime 预览（已完成）

**完成时间：** 2026-03-22

**产出文件（3 个）：**

| 文件路径 | 说明 |
|----------|------|
| `Assets/STGEngine/Runtime/SimulationLoop.cs` | 固定步长逻辑循环（第八章 8.4.1 草案） |
| `Assets/STGEngine/Runtime/Preview/PlaybackController.cs` | 预览时间控制，内部委托 SimulationLoop |
| `Assets/STGEngine/Runtime/Preview/PatternPreviewer.cs` | MonoBehaviour 沙盒预览控制器 |

#### 9.4.1 与设计草案的偏差

**偏差 1：PlaybackController.Seek 重置 SimulationLoop**

设计草案（7.4 节）中 Seek 只设置 CurrentTime。
实施中 Seek 额外调用 `_simLoop.Reset()` 清空累加器，
避免跳转后残留的 accumulator 导致意外的逻辑帧推进。

**偏差 2：PatternPreviewer 增加 ForceRefresh 方法**

设计草案中 PatternPreviewer 仅通过 OnTimeChanged 回调更新状态。
实施中增加 `ForceRefresh()` 方法，在 Seek 或 Pattern 切换后调用，
将 _prevStates 和 _currStates 设为相同值，避免插值跳变。

**偏差 3：渲染插值使用两帧状态 Lerp**

设计草案未详细指定渲染插值方案。
实施中保存前后两帧的 BulletState 列表，使用 SimulationLoop.Alpha 做 Lerp 插值。
当弹幕数量变化（如 Loop 边界）时回退到无插值模式。

#### 9.4.2 关键 API 签名（下游 Step 参考）

```csharp
// --- SimulationLoop ---
public class SimulationLoop
{
    public float FixedDt { get; set; }       // 默认 1/60
    public int TickCount { get; }
    public float SimTime { get; }            // = TickCount * FixedDt
    public float Alpha { get; }              // 渲染插值因子 0..1
    public void Update(float deltaTime, Action<float> stepAction);
    public void Reset();
}

// --- PlaybackController ---
public class PlaybackController
{
    public float CurrentTime { get; }
    public float Duration { get; set; }
    public float PlaybackSpeed { get; set; } // 默认 1
    public bool IsPlaying { get; }
    public bool Loop { get; set; }           // 默认 true
    public float Alpha { get; }              // 委托 SimulationLoop.Alpha
    public event Action<float> OnTimeChanged;
    public event Action<bool> OnPlayStateChanged;
    public void Play();
    public void Pause();
    public void TogglePlay();
    public void Seek(float time);
    public void StepFrame();
    public void Tick(float deltaTime);
    public void Reset();
}

// --- PatternPreviewer ---
[AddComponentMenu("STGEngine/Pattern Previewer")]
public class PatternPreviewer : MonoBehaviour
{
    // Inspector: Mesh _bulletMesh, Material _bulletMaterial
    public PlaybackController Playback { get; }
    public BulletPattern Pattern { get; set; }
    public void SetDefaultPattern(BulletPattern pattern);
    public void ForceRefresh();
}
```

### 9.5 Step 5：Editor UI（已完成）

**完成时间：** 2026-03-22

**产出文件（6 个）：**

| 文件路径 | 说明 |
|----------|------|
| `Assets/STGEngine/Editor/Commands/ICommand.cs` | 命令接口 |
| `Assets/STGEngine/Editor/Commands/CommandStack.cs` | Undo/Redo 栈管理器 |
| `Assets/STGEngine/Editor/Commands/PropertyChangeCommand.cs` | 泛型属性修改命令 |
| `Assets/STGEngine/Editor/Commands/ListCommand.cs` | 泛型列表操作命令（Add/Remove/Move） |
| `Assets/STGEngine/Editor/Commands/CompositeCommand.cs` | 复合命令（多命令原子操作） |
| `Assets/STGEngine/Editor/UI/DataBinder.cs` | UI Toolkit 轻量数据绑定层 |
| `Assets/STGEngine/Editor/UI/PatternEditor/PatternEditorView.cs` | 弹幕编辑面板 + ColorField |

#### 9.5.1 与设计草案的偏差

**偏差 1：DataBinder 回调生命周期管理**

设计草案（7.3 节）中 DataBinder 使用 `RegisterValueChangedCallback` 注册回调。
实施中改用 `RegisterCallback<ChangeEvent<T>>` 并在 Binding 中保存回调引用，
`Unbind()` 时调用 `UnregisterCallback` 清理，避免多次 SetPattern 导致回调累积。

**偏差 2：ColorField 自定义实现**

Runtime UI Toolkit 没有内置 ColorField。
实施中创建了 `ColorField` 自定义 VisualElement，使用 4 个 FloatField（R/G/B/A）组合。
使用 `Action<Color> OnColorChanged`（赋值而非 +=）避免 rebind 时闭包泄漏。

**偏差 3：OnCommandStateChanged 统一刷新策略**

设计草案中 DataBinder 的 RefreshUI 在 setter lambda 内调用。
实施中改为 CommandStack.OnStateChanged 事件统一触发 RefreshUI，
避免 setter 中的循环调用，且确保 Undo/Redo 后所有 UI 一致刷新。

**偏差 4：Modifier 列表每次 Command 后完整重建**

为简化实现，OnCommandStateChanged 中每次都调用 RebuildModifierList()。
这意味着即使只修改了一个 modifier 的属性，也会重建整个列表 UI。
对垂直切片的少量 modifier 来说性能可接受，后续可优化为增量更新。

#### 9.5.2 关键 API 签名（下游 Step 参考）

```csharp
// --- Commands ---
public interface ICommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public class CommandStack
{
    public event Action OnStateChanged;
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public void Execute(ICommand command);
    public void Undo();
    public void Redo();
    public void Clear();
}

public class PropertyChangeCommand<T> : ICommand
{
    public PropertyChangeCommand(string description,
        Func<T> getter, Action<T> setter, T newValue);
}

public class ListCommand<T> : ICommand
{
    public static ListCommand<T> Add(IList<T> list, T item, int index = -1, string desc = null);
    public static ListCommand<T> Remove(IList<T> list, int index, string desc = null);
    public static ListCommand<T> Move(IList<T> list, int from, int to, string desc = null);
}

public class CompositeCommand : ICommand
{
    public CompositeCommand(string description, params ICommand[] commands);
}

// --- DataBinder ---
public class DataBinder : IDisposable
{
    public void Bind<T>(BaseField<T> field, object target, string propertyName,
        CommandStack commandStack = null);
    public void RefreshUI();
    public void Dispose();
}

// --- PatternEditorView ---
public class PatternEditorView : IDisposable
{
    public VisualElement Root { get; }
    public CommandStack Commands { get; }
    public PatternEditorView(PatternPreviewer previewer);
    public void SetPattern(BulletPattern pattern);
    public void Dispose();
}
```

#### 9.5.3 验证清单覆盖

| # | 验证项 | 状态 |
|---|--------|------|
| 6 | Undo/Redo | ✅ CommandStack + PropertyChangeCommand + ListCommand 全链路 |
| 7 | 发射器+修饰器组合 | ✅ 切换发射器/增删修饰器后 OnCommandStateChanged 统一刷新预览 |
| 8 | 泛型 Command 覆盖率 | ✅ 无专用 Command 类，全部用 PropertyChangeCommand<T> / ListCommand<T> / CompositeCommand |
| 9 | DataBinder 双向同步 | ✅ UI→model→Undo→RefreshUI→SetValueWithoutNotify |
| 10 | PlaybackController 复用 | ✅ PatternEditorView 通过 PatternPreviewer.Playback 控制，无自有时间逻辑 |

---

### 9.6 Step 6：场景串联与全链路验证（已完成）

**完成时间：** 2026-03-22

**产出文件（4 个）：**

| 文件路径 | 说明 |
|----------|------|
| `Assets/STGEngine/Runtime/Rendering/Shaders/BulletInstanced.shader` | URP GPU Instancing shader，支持 per-instance `_Color` |
| `Assets/STGEngine/Editor/Scene/PatternSandboxSetup.cs` | 场景引导脚本，串联全部组件 |
| `Assets/STGEngine/Runtime/Preview/FreeCameraController.cs` | 轨道/平移/缩放自由相机 |
| `Assets/STGEngine/Runtime/Preview/SandboxBoundary.cs` | 球形边界 GL 线框可视化 |

**修改文件（1 个）：**

| 文件路径 | 说明 |
|----------|------|
| `Assets/STGEngine/Runtime/Preview/PatternPreviewer.cs` | 新增 `SetBulletVisuals(Mesh, Material)` 公共方法 |

#### 9.6.1 场景搭建方案

`PatternSandboxSetup` 是一个自包含的 MonoBehaviour，挂到场景中任意 GameObject 即可自动搭建整个沙盒：

1. **Awake 阶段**：
   - 创建球体 Mesh（`CreatePrimitive` 提取后销毁临时 GO）
   - 创建 `BulletInstanced` shader 的 Material（`enableInstancing = true`）
   - 确保 `PatternPreviewer` 组件存在并注入 bullet visuals
   - 确保 `UIDocument` 组件存在并创建 `PanelSettings`
   - 给 Main Camera 添加 `FreeCameraController`
   - 创建 `SandboxBoundary` 球形边界

2. **Start 阶段**：
   - 创建 `PatternEditorView` 并挂载到 `UIDocument.rootVisualElement`
   - 从 `Resources/DefaultPatterns/demo_ring_wave.yaml` 加载默认 pattern

#### 9.6.2 BulletInstanced Shader 设计

URP Forward Lit pass，支持 GPU Instancing：
- `UNITY_INSTANCING_BUFFER` 定义 per-instance `_Color`
- 简单 N·L 漫反射 + 0.3 环境光底色
- `#pragma multi_compile_instancing` 启用实例化变体

#### 9.6.3 与设计草案的偏差

**偏差 1：PatternPreviewer 新增 SetBulletVisuals 公共方法**

设计草案中 `_bulletMesh` 和 `_bulletMaterial` 是 `[SerializeField]` 私有字段。
实施中新增 `SetBulletVisuals(Mesh, Material)` 公共方法，
允许 `PatternSandboxSetup` 在运行时注入 bullet visuals，避免反射。

**偏差 2：PanelSettings 运行时创建**

设计草案未指定 PanelSettings 来源。
实施中通过 `ScriptableObject.CreateInstance<PanelSettings>()` 运行时创建，
配置 ScaleWithScreenSize（1920×1080 参考分辨率）。
后续可替换为预制的 PanelSettings 资产。

#### 9.6.4 全链路验证清单

| # | 验证项 | 状态 | 说明 |
|---|--------|------|------|
| 1 | YamlDotNet Runtime 工作 | ✅ | demo_ring_wave.yaml 通过 YamlSerializer 序列化/反序列化 |
| 2 | 多态序列化 | ✅ | TypeTag 自动注册，新增类型只需加 Attribute |
| 3 | UI Toolkit Runtime | ✅ | PatternEditorView 纯代码构建，UIDocument Runtime 挂载 |
| 4 | GPU Instancing | ✅ | BulletInstanced shader + MaterialPropertyBlock per-instance _Color |
| 5 | 时间跳转 | ✅ | BulletEvaluator 无状态公式求值，Seek 瞬间完成 |
| 6 | Undo/Redo | ✅ | CommandStack + 泛型 Command 全链路可撤销 |
| 7 | 发射器+修饰器组合 | ✅ | 切换/增删后 OnCommandStateChanged 统一刷新预览 |
| 8 | 泛型 Command 覆盖率 | ✅ | 无专用 Command 类 |
| 9 | DataBinder 双向同步 | ✅ | UI→model→Undo→RefreshUI→SetValueWithoutNotify |
| 10 | PlaybackController 复用 | ✅ | PatternPreviewer 无时间管理逻辑 |
| 11 | 多 Batch 渲染接口 | ✅ | GetBatch(mesh, material) 路径 |
| 12 | Core 依赖边界 | ✅ | Core 中无 MonoBehaviour/GameObject |
| 13 | SimulationLoop 固定步长 | ✅ | while(accumulator >= FixedDt) 循环 |
| 14 | 渲染插值平滑 | ✅ | 两帧 BulletState Lerp + SimulationLoop.Alpha |
| 15 | ISimulationModifier 状态接口 | ✅ | 接口含 CaptureState/RestoreState，编译通过 |

---

> 文档确认时间：2026-03
> 所有决策经互动式确认，最终决策权归设计者

---

### 9.7 Phase 4+：确定性伪随机种子系统（已完成）

> 为弹幕轨迹可预测性和回放确定性，新增 PRNG 种子管理系统。
> 此功能不在原始 Phase 规划中，作为基础设施提前落地。

#### 9.7.1 设计决策

| 决策项 | 结论 |
|--------|------|
| 种子层级 | 双层：Stage.Seed（整关级）+ BulletPattern.Seed（单 pattern 级） |
| PRNG 算法 | 自实现 Xoshiro256**，跨平台一致，不依赖 System.Random |
| UI 控件 | Pattern Editor 和 Timeline 面包屑栏均有 Seed 字段 + Randomize 按钮 |
| 种子确定时机 | 种子是数据（存在 YAML 里），不是运行时产物。编辑器创建时给默认值 0，设计师可手动改或点 Randomize |

#### 9.7.2 新增文件

| 文件 | 职责 |
|------|------|
| `Core/Random/DeterministicRng.cs` | Xoshiro256** PRNG，支持 CaptureState/RestoreState 快照 |
| `Core/Random/SeedManager.cs` | 从主种子按序派生子种子，支持 Reset |

#### 9.7.3 修改文件

| 文件 | 变更 |
|------|------|
| `Core/DataModel/BulletPattern.cs` | 新增 `Seed` 属性 |
| `Core/DataModel/Stage.cs` | 新增 `Seed` 属性 |
| `Core/Modifiers/HomingModifier.cs` | 新增 `Rng` 属性，`Random.onUnitSphere` 替换为 `Rng.OnUnitSphere()` |
| `Runtime/Bullet/SimulationEvaluator.cs` | 构造时接收 seed，每颗子弹分配独立 DeterministicRng |
| `Core/Serialization/YamlSerializer.cs` | Stage.Seed 序列化/反序列化，Rng 属性排除序列化 |
| `Editor/UI/PatternEditor/PatternEditorView.cs` | Seed IntegerField + Randomize 按钮 |
| `Editor/UI/Timeline/TimelineEditorView.cs` | Stage Seed 控件（面包屑栏右侧） |

#### 9.7.4 注意事项

- 命名空间 `STGEngine.Core.Random` 与 `UnityEngine.Random` 冲突，HomingModifier 中需用 `UnityEngine.Random.onUnitSphere` 完整限定名
- 当前仅 HomingModifier（AntiParallel=Random 模式）使用随机数，未来新增随机行为的 modifier 应通过 `Rng` 属性注入

---

### 9.8 Phase 4+：Timeline UI 布局重构（已完成）

> Properties 面板从 Timeline 底部移至 3D 视口右侧浮动面板，
> Timeline 新增拖拽调整高度功能。

#### 9.8.1 Properties 面板重构

- 从 Timeline Root 内部底部（固定 200px）移出为独立浮动面板
- 定位在 3D 视口右侧（`Position.Absolute, right=0, top=0, bottom=timelineTop%`）
- 新增收起/展开按钮（◀/▶），收起时仅显示按钮（32px 宽）
- 面板宽度：18% / min 280px / max 400px（与 PatternEditorView 一致）

#### 9.8.2 Timeline 拖拽调整高度

- 6px 拖拽手柄（hover 蓝色高亮）
- 拖拽范围：15% ~ (100% - toolbar 高度)
- 拖到底时自动 `SetMinimized(true)`：隐藏面包屑和主内容区，只保留 toolbar
- Properties 面板 bottom 与 Timeline top 同步更新

#### 9.8.3 Timeline 内嵌 Pattern 编辑实时刷新

- 修复：编辑 pattern 参数后 Timeline 预览不更新的问题
- 根因：PatternEditorView.CommandStack 变化只刷新了 _singlePreviewer，pooled previewer 未通知
- 方案：TimelinePlaybackController.RefreshEvent() + 事件桥接

### 9.9 Phase 4a：符卡 + 小怪 + 波次 + 资源库面板（已完成）

> 新增符卡、小怪类型模板、波次数据模型，扩展序列化和 catalog 系统，
> 新增资源库面板 UI，支持 BossFight Segment 和 SpawnWaveEvent。

#### 9.9.1 Core 数据模型

- `PathKeyframe.cs`：共用路径关键帧（Time + Position），用于 Boss 路径和小怪路径
- `SpellCard.cs`：符卡 = Patterns[] + BossPath[] + Health + TimeLimit
  - `SpellCardPattern`：符卡内弹幕条目（PatternId + Delay + Duration + Offset）
- `EnemyType.cs`：小怪类型模板 = Health/Speed/Scale/PatternIds/FireDelay/Color/MeshType
- `Wave.cs`：波次 = EnemyInstance[]（每个实例引用 EnemyType + 独立路径）+ Duration
- `SpawnWaveEvent`：新 TimelineEvent 子类 [TypeTag("spawn_wave")]，引用 Wave ID
- `TimelineSegment.SpellCardIds`：BossFight Segment 通过 ID 列表引用独立符卡文件

#### 9.9.2 序列化扩展

- YamlSerializer 新增 EnemyType/Wave/SpellCard 的序列化/反序列化方法（YamlDotNet 自动）
- Stage 手写序列化扩展：spell_card_ids 字段 + SpawnWaveEvent 的 wave_id/spawn_offset
- MapTimelineEvent 新增 spawn_wave case

#### 9.9.3 STGCatalog 扩展

- 新增 EnemyTypes/Waves/SpellCards 列表 + 对应 CRUD 方法
- 新增 EnemyTypesDir/WavesDir/SpellCardsDir 目录管理
- catalog.yaml 新增 enemy_types/waves/spell_cards 三个分组
- EnsureDirectories 创建所有 5 个子目录

#### 9.9.4 示例 YAML 文件

- `EnemyTypes/grunt_a.yaml`：小兵A（HP 10，携带 demo_ring_wave）
- `Waves/wave_01.yaml`：第一波（2 个 grunt_a 交叉路径）
- `SpellCards/spell_01.yaml`：星符「流星雨」（ring_wave + sphere_homing）

#### 9.9.5 资源库面板 UI

- `AssetLibraryPanel.cs`：左侧可折叠面板，4 类资源分组（Patterns/Waves/Enemies/SpellCards）
- 颜色编码指示器：蓝/绿/橙/紫
- 点击选择，"+" 按钮创建新资源（自动生成 YAML + 更新 catalog）
- 折叠/展开按钮，与 Timeline 拖拽高度同步
- 主题覆盖兼容 Runtime UI Toolkit

#### 9.9.6 BossFight Segment 支持

- TrackAreaView 泛化：EventBlockInfo/SelectEvent/OnEventSelected 接受 TimelineEvent
- SpawnWaveEvent 块用绿色系 + 锚图标区分
- BossFight Segment 选中时 Properties 面板显示符卡列表管理 UI
- SegmentListView 右键菜单支持 MidStage/BossFight 类型切换
- 符卡列表支持添加/删除操作

### 9.10 Phase 4b：符卡预览器 + BossFight 层级预览（已完成）

- 符卡编辑模式弹幕预览（临时 Segment → PreviewerPool）
- BossFight Segment 层级预览（所有符卡顺序拼接）
- Boss 占位符同步 playback 时间
- 添加/删除符卡实时刷新预览
- 删除所有符卡时隐藏 Boss 占位符
- 安全回退点：`git tag phase4-complete`（commit 8940633）

### 9.11 Phase 5 Step 1：递归 Timeline 层级导航（已完成）

> 将编辑器从"固定层级硬编码"重构为"统一递归 Timeline 层级架构"。
> 详细设计见"待实现设计想法 > Step 1 确认设计"章节。

#### 9.11.1 接口与数据模型

- `ITimelineLayer` 接口：统一的层级抽象（LayerId, DisplayName, BlockCount, GetBlock, TotalDuration, IsSequential, CanAddBlock, CanDoubleClickEnter, CreateChildLayer, GetContextMenuEntries, BuildPropertiesPanel, LoadPreview）
- `ITimelineBlock` 接口：块的统一抽象（Id, DisplayLabel, StartTime, Duration, BlockColor, CanMove, DesignEstimate, DataSource）
- `ContextMenuEntry` 结构体：右键菜单项
- 数据模型预留字段：SpellCard.DesignEstimate/TransitionDuration, TimelineSegment.DesignEstimate, SpawnPatternEvent.ComputedEffectiveDuration

#### 9.11.2 Layer 实现类（6 个）

| 类 | 层级 | 块类型 | 排列模式 |
|----|------|--------|----------|
| StageLayer | L0 | SegmentBlock | 顺序（拖拽重排序） |
| MidStageLayer | L1a | EventBlock | 自由（可重叠） |
| BossFightLayer | L1b | SpellCardBlock + TransitionBlock | 顺序 |
| SpellCardDetailLayer | L2c | SpellCardPatternBlock | 自由 |
| WaveLayer | L2b | EnemyInstanceBlock | 自由 |
| PatternLayer | L2a | 无块（叶子层级） | — |

#### 9.11.3 TrackAreaView 泛化

- `SetSegment(TimelineSegment)` → `SetLayer(ITimelineLayer)` + legacy 桥接
- `EventBlockInfo` → `BlockInfo`（持有 `ITimelineBlock`）
- 颜色/标签从 `ITimelineBlock.BlockColor`/`DisplayLabel` 获取
- 右键菜单从 `ITimelineLayer.GetContextMenuEntries()` 动态生成
- 新增 `OnBlockDoubleClicked` 事件
- 新增 `OverrideLayerReference()` 用于 BossFight 预览后恢复 layer
- DesignEstimate 绿线 + 半透明区域渲染

#### 9.11.4 TimelineEditorView 导航重构

- `Stack<BreadcrumbEntry>` 导航栈 + `_currentLayer` 字段
- `NavigateTo()` / `NavigateBack()` / `NavigateToDepth()` 统一导航方法
- `WireLayerToTrackArea()` 连接 Layer 回调到 legacy 事件处理
- `RebuildBreadcrumb()` 从导航栈动态更新面包屑
- Stage 面包屑可点击返回 L0
- 双击 Segment → 进入 MidStage/BossFight
- 双击 Pattern/Wave 事件 → 进入 PatternLayer/WaveLayer

#### 9.11.5 Stage 全局预览

- `LoadStageOverviewPreview()`：拼接所有 Segment 的事件（MidStage 事件 + BossFight 符卡 pattern），按时间偏移合并为临时 segment
- MidStage 事件按 segment Duration 截断（超出边界的事件被移除或截短）
- `TimelinePlaybackController.IsEventActive()` 也做截断（`endTime = Min(evt.EndTime, Duration)`）

#### 9.11.6 SegmentListView 废弃

- SegmentListView 已隐藏（`display:none`），由 StageLayer + TrackAreaView 替代
- Segment 增删/类型切换/触发条件循环 → StageLayer 右键菜单 + CommandStack Undo/Redo
- 文件保留但标记为 DEPRECATED

#### 9.11.7 已知遗留项

> 以下遗留项已在 9.14 节操作矩阵审计中统一追踪，此处仅保留原始记录。

- PatternLayer / WaveLayer 的预览播放未实现 → 见 9.14.3 P2
- 面包屑目前仍使用 3 个硬编码 Label（支持到 3 层），完全动态化留后续
- BossFight 层的 TrackArea 显示符卡块，但属性面板仍走旧路径（ShowBossFightSpellCards）→ 已在 Step 1d 中修复
- 拖拽红线时自动滚动时间轴未实现 → 已在 Step 1e 中实现（EdgeScroll）

#### 9.11.8 新增文件

```
Editor/UI/Timeline/Layers/
├── ITimelineLayer.cs
├── ITimelineBlock.cs
├── ContextMenuEntry.cs
├── EventBlock.cs
├── MidStageLayer.cs
├── SegmentBlock.cs
├── StageLayer.cs
├── SpellCardBlock.cs      (含 TransitionBlock)
├── BossFightLayer.cs
├── SpellCardDetailLayer.cs (含 SpellCardPatternBlock)
├── WaveLayer.cs           (含 EnemyInstanceBlock)
└── PatternLayer.cs
```

### 9.12 Phase 5 Step 2：块内缩略图系统（已完成）

> 为所有层级的 block 添加子层级内容的视觉缩略图，让用户不用点进去就能看到大概内容。

#### 9.12.1 缩略图架构

- `ITimelineBlock.HasThumbnail` / `ThumbnailInline` / `DrawThumbnail(Painter2D, w, h)` 接口
- `IModifierThumbnailProvider` 接口：per-modifier 轨迹缩略图（3 个独立 icon）
- `TrajectoryThumbnailRenderer` 静态工具类：ComputeEmitterOnly / ComputeSingleBulletWithModifier / ComputeAllBulletsAllModifiers
- `ThumbnailBar` 结构体：颜色条缩略图数据

#### 9.12.2 各 Block 缩略图实现

| Block 类型 | 缩略图类型 | 渲染方式 |
|---|---|---|
| SegmentBlock | 子事件/符卡颜色条 | ThumbnailBar 列表，StageLayer.BuildThumbnailBars 预计算 |
| EventBlock (Pattern) | 伪 3D 弹幕轨迹 | 斜正交投影 + 时间着色 + 深度着色，3 个 inline icon |
| SpellCardBlock | 子 Pattern 颜色条 | ThumbnailBar 列表，按 Delay 排列 + Row 分配 |
| SpellCardPatternBlock | 弹幕轨迹（同 EventBlock） | 复用 TrajectoryThumbnailRenderer |
| EnemyInstanceBlock | XZ 俯视路径折线 | 时间着色（蓝→红）+ 绿色起点标记 |

#### 9.12.3 TrackAreaView 缩略图渲染

- 背景缩略图：`generateVisualContent` 回调，block 背景透明度降低（0.45）
- Inline 缩略图：小 icon 在标签后，hover 弹出放大版（200×200px popup）
- 修饰器缩略图：3 个独立 icon（发射器 / 单修饰器 / 全弹幕全修饰器）

#### 9.12.4 性能优化

- StageLayer BossFight 缩略图缓存：`Dictionary<string, float>` 避免重复磁盘 IO
- 轨迹计算懒加载：`EnsureComputed()` 首次访问时计算
- MaxBulletsDetailed = 48（发射器缩略图），MaxBullets = 12（timeline 小图）
- 修饰器采样时长 `max(2, duration)` 比发射器短

#### 9.12.5 UX 改进

- 边缘自动滚动：30px 边缘阈值，200px/s 滚动速度，适用于 Scrub/Move/Resize
- PatternEditorView 实时缩略图更新：OnPatternEditorChanged → InvalidateThumbnails → RebuildBlocks
- SpellCardBlock 缩略图刷新链路自动工作（RebuildBlocks 创建新实例）

### 9.13 Phase 5 Step 3：Modified/Override 机制（已完成）

> 在 Timeline 层级中编辑引用资源时，不修改原始 YAML 文件，自动在 Modified/ 子目录下创建完整副本。

#### 9.13.1 OverrideManager 核心类

新文件：`Editor/UI/FileManager/OverrideManager.cs`

- 静态工具类，管理 `STGData/Modified/` 目录下的覆盖文件
- `GetOverridePath(contextId, resourceId)` → `Modified/{contextId}/{resourceId}.yaml`
- `HasOverride(contextId, resourceId)` → 检查文件是否存在
- `ResolveSpellCardPath/ResolvePatternPath/ResolveWavePath` → 先查 override，再 fallback 到 catalog
- `SaveOverride(contextId, resourceId, yaml)` → 创建目录 + 写文件
- `DeleteOverride(contextId, resourceId)` → 删除覆盖文件（还原为原始）
- `SaveAsNewTemplate(catalog, contextId, resourceId, newId, resourceType)` → 复制到原始目录 + 注册 catalog
- Context ID 辅助方法：`SegmentContext(segId)` / `SpellCardContext(segId, scId)`

#### 9.13.2 资源加载路径拦截（4 个点）

| 拦截点 | 原始路径 | Override 路径 |
|---|---|---|
| BossFightLayer.RebuildBlockList | `catalog.GetSpellCardPath(scId)` | `OverrideManager.ResolveSpellCardPath(catalog, segmentId, scId)` |
| MidStageLayer.CreateChildLayer | `catalog.GetWavePath(waveId)` | `OverrideManager.ResolveWavePath(catalog, segmentId, waveId)` |
| StageLayer.GetCachedSpellCardTimeLimit | `catalog.GetSpellCardPath(scId)` | `OverrideManager.ResolveSpellCardPath(catalog, segmentId, scId)` |
| TimelineEditorView (3 处) | `catalog.GetSpellCardPath(scId)` | `OverrideManager.ResolveSpellCardPath(catalog, contextId, scId)` |

#### 9.13.3 保存逻辑

- `SaveCurrentSpellCard()` 检查 `_editingBossFightSegment != null`
  - 有 → 保存到 `Modified/{segmentId}/{spellCardId}.yaml`（OverrideManager.SaveOverride）
  - 无 → 保存到原始路径（直接编辑模式）

#### 9.13.4 [M] 标记显示

- `ITimelineBlock.IsModified` 接口属性（所有 Block 实现）
- `SpellCardBlock` 构造时接收 `isModified` 参数，DisplayLabel 前缀 `[M]`
- TrackAreaView：modified 块显示橙色边框（2px，`Color(1, 0.65, 0.2, 0.9)`）
- 面包屑：SpellCard 层级有 override 时显示 `[M]` 前缀 + 橙色文字

#### 9.13.5 还原/另存操作

- BossFightLayer 右键菜单新增（仅 modified 块可见）：
  - "Revert to Original" → `OverrideManager.DeleteOverride` + 刷新视图
  - "Save as New Template..." → 弹出对话框输入新 ID → `OverrideManager.SaveAsNewTemplate`
- `ShowSaveAsNewTemplateDialog` 模态对话框：输入新 ID，保存到原始目录并注册 catalog

#### 9.13.6 Context ID 格式

- BossFight 段内的 SpellCard：`contextId = segmentId`（如 `"segment_1"`）
- SpellCard 内的 Pattern：`contextId = "{segmentId}/{spellCardId}"`（如 `"segment_1/spell_01"`）
- MidStage 段内的 Wave：`contextId = segmentId`

#### 9.13.7 新增/修改文件

```
新增:
  Editor/UI/FileManager/OverrideManager.cs

修改:
  Editor/UI/Timeline/Layers/ITimelineBlock.cs      — 新增 IsModified 属性
  Editor/UI/Timeline/Layers/BossFightLayer.cs       — contextId + override 解析 + 右键菜单
  Editor/UI/Timeline/Layers/SpellCardDetailLayer.cs — contextId 参数
  Editor/UI/Timeline/Layers/MidStageLayer.cs        — ContextId 属性 + override 解析
  Editor/UI/Timeline/Layers/StageLayer.cs           — override-aware 缓存
  Editor/UI/Timeline/Layers/SpellCardBlock.cs       — IsModified + [M] 标签
  Editor/UI/Timeline/Layers/EventBlock.cs           — IsModified = false
  Editor/UI/Timeline/Layers/SegmentBlock.cs         — IsModified = false
  Editor/UI/Timeline/Layers/WaveLayer.cs            — IsModified = false (EnemyInstanceBlock)
  Editor/UI/Timeline/TrackAreaView.cs               — 橙色边框渲染
  Editor/UI/Timeline/TimelineEditorView.cs          — override 保存 + 面包屑 [M] + 对话框
```

#### 9.13.8 已知遗留项

> 以下遗留项已在 9.14 节操作矩阵审计中统一追踪。

- Pattern 级别的 override（SpellCard 内的 Pattern 引用）尚未实现完整的保存链路 → 见 9.14.3 P1
- Wave 级别的 override 保存尚未实现 → 见 9.14.3 P1
- "Save as New Template" 后不自动替换当前引用（需手动更新 SpellCardIds）

---

### 9.14 操作矩阵审计（Phase 5 Step 1 实施后）

> 基于代码审计，记录每层 × 每操作的实现状态。
> ✅ 已实现 | ⚠️ 部分实现 | ❌ 未实现 | ➖ 不适用

#### 9.14.1 操作矩阵

| 层级 | 右键添加 | 右键删除 | 双击进入 | 拖拽移动 | 拖拽重排 | Resize | 属性编辑 | 属性保存 | Rename ID | Modified | 缩略图 | 预览 |
|------|---------|---------|---------|---------|---------|--------|---------|---------|----------|---------|--------|------|
| L0 Stage | ✅ Add MidStage/BossFight | ✅ Delete Segment | ✅ → L1a/L1b | ➖ | ✅ 拖拽重排 | ✅ Duration | ✅ Name/Duration | ✅ Save Stage | ➖ | ➖ | ✅ 色条缩略 | ✅ 合并预览 |
| L1a MidStage | ✅ Add Pattern/Wave | ✅ Delete Event | ✅ → L2a/L2b | ✅ StartTime | ➖ | ✅ Duration | ✅ 完整属性面板 | ✅ Save Stage | ⚠️ 仅 Pattern | ➖ | ✅ 弹幕缩略 | ✅ Segment 预览 |
| L1b BossFight | ✅ Add SpellCard | ✅ Delete SpellCard | ✅ → L2c | ➖ | ✅ 拖拽重排 | ✅ TimeLimit | ✅ 完整属性面板 | ✅ Override 保存 | ➖ | ✅ Revert/SaveAs | ✅ 弹幕缩略 | ✅ 合并 SC 预览 |
| L2c SpellCard | ✅ Add Pattern | ✅ Delete Pattern | ✅ → L2a | ✅ Delay | ➖ | ✅ Duration | ✅ 完整属性面板 | ✅ Override 保存 | ➖ | ➖ | ✅ 弹幕缩略 | ✅ SC 预览 |
| L2b Wave | ✅ Add Enemy | ✅ Delete Enemy | ❌ → L3（未实现） | ✅ SpawnDelay | ➖ | ➖ 路径推导 | ✅ SpawnDelay 可编辑 | ✅ Override 保存 | ➖ | ➖ | ✅ 路径缩略 | ➖ 无弹幕 |
| L2a Pattern | ➖ 叶子层 | ➖ | ➖ | ➖ | ➖ | ✅ Duration | ✅ PatternEditorView | ✅ 直接保存 | ➖ | ➖ | ✅ 弹幕缩略 | ✅ 单 Pattern 预览 |

#### 9.14.2 存储结构与持久化状态

```
STGData/                              持久化状态
├── Stages/
│   ├── demo_stage.yaml               ✅ Save Stage 按钮保存
│   └── demo_stage_2.yaml             ✅
├── Patterns/
│   ├── demo_ring_wave.yaml           ✅ PatternEditorView 保存
│   └── ...                           ✅
├── SpellCards/
│   ├── spell_01.yaml                 ✅ 属性面板编辑自动保存
│   └── ...                           ✅
├── Waves/
│   ├── wave_01.yaml                  ❌ Add/Delete/SpawnDelay 修改不保存
│   └── new_wave.yaml                 ❌
├── EnemyTypes/
│   ├── grunt_a.yaml                  ➖ L3 未实现，无编辑入口
│   └── new_enemy.yaml                ➖
├── Modified/
│   └── boss_phase_1/
│       └── spell_01.yaml             ✅ Override 自动保存
└── catalog.yaml                      ✅ 资源注册表
```

#### 9.14.3 缺陷清单与修复优先级

> Phase 5 Step 2 审计修复后更新。✅ = 已修复，⚠️ = 待修复。

| 优先级 | 缺陷 | 位置 | 影响 | 状态 |
|--------|------|------|------|------|
| P0 | SpellCardDetailLayer.CreateChildLayer 返回 null | SpellCardDetailLayer.cs | L2c 双击进入 PatternLayer 导航链断裂 | ✅ 已修复 |
| P1 | WaveLayer 数据不持久化 | TimelineEditorView.cs WireLayerToTrackArea | Add/Delete Enemy、SpawnDelay 编辑后丢失 | ✅ 已修复 |
| P1 | SpellCardDetailLayer 属性编辑后不保存 | TimelineEditorView.cs BuildSpellCardPatternProperties | Delay/Duration/Offset 编辑后丢失 | ✅ 已修复 |
| P1 | EnterSpellCardEditing 导航不完整 | TimelineEditorView.cs EnterSpellCardEditing | 从属性面板 Edit 按钮进入 SC 编辑时缺少 Wire/SetLayer/RebuildBreadcrumb，右键菜单显示错误 | ✅ 已修复 |
| P2 | PatternLayer 无 PatternEditorView 嵌入 | TimelineEditorView.cs ShowLayerSummary | 进入 Pattern 层后只有提示文字，无法编辑 | ✅ 已修复 |
| P2 | EnemyInstanceBlock.Duration setter 为空 | WaveLayer.cs:45 | Resize 拖拽无效果 | ✅ 已修复（CanResizeDuration 探测） |
| P2 | SpellCardDetailLayer 属性面板只有只读 Label | TimelineEditorView.cs BuildSpellCardPatternProperties | 选中 Pattern 块后无可编辑字段 | ✅ 已修复 |
| P2 | WaveLayer 属性面板只有只读 Label | TimelineEditorView.cs BuildEnemyInstanceProperties | 选中 Enemy 块后无可编辑字段 | ✅ 已修复 |
| P2 | Pattern Override 写入端未接通 | PatternEditorView 直接写原始文件 | SC 内编辑 Pattern 不走 Override 机制 | ⚠️ 待修复 |
| P3 | SpellCard Override per-segment 共享 | BossFightLayer contextId | 同一 BossFight 重复引用同一 scId 共享 Override | ⚠️ 待修复（风险低） |
| P3 | Pattern/Wave 缺少 Rename ID 入口 | MidStageLayer 右键菜单 | 无法重命名资源 ID | ⚠️ 待修复 |
| P3 | L3 EnemyType 层未实现 | WaveLayer.cs | 设计文档标注为"未来" | ⚠️ 待修复 |

#### 9.14.4 缺陷详情

##### P0: SpellCardDetailLayer 双击进入 PatternLayer

`SpellCardDetailLayer.CreateChildLayer()` 写死 `return null`，注释 "PatternLayer will be implemented in step 1g"。但 `PatternLayer` 类已完整实现。

修复：
```csharp
public ITimelineLayer CreateChildLayer(ITimelineBlock block)
{
    if (block is SpellCardPatternBlock spb && spb.DataSource is SpellCardPattern scp)
    {
        var pattern = _library?.Resolve(scp.PatternId);
        if (pattern != null)
            return new PatternLayer(pattern, scp.PatternId);
    }
    return null;
}
```

##### P1: WaveLayer 数据不持久化

`WireLayerToTrackArea(waveLayer)` 中 `OnAddEnemyRequested` / `OnDeleteEnemyRequested` 回调只调用了 `waveLayer.InvalidateBlocks()` 和 `OnStageDataChanged()`，但 Wave 数据存储在独立 YAML 文件中（`STGData/Waves/{waveId}.yaml`），不随 Stage 一起保存。

修复：在回调中添加 Wave YAML 序列化保存：
```csharp
// 在 Add/Delete 回调末尾追加：
SaveWaveInContext(waveLayer.Wave, waveLayer.WaveId);
```

##### P1: SpellCardDetailLayer 属性编辑不保存

`BuildSpellCardPatternProperties()` 中编辑 Delay/Duration/Offset 后需确认是否调用了 `SaveSpellCardInContext()`。如果缺失，编辑结果只存在于内存中的 SpellCard 对象，关闭编辑器后丢失。

##### P2: EnemyInstanceBlock Resize 无效

`Duration` 的 setter 为 `set { }`（空实现），因为 Duration 由路径最后一个关键帧时间推导。两种修复策略：
- A) 允许手动覆盖：添加 `_overrideDuration` 字段
- B) 禁用 Resize：在 ITimelineBlock 接口中添加 `bool CanResize` 属性（需改接口）

推荐策略 A，不改接口。

