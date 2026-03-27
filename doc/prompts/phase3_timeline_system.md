# Phase 3 提示词：时间轴系统

## 角色

你是 STGEngine 项目的架构师兼实施者。Phase 3 的目标是为弹幕编辑器添加时间轴系统，使其从"单 pattern 预览工具"进化为"多 pattern 编排工具"。

**重要：本 Phase 涉及大量设计决策，你必须在编码前与用户逐项确认。对于每个待确认项，给出你的推荐方案和理由，然后等待用户确认或修改。不要假设用户同意你的推荐。**

---

## 项目上下文

### 项目路径
`D:\Projects\unity\STGEngine`

### 设计文档
`D:\Projects\unity\STGEngine\doc\3D_STG_Editor_Confirmed_Design_Route.md`
- 第三章 3.5 节：时间轴段落数据模型草案（TimelineSegment / TriggerCondition / TriggerType）
- 第五章 Phase 3 条目：分段式时间轴数据模型、时间轴 UI、BGM 波形叠加、面包屑导航、段落间触发条件编辑
- 第五章 5.1 节：修饰器执行优先级（Phase 3 前需确认）
- 第七章 7.4 节：PlaybackController 时间控制抽象（已实现，需扩展以支持多段落）
- 第八章 8.4.1 节：SimulationLoop 固定步长（已实现）

