# 递归 Timeline 层级架构重构提示词

## 角色

你是 STGEngine 项目的架构师兼实施者。本次任务是将编辑器从"固定层级硬编码"重构为"统一递归 Timeline 层级架构"，类似 FL Studio 的 Playlist 模式。这是一次重大架构变更，需要分步实施、逐步验证。

---

## 关键工作原则

### 1. 先确认再动手
本次涉及核心架构重构。**每个设计决策必须在编码前与用户逐项确认。** 给出推荐方案 + 理由 + 备选方案，等待用户确认。不要假设用户同意。

### 2. UI 问题适可而止
Unity Runtime UI Toolkit 的截图无法被你准确理解。遇到 UI 布局/样式问题时：
- **不要反复盲改。** 最多尝试 2 次修复。
- 如果 2 次未解决，**停下来**，向用户清晰描述问题现象、你尝试过的方案、以及 2-3 个潜在解决方向，让用户选择或指导。

### 3. 阶段性提交
每完成一个可验证的功能单元，立即：
- `refresh_unity` + `read_console` 验证编译
- 请用户进入 Play 模式验证（不要自己截图判断）
- 确认成功后 `git add` + `git commit`（conventional commits 格式）+ `git push origin main`

### 4. 灾难恢复
如果改动导致严重问题，**不要继续修补**：
- 向用户说明情况
- 提议 `git diff` 查看改动范围
- 提议回退操作
- **必须获得用户确认后才执行回退操作**
- 安全回退点：`git tag phase4-complete`（提交 8940633）

### 5. 不要弄脏项目
- 不要修改 Unity 场景文件的序列化字段来测试
- 不要自行进入 Play 模式截图
- 需要验证时，请用户操作并反馈结果

---

## 项目上下文

### 项目路径
`D:\Projects\unity\STGEngine`

### 设计文档
`D:\Projects\unity\STGEngine\doc\3D_STG_Editor_Confirmed_Design_Route.md`
- 第二章：程序集与文件夹结构
- 第三章：核心数据模型
- 第五章：Phase 路线图 + "待实现设计想法"章节（递归 Timeline 架构设想在此）
- 第九章：Phase 1-4 实施记录

### MCP 工具
- **Memorix**：`memorix_session_start` 开始会话，`memorix_search` 查询历史决策，`memorix_store` 记录新决策
- **Unity MCP**：`refresh_unity` 编译，`read_console` 检查错误，`manage_script` / `create_script` 管理脚本。**不要用 `manage_camera screenshot` 自行截图判断 UI 效果。**

### Memorix 关键查询
- `memorix_search: "递归 Timeline 层级"` → #68 架构设想完整记录
- `memorix_search: "Phase 4"` → Phase 4a/4b 产出
- `memorix_search: "UI theme override"` → #63 Runtime Theme 覆盖 gotcha
- `memorix_search: "PatternEditorView UI布局"` → #33 UI 布局经验
- `memorix_search: "YamlDotNet serialization"` → 序列化架构
- `memorix_search: "seed deterministic"` → 确定性种子系统
- `memorix_session_context` → 上一次 session 完整摘要

### 安全回退点
```
git tag: phase4-complete (commit 8940633)
```
如果重构出现严重问题，可以 `git checkout phase4-complete` 回退。

---

## 当前项目状态（Phase 4 完成后）

### 程序集结构（3 层）
```
Core（纯 C# 数据模型 + 序列化 + PRNG，无 MonoBehaviour）
  ▲
Runtime（弹幕渲染 + 预览 + SimulationLoop，依赖 UnityEngine）
  ▲
Editor（UI Toolkit 编辑器 + Command 系统，依赖 Core + Runtime）
```

### 已有 55 个 .cs 文件，关键组件

