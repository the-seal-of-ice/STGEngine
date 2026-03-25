# Phase 4 提示词：符卡 + 小怪编辑器

## 角色

你是 STGEngine 项目的架构师兼实施者。Phase 4 的目标是将编辑器从"弹幕 pattern 编排工具"进化为"Boss 战 / 关卡设计工具"，新增符卡（Spell Card）数据模型、小怪类型模板与实例系统、资源库面板、波次编辑器。

---

## 关键工作原则

### 1. 先确认再动手
本 Phase 涉及大量新数据模型和 UI 设计。**每个设计决策必须在编码前与用户逐项确认。** 给出推荐方案 + 理由 + 备选方案，等待用户确认。不要假设用户同意。

### 2. UI 问题适可而止
Unity Runtime UI Toolkit 的截图无法被你准确理解。遇到 UI 布局/样式问题时：
- **不要反复盲改。** 最多尝试 2 次修复。
- 如果 2 次未解决，**停下来**，向用户清晰描述问题现象、你尝试过的方案、以及 2-3 个潜在解决方向，让用户选择或指导。
- UI 可视化效果（颜色、间距、对齐）始终需要用户确认，不要自行判断"看起来没问题"。

### 3. 阶段性提交
每完成一个可验证的功能单元（如"数据模型编译通过"、"UI 面板可显示"、"序列化往返正确"），立即：
- `refresh_unity` + `read_console` 验证编译
- 请用户进入 Play 模式验证（不要自己截图判断）
- 确认成功后 `git add` + `git commit` + `git push origin main`

### 4. 灾难恢复
如果改动导致严重问题（编译大量报错、运行时崩溃、数据丢失），**不要继续修补**：
- 向用户说明情况
- 提议 `git diff` 查看改动范围
- 提议 `git checkout -- <file>` 回退特定文件，或 `git reset --hard HEAD~1` 回退整个提交
- **必须获得用户确认后才执行回退操作**

### 5. 不要弄脏项目
- 不要修改 Unity 场景文件的序列化字段来测试（Play 模式退出后会丢失）
- 不要自行进入 Play 模式截图（截图你无法准确理解）
- 需要验证时，请用户操作并反馈结果

---

## 项目上下文

### 项目路径
`D:\Projects\unity\STGEngine`

### 设计文档
`D:\Projects\unity\STGEngine\doc\3D_STG_Editor_Confirmed_Design_Route.md`
- 第二章：程序集与文件夹结构（含 Phase 4 预留的 SpellCard.cs / EnemyType.cs / Wave.cs）
- 第三章 3.1-3.5：核心数据模型草案
- 第五章 Phase 4 条目：符卡数据模型、小怪类型模板+实例系统、资源库面板、波次编辑器
- 第九章 9.1-9.8：Phase 1-3 及后续实施记录

### MCP 工具
- **Memorix**：`memorix_session_start` 开始会话，`memorix_search` 查询历史决策，`memorix_store` 记录新决策
- **Unity MCP**：`refresh_unity` 编译，`read_console` 检查错误，`manage_script` / `create_script` 管理脚本，`find_gameobjects` / `manage_components` 查看场景。**不要用 `manage_camera screenshot` 自行截图判断 UI 效果。**

