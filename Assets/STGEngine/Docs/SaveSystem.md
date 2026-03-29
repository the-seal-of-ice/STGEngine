# STGEngine 保存体系

## 概览

STGEngine 的数据持久化分为两大模型：**结构数据**（嵌入 Stage YAML）和**资源模板**（独立 YAML 文件 + Override 机制）。

---

## 1. 数据存储结构

```
STGData/
├── catalog.yaml              ← 资源索引（ID → 文件路径映射）
├── settings.yaml             ← [GAMEPLAY] 项目级游玩参数
├── editor_prefs.yaml         ← [EDITOR] 编辑器偏好（不进版本控制）
├── Stages/{id}.yaml          ← Stage 文件（含 Segments + Events + SpellCardIds）
├── Patterns/{id}.yaml        ← 弹幕模板
├── SpellCards/{id}.yaml      ← 符卡模板
├── Waves/{id}.yaml           ← 波次模板
├── EnemyTypes/{id}.yaml      ← 小怪类型模板
└── Modified/                 ← Override 文件（实例级覆盖）
    ├── {segmentId}/
    │   ├── {eventId}/
    │   │   ├── {patternId}.yaml      ← Pattern Override
    │   │   └── {enemyTypeId}.yaml    ← EnemyType Override
    │   └── sc_{index}/
    │       ├── {spellCardId}.yaml    ← SpellCard Override
    │       └── {spellCardId}/
    │           └── {patternId}.yaml  ← SpellCard 内 Pattern Override
    └── {segmentId}/{eventId}/
        └── {waveId}.yaml             ← Wave Override
```

---

## 2. 两大保存模型

### 2.1 结构数据 → Stage YAML（无 Override）

以下数据嵌入 Stage 文件，没有独立的 Override 机制：

| 数据 | 所属层级 | 存储位置 |
|---|---|---|
| Segments 列表 | StageLayer | `stages/{id}.yaml → segments[]` |
| MidStage Events | MidStageLayer | `segments[].events[]` |
| BossFight SpellCardIds | BossFightLayer | `segments[].spell_card_ids[]` |

保存触发：
- `AutoSaveStage()` — 命令执行后自动保存
- `Ctrl+S` — 在 Stage/MidStage 层弹出 Stage 保存对话框

### 2.2 资源模板 → 按 ContextId 决定 Override 或原始文件

| 资源类型 | Override 路径 | 原始文件路径 | 保存函数 |
|---|---|---|---|
| Pattern | `Modified/{ctx}/{patternId}.yaml` | `Patterns/{id}.yaml` | `SaveEditedPattern` |
| Wave | `Modified/{ctx}/{waveId}.yaml` | `Waves/{id}.yaml` | `SaveWaveData` |
| EnemyType | `Modified/{ctx}/{etId}.yaml` | `EnemyTypes/{id}.yaml` | `SaveEnemyType` |
| SpellCard | `Modified/{ctx}/{scId}.yaml` | `SpellCards/{id}.yaml` | `SaveSpellCardInContext` |

规则：有 ContextId → 保存 Override；无 ContextId → 保存原始文件。

---

## 3. Override Context 粒度

| 资源 | ContextId 格式 | 含义 |
|---|---|---|
| Pattern (MidStage) | `segmentId/eventId` | 每个 SpawnPatternEvent 实例独立 |
| Pattern (SpellCard) | `segmentId/sc_{index}/{spellCardId}` | 每个 SpellCard 实例内独立 |
| Pattern (EnemyType) | 继承父 WaveLayer 的 contextId | 跟随 Wave 实例 |
| Wave | `segmentId/eventId` | 每个 SpawnWaveEvent 实例独立 |
| EnemyType | 继承父 WaveLayer 的 contextId | 跟随 Wave 实例 |
| SpellCard | `segmentId/sc_{index}` | 每个 BossFight 中的列表位置独立 |

核心原则：**同一个模板在不同引用位置的修改必须互相隔离。**

---

## 4. 快捷键

| 快捷键 | 行为 |
|---|---|
| `Ctrl+S` | 上下文感知保存：资源层直接保存当前 YAML，Stage/MidStage 弹保存对话框 |
| `Ctrl+Shift+S` | 另存为新模板：弹对话框输入名称，从内存序列化，生成新 UUID |
| `Ctrl+Z` | 撤销（优先 PatternEditor 的 CommandStack） |
| `Ctrl+Y` | 重做 |
| `Delete` | 删除选中 block |
| `Ctrl+C/V/D` | 复制/粘贴/原地复制 |
| `Space` | 播放/暂停 |

---

## 5. 右键菜单 Override 操作

| 层级 | Revert to Original | Save as New Template |
|---|---|---|
| BossFight (SpellCard block) | 有 Override 时显示 | 有 Override 时显示 |
| WaveLayer | 有 Override 时显示 | 有 Override 时显示 |
| EnemyTypeLayer | 有 Override 时显示 | 有 Override 时显示 |

---

## 6. 保存可视化

| 位置 | 指示 | 含义 |
|---|---|---|
| 属性面板顶部 | 橙色条 ✎ | Override 模式 — 修改保存到实例副本 |
| 属性面板顶部 | 黄色条 ⚠ | 原始模板模式 — 修改影响所有引用 |
| Block 边框 | 橙色 2px 边框 | 该 block 使用了 Override 版本 |
| Block 标签 | `[M]` 前缀 | 该 block 已被修改（Override） |
| 面包屑 | `[M]` 前缀 | 当前编辑的 SpellCard 有 Override |
| PatternEditor | 橙色文字 | "Override mode — changes auto-saved" |
| Toolbar | 绿色 ✓ 闪烁 | Ctrl+S 保存确认（2秒消失） |

---

## 7. 自动保存机制

所有通过 `CommandStack.Execute()` 的操作（拖拽、缩放、属性修改、增删）都会触发：

```
OnCommandStateChanged()
  → InvalidateCurrentLayerBlocks()
  → _trackArea.RebuildBlocks()
  → LoadPreviewForLayer()
  → 刷新属性面板
  → AutoSaveCurrentLayer()    ← 自动保存到磁盘
  → NotifyWavePlaceholders()
  → RefreshUndoRedoButtons()
```

`AutoSaveCurrentLayer()` 按当前层级类型分发到对应的保存函数。

---

## 8. 新增资源类型 Checklist（保存相关）

添加新的资源类型时，保存体系需要完成：

- [ ] 实现序列化/反序列化方法（YamlSerializer）
- [ ] OverrideManager 添加 `Resolve{Type}Path` 方法
- [ ] Layer 类添加 `ContextId` 属性
- [ ] 父层 `CreateChildLayer` 传递 ContextId
- [ ] `WireLayerToTrackArea` 中绑定保存回调（传入 ContextId）
- [ ] `AutoSaveCurrentLayer` 添加新层级分支
- [ ] `SaveCurrentLayerExplicit` 添加新层级分支
- [ ] `SaveCurrentLayerAs` 添加新层级分支
- [ ] 右键菜单添加 Revert/SaveAsNew（如果支持 Override）
- [ ] `ShowLayerSummary` 中添加 `CreateSaveContextBar`
- [ ] `OverrideManager.SaveAsNewTemplate` 的 switch 中添加新类型