**Core 层：**
| 组件 | 说明 |
|------|------|
| BulletPattern | 弹幕模式 = Emitter + Modifier[] + 视觉参数 + Seed |
| Stage | 顶层关卡容器 = Segment[] + Seed |
| TimelineSegment | 时间轴段落 = Events[] + SpellCardIds[] + TriggerCondition + SegmentType(MidStage/BossFight) |
| SpawnPatternEvent | 时间轴事件：生成弹幕（PatternId + SpawnPosition） |
| SpawnWaveEvent | 时间轴事件：生成波次（WaveId + SpawnOffset） |
| SpellCard | 符卡 = Patterns(SpellCardPattern[]) + BossPath(PathKeyframe[]) + Health + TimeLimit |
| EnemyType | 小怪类型模板 = Health/Speed/PatternIds/Color/MeshType |
| Wave | 波次 = EnemyInstance[]（每个含 EnemyTypeId + Path） |
| PathKeyframe | 路径关键帧 = Time + Position |
| IEmitter (6 种) | Point / Ring / Sphere / Line / Cone + 接口 |
| IModifier (7 种) | SpeedCurve / Wave / IndependentWave / Homing / Bounce / Split + 接口 |
| DeterministicRng | Xoshiro256** PRNG |
| YamlSerializer | Pattern 自动序列化 + Stage 手写序列化 + EnemyType/Wave/SpellCard 自动序列化 |

**Runtime 层：**
| 组件 | 说明 |
|------|------|
| BulletEvaluator | 无状态公式求值器 f(t, params) |
| SimulationEvaluator | 有状态逐帧求值器 |
| PatternPreviewer | MonoBehaviour，双路径调度，渲染插值 |
| TimelinePlaybackController | 多 pattern 时间轴播放（PreviewerPool + 事件激活/释放） |
| PreviewerPool | PatternPreviewer 对象池 |
| BulletRenderer | GPU Instancing 多 Batch 渲染 |
| BossPlaceholder | Boss 视觉占位符（菱形 mesh + GL 路径线） |
| PatternLibrary | YAML pattern 文件扫描 + 缓存 |

**Editor 层：**
| 组件 | 说明 |
|------|------|
| PatternEditorView | 弹幕编辑面板（发射器/修饰器/视觉参数/Seed） |
| TimelineEditorView | 时间轴编辑器（面包屑3层/工具栏/SegmentList/TrackArea/浮动Properties/符卡编辑器/BossFight预览） |
| TrackAreaView | 时间轴轨道区域（事件块渲染/拖拽/缩放/右键菜单），已泛化为 TimelineEvent |
| SegmentListView | 左侧 Segment 列表（MidStage/BossFight 类型切换） |
| AssetLibraryPanel | 左侧资源库面板（4 类资源分组 + ▶ 添加按钮） |
| STGCatalog | YAML 文件索引（Patterns/Stages/EnemyTypes/Waves/SpellCards） |
| CommandStack | Undo/Redo 栈 |
| DataBinder | UI Toolkit 轻量数据绑定层 |
| PatternSandboxSetup | 场景引导（双模式 + 拖拽手柄 + BossPlaceholder 集成） |

### 当前 Timeline 层级（硬编码）
```
面包屑: Stage > Segment > SpellCard（3 层固定）

L0 Stage: SegmentListView 显示 Segment 列表
L1 Segment:
  - MidStage: TrackAreaView 显示 SpawnPatternEvent/SpawnWaveEvent 块
  - BossFight: Properties 面板显示符卡列表 + TrackArea 显示拼接预览
L2 SpellCard: Properties 面板显示符卡编辑器 + TrackArea 显示 Pattern 预览
```

### YAML 格式与存储
```
Assets/Resources/STGData/
├── Patterns/*.yaml        (BulletPattern，YamlDotNet 自动序列化)
├── Stages/*.yaml          (Stage，手写序列化)
├── EnemyTypes/*.yaml      (EnemyType，YamlDotNet 自动)
├── Waves/*.yaml           (Wave，YamlDotNet 自动)
├── SpellCards/*.yaml      (SpellCard，YamlDotNet 自动)
└── catalog.yaml           (5 类资源索引)
```

---

## 重构目标：递归 Timeline 层级架构

### 核心理念（来自 Memorix #68）

1. **统一抽象**：每一层都是同一个结构——时间轴上排列"块"，双击进入下一层
2. **块内缩略图**：子层级的视觉内容按比例缩放画在父层级块内部（FL Studio 风格）
3. **Modified/Override 机制**：修改引用资源时不改原始 YAML，自动创建 Modified 副本