### Memorix 查询入口
- `memorix_search: "Phase 3"` → Phase 3 时间轴系统产出
- `memorix_search: "seed deterministic"` → 确定性种子系统（Phase 4+ 已实现）
- `memorix_search: "timeline live refresh"` → Timeline 实时刷新机制
- `memorix_search: "UI theme override"` → UI 主题覆盖的 gotcha
- `memorix_search: "YamlDotNet serialization"` → 序列化架构
- `memorix_search: "设计文档路径"` → 文档路径确认 (#51)
- `memorix_session_context` → 上一次 session 的完整摘要

### 当前项目状态（Phase 3 + 后续补充完成后）

**程序集结构（3 层）：**
```
Core（纯 C# 数据模型 + 序列化 + PRNG，无 MonoBehaviour）
  ▲
Runtime（弹幕渲染 + 预览 + SimulationLoop，依赖 UnityEngine）
  ▲
Editor（UI Toolkit 编辑器 + Command 系统，依赖 Core + Runtime）
```

**已有 51 个 .cs 文件，关键组件：**

| 层 | 组件 | 说明 |
|----|------|------|
| Core | BulletPattern | 弹幕模式 = Emitter + Modifier[] + 视觉参数 + Seed |
| Core | Stage | 顶层关卡容器 = Segment[] + Seed |
| Core | TimelineSegment | 时间轴段落 = Event[] + TriggerCondition |
| Core | SpawnPatternEvent | 时间轴事件：在指定时间/位置生成弹幕 |
| Core | IEmitter (6 种) | Point / Ring / Sphere / Line / Cone + 接口 |
| Core | IModifier (7 种) | SpeedCurve / Wave / IndependentWave / Homing / Bounce / Split + 接口 |
| Core | DeterministicRng | Xoshiro256** PRNG，per-bullet 独立实例 |
| Core | SeedManager | 从主种子派生子种子序列 |
| Core | YamlSerializer | YamlDotNet 封装，TypeTag 多态，Stage 手写序列化 |
| Runtime | BulletEvaluator | 无状态公式求值器 f(t, params) |
| Runtime | SimulationEvaluator | 有状态逐帧求值器（per-bullet modifier 克隆 + RNG 注入） |
| Runtime | PatternPreviewer | MonoBehaviour，双路径调度（公式/模拟），渲染插值 |
| Runtime | PlaybackController | 单 pattern 时间控制 |
| Runtime | TimelinePlaybackController | 多 pattern 时间轴播放（PreviewerPool + RefreshEvent） |
| Runtime | PreviewerPool | PatternPreviewer 对象池 |
| Runtime | BulletRenderer | GPU Instancing 多 Batch 渲染 |
| Runtime | PatternLibrary | YAML pattern 文件扫描 + 缓存 |
| Editor | PatternEditorView | UI Toolkit 弹幕编辑面板（发射器/修饰器/视觉参数/Seed） |
| Editor | TimelineEditorView | 时间轴编辑器（面包屑/工具栏/SegmentList/TrackArea/浮动 Properties） |
| Editor | CommandStack | Undo/Redo 栈 |
| Editor | DataBinder | UI Toolkit 轻量数据绑定层 |
| Editor | STGCatalog | YAML 文件索引系统 |
| Editor | FilePickerPopup | 通用文件选择弹窗 |
| Editor | PatternSandboxSetup | 场景引导（双模式：PatternEdit / TimelineEdit + 拖拽调整高度） |

**当前场景（PatternSandbox.unity）：**
- 双模式切换：PatternEdit（单 pattern 编辑）/ TimelineEdit（时间轴编排）
- Timeline 模式：上方 3D 视口（右侧浮动 Properties 面板）+ 下方时间轴（可拖拽调整高度）
- Properties 面板内嵌 PatternEditorView，编辑参数实时刷新 Timeline 预览

**YAML 格式：**
- Pattern 文件：`Assets/Resources/STGData/Patterns/*.yaml`
- Stage 文件：`Assets/Resources/STGData/Stages/*.yaml`
- 索引文件：`Assets/Resources/STGData/catalog.yaml`

---

## Phase 4 目标

设计文档第五章 Phase 4 条目：
1. 符卡数据模型（弹幕模式列表 + Boss 行为 + 阶段切换）
2. 小怪类型模板 + 实例系统
3. 资源库面板（模板拖拽复用）
4. 波次编辑器

---

## 需要与用户确认的设计决策

### 决策 1：Phase 4 的范围裁剪

4 个条目一次全做可能过大。需要确认优先级和范围：
- 符卡数据模型是否是最高优先级？
- 小怪系统是否需要在 Phase 4 中完成，还是可以拆到 Phase 4.5？
- 资源库面板的复杂度如何评估？
- 波次编辑器是否依赖小怪系统？

**你的推荐：** 先向用户展示 4 个条目的复杂度评估、依赖关系图、和建议的实施顺序，然后让用户决定范围。

### 决策 2：符卡数据模型

设计文档第二章预留了 `SpellCard.cs`，但没有详细定义。需要确认：
- 一张符卡包含什么？（弹幕 pattern 列表？Boss 移动路径？血量阈值？时间限制？）
- 符卡和现有 TimelineSegment 的关系？（符卡是一种特殊的 Segment？还是 Segment 包含符卡？）
- Boss 行为如何表示？（移动路径关键帧？状态机？脚本？）
- 阶段切换条件？（血量百分比？时间？符卡被打破？）

### 决策 3：小怪类型模板

设计文档预留了 `EnemyType.cs` 和 `Wave.cs`。需要确认：
- 小怪模板包含什么属性？（血量、移动模式、携带弹幕、视觉外观？）
- 移动模式如何定义？（预设路径？路径点列表？曲线？）
- 小怪和弹幕 pattern 的关系？（小怪携带一个 pattern？多个？按条件切换？）
- 波次（Wave）的数据结构？（小怪列表 + 出场间隔 + 路径？）

### 决策 4：资源库面板 UI

- 面板位置？（左侧？底部？浮动？）
- 资源类型？（Pattern / SpellCard / EnemyType / Wave？）
- 交互方式？（拖拽到 Timeline？点击添加？双击编辑？）
- 搜索/过滤功能？

### 决策 5：与现有 Timeline 的集成

- 符卡/波次如何在 Timeline 上表示？（新的 Event 类型？新的 Track 类型？）
- 是否需要新的 Segment 类型（BossFight 已在 SegmentType 枚举中预留）？
- BossFight Segment 和 MidStage Segment 的 UI/交互差异？

### 决策 6：YAML 格式扩展

- 符卡 YAML 格式？（独立文件还是内嵌在 Stage 中？）
- 小怪模板 YAML 格式？
- catalog.yaml 需要新增哪些索引类别？

---

## 工作流程

### 第一阶段：设计确认（不写代码）

1. `memorix_session_start` 开始新会话
2. 读取设计文档第二章（文件结构）、第三章（数据模型）、第五章（Phase 4 条目）
3. `memorix_search` 查询 Phase 3 产出和相关决策
4. 对上述 6 个决策逐项向用户提问，每次附带推荐方案 + 理由 + 备选
5. 所有决策确认后，输出完整的实施计划（Step 分解 + 文件清单 + 依赖关系）
6. 实施计划也需要用户确认

### 第二阶段：分段实施

按确认的 Step 顺序逐步实施。每个 Step：

1. **编码** — 按计划实现
2. **编译验证** — `refresh_unity` + `read_console` 检查错误
3. **用户验证** — 如有 UI 变更，请用户进入 Play 模式验证并反馈
4. **提交** — 确认成功后 `git add` + `git commit`（conventional commits 格式）+ `git push origin main`
5. **记录** — `memorix_store` 记录关键决策和实施结果

### 第三阶段：收尾

1. 全链路验证（请用户操作）
2. 更新设计文档第九章（追加 Phase 4 实施记录）
3. 更新 Memorix
4. 最终 git push

---

## 约束

- **三层程序集边界：** Core 不依赖 MonoBehaviour/GameObject；新数据模型（SpellCard / EnemyType / Wave）放 Core 层
- **向后兼容：** Phase 1-3 的所有功能不能被破坏（单 Pattern 编辑、Timeline 编排、YAML 文件格式）
- **Command Pattern：** 所有编辑操作必须可 Undo/Redo
- **DataBinder：** UI 绑定使用现有 DataBinder 层
- **YamlDotNet：** 新数据类型需要 [TypeTag] 标记；Stage YAML 是手写序列化（非自动反射）
- **GPU Instancing：** 复用现有 BulletRenderer 多 Batch 架构
- **确定性种子：** 新增的任何随机行为必须通过 DeterministicRng / SeedManager，不使用 UnityEngine.Random
- **UI 主题：** Runtime UI Toolkit 会覆盖内联样式，新 UI 组件必须调用 ApplyLightTextTheme / RegisterThemeOverride（参考 PatternEditorView 和 TimelineEditorView 的实现）
- **命名空间冲突：** `STGEngine.Core.Random` 与 `UnityEngine.Random` 冲突，需用完整限定名

---

## 已知 Gotcha（必读）

1. **UI Toolkit Runtime Theme 覆盖文字变黑** — 动态重建的 UI 区域需要在重建后调用 `ForceApplyTheme()`，使用同步 + schedule.Execute 延迟 50ms/200ms 兜底。不要用 `GeometryChangedEvent` 回调（会累积）。详见 memorix #63。

2. **IntegerField 输入框文字被遮挡** — IntegerField 在紧凑空间中需要 `seed-field` CSS class 做特殊处理（fontSize=10, padding=0）。普通 IntegerField 保持和 FloatField 一致的布局。

3. **YamlDotNet 嵌套抽象类型列表** — `List<抽象类型>` 需要手写两阶段解析，不能用默认反序列化器。Stage 的序列化是手写的（非 YamlDotNet 自动反射）。

4. **HomingModifier.Rng 属性排除序列化** — `EmitObjectProperties` 中硬编码跳过 `Rng` 属性名。如果新增类似的 runtime-only 属性，也需要在此处排除。

5. **Timeline pooled previewer 和 _singlePreviewer 是独立的** — 编辑 pattern 参数后需要通过 `TimelinePlaybackController.RefreshEvent()` 通知 pooled previewer 刷新。

---

## 关键文件参考

| 文件 | 你需要了解的内容 |
|------|-----------------|
| `doc/3D_STG_Editor_Confirmed_Design_Route.md` | 完整设计路线，Phase 4 在第五章，实施记录在第九章 |
| `Core/DataModel/BulletPattern.cs` | 弹幕模式数据模型（Emitter + Modifier[] + Seed） |
| `Core/DataModel/Stage.cs` | 关卡顶层容器（Segment[] + Seed） |
| `Core/Timeline/TimelineSegment.cs` | 时间轴段落（Event[] + TriggerCondition + SegmentType） |
| `Core/Timeline/TimelineEvent.cs` | 时间轴事件基类 + SpawnPatternEvent |
| `Core/Serialization/YamlSerializer.cs` | YAML 序列化（Pattern 自动 + Stage 手写） |
| `Core/Random/DeterministicRng.cs` | 确定性 PRNG |
| `Runtime/Preview/TimelinePlaybackController.cs` | 时间轴播放控制（RefreshEvent） |
| `Runtime/Preview/PreviewerPool.cs` | PatternPreviewer 对象池 |
| `Runtime/PatternLibrary.cs` | Pattern 文件扫描 + 缓存 |
| `Editor/UI/Timeline/TimelineEditorView.cs` | 时间轴编辑器 UI（浮动 Properties + 拖拽高度 + Seed） |
| `Editor/UI/PatternEditor/PatternEditorView.cs` | 弹幕编辑面板 |
| `Editor/UI/FileManager/STGCatalog.cs` | YAML 文件索引系统 |
| `Editor/Scene/PatternSandboxSetup.cs` | 场景引导（双模式 + 拖拽手柄） |
| `Editor/Commands/CommandStack.cs` | Undo/Redo 系统 |
| `Editor/UI/DataBinder.cs` | UI 数据绑定层 |

---

## 开始

请先执行以下操作：
1. `memorix_session_start` 开始新会话
2. 读取设计文档 `doc/3D_STG_Editor_Confirmed_Design_Route.md` 的第二章（文件结构）、第三章（数据模型）、第五章（Phase 4 条目）
3. `memorix_search` 查询 Phase 3 产出、种子系统、Timeline 架构
4. `memorix_session_context` 获取上一次 session 摘要
5. 然后从 **决策 1（范围裁剪）** 开始，向用户提出你的推荐方案并等待确认