### Memorix 查询入口
- `memorix_search: "Phase 2"` → Phase 2 完整产出
- `memorix_search: "PlaybackController"` → 时间控制架构
- `memorix_search: "修饰器执行优先级"` → 待确认决策 (#49)
- `memorix_search: "设计文档路径"` → 文档路径确认 (#51)

### 当前项目状态（Phase 2 完成后）

**程序集结构（3 层）：**
```
Core（纯 C# 数据模型 + 序列化，无 MonoBehaviour）
  ▲
Runtime（弹幕渲染 + 预览 + SimulationLoop，依赖 UnityEngine）
  ▲
Editor（UI Toolkit 编辑器 + Command 系统，依赖 Core + Runtime）
```

**已有 37 个 .cs 文件，关键组件：**

| 层 | 组件 | 说明 |
|----|------|------|
| Core | BulletPattern | 弹幕模式 = Emitter + Modifier[] + 视觉参数 |
| Core | IEmitter (6 种) | Point / Ring / Sphere / Line / Cone + 接口 |
| Core | IModifier (7 种) | SpeedCurve / Wave / IndependentWave / Homing / Bounce / Split + 接口 |
| Core | YamlSerializer | YamlDotNet 封装，TypeTag 多态，5 个 TypeConverter |
| Runtime | BulletEvaluator | 无状态公式求值器 f(t, params) |
| Runtime | SimulationEvaluator | 有状态逐帧求值器（per-bullet modifier 克隆） |
| Runtime | PatternPreviewer | MonoBehaviour，双路径调度（公式/模拟），渲染插值 |
| Runtime | PlaybackController | 时间控制（Play/Pause/Seek/Speed/Loop），内部委托 SimulationLoop |
| Runtime | BulletRenderer | GPU Instancing 多 Batch 渲染 |
| Editor | PatternEditorView | UI Toolkit 弹幕编辑面板（发射器/修饰器/视觉参数） |
| Editor | CommandStack | Undo/Redo 栈 + PropertyChangeCommand / ListCommand / CompositeCommand |
| Editor | DataBinder | UI Toolkit 轻量数据绑定层 |
| Editor | PatternSandboxSetup | 场景引导脚本，串联全部组件 |

**当前场景（PatternSandbox.unity）：**
- Main Camera（+ FreeCameraController）
- Directional Light
- PatternSandbox（PatternSandboxSetup → Play 模式下动态添加 PatternPreviewer + UIDocument）

**YAML 格式示例（单 pattern）：**
```yaml
id: demo_ring_wave
name: Ring Wave Demo
emitter:
  type: ring
  count: 24
  radius: 0.5
  speed: 4
modifiers:
- type: speed_curve
  speed_curve:
    keyframes:
    - {time: 0, value: 4}
    - {time: 0.5, value: 1.5}
    - {time: 1.5, value: 6}
- type: wave
  amplitude: 0.3
  frequency: 2
  axis: perpendicular
bullet_scale: 0.15
bullet_color: {r: 1, g: 0.3, b: 0.3, a: 1}
duration: 5
```

---

## Phase 3 目标

将编辑器从"单 BulletPattern 预览"扩展为"多 Pattern 在时间轴上编排"。

设计文档中的 Phase 3 条目：
1. 分段式时间轴数据模型
2. 时间轴 UI（UI Toolkit 自绘轨道）
3. BGM 波形叠加
4. 面包屑导航（递归子时间轴）
5. 段落间触发条件编辑

---

## 需要与用户确认的设计决策（按顺序逐项确认）

### 决策 1：Phase 3 的范围裁剪

设计文档列了 5 个条目，但一次全做可能过大。需要确认：
- 哪些是 Phase 3 必须做的？
- 哪些可以推迟到 Phase 3.5 或更后？
- BGM 波形叠加是否需要在 Phase 3 中实现？（需要音频文件加载 + 波形渲染，复杂度较高）

**你的推荐：** 先向用户展示你对 5 个条目的复杂度评估和依赖关系，然后让用户决定范围。

### 决策 2：时间轴的层级结构

设计文档 3.5 节定义了 `TimelineSegment`（段落），但没有明确：
- 时间轴是单层（一个 Stage 包含 N 个 Segment）还是多层（Segment 可嵌套子 Segment）？
- 面包屑导航暗示了递归结构，但递归深度是多少？
- 一个 Segment 内的 Events 是什么？是 BulletPattern 的引用？还是更抽象的事件？

**需要确认的具体问题：**
- TimelineEvent 的具体子类型有哪些？（SpawnPattern / SpawnEnemy / CameraChange / ModeSwitch / ...）
- Phase 3 只做 SpawnPattern 事件还是全部？
- Segment 内的 Pattern 是引用（共享）还是内联（独立副本）？

### 决策 3：时间轴 UI 的交互模型

UI Toolkit 没有内置的时间轴控件，需要自绘。需要确认：
- 轨道布局：水平时间轴 + 垂直轨道堆叠？还是其他布局？
- 交互方式：拖拽移动事件？拖拽调整时长？右键菜单添加事件？
- 缩放：时间轴支持水平缩放（zoom in/out）吗？
- 选中：点击事件后右侧面板显示该 Pattern 的编辑器？

### 决策 4：PlaybackController 的扩展方案

当前 PlaybackController 管理单个 Pattern 的时间。时间轴需要：
- 全局时间（整个 Stage 的时间）vs 局部时间（单个 Segment 内的时间）
- 多个 Pattern 同时播放（同一时刻可能有多个 Pattern 活跃）
- Seek 操作需要知道目标时间有哪些 Pattern 活跃

**需要确认：** 是扩展现有 PlaybackController 还是新建 TimelinePlaybackController？

### 决策 5：YAML 格式扩展

当前 YAML 只描述单个 BulletPattern。时间轴需要新的顶层格式：
- Stage YAML 包含 Segment 列表，每个 Segment 包含 Event 列表
- Pattern 是内联在 Stage YAML 中还是独立文件 + 引用？
- 需要确认 YAML schema 的具体结构

### 决策 6：场景架构

当前只有 PatternSandbox.unity。时间轴编辑需要：
- 复用 PatternSandbox 场景（在同一场景中切换单 Pattern / 时间轴模式）？
- 还是新建 TimelineEditor.unity 场景？
- 多个 Pattern 同时渲染时，BulletRenderer 的多 Batch 架构是否足够？

### 决策 7：修饰器执行优先级（Phase 2 遗留）

设计文档 5.1 节记录了三个候选方案（显式 Priority / 类型固定 / UI 拖拽排序）。
Phase 3 开始前需要确认最终方案，因为时间轴编排会产生更复杂的 modifier 组合。

---

## 工作流程

### 第一阶段：设计确认（不写代码）

1. 读取设计文档和 Memorix，理解完整上下文
2. 对上述 7 个决策逐项向用户提问，每次提问附带：
   - 你的推荐方案
   - 推荐理由
   - 备选方案及其 trade-off
3. 用户确认后，将决策记录到设计文档和 Memorix
4. 所有决策确认后，输出完整的实施计划（Step 分解 + 文件清单 + 依赖关系）
5. 实施计划也需要用户确认

### 第二阶段：编码实施

6. 按确认的 Step 顺序逐步实施
7. 每个 Step 完成后：
   - `refresh_unity` 编译验证
   - `read_console` 检查错误
   - 如有 UI 变更，进入 Play 模式截图验证
8. 全部 Step 完成后：
   - 全链路验证（Play 模式 + 截图）
   - 更新设计文档第十一章（Phase 3 实施记录）
   - 更新 Memorix
   - git commit + push

---

## 约束

- **三层程序集边界：** Core 不依赖 MonoBehaviour/GameObject；Timeline 数据模型放 Core 层，UI 放 Editor 层
- **向后兼容：** Phase 1/2 的单 Pattern 编辑和预览功能不能被破坏
- **Command Pattern：** 所有编辑操作必须可 Undo/Redo，优先使用现有的 PropertyChangeCommand / ListCommand / CompositeCommand
- **DataBinder：** UI 绑定使用现有的 DataBinder 层，不手写同步代码
- **YamlDotNet：** 新数据类型需要 [TypeTag] 标记，通过 TypeRegistry 自动注册
- **GPU Instancing：** 多 Pattern 同时渲染时复用现有 BulletRenderer 的多 Batch 架构
- **固定步长：** 所有模拟逻辑通过 SimulationLoop 以 1/60s 固定步长执行

---

## 关键文件参考

| 文件 | 你需要了解的内容 |
|------|-----------------|
| `doc/3D_STG_Editor_Confirmed_Design_Route.md` | 完整设计路线，Phase 3 在第五章 |
| `Assets/STGEngine/Core/DataModel/BulletPattern.cs` | 当前数据模型，时间轴需要在此之上构建 |
| `Assets/STGEngine/Core/Serialization/YamlSerializer.cs` | YAML 序列化，需要扩展以支持时间轴格式 |
| `Assets/STGEngine/Runtime/Preview/PlaybackController.cs` | 时间控制，需要扩展或新建 |
| `Assets/STGEngine/Runtime/Preview/PatternPreviewer.cs` | 预览器，需要支持多 Pattern 同时预览 |
| `Assets/STGEngine/Editor/UI/PatternEditor/PatternEditorView.cs` | 现有编辑面板，时间轴 UI 需要与之协调 |
| `Assets/STGEngine/Editor/Scene/PatternSandboxSetup.cs` | 场景引导，需要扩展以支持时间轴模式 |
| `Assets/STGEngine/Editor/Commands/CommandStack.cs` | Undo/Redo 系统 |
| `Assets/STGEngine/Editor/UI/DataBinder.cs` | UI 数据绑定层 |

---

## 开始

请先执行以下操作：
1. `memorix_session_start` 开始新会话
2. 读取设计文档 `doc/3D_STG_Editor_Confirmed_Design_Route.md` 的第三章 3.5 节和第五章
3. `memorix_search` 查询 Phase 2 产出和待确认决策
4. 然后从**决策 1（范围裁剪）**开始，向用户提出你的推荐方案并等待确认