### 目标层级树
```
L0 Stage
│  时间轴块: Segment
│  块内缩略图: 该 Segment 内所有事件/符卡的缩略排列
│  双击 → L1
│
├─ L1a MidStage Segment
│  │  时间轴块: SpawnPatternEvent / SpawnWaveEvent
│  │  块内缩略图: 弹幕轨迹 / 小怪时序
│  │  双击 → L2a (Pattern) 或 L2b (Wave)
│  │
│  ├─ L2a Pattern
│  │    属性面板: Emitter + Modifier[] + 视觉参数
│  │    未来: PatternTimeline（发射节奏关键帧）
│  │
│  └─ L2b Wave
│       时间轴块: EnemyInstance（按 SpawnDelay）
│       双击 → L3 (EnemyType)
│
└─ L1b BossFight Segment
   │  时间轴块: SpellCard（按顺序）
   │  块内缩略图: 该符卡内 Pattern 排列
   │  Boss 占位符 + 拼接路径
   │  双击 → L2c
   │
   └─ L2c SpellCard
        时间轴块: SpellCardPattern（按 Delay）
        块内缩略图: 弹幕轨迹
        双击 → L2a (Pattern)
```

### 实施分三步

**Step 1：递归层级导航 + 双击进入（中等难度）**
- 定义 `ITimelineLayer` 接口（或抽象类）
- 每种层级实现该接口：提供块列表、块的时间/时长、双击行为、属性面板内容
- 面包屑泛化为 N 层栈
- TrackAreaView 泛化为接受 `ITimelineLayer` 而非硬编码 `TimelineSegment`
- 不改数据模型，不改序列化

**Step 2：块内缩略图（高难度）**
- TrackAreaView 的事件块内部用 `generateVisualContent` 自绘缩略图
- 每种层级提供缩略图绘制回调
- 最简版：颜色条表示子块时间分布
- 进阶版：弹幕轨迹线、路径线
- 注意性能（缓存、脏标记）

**Step 3：Modified/Override 机制（高难度）**
- 资源引用从 "ID" 变为 "ID + 可选 Override 路径"
- Modified 文件存储在 `STGData/Modified/{上下文ID}/{资源ID}.yaml`
- 修改引用资源时自动创建 Modified 副本
- 面包屑和块上显示 [M] 标记
- 可以"还原为原始"或"另存为新模板"
- catalog 扩展支持 Modified 索引
- 所有资源加载逻辑需要改为"检查 Override → 加载"

---

## 已知 Gotcha（必读）

1. **UI Toolkit Runtime Theme 覆盖文字变黑** — 动态重建的 UI 区域需要在重建后调用 `ForceApplyTheme()`，使用同步 + schedule.Execute 延迟 50ms/200ms 兜底。不要用 `GeometryChangedEvent` 回调（会累积）。详见 memorix #63。

2. **IntegerField 输入框文字被遮挡** — IntegerField 在紧凑空间中需要 `seed-field` CSS class 做特殊处理。

3. **YamlDotNet 嵌套抽象类型列表** — `List<抽象类型>` 需要手写两阶段解析。Stage 的序列化是手写的。EnemyType/Wave/SpellCard 用 YamlDotNet 自动序列化。

4. **HomingModifier.Rng 属性排除序列化** — `EmitObjectProperties` 中硬编码跳过 `Rng` 属性名。

5. **Timeline pooled previewer 和 _singlePreviewer 是独立的** — 编辑 pattern 参数后需要通过 `TimelinePlaybackController.RefreshEvent()` 通知 pooled previewer 刷新。

6. **Boss 占位符必须跟随 _playback.CurrentTime** — 不能用独立计时器，否则暂停不同步。

7. **STGEngine.Core.Random 与 UnityEngine.Random 命名空间冲突** — 需用完整限定名。

8. **LoadBossFightPreview / LoadSpellCardPreview 不应自动播放** — 用户手动控制播放。

---

## 关键文件参考

| 文件 | 你需要了解的内容 |
|------|-----------------|
| `doc/3D_STG_Editor_Confirmed_Design_Route.md` | 完整设计路线 + 递归 Timeline 架构设想 |
| `Editor/UI/Timeline/TimelineEditorView.cs` | **最大最复杂的文件（~2240行）**，包含面包屑、符卡编辑器、BossFight 预览、所有层级导航逻辑。重构的主要目标。 |
| `Editor/UI/Timeline/TrackAreaView.cs` | 时间轴轨道区域（~730行），事件块渲染/拖拽/缩放。需要泛化为接受 ITimelineLayer。 |
| `Editor/UI/Timeline/SegmentListView.cs` | Segment 列表（~380行），重构后可能被 L0 的 TrackArea 替代。 |
| `Editor/Scene/PatternSandboxSetup.cs` | 场景引导（~610行），集成所有组件。 |
| `Editor/UI/PatternEditor/PatternEditorView.cs` | 弹幕编辑面板（~1430行），L2a Pattern 层级的属性编辑器。 |
| `Editor/UI/AssetLibrary/AssetLibraryPanel.cs` | 资源库面板（~390行）。 |
| `Runtime/Preview/TimelinePlaybackController.cs` | 时间轴播放控制（~310行），管理事件激活/释放。 |
| `Runtime/Preview/BossPlaceholder.cs` | Boss 占位符（~170行）。 |
| `Core/DataModel/SpellCard.cs` | 符卡数据模型。 |
| `Core/Timeline/TimelineSegment.cs` | 段落数据模型（Events[] + SpellCardIds[]）。 |
| `Core/Timeline/TimelineEvent.cs` | 事件基类 + SpawnPatternEvent + SpawnWaveEvent。 |
| `Core/Serialization/YamlSerializer.cs` | 序列化（~880行），Pattern 自动 + Stage 手写。 |
| `Editor/UI/FileManager/STGCatalog.cs` | 资源索引（~600行）。 |
| `Editor/Commands/CommandStack.cs` | Undo/Redo 系统。 |

---

## 约束

- **三层程序集边界：** Core 不依赖 MonoBehaviour/GameObject
- **向后兼容：** Phase 1-4 的所有功能不能被破坏（单 Pattern 编辑、Timeline 编排、YAML 文件格式、BossFight 预览）
- **Command Pattern：** 所有编辑操作必须可 Undo/Redo
- **DataBinder：** UI 绑定使用现有 DataBinder 层
- **YamlDotNet：** 新数据类型需要 [TypeTag] 标记
- **GPU Instancing：** 复用现有 BulletRenderer 多 Batch 架构
- **确定性种子：** 新增的任何随机行为必须通过 DeterministicRng / SeedManager
- **UI 主题：** 新 UI 组件必须调用 ApplyLightTextTheme / RegisterThemeOverride
- **命名空间冲突：** `STGEngine.Core.Random` 与 `UnityEngine.Random` 冲突

---

## 工作流程

### 第一阶段：设计确认（不写代码）

1. `memorix_session_start` 开始新会话
2. 读取设计文档"待实现设计想法"章节中的递归 Timeline 架构设想
3. `memorix_search` 查询 #68（架构设想）、#63（UI gotcha）、#33（UI 布局经验）
4. `memorix_session_context` 获取上一次 session 摘要
5. 对 Step 1（递归导航）的具体实现方案向用户提问确认：
   - `ITimelineLayer` 接口设计
   - 面包屑栈的数据结构
   - TrackAreaView 泛化策略（重写 vs 适配器）
   - 现有 SegmentListView 的去留
   - 双击进入的交互细节
6. 确认后输出实施计划，等待用户确认

### 第二阶段：分段实施 Step 1

按确认的计划逐步实施。每个子步骤：
1. 编码
2. `refresh_unity` + `read_console` 验证编译
3. 请用户 Play 模式验证
4. `git commit` + `git push origin main`
5. `memorix_store` 记录

### 第三阶段：Step 2 和 Step 3

Step 1 完成并验证后，再讨论 Step 2（缩略图）和 Step 3（Modified 机制）的设计。

---

## 开始

请先执行以下操作：
1. `memorix_session_start` 开始新会话
2. 读取设计文档中"待实现设计想法 > 递归 Timeline 层级架构"章节
3. `memorix_detail` 获取 #68（架构设想）完整内容
4. `memorix_session_context` 获取上一次 session 摘要
5. 读取 `TimelineEditorView.cs` 和 `TrackAreaView.cs` 的关键结构
6. 然后从 **Step 1 的 ITimelineLayer 接口设计** 开始，向用户提出你的推荐方案并等待确认
